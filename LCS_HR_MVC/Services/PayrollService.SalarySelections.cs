using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        private const string FinanceCatalog = "lcs_finance.";

        public async Task<SalaryReprocessViewModel> GetSalaryReprocessPageAsync(DateTime workingDate, string currentUserId, SalaryReprocessViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var model = existingModel ?? new SalaryReprocessViewModel();
            if (model.Year <= 0)
            {
                model.Year = workingDate.Year;
            }

            if (model.Month <= 0)
            {
                model.Month = workingDate.Month;
            }

            model.Years = BuildYearSelectList(workingDate);
            model.Zones = await BuildUserZoneSelectItemsAsync(connection, currentUserId, "Please Select", "00");
            model.Cities = await BuildUserCitySelectItemsAsync(connection, currentUserId, model.ZoneId, "Please Select", "0", includeAllCity: false);
            model.SubDepartments = await LoadSalaryReprocessSubDepartmentSelectItemsAsync(connection, model.CityCode, currentUserId);
            model.EmployeeOptions = await LoadSalaryReprocessEmployeeOptionsInternalAsync(connection, model.CityCode, model.SubDepartmentId, currentUserId);

            return model;
        }

        public async Task<IEnumerable<dynamic>> GetSalaryReprocessSubDepartmentsAsync(string cityCode, string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return Enumerable.Empty<dynamic>();
            }

            await connection.OpenAsync();
            return await LoadSalaryReprocessSubDepartmentsAsync(connection, cityCode, currentUserId);
        }

        public async Task<IEnumerable<SalaryProcessEmployeeOption>> GetSalaryReprocessEmployeesAsync(string cityCode, string subDepartmentId, string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return Enumerable.Empty<SalaryProcessEmployeeOption>();
            }

            await connection.OpenAsync();
            return await LoadSalaryReprocessEmployeeOptionsInternalAsync(connection, cityCode, subDepartmentId, currentUserId);
        }

        public async Task<SalaryVouchersViewModel> GetSalaryVouchersPageAsync(DateTime workingDate, string currentUserId, SalaryVouchersViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var model = existingModel ?? new SalaryVouchersViewModel();
            if (model.Year <= 0)
            {
                model.Year = workingDate.Year;
            }

            if (model.Month <= 0)
            {
                model.Month = workingDate.Month;
            }

            model.Years = BuildYearSelectList(workingDate);
            model.Zones = await BuildUserZoneSelectItemsAsync(connection, currentUserId, "Please Select", "00");
            model.Cities = await BuildUserCitySelectItemsAsync(connection, currentUserId, model.ZoneId, "Please Select", "0", includeAllCity: true);
            model.SubDepartments = await LoadSalaryVoucherSubDepartmentSelectItemsAsync(connection, model.CityCode, currentUserId);
            model.EmployeeOptions = await LoadSalaryVoucherEmployeeOptionsInternalAsync(connection, model.ZoneId, model.CityCode, model.SubDepartmentId, currentUserId);

            return model;
        }

        public async Task<IEnumerable<dynamic>> GetSalaryVoucherSubDepartmentsAsync(string cityCode, string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return Enumerable.Empty<dynamic>();
            }

            await connection.OpenAsync();
            return await LoadSalaryVoucherSubDepartmentsAsync(connection, cityCode, currentUserId);
        }

        public async Task<IEnumerable<SalaryProcessEmployeeOption>> GetSalaryVoucherEmployeesAsync(string zoneId, string cityCode, string subDepartmentId, string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return Enumerable.Empty<SalaryProcessEmployeeOption>();
            }

            await connection.OpenAsync();
            return await LoadSalaryVoucherEmployeeOptionsInternalAsync(connection, zoneId, cityCode, subDepartmentId, currentUserId);
        }

        public async Task<(bool success, string message)> GenerateSalaryVouchersAsync(SalaryVouchersViewModel model, string currentUserId)
        {
            var selectedEmployeeIds = NormalizeEmployeeIds(model.SelectedEmployeeIds);
            if (selectedEmployeeIds.Count == 0)
            {
                return (false, "Please select at least one employee.");
            }

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return (false, "Database error");
            }

            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                foreach (string employeeId in selectedEmployeeIds)
                {
                    await connection.ExecuteAsync(
                        $"{FinanceCatalog}CreateNewJV_Test",
                        new { empno = NormalizeEmployeeNumberForVoucherProcedure(employeeId) },
                        transaction,
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 300);
                }

                await transaction.CommitAsync();
                return (true, "Journal Vouchers created successfully for selected employees.");
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool success, string message)> ProcessSalaryReprocessAsync(SalaryReprocessViewModel model, string currentUserId)
        {
            if (model.Year <= 0 || model.Month <= 0)
            {
                return (false, "Year and month are required.");
            }

            if (string.IsNullOrWhiteSpace(model.CityCode) || model.CityCode == "0")
            {
                return (false, "Please Select City!");
            }

            if (string.IsNullOrWhiteSpace(model.SubDepartmentId) || model.SubDepartmentId == "0")
            {
                return (false, "Please Select Sub-Department!");
            }

            if (!model.BillingStatusConfirmed || !model.AttendanceStatusConfirmed || !model.CommissionStatusConfirmed || !model.OneTimeActivityConfirmed)
            {
                return (false, "Acknowledgment Failed!");
            }

            var selectedEmployeeIds = NormalizeEmployeeIds(model.SelectedEmployeeIds);
            if (selectedEmployeeIds.Count == 0)
            {
                return (false, "Please select at least one employee.");
            }

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return (false, "Database error");
            }

            await connection.OpenAsync();

            int hasCityAccess = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM lcs_user_location
                  WHERE userid = @UserId
                    AND city_code = @CityCode",
                new
                {
                    UserId = currentUserId,
                    CityCode = model.CityCode
                });

            if (hasCityAccess == 0)
            {
                return (false, "You are not allowed to process salary for the selected city.");
            }

            try
            {
                await EnsureProcessesOpenAsync(connection, model.Year, model.Month, model.CityCode);
            }
            catch (ArgumentException ex)
            {
                return (false, ex.Message);
            }

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                string userRole = (await GetSalaryProcessUserRoleAsync(connection, transaction as MySqlTransaction, currentUserId))?.Trim() ?? string.Empty;
                bool isExecutive = string.Equals(userRole, "023", StringComparison.Ordinal);
                DateTime salaryDate = GetSalaryProcessDate(model.Year, model.Month);
                var (fromDate, toDate) = GetPayrollPeriod(model.Year, model.Month);
                int station = await GetSalaryProcessStationAsync(connection, transaction as MySqlTransaction, model.CityCode);
                int location = await GetSalaryProcessLocationAsync(connection, transaction as MySqlTransaction, model.CityCode);
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

                string departmentName = await connection.ExecuteScalarAsync<string>(
                    @"SELECT FullName
                      FROM hr_subdepartment
                      WHERE SDID = @DepartmentId
                      LIMIT 1",
                    new { DepartmentId = model.SubDepartmentId },
                    transaction) ?? model.SubDepartmentId;

                DataTable allowanceCatalog = DAL.ExecuteDataTable(
                    connection,
                    CommandType.Text,
                    @"SELECT FullName, TYPE, Pct_Amount, Fix_Amount, exclude_absent, Code, glcode
                      FROM hr_allow_ded_details
                      WHERE EmpWise_Flag IN ('All', @DepartmentId)",
                    new MySqlParameter("@DepartmentId", model.SubDepartmentId));

                string employeeQuery = @"
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
                      AND he.EMP_NO IN @EmpNos
                      AND ed.DeptCode = @DepartmentId
                      AND he.IsExecutive = @IsExecutive
                      AND he.P_CITY_CODE = @CityCode
                      AND @SalaryDate BETWEEN ed.fromdate AND IFNULL(ed.todate,'2099-08-28')
                    ORDER BY ed.Code DESC";

                var employeeRows = (await connection.QueryAsync<SalaryProcessEmployeeRow>(
                    employeeQuery,
                    new
                    {
                        EmpNos = selectedEmployeeIds.ToArray(),
                        DepartmentId = model.SubDepartmentId,
                        IsExecutive = isExecutive ? 1 : 0,
                        CityCode = model.CityCode,
                        SalaryDate = salaryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    },
                    transaction))
                    .GroupBy(static row => row.EmpNo, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => group.First())
                    .ToList();

                if (employeeRows.Count != selectedEmployeeIds.Count)
                {
                    return (false, "Can not process selected employee salary.");
                }

                StateHelper.userid = currentUserId;
                StateHelper.user_role = userRole;
                DAL.ConnectionString = _connectionFactory.ConnectionString;

                int loanCodeSeed = await GetNextLoanDeductionSequenceAsync(connection, transaction as MySqlTransaction);
                int processedCount = 0;

                foreach (var employee in employeeRows)
                {
                    processedCount += await ReprocessSingleSalaryAsync(
                        connection,
                        transaction as MySqlTransaction ?? throw new InvalidOperationException("Transaction required."),
                        model,
                        currentUserId,
                        userRole,
                        isExecutive,
                        departmentName,
                        allowanceCatalog,
                        taxSlabList,
                        salaryDate,
                        fromDate,
                        toDate,
                        station,
                        location,
                        fuelAvrgPrice,
                        fuelAvrgPrice1,
                        employee,
                        loanCodeSeed);

                    loanCodeSeed = await GetNextLoanDeductionSequenceAsync(connection, transaction as MySqlTransaction);
                }

                await transaction.CommitAsync();
                return (true, $"{processedCount} Pay Slip(s) generated.");
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task<int> ReprocessSingleSalaryAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            SalaryReprocessViewModel model,
            string currentUserId,
            string userRole,
            bool isExecutive,
            string departmentName,
            DataTable allowanceCatalog,
            IReadOnlyCollection<TaxSlabList> taxSlabList,
            DateTime salaryDate,
            DateTime fromDate,
            DateTime toDate,
            int station,
            int location,
            decimal fuelAvrgPrice,
            decimal fuelAvrgPrice1,
            SalaryProcessEmployeeRow employee,
            int loanCodeSeed)
        {
            var existingHeader = await connection.QueryFirstOrDefaultAsync<SalaryReprocessHeaderState>(
                @"SELECT IsPaid, IsReprocessed
                  FROM hr_salaryprocessed_hdr
                  WHERE SalaryMonth = @Month
                    AND SalaryYear = @Year
                    AND Emp_No = @EmpNo
                  LIMIT 1",
                new
                {
                    Month = model.Month,
                    Year = model.Year,
                    EmpNo = employee.EmpNo
                },
                transaction);

            if (existingHeader?.IsPaid == true)
            {
                throw new ArgumentException("Salary has been Paid");
            }

            await DeleteSalaryReprocessLogDetailAsync(connection, transaction, model, employee.EmpNo, salaryDate);

            var processModel = new SalariesProcessViewModel
            {
                Year = model.Year,
                Month = model.Month,
                CityCode = model.CityCode,
                CommissionFilter = 0
            };

            var engine = new LegacySalaryProcessEngine(
                connection,
                transaction,
                processModel,
                currentUserId,
                userRole,
                model.SubDepartmentId,
                departmentName,
                new List<SalaryProcessEmployeeRow> { employee },
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
                model.CityCode);

            SalaryProcessPreparedData preparedData = engine.Execute();

            if (existingHeader != null)
            {
                await MaintainSalaryReprocessLogAsync(connection, transaction, model, employee.EmpNo);
                await CreateSalaryReversalVouchersAsync(connection, transaction, model, employee.EmpNo, existingHeader.IsReprocessed ? 1 : 0);
            }

            await DeleteSalaryReprocessRecordsAsync(connection, transaction, model, employee.EmpNo);

            var persistResult = await MasterDetailEntryAsync(preparedData.DataSet, connection, transaction);
            var salaryCfg = await SalaryConfig.LoadAsync(connection, transaction);
            await TaxCorrectAsync(preparedData.DataSet, connection, transaction, model.Year, model.Month, salaryCfg);

            if (existingHeader?.IsReprocessed == true)
            {
                await UpdateSalaryReprocessDifferenceAsync(connection, transaction, model, employee.EmpNo, currentUserId);
            }

            await InsertSalaryReprocessAcknowledgmentAsync(connection, transaction, currentUserId, model);
            return persistResult.HeaderRowsInserted;
        }

        private static List<SelectListItem> BuildYearSelectList(DateTime workingDate)
        {
            return Enumerable.Range(workingDate.Year - 5, 7)
                .Select(year => new SelectListItem
                {
                    Value = year.ToString(CultureInfo.InvariantCulture),
                    Text = year.ToString(CultureInfo.InvariantCulture)
                })
                .ToList();
        }

        private static List<string> NormalizeEmployeeIds(IEnumerable<string>? employeeIds)
        {
            return employeeIds?
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();
        }

        private static int NormalizeEmployeeNumberForVoucherProcedure(string empNo)
        {
            string normalized = (empNo ?? string.Empty).Trim().TrimStart('0');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = "0";
            }

            return int.Parse(normalized, CultureInfo.InvariantCulture);
        }

        private static async Task<List<SelectListItem>> BuildUserZoneSelectItemsAsync(
            MySqlConnection connection,
            string currentUserId,
            string placeholderText,
            string placeholderValue)
        {
            var zones = new List<SelectListItem> { new() { Value = placeholderValue, Text = placeholderText } };
            zones.AddRange((await connection.QueryAsync<SelectListItem>(
                @"SELECT DISTINCT z.Code AS Value, z.FullName AS Text
                  FROM hr_regionalzones z
                  INNER JOIN hr_city c ON c.RZoneCode = z.Code
                  INNER JOIN lcs_user_location ul ON ul.city_code = c.Code
                  WHERE ul.userid = @UserId
                  ORDER BY z.FullName ASC",
                new { UserId = currentUserId })).ToList());
            return zones;
        }

        private static async Task<List<SelectListItem>> BuildUserCitySelectItemsAsync(
            MySqlConnection connection,
            string currentUserId,
            string? zoneId,
            string placeholderText,
            string placeholderValue,
            bool includeAllCity)
        {
            var cities = new List<SelectListItem> { new() { Value = placeholderValue, Text = placeholderText } };
            if (includeAllCity)
            {
                cities.Add(new SelectListItem { Value = "ALL", Text = "All City" });
            }

            cities.AddRange((await connection.QueryAsync<SelectListItem>(
                @"SELECT c.Code AS Value, c.FullName AS Text
                  FROM hr_city c
                  INNER JOIN lcs_user_location ul ON ul.city_code = c.Code
                  WHERE ul.userid = @UserId
                    AND (@ZoneId = '00' OR c.RZoneCode = @ZoneId)
                  ORDER BY c.FullName ASC",
                new
                {
                    UserId = currentUserId,
                    ZoneId = string.IsNullOrWhiteSpace(zoneId) ? "00" : zoneId
                })).ToList());

            return cities;
        }

        private async Task<List<SelectListItem>> LoadSalaryReprocessSubDepartmentSelectItemsAsync(MySqlConnection connection, string cityCode, string currentUserId)
        {
            var items = new List<SelectListItem> { new() { Value = "0", Text = "Please Select" } };
            items.AddRange((await LoadSalaryReprocessSubDepartmentsAsync(connection, cityCode, currentUserId))
                .Select(record => new SelectListItem
                {
                    Value = Convert.ToString(record.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    Text = Convert.ToString(record.Text, CultureInfo.InvariantCulture) ?? string.Empty
                }));
            return items;
        }

        private async Task<IEnumerable<dynamic>> LoadSalaryReprocessSubDepartmentsAsync(MySqlConnection connection, string cityCode, string currentUserId)
        {
            if (string.IsNullOrWhiteSpace(cityCode) || cityCode == "0")
            {
                return Enumerable.Empty<dynamic>();
            }

            string userRole = (await connection.ExecuteScalarAsync<string>(
                @"SELECT user_role
                  FROM lcs_users
                  WHERE userid = @UserId
                  LIMIT 1",
                new { UserId = currentUserId }))?.Trim() ?? string.Empty;

            int isExecutive = string.Equals(userRole, "023", StringComparison.Ordinal) ? 1 : 0;

            return await connection.QueryAsync(
                @"SELECT sd.SDID AS Value, sd.FullName AS Text
                  FROM hr_subdepartment sd
                  INNER JOIN hr_employeedepartmentdetails dd ON dd.DeptCode = sd.SDID AND dd.ToDate IS NULL
                  INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = dd.Emp_No
                  WHERE p.LEFT_DATE IS NULL
                    AND p.IsExecutive = @IsExecutive
                    AND p.P_CITY_CODE = @CityCode
                  GROUP BY sd.SDID, sd.FullName
                  ORDER BY sd.FullName ASC",
                new
                {
                    IsExecutive = isExecutive,
                    CityCode = cityCode
                });
        }

        private async Task<List<SalaryProcessEmployeeOption>> LoadSalaryReprocessEmployeeOptionsInternalAsync(
            MySqlConnection connection,
            string cityCode,
            string subDepartmentId,
            string currentUserId)
        {
            if (string.IsNullOrWhiteSpace(cityCode) || cityCode == "0" || string.IsNullOrWhiteSpace(subDepartmentId) || subDepartmentId == "0")
            {
                return new List<SalaryProcessEmployeeOption>();
            }

            string userRole = (await connection.ExecuteScalarAsync<string>(
                @"SELECT user_role
                  FROM lcs_users
                  WHERE userid = @UserId
                  LIMIT 1",
                new { UserId = currentUserId }))?.Trim() ?? string.Empty;

            int isExecutive = string.Equals(userRole, "023", StringComparison.Ordinal) ? 1 : 0;

            return (await connection.QueryAsync<SalaryProcessEmployeeOption>(
                @"SELECT
                      empDetail.EMP_NO AS EmpNo,
                      CONCAT(empDetail.NAME, '-', empDetail.EMP_NO) AS DisplayName
                  FROM hr_employeepersonaldetail empDetail
                  INNER JOIN hr_employeedepartmentdetails empDept ON empDept.Emp_No = empDetail.EMP_NO
                  WHERE empDept.DeptCode = @SubDepartmentId
                    AND empDetail.LEFT_DATE IS NULL
                    AND empDept.ToDate IS NULL
                    AND empDetail.IsExecutive = @IsExecutive
                    AND empDetail.P_CITY_CODE = @CityCode
                  ORDER BY empDetail.EMP_NO",
                new
                {
                    SubDepartmentId = subDepartmentId,
                    IsExecutive = isExecutive,
                    CityCode = cityCode
                })).ToList();
        }

        private async Task<List<SelectListItem>> LoadSalaryVoucherSubDepartmentSelectItemsAsync(MySqlConnection connection, string cityCode, string currentUserId)
        {
            var items = new List<SelectListItem> { new() { Value = "0", Text = "Please Select" } };

            if (string.Equals(cityCode, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new SelectListItem { Value = "ALL", Text = "All Department" });
                return items;
            }

            items.AddRange((await LoadSalaryVoucherSubDepartmentsAsync(connection, cityCode, currentUserId))
                .Select(record => new SelectListItem
                {
                    Value = Convert.ToString(record.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    Text = Convert.ToString(record.Text, CultureInfo.InvariantCulture) ?? string.Empty
                }));

            return items;
        }

        private async Task<IEnumerable<dynamic>> LoadSalaryVoucherSubDepartmentsAsync(MySqlConnection connection, string cityCode, string currentUserId)
        {
            if (string.Equals(cityCode, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new { Value = "ALL", Text = "All Department" }
                };
            }

            if (string.IsNullOrWhiteSpace(cityCode) || cityCode == "0")
            {
                return Enumerable.Empty<dynamic>();
            }

            return await connection.QueryAsync(
                @"SELECT DISTINCT sd.SDID AS Value, sd.FullName AS Text
                  FROM hr_subdepartment sd
                  INNER JOIN hr_employeedepartmentdetails ed ON ed.DeptCode = sd.SDID
                  INNER JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = ed.Emp_No
                  WHERE epd.P_CITY_CODE = @CityCode
                    AND epd.left_date IS NULL
                    AND ed.ToDate IS NULL
                  ORDER BY sd.FullName ASC",
                new { CityCode = cityCode });
        }

        private async Task<List<SalaryProcessEmployeeOption>> LoadSalaryVoucherEmployeeOptionsInternalAsync(
            MySqlConnection connection,
            string zoneId,
            string cityCode,
            string subDepartmentId,
            string currentUserId)
        {
            string query;
            object parameters;

            if (string.Equals(cityCode, "ALL", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(subDepartmentId, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = @"
                    SELECT
                        empDetail.EMP_NO AS EmpNo,
                        CONCAT(empDetail.NAME, '-', empDetail.EMP_NO) AS DisplayName
                    FROM hr_employeepersonaldetail empDetail
                    INNER JOIN hr_employeedepartmentdetails empDept ON empDept.Emp_No = empDetail.EMP_NO
                    INNER JOIN hr_city city ON city.code = empDetail.P_CITY_CODE
                    WHERE empDetail.left_date IS NULL
                      AND empDept.ToDate IS NULL
                      AND city.RZoneCode = @ZoneId
                      AND empDept.DeptCode IN (
                          SELECT DISTINCT sd.SDID
                          FROM hr_subdepartment sd
                          INNER JOIN hr_employeedepartmentdetails ed ON ed.DeptCode = sd.SDID
                          INNER JOIN hr_employeepersonaldetail epd ON epd.EMP_NO = ed.Emp_No
                          WHERE epd.left_date IS NULL
                            AND ed.ToDate IS NULL
                      )
                    ORDER BY empDetail.EMP_NO";

                parameters = new { ZoneId = string.IsNullOrWhiteSpace(zoneId) ? "00" : zoneId };
            }
            else if (cityCode == "0")
            {
                query = @"
                    SELECT
                        empDetail.EMP_NO AS EmpNo,
                        CONCAT(empDetail.NAME, '-', empDetail.EMP_NO) AS DisplayName
                    FROM hr_employeepersonaldetail empDetail
                    INNER JOIN hr_employeedepartmentdetails empDept ON empDept.Emp_No = empDetail.EMP_NO
                    WHERE empDept.DeptCode = @SubDepartmentId
                      AND empDetail.left_date IS NULL
                      AND empDept.ToDate IS NULL
                    ORDER BY empDetail.EMP_NO";

                parameters = new { SubDepartmentId = subDepartmentId };
            }
            else if (subDepartmentId == "0")
            {
                query = @"
                    SELECT
                        empDetail.EMP_NO AS EmpNo,
                        CONCAT(empDetail.NAME, '-', empDetail.EMP_NO) AS DisplayName
                    FROM hr_employeepersonaldetail empDetail
                    INNER JOIN hr_employeedepartmentdetails empDept ON empDept.Emp_No = empDetail.EMP_NO
                    WHERE empDetail.P_CITY_CODE = @CityCode
                      AND empDetail.left_date IS NULL
                      AND empDept.ToDate IS NULL
                    ORDER BY empDetail.EMP_NO";

                parameters = new { CityCode = cityCode };
            }
            else if (!string.Equals(subDepartmentId, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                query = @"
                    SELECT
                        empDetail.EMP_NO AS EmpNo,
                        CONCAT(empDetail.NAME, '-', empDetail.EMP_NO) AS DisplayName
                    FROM hr_employeepersonaldetail empDetail
                    INNER JOIN hr_employeedepartmentdetails empDept ON empDept.Emp_No = empDetail.EMP_NO
                    WHERE empDept.DeptCode = @SubDepartmentId
                      AND empDetail.P_CITY_CODE = @CityCode
                      AND empDetail.left_date IS NULL
                      AND empDept.ToDate IS NULL
                    ORDER BY empDetail.EMP_NO";

                parameters = new
                {
                    SubDepartmentId = subDepartmentId,
                    CityCode = cityCode
                };
            }
            else if (!string.IsNullOrWhiteSpace(cityCode) && cityCode != "0")
            {
                query = @"
                    SELECT
                        empDetail.EMP_NO AS EmpNo,
                        CONCAT(empDetail.NAME, '-', empDetail.EMP_NO) AS DisplayName
                    FROM hr_employeepersonaldetail empDetail
                    INNER JOIN hr_employeedepartmentdetails empDept ON empDept.Emp_No = empDetail.EMP_NO
                    WHERE empDetail.P_CITY_CODE = @CityCode
                      AND empDetail.left_date IS NULL
                      AND empDept.ToDate IS NULL
                    ORDER BY empDetail.EMP_NO";

                parameters = new { CityCode = cityCode };
            }
            else
            {
                return new List<SalaryProcessEmployeeOption>();
            }

            return (await connection.QueryAsync<SalaryProcessEmployeeOption>(query, parameters)).ToList();
        }

        private static async Task DeleteSalaryReprocessRecordsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SalaryReprocessViewModel model,
            string employeeId)
        {
            await connection.ExecuteAsync(
                @"DELETE FROM hr_salaryprocessed_dtl
                  WHERE emp_no = @EmpNo
                    AND SalaryYear = @Year
                    AND SalaryMonth = @Month",
                new
                {
                    EmpNo = employeeId,
                    Year = model.Year,
                    Month = model.Month
                },
                transaction);

            await connection.ExecuteAsync(
                @"DELETE FROM hr_salaryprocessed_hdr
                  WHERE Emp_No = @EmpNo
                    AND SalaryYear = @Year
                    AND SalaryMonth = @Month",
                new
                {
                    EmpNo = employeeId,
                    Year = model.Year,
                    Month = model.Month
                },
                transaction);
        }

        private static async Task DeleteSalaryReprocessLogDetailAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SalaryReprocessViewModel model,
            string employeeId,
            DateTime salaryDate)
        {
            await connection.ExecuteAsync(
                @"DELETE FROM hr_Logsalaryprocessed_dtl
                  WHERE emp_no = @EmpNo
                    AND SalaryYear = @Year
                    AND SalaryMonth = @Month",
                new
                {
                    EmpNo = employeeId,
                    Year = model.Year,
                    Month = model.Month
                },
                transaction);

            await connection.ExecuteAsync(
                @"DELETE a
                  FROM hr_employeeloandeduction a
                  INNER JOIN hr_employeedepartmentdetails b ON a.emp_no = b.emp_no
                  WHERE b.emp_no = @EmpNo
                    AND @SalaryDate BETWEEN b.fromdate AND IFNULL(b.todate,'2099-08-28')
                    AND a.comments = @Comments",
                new
                {
                    EmpNo = employeeId,
                    SalaryDate = salaryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Comments = GetSalaryLoanComment(model.Year, model.Month)
                },
                transaction);
        }

        private static async Task<int> MaintainSalaryReprocessLogAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SalaryReprocessViewModel model,
            string employeeId)
        {
            int inserted = await connection.ExecuteAsync(
                @"INSERT INTO hr_Logsalaryprocessed_hdr
                  SELECT
                      SalaryYear, SalaryMonth, Emp_No, Dept, SalaryProcessedDate, BasicSalary, WorkedDays, currentsalary,
                      AbsentDays, RAbsentDays, Absent_amt, OT_Amount, PT_Amount, extra_hours, extra_hours_amt, extra_days,
                      extra_days_amt, extra_fuel, extra_fuel_amt, Extra_amount, Fuel_pday, Fuel_days, Fuel_Amount,
                      CommAmount, Allowances, deductions, Loan, loan_balance, Advance, Tax, GrossPay, Total_Deduction,
                      NetPay, CashPayment, amount_bank, amount_cash, Payment_Mode, Comments, CreatedBy, Created_Date,
                      UpdatedBy, Updated_Date,
                      (SELECT SUM(processcount) + 1
                       FROM (
                           SELECT COUNT(*) AS processcount
                           FROM hr_Logsalaryprocessed_hdr
                           WHERE SalaryYear = @Year
                             AND SalaryMonth = @Month
                             AND Emp_No = @EmpNo
                           UNION
                           SELECT 0 AS processcount
                       ) AS xb) AS processcount,
                      IsPrint, IsPaid, createdStation, CreatedLocation
                  FROM hr_salaryprocessed_hdr
                  WHERE SalaryYear = @Year
                    AND SalaryMonth = @Month
                    AND Emp_No = @EmpNo",
                new
                {
                    Year = model.Year,
                    Month = model.Month,
                    EmpNo = employeeId
                },
                transaction);

            inserted += await connection.ExecuteAsync(
                @"INSERT INTO hr_Logsalaryprocessed_dtl
                  SELECT *
                  FROM hr_salaryprocessed_dtl
                  WHERE SalaryMonth = @Month
                    AND SalaryYear = @Year
                    AND Emp_No = @EmpNo",
                new
                {
                    Year = model.Year,
                    Month = model.Month,
                    EmpNo = employeeId
                },
                transaction);

            return inserted;
        }

        private static async Task<int> UpdateSalaryReprocessDifferenceAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SalaryReprocessViewModel model,
            string employeeId,
            string currentUserId)
        {
            int updated = await connection.ExecuteAsync(
                @"UPDATE Diff_hr_salaryprocessed_hdr diff
                  JOIN hr_salaryprocessed_hdr hdr
                    ON hdr.Emp_No = diff.Emp_No
                   AND hdr.SalaryMonth = diff.SalaryMonth
                   AND diff.SalaryYear = hdr.SalaryYear
                  JOIN hr_Logsalaryprocessed_hdr log
                    ON hdr.Emp_No = log.Emp_No
                   AND log.SalaryMonth = hdr.SalaryMonth
                   AND hdr.SalaryYear = log.SalaryYear
                  SET diff.BasicSalary = diff.BasicSalary + hdr.BasicSalary - log.BasicSalary,
                      diff.WorkedDays = diff.WorkedDays + hdr.WorkedDays - log.WorkedDays,
                      diff.currentsalary = diff.currentsalary + log.currentsalary - hdr.currentsalary,
                      diff.AbsentDays = diff.AbsentDays + hdr.AbsentDays - log.AbsentDays,
                      diff.RAbsentDays = diff.RAbsentDays + hdr.RAbsentDays - log.RAbsentDays,
                      diff.Absent_amt = diff.Absent_amt + hdr.Absent_amt - log.Absent_amt,
                      diff.OT_Amount = diff.OT_Amount + hdr.OT_Amount - log.OT_Amount,
                      diff.PT_Amount = diff.PT_Amount + hdr.PT_Amount - log.PT_Amount,
                      diff.extra_hours = diff.extra_hours + hdr.extra_hours - log.extra_hours,
                      diff.extra_hours_amt = diff.extra_hours_amt + hdr.extra_hours_amt - log.extra_hours_amt,
                      diff.extra_days = diff.extra_days + hdr.extra_days - log.extra_days,
                      diff.extra_days_amt = diff.extra_days_amt + hdr.extra_days_amt - log.extra_days_amt,
                      diff.extra_fuel = diff.extra_fuel + hdr.extra_fuel - log.extra_fuel,
                      diff.extra_fuel_amt = diff.extra_fuel_amt + log.extra_fuel_amt - hdr.extra_fuel_amt,
                      diff.Extra_amount = diff.Extra_amount + hdr.Extra_amount - log.Extra_amount,
                      diff.Fuel_pday = diff.Fuel_pday + hdr.Fuel_pday - log.Fuel_pday,
                      diff.Fuel_days = diff.Fuel_days + hdr.Fuel_days - log.Fuel_days,
                      diff.Fuel_Amount = diff.Fuel_Amount + hdr.Fuel_Amount - log.Fuel_Amount,
                      diff.CommAmount = diff.CommAmount + hdr.CommAmount - log.CommAmount,
                      diff.Allowances = diff.Allowances + hdr.Allowances - log.Allowances,
                      diff.deductions = diff.deductions + hdr.deductions - log.deductions,
                      diff.Loan = diff.Loan + hdr.Loan - log.Loan,
                      diff.loan_balance = diff.loan_balance + hdr.loan_balance - log.loan_balance,
                      diff.Advance = diff.Advance + hdr.Advance - log.Advance,
                      diff.Tax = diff.Tax + hdr.Tax - log.Tax,
                      diff.GrossPay = diff.GrossPay + hdr.GrossPay - log.GrossPay,
                      diff.Total_Deduction = diff.Total_Deduction + hdr.Total_Deduction - log.Total_Deduction,
                      diff.NetPay = diff.NetPay + hdr.NetPay - log.NetPay,
                      diff.CashPayment = diff.CashPayment + hdr.CashPayment - log.CashPayment,
                      diff.amount_bank = diff.amount_bank + hdr.amount_bank - log.amount_bank,
                      diff.amount_cash = diff.amount_cash + hdr.amount_cash - log.amount_cash,
                      diff.Comments = 'Difference Added',
                      diff.UpdatedBy = @UpdatedBy,
                      diff.Updated_Date = NOW()
                  WHERE diff.SalaryYear = @Year
                    AND diff.SalaryMonth = @Month
                    AND diff.Emp_No = @EmpNo",
                new
                {
                    Year = model.Year,
                    Month = model.Month,
                    EmpNo = employeeId,
                    UpdatedBy = currentUserId
                },
                transaction);

            updated += await connection.ExecuteAsync(
                @"UPDATE Diff_hr_salaryprocessed_dtl diff
                  JOIN hr_salaryprocessed_dtl dtl
                    ON dtl.Emp_No = diff.Emp_No
                   AND dtl.SalaryMonth = diff.SalaryMonth
                   AND diff.SalaryYear = dtl.SalaryYear
                  LEFT JOIN hr_Logsalaryprocessed_dtl log
                    ON dtl.Emp_No = log.Emp_No
                   AND log.SalaryMonth = dtl.SalaryMonth
                   AND dtl.SalaryYear = log.SalaryYear
                   AND dtl.glcode = log.glcode
                   AND log.Description = dtl.Description
                  SET diff.Amount = diff.Amount + dtl.Amount - log.Amount,
                      diff.Allow_Code = dtl.Allow_Code,
                      diff.Description = dtl.Description,
                      diff.Deduction_Type = dtl.Deduction_Type
                  WHERE diff.SalaryMonth = @Month
                    AND diff.SalaryYear = @Year
                    AND diff.emp_no = @EmpNo",
                new
                {
                    Year = model.Year,
                    Month = model.Month,
                    EmpNo = employeeId
                },
                transaction);

            return updated;
        }

        private static async Task CreateSalaryReversalVouchersAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SalaryReprocessViewModel model,
            string employeeId,
            int isReprocessed)
        {
            var vouchers = await connection.QueryAsync<(int SalaryMonth, int SalaryYear, string Emp_No)>(
                @"SELECT SalaryMonth, SalaryYear, Emp_No
                  FROM hr_salaryprocessed_hdr
                  WHERE SalaryMonth = @Month
                    AND SalaryYear = @Year
                    AND Emp_No = @EmpNo",
                new
                {
                    Month = model.Month,
                    Year = model.Year,
                    EmpNo = employeeId
                },
                transaction);

            foreach (var voucher in vouchers)
            {
                await connection.ExecuteAsync(
                    $"{FinanceCatalog}CreateJvReversalForSalary",
                    new
                    {
                        smonth = voucher.SalaryMonth,
                        syear = voucher.SalaryYear,
                        empno = voucher.Emp_No,
                        isReprocessed
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300);
            }
        }

        private static async Task<int> InsertSalaryReprocessAcknowledgmentAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string currentUserId,
            SalaryReprocessViewModel model)
        {
            return await connection.ExecuteAsync(
                $@"INSERT INTO {AcknowledgmentTable}
                  VALUES (5, @UserId, NOW(), @Billing, @Attendance, @Commission, @OneTime)",
                new
                {
                    UserId = currentUserId,
                    Billing = model.BillingStatusConfirmed,
                    Attendance = model.AttendanceStatusConfirmed,
                    Commission = model.CommissionStatusConfirmed,
                    OneTime = model.OneTimeActivityConfirmed
                },
                transaction);
        }

        private sealed class SalaryReprocessHeaderState
        {
            public bool IsPaid { get; set; }
            public bool IsReprocessed { get; set; }
        }
    }
}
