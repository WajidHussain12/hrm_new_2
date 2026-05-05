using System;
using System.Collections.Generic;
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
        public async Task<SalaryProcessPreviewResult> PreviewSalaryProcessAsync(
            SalariesProcessViewModel model,
            string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var preview = await BuildSalaryProcessPreviewAsync(connection, transaction, model, currentUserId);
                var baseline = preview.Departments
                    .Select(department => new SalaryProcessPreviewBaseline
                    {
                        DepartmentId = department.DepartmentId,
                        HeaderCount = department.ExistingHeaderCount,
                        DetailCount = department.ExistingDetailCount,
                        LoanDeductionCount = department.ExistingLoanDeductionCount,
                        DepartmentStatusCount = department.ExistingDepartmentStatusCount
                    })
                    .ToList();

                await transaction.RollbackAsync();
                preview.RollbackIntegrityPreserved = await VerifySalaryProcessPreviewBaselineAsync(
                    connection,
                    model,
                    currentUserId,
                    baseline);

                return preview;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                }

                throw;
            }
        }

        private async Task<SalaryProcessPreviewResult> BuildSalaryProcessPreviewAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SalariesProcessViewModel model,
            string currentUserId)
        {
            var runResult = await ExecuteSalaryProcessAsync(
                connection,
                transaction ?? throw new ArgumentNullException(nameof(transaction)),
                model,
                currentUserId);

            return new SalaryProcessPreviewResult
            {
                Year = runResult.Year,
                Month = runResult.Month,
                CityCode = runResult.CityCode,
                CommissionFilter = runResult.CommissionFilter,
                IsExecutive = runResult.IsExecutive,
                TotalPersistedHeaderRows = runResult.PersistedHeaderRows,
                TotalPersistedDetailRows = runResult.PersistedDetailRows,
                TotalPersistedCarryForwardPenaltyRows = runResult.PersistedCarryForwardRows,
                TotalLoanDeductionRowsInserted = runResult.LoanDeductionRowsInserted,
                TotalAcknowledgmentRowsInserted = runResult.AcknowledgmentRowsInserted,
                TotalDepartmentStatusRowsAffected = runResult.DepartmentStatusRowsAffected,
                Departments = runResult.Departments
                    .Select(static department => new SalaryProcessPreviewDepartment
                    {
                        DepartmentId = department.DepartmentId,
                        DepartmentName = department.DepartmentName,
                        CandidateRowCount = department.CandidateRowCount,
                        DistinctEmployeeCount = department.DistinctEmployeeCount,
                        ExistingHeaderCount = department.ExistingHeaderCount,
                        ExistingDetailCount = department.ExistingDetailCount,
                        ExistingLoanDeductionCount = department.ExistingLoanDeductionCount,
                        ExistingDepartmentStatusCount = department.ExistingDepartmentStatusCount,
                        DeletedHeaderRows = department.DeletedHeaderRows,
                        DeletedDetailRows = department.DeletedDetailRows,
                        DeletedLoanDeductionRows = department.DeletedLoanDeductionRows,
                        PreparedHeaderRows = department.PreparedHeaderRows,
                        PreparedDetailRows = department.PreparedDetailRows,
                        PreparedCarryForwardPenaltyRows = department.PreparedCarryForwardPenaltyRows,
                        PreparedTaxableCashHeaderRows = department.PreparedTaxableCashHeaderRows,
                        PreparedTaxableNonCashHeaderRows = department.PreparedTaxableNonCashHeaderRows,
                        MonthWideCashHeaderRowsImpacted = department.MonthWideCashHeaderRowsImpacted,
                        ExpectedTaxRowsAffected = department.ExpectedTaxRowsAffected,
                        PersistedHeaderRows = department.PersistedHeaderRows,
                        PersistedDetailRows = department.PersistedDetailRows,
                        PersistedCarryForwardPenaltyRows = department.PersistedCarryForwardPenaltyRows,
                        LoanDeductionRowsInserted = department.LoanDeductionRowsInserted,
                        TaxRowsAffected = department.TaxRowsAffected,
                        AcknowledgmentRowsInserted = department.AcknowledgmentRowsInserted,
                        DepartmentStatusRowsAffected = department.DepartmentStatusRowsAffected
                    })
                    .ToList()
            };
        }

        private static List<string> NormalizeSalaryDepartments(IEnumerable<string>? selectedDepartments)
        {
            return selectedDepartments?
                .Where(static department => !string.IsNullOrWhiteSpace(department))
                .Select(static department => department.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
        }

        private async Task<bool> IsExecutiveSalaryProcessUserAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string currentUserId)
        {
            string? userRole = await connection.ExecuteScalarAsync<string>(
                @"SELECT user_role
                  FROM lcs_users
                  WHERE userid = @UserId
                  LIMIT 1",
                new { UserId = currentUserId },
                transaction);

            return string.Equals(userRole?.Trim(), "023", StringComparison.Ordinal);
        }

        private static DateTime GetSalaryProcessDate(int year, int month)
        {
            return new DateTime(year, month, DateTime.DaysInMonth(year, month));
        }

        private void ValidateSalaryGenerationWindow(DateTime salaryDate)
        {
            if (_overrideSalaryGeneration)
            {
                return;
            }

            if (DateTime.Now.Date.AddDays(1) < salaryDate.Date)
            {
                throw new ArgumentException("Process Can not run on current working Month");
            }
        }

        private async Task ValidatePendingAdvanceSalariesAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            IReadOnlyCollection<string> selectedDepartments,
            SalariesProcessViewModel model,
            DateTime salaryDate)
        {
            int pendingAdvanceCount = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM hr_employeedepartmentdetails ed
                  INNER JOIN hr_employeepersonaldetail he ON ed.Emp_No = he.EMP_NO
                  INNER JOIN hr_advance_salary adv ON he.EMP_NO = adv.emp_no
                  WHERE he.emp_status <> 'I'
                    AND adv.Year = @Year
                    AND adv.Month = @Month
                    AND adv.status <> 'A'
                    AND ed.DeptCode IN @SelectedDepartments
                    AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate, '2099-08-28')",
                new
                {
                    model.Year,
                    model.Month,
                    SelectedDepartments = selectedDepartments.ToArray(),
                    SalaryDate = salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
                },
                transaction);

            if (pendingAdvanceCount > 0)
            {
                throw new ArgumentException("Pending advance salaries found.");
            }
        }

        private async Task<Dictionary<string, string>> GetSalaryDepartmentNamesAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            IReadOnlyCollection<string> selectedDepartments)
        {
            var records = await connection.QueryAsync<(string DepartmentId, string DepartmentName)>(
                @"SELECT SDID AS DepartmentId, FullName AS DepartmentName
                  FROM hr_subdepartment
                  WHERE SDID IN @SelectedDepartments",
                new { SelectedDepartments = selectedDepartments.ToArray() },
                transaction);

            return records.ToDictionary(
                static record => record.DepartmentId,
                static record => string.IsNullOrWhiteSpace(record.DepartmentName) ? record.DepartmentId : record.DepartmentName,
                StringComparer.OrdinalIgnoreCase);
        }

        private async Task<IEnumerable<SalaryProcessEmployeeRow>> LoadSalaryProcessEmployeesAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            SalariesProcessViewModel model,
            DateTime salaryDate,
            bool isExecutive)
        {
            string query = model.CommissionFilter switch
            {
                1 => @"
                    SELECT
                        ed.Emp_No AS EmpNo,
                        ed.DeptCode AS DeptCode,
                        DATE(he.APPOINT_DATE) AS AppointmentDate
                    FROM hr_employeedepartmentdetails ed
                    INNER JOIN hr_employeepersonaldetail he
                        ON ed.Emp_No = he.EMP_NO
                       AND ed.ToDate IS NULL
                    INNER JOIN hr_employeeroutecode rd ON rd.Emp_No = he.EMP_NO
                    WHERE he.dual_job_approve <> 'N'
                      AND he.emp_status = 'A'
                      AND ed.DeptCode = @DepartmentId
                      AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate,'2099-08-28')
                      AND he.IsExecutive = @IsExecutive
                      AND he.P_CITY_CODE = @CityCode
                    ORDER BY ed.Code DESC",
                2 => @"
                    SELECT
                        ed.Emp_No AS EmpNo,
                        ed.DeptCode AS DeptCode,
                        DATE(he.APPOINT_DATE) AS AppointmentDate
                    FROM hr_employeedepartmentdetails ed
                    INNER JOIN hr_employeepersonaldetail he
                        ON ed.Emp_No = he.EMP_NO
                       AND ed.ToDate IS NULL
                    WHERE he.dual_job_approve <> 'N'
                      AND he.emp_status = 'A'
                      AND ed.Emp_No NOT IN (
                          SELECT Emp_No
                          FROM hr_employeeroutecode
                          WHERE ToDate IS NULL
                      )
                      AND ed.DeptCode = @DepartmentId
                      AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate,'2099-08-28')
                      AND he.IsExecutive = @IsExecutive
                      AND he.P_CITY_CODE = @CityCode
                    ORDER BY ed.Code DESC",
                _ => @"
                    SELECT
                        ed.Emp_No AS EmpNo,
                        ed.DeptCode AS DeptCode,
                        DATE(he.APPOINT_DATE) AS AppointmentDate
                    FROM hr_employeedepartmentdetails ed
                    INNER JOIN hr_employeepersonaldetail he
                        ON ed.Emp_No = he.EMP_NO
                       AND ed.ToDate IS NULL
                    WHERE he.dual_job_approve <> 'N'
                      AND he.emp_status = 'A'
                      AND ed.DeptCode = @DepartmentId
                      AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate,'2099-08-28')
                      AND he.IsExecutive = @IsExecutive
                      AND he.P_CITY_CODE = @CityCode
                    ORDER BY ed.Code DESC"
            };

            return await connection.QueryAsync<SalaryProcessEmployeeRow>(
                query,
                new
                {
                    DepartmentId = departmentId,
                    SalaryDate = salaryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    IsExecutive = isExecutive ? 1 : 0,
                    CityCode = model.CityCode?.Trim()
                },
                transaction);
        }

        private string GetSalaryProcessEmployeeScopeSubquery(int commissionFilter)
        {
            return commissionFilter switch
            {
                1 => @"
                    SELECT ed.Emp_No
                    FROM hr_employeedepartmentdetails ed
                    INNER JOIN hr_employeepersonaldetail he ON ed.Emp_No = he.EMP_NO
                    INNER JOIN hr_employeeroutecode rd ON rd.Emp_No = he.EMP_NO
                    WHERE he.dual_job_approve <> 'N'
                      AND he.emp_status = 'A'
                      AND he.IsExecutive = @IsExecutive
                      AND ed.DeptCode = @DepartmentId
                      AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate,'2099-08-28')
                      AND he.P_CITY_CODE = @CityCode",
                2 => @"
                    SELECT ed.Emp_No
                    FROM hr_employeedepartmentdetails ed
                    INNER JOIN hr_employeepersonaldetail he ON ed.Emp_No = he.EMP_NO
                    WHERE he.dual_job_approve <> 'N'
                      AND he.emp_status = 'A'
                      AND he.IsExecutive = @IsExecutive
                      AND ed.Emp_No NOT IN (
                          SELECT Emp_No
                          FROM hr_employeeroutecode
                          WHERE ToDate IS NULL
                      )
                      AND ed.DeptCode = @DepartmentId
                      AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate,'2099-08-28')
                      AND he.P_CITY_CODE = @CityCode",
                _ => @"
                    SELECT ed.Emp_No
                    FROM hr_employeedepartmentdetails ed
                    INNER JOIN hr_employeepersonaldetail he ON ed.Emp_No = he.EMP_NO
                    WHERE he.dual_job_approve <> 'N'
                      AND he.emp_status = 'A'
                      AND he.IsExecutive = @IsExecutive
                      AND ed.DeptCode = @DepartmentId
                      AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate,'2099-08-28')
                      AND he.P_CITY_CODE = @CityCode"
            };
        }

        private async Task<int> CountSalaryProcessScopeDetailsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            SalariesProcessViewModel model,
            DateTime salaryDate,
            bool isExecutive)
        {
            string employeeScope = GetSalaryProcessEmployeeScopeSubquery(model.CommissionFilter);
            string query = $@"
                SELECT COUNT(*)
                FROM hr_salaryprocessed_dtl
                WHERE SalaryYear = @Year
                  AND SalaryMonth = @Month
                  AND emp_no IN ({employeeScope})";

            return await connection.ExecuteScalarAsync<int>(
                query,
                BuildSalaryScopeParameters(model, departmentId, salaryDate, isExecutive),
                transaction);
        }

        private async Task<int> CountSalaryProcessScopeHeadersAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            SalariesProcessViewModel model,
            DateTime salaryDate,
            bool isExecutive)
        {
            string employeeScope = GetSalaryProcessEmployeeScopeSubquery(model.CommissionFilter);
            string query = $@"
                SELECT COUNT(*)
                FROM hr_salaryprocessed_hdr
                WHERE SalaryYear = @Year
                  AND SalaryMonth = @Month
                  AND emp_no IN ({employeeScope})";

            return await connection.ExecuteScalarAsync<int>(
                query,
                BuildSalaryScopeParameters(model, departmentId, salaryDate, isExecutive),
                transaction);
        }

        private async Task<int> CountSalaryProcessScopeLoanDeductionsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            SalariesProcessViewModel model,
            DateTime salaryDate,
            string salaryComment,
            bool isExecutive)
        {
            string employeeScope = GetSalaryProcessEmployeeScopeSubquery(model.CommissionFilter);
            string query = $@"
                SELECT COUNT(*)
                FROM hr_employeeloandeduction
                WHERE Comments = @Comments
                  AND emp_no IN ({employeeScope})";

            var parameters = BuildSalaryScopeParameters(model, departmentId, salaryDate, isExecutive);
            parameters.Add("@Comments", salaryComment);

            return await connection.ExecuteScalarAsync<int>(query, parameters, transaction);
        }

        private async Task<int> DeleteSalaryProcessScopeDetailsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            SalariesProcessViewModel model,
            DateTime salaryDate,
            bool isExecutive)
        {
            string employeeScope = GetSalaryProcessEmployeeScopeSubquery(model.CommissionFilter);
            string query = $@"
                DELETE FROM hr_salaryprocessed_dtl
                WHERE SalaryYear = @Year
                  AND SalaryMonth = @Month
                  AND emp_no IN ({employeeScope})";

            return await connection.ExecuteAsync(
                query,
                BuildSalaryScopeParameters(model, departmentId, salaryDate, isExecutive),
                transaction);
        }

        private async Task<int> DeleteSalaryProcessScopeHeadersAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            SalariesProcessViewModel model,
            DateTime salaryDate,
            bool isExecutive)
        {
            string employeeScope = GetSalaryProcessEmployeeScopeSubquery(model.CommissionFilter);
            string query = $@"
                DELETE h
                FROM hr_salaryprocessed_hdr h
                WHERE h.SalaryYear = @Year
                  AND h.SalaryMonth = @Month
                  AND h.emp_no IN ({employeeScope})";

            return await connection.ExecuteAsync(
                query,
                BuildSalaryScopeParameters(model, departmentId, salaryDate, isExecutive),
                transaction);
        }

        private async Task<int> DeleteSalaryProcessScopeLoanDeductionsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string departmentId,
            SalariesProcessViewModel model,
            DateTime salaryDate,
            string salaryComment,
            bool isExecutive)
        {
            string employeeScope = GetSalaryProcessEmployeeScopeSubquery(model.CommissionFilter);
            string query = $@"
                DELETE a
                FROM hr_employeeloandeduction a
                WHERE a.Comments = @Comments
                  AND a.emp_no IN ({employeeScope})";

            var parameters = BuildSalaryScopeParameters(model, departmentId, salaryDate, isExecutive);
            parameters.Add("@Comments", salaryComment);

            return await connection.ExecuteAsync(query, parameters, transaction);
        }

        private DynamicParameters BuildSalaryScopeParameters(
            SalariesProcessViewModel model,
            string departmentId,
            DateTime salaryDate,
            bool isExecutive)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@Year", model.Year);
            parameters.Add("@Month", model.Month);
            parameters.Add("@DepartmentId", departmentId);
            parameters.Add("@SalaryDate", salaryDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            parameters.Add("@CityCode", model.CityCode?.Trim());
            parameters.Add("@IsExecutive", isExecutive ? 1 : 0);
            return parameters;
        }

        private async Task<bool> VerifySalaryProcessPreviewBaselineAsync(
            MySqlConnection connection,
            SalariesProcessViewModel model,
            string currentUserId,
            IReadOnlyCollection<SalaryProcessPreviewBaseline> baseline)
        {
            bool isExecutive = await IsExecutiveSalaryProcessUserAsync(connection, transaction: null, currentUserId);
            DateTime salaryDate = GetSalaryProcessDate(model.Year, model.Month);
            string salaryComment = GetSalaryLoanComment(model.Year, model.Month);

            foreach (var entry in baseline)
            {
                int headerCount = await CountSalaryProcessScopeHeadersAsync(
                    connection,
                    transaction: null,
                    entry.DepartmentId,
                    model,
                    salaryDate,
                    isExecutive);
                int detailCount = await CountSalaryProcessScopeDetailsAsync(
                    connection,
                    transaction: null,
                    entry.DepartmentId,
                    model,
                    salaryDate,
                    isExecutive);
                int loanCount = await CountSalaryProcessScopeLoanDeductionsAsync(
                    connection,
                    transaction: null,
                    entry.DepartmentId,
                    model,
                    salaryDate,
                    salaryComment,
                    isExecutive);
                int departmentStatusCount = await CountSalaryDepartmentStatusAsync(
                    connection,
                    transaction: null,
                    entry.DepartmentId,
                    model.Month,
                    model.Year,
                    model.CityCode?.Trim() ?? string.Empty);

                if (headerCount != entry.HeaderCount ||
                    detailCount != entry.DetailCount ||
                    loanCount != entry.LoanDeductionCount ||
                    departmentStatusCount != entry.DepartmentStatusCount)
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetSalaryLoanComment(int year, int month)
        {
            string monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
            return $"From Salary of {monthName} {year}";
        }

    }

    internal sealed class SalaryProcessPreviewBaseline
    {
        public string DepartmentId { get; set; } = string.Empty;
        public int HeaderCount { get; set; }
        public int DetailCount { get; set; }
        public int LoanDeductionCount { get; set; }
        public int DepartmentStatusCount { get; set; }
    }

    internal sealed class SalaryProcessEmployeeRow
    {
        public string EmpNo { get; set; } = string.Empty;
        public string DeptCode { get; set; } = string.Empty;
        public DateTime? AppointmentDate { get; set; }
    }
}
