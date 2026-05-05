using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        private async Task<string?> GetSalaryProcessUserRoleAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string currentUserId)
        {
            return await connection.ExecuteScalarAsync<string>(
                @"SELECT user_role
                  FROM lcs_users
                  WHERE userid = @UserId
                  LIMIT 1",
                new { UserId = currentUserId },
                transaction);
        }

        private async Task<SalaryProcessRunResult> ExecuteSalaryProcessAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            SalariesProcessViewModel model,
            string currentUserId)
        {
            var selectedDepartments = NormalizeSalaryDepartments(model.SelectedSubDepartments);
            if (selectedDepartments.Count == 0)
            {
                throw new ArgumentException("Please select at least one sub department.");
            }

            await EnsureProcessesOpenAsync(connection, model.Year, model.Month, model.CityCode);

            var (isCommission, isNonCommission) = GetSalaryProcessFlags(model.CommissionFilter);
            var alreadyProcessedDepartments = await GetAlreadyProcessedSalaryDepartmentsAsync(
                connection,
                selectedDepartments,
                model.Month,
                model.Year,
                model.CityCode,
                isCommission,
                isNonCommission);

            if (alreadyProcessedDepartments.Count > 0)
            {
                string departmentMessage = string.Join(", ", alreadyProcessedDepartments);
                throw new ArgumentException($"{departmentMessage} Department(s) Already processed".ToUpperInvariant());
            }

            DateTime salaryDate = GetSalaryProcessDate(model.Year, model.Month);
            ValidateSalaryGenerationWindow(salaryDate);
            await ValidatePendingAdvanceSalariesAsync(connection, transaction, selectedDepartments, model, salaryDate);

            string userRole = (await GetSalaryProcessUserRoleAsync(connection, transaction, currentUserId))?.Trim() ?? string.Empty;
            bool isExecutive = string.Equals(userRole, "023", StringComparison.Ordinal);
            var departmentNames = await GetSalaryDepartmentNamesAsync(connection, transaction, selectedDepartments);

            var (fromDate, toDate) = GetPayrollPeriod(model.Year, model.Month);
            int station = await GetSalaryProcessStationAsync(connection, transaction, model.CityCode);
            int location = await GetSalaryProcessLocationAsync(connection, transaction, model.CityCode);
            decimal fuelAvrgPrice = LCS.GetFuelAvrfPrice(connection, fromDate, toDate, model.Year, model.Month);
            decimal fuelAvrgPrice1 = LCS.GetFuelAvrfPrice1(connection, fromDate, toDate, model.Year, model.Month);
            var taxSlabList = (await connection.QueryAsync<TaxSlabList>(
                @"SELECT htd.sno, htd.LimitFrom, htd.LimitTo, htd.Pct_Amount, htd.Fix_Amount
                  FROM hr_tax_hdr hth
                  INNER JOIN hr_tax_dtl htd ON hth.Code = htd.TaxCode
                  WHERE @SalaryDate BETWEEN hth.DateFrom AND IFNULL(hth.DateTo,'2099-08-28')
                  ORDER BY htd.sno ASC",
                new { SalaryDate = salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) },
                transaction)).ToList();

            StateHelper.userid = currentUserId;
            StateHelper.user_role = userRole;
            DAL.ConnectionString = _connectionFactory.ConnectionString;

            var result = new SalaryProcessRunResult
            {
                Year = model.Year,
                Month = model.Month,
                CityCode = model.CityCode?.Trim() ?? string.Empty,
                CommissionFilter = model.CommissionFilter,
                IsExecutive = isExecutive
            };

            foreach (string departmentId in selectedDepartments)
            {
                string departmentName = departmentNames.TryGetValue(departmentId, out string? resolvedName)
                    ? resolvedName
                    : departmentId;

                var employees = (await LoadSalaryProcessEmployeesAsync(
                    connection,
                    transaction,
                    departmentId,
                    model,
                    salaryDate,
                    isExecutive)).ToList();

                if (employees.Count == 0)
                {
                    throw new ArgumentException($"No employee to process in {departmentName} Department");
                }

                var departmentResult = await ExecuteSalaryDepartmentAsync(
                    connection,
                    transaction,
                    model,
                    currentUserId,
                    userRole,
                    departmentId,
                    departmentName,
                    employees,
                    station,
                    location,
                    fuelAvrgPrice,
                    fuelAvrgPrice1,
                    taxSlabList,
                    isCommission,
                    isNonCommission,
                    isExecutive);

                result.Departments.Add(departmentResult);
                result.MessageCount += departmentResult.PersistedHeaderRows;
                result.PersistedHeaderRows += departmentResult.PersistedHeaderRows;
                result.PersistedDetailRows += departmentResult.PersistedDetailRows;
                result.PersistedCarryForwardRows += departmentResult.PersistedCarryForwardPenaltyRows;
                result.LoanDeductionRowsInserted += departmentResult.LoanDeductionRowsInserted;
                result.AcknowledgmentRowsInserted += departmentResult.AcknowledgmentRowsInserted;
                result.DepartmentStatusRowsAffected += departmentResult.DepartmentStatusRowsAffected;
            }

            return result;
        }

        private async Task<SalaryProcessDepartmentRunResult> ExecuteSalaryDepartmentAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            SalariesProcessViewModel model,
            string currentUserId,
            string userRole,
            string departmentId,
            string departmentName,
            IReadOnlyCollection<SalaryProcessEmployeeRow> employees,
            int station,
            int location,
            decimal fuelAvrgPrice,
            decimal fuelAvrgPrice1,
            IReadOnlyCollection<TaxSlabList> taxSlabList,
            int isCommission,
            int isNonCommission,
            bool isExecutive)
        {
            var salaryCfg = await SalaryConfig.LoadAsync(connection, transaction);

            DateTime salaryDate = GetSalaryProcessDate(model.Year, model.Month);
            string salaryComment = GetSalaryLoanComment(model.Year, model.Month);

            var result = new SalaryProcessDepartmentRunResult
            {
                DepartmentId = departmentId,
                DepartmentName = departmentName,
                CandidateRowCount = employees.Count,
                DistinctEmployeeCount = employees.Select(static employee => employee.EmpNo).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            };

            result.ExistingHeaderCount = await CountSalaryProcessScopeHeadersAsync(connection, transaction, departmentId, model, salaryDate, isExecutive);
            result.ExistingDetailCount = await CountSalaryProcessScopeDetailsAsync(connection, transaction, departmentId, model, salaryDate, isExecutive);
            result.ExistingLoanDeductionCount = await CountSalaryProcessScopeLoanDeductionsAsync(connection, transaction, departmentId, model, salaryDate, salaryComment, isExecutive);
            result.ExistingDepartmentStatusCount = await CountSalaryDepartmentStatusAsync(
                connection,
                transaction,
                departmentId,
                model.Month,
                model.Year,
                model.CityCode?.Trim() ?? string.Empty);

            result.DeletedDetailRows = await DeleteSalaryProcessScopeDetailsAsync(connection, transaction, departmentId, model, salaryDate, isExecutive);
            result.DeletedHeaderRows = await DeleteSalaryProcessScopeHeadersAsync(connection, transaction, departmentId, model, salaryDate, isExecutive);
            result.DeletedLoanDeductionRows = await DeleteSalaryProcessScopeLoanDeductionsAsync(connection, transaction, departmentId, model, salaryDate, salaryComment, isExecutive);

            DataTable allowanceCatalog = DAL.ExecuteDataTable(
                connection,
                CommandType.Text,
                @"SELECT FullName, TYPE, Pct_Amount, Fix_Amount, exclude_absent, Code, glcode
                  FROM hr_allow_ded_details
                  WHERE EmpWise_Flag IN ('All', @DepartmentId)",
                new MySqlParameter("@DepartmentId", departmentId));

            int loanCodeSeed = await GetNextLoanDeductionSequenceAsync(connection, transaction);
            var (fromDate, toDate) = GetPayrollPeriod(model.Year, model.Month);

            var engine = new LegacySalaryProcessEngine(
                connection,
                transaction,
                model,
                currentUserId,
                userRole,
                departmentId,
                departmentName,
                employees,
                allowanceCatalog,
                salaryDate,
                fromDate,
                toDate,
                fromDate,
                toDate,
                station,
                location,
                fuelAvrgPrice,
                fuelAvrgPrice1,
                taxSlabList.ToList(),
                loanCodeSeed,
                model.CityCode?.Trim() ?? string.Empty);

            SalaryProcessPreparedData preparedData = engine.Execute();
            result.PreparedHeaderRows = preparedData.PreparedHeaderRows;
            result.PreparedDetailRows = preparedData.PreparedDetailRows;
            result.PreparedCarryForwardPenaltyRows = preparedData.PreparedCarryForwardPenaltyRows;
            result.LoanDeductionRowsInserted = preparedData.LoanDeductionRowsInserted;
            result.PreparedTaxableCashHeaderRows = preparedData.PreparedTaxableCashHeaderRows;
            result.PreparedTaxableNonCashHeaderRows = preparedData.PreparedTaxableNonCashHeaderRows;

            var persistResult = await MasterDetailEntryAsync(preparedData.DataSet, connection, transaction);
            result.PersistedHeaderRows = persistResult.HeaderRowsInserted;
            result.PersistedDetailRows = persistResult.DetailRowsWritten;
            result.PersistedCarryForwardPenaltyRows = persistResult.PenaltyRowsWritten;

            if (result.PreparedTaxableCashHeaderRows > 0)
            {
                result.MonthWideCashHeaderRowsImpacted = await CountCashSalaryHeadersForMonthAsync(connection, transaction, model.Year, model.Month);
            }

            result.ExpectedTaxRowsAffected = result.PreparedTaxableNonCashHeaderRows
                + (result.PreparedTaxableCashHeaderRows > 0 ? result.MonthWideCashHeaderRowsImpacted * 4 : 0);

            result.TaxRowsAffected = await TaxCorrectAsync(preparedData.DataSet, connection, transaction, model.Year, model.Month, salaryCfg);
            result.AcknowledgmentRowsInserted = await InsertSalaryAcknowledgmentAsync(connection, transaction, model, currentUserId);
            result.DepartmentStatusRowsAffected = await CreateStatusDepartmentWiseAsync(connection, transaction, departmentId, model.Month, model.Year, isCommission, isNonCommission, model.CityCode?.Trim() ?? string.Empty);
            return result;
        }

        private async Task<int> GetSalaryProcessStationAsync(MySqlConnection connection, MySqlTransaction transaction, string cityCode)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT CAST(TRIM(LEADING '0' FROM station_id) AS SIGNED)
                  FROM hr_city
                  WHERE Code = @CityCode
                  LIMIT 1",
                new { CityCode = cityCode?.Trim() },
                transaction);
        }

        private async Task<int> GetSalaryProcessLocationAsync(MySqlConnection connection, MySqlTransaction transaction, string cityCode)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT CAST(TRIM(LEADING '0' FROM BranchLocation) AS SIGNED)
                  FROM hr_city
                  WHERE Code = @CityCode
                  LIMIT 1",
                new { CityCode = cityCode?.Trim() },
                transaction);
        }

        private async Task<int> GetNextLoanDeductionSequenceAsync(MySqlConnection connection, MySqlTransaction transaction)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COALESCE(MAX(CAST(LDed_No AS UNSIGNED)), 0) + 1
                  FROM hr_employeeloandeduction",
                transaction: transaction);
        }

        private async Task<int> CountSalaryDepartmentStatusAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            int month,
            int year,
            string cityCode)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM hr_DeptSalaryProcessStatus
                  WHERE month = @Month
                    AND year = @Year
                    AND Dept = @DepartmentId
                    AND cityID = @CityCode",
                new
                {
                    Month = month,
                    Year = year,
                    DepartmentId = departmentId,
                    CityCode = cityCode
                },
                transaction);
        }

        private async Task<int> CountCashSalaryHeadersForMonthAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM hr_salaryprocessed_hdr
                  WHERE SalaryYear = @Year
                    AND SalaryMonth = @Month
                    AND Payment_Mode = 'Cash'",
                new
                {
                    Year = year,
                    Month = month
                },
                transaction);
        }
    }

    internal sealed class SalaryProcessRunResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int CommissionFilter { get; set; }
        public bool IsExecutive { get; set; }
        public int MessageCount { get; set; }
        public int PersistedHeaderRows { get; set; }
        public int PersistedDetailRows { get; set; }
        public int PersistedCarryForwardRows { get; set; }
        public int LoanDeductionRowsInserted { get; set; }
        public int AcknowledgmentRowsInserted { get; set; }
        public int DepartmentStatusRowsAffected { get; set; }
        public List<SalaryProcessDepartmentRunResult> Departments { get; } = new();
    }

    internal sealed class SalaryProcessDepartmentRunResult
    {
        public string DepartmentId { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public int CandidateRowCount { get; set; }
        public int DistinctEmployeeCount { get; set; }
        public int ExistingHeaderCount { get; set; }
        public int ExistingDetailCount { get; set; }
        public int ExistingLoanDeductionCount { get; set; }
        public int ExistingDepartmentStatusCount { get; set; }
        public int DeletedHeaderRows { get; set; }
        public int DeletedDetailRows { get; set; }
        public int DeletedLoanDeductionRows { get; set; }
        public int PreparedHeaderRows { get; set; }
        public int PreparedDetailRows { get; set; }
        public int PreparedCarryForwardPenaltyRows { get; set; }
        public int PreparedTaxableCashHeaderRows { get; set; }
        public int PreparedTaxableNonCashHeaderRows { get; set; }
        public int MonthWideCashHeaderRowsImpacted { get; set; }
        public int ExpectedTaxRowsAffected { get; set; }
        public int PersistedHeaderRows { get; set; }
        public int PersistedDetailRows { get; set; }
        public int PersistedCarryForwardPenaltyRows { get; set; }
        public int LoanDeductionRowsInserted { get; set; }
        public int TaxRowsAffected { get; set; }
        public int AcknowledgmentRowsInserted { get; set; }
        public int DepartmentStatusRowsAffected { get; set; }
    }

    internal sealed class SalaryProcessPreparedData
    {
        public required DataSet DataSet { get; init; }
        public int PreparedHeaderRows { get; init; }
        public int PreparedDetailRows { get; init; }
        public int PreparedCarryForwardPenaltyRows { get; init; }
        public int LoanDeductionRowsInserted { get; init; }
        public int PreparedTaxableCashHeaderRows { get; init; }
        public int PreparedTaxableNonCashHeaderRows { get; init; }
    }
}
