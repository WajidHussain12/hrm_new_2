using System;
using System.Collections.Generic;
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
        public async Task<LeaveProcessViewModel> GetLeaveProcessPageAsync(DateTime workingDate, string currentUserId, LeaveProcessViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var model = existingModel ?? new LeaveProcessViewModel();
            if (model.Year <= 0)
            {
                model.Year = workingDate.Year;
            }

            model.Years = Enumerable.Range(workingDate.Year - 5, 7)
                .Select(year => new SelectListItem
                {
                    Value = year.ToString(CultureInfo.InvariantCulture),
                    Text = year.ToString(CultureInfo.InvariantCulture)
                })
                .ToList();

            model.Zones = new List<SelectListItem> { new() { Value = "00", Text = "All Zones" } };
            model.Zones.AddRange((await connection.QueryAsync<SelectListItem>(
                @"SELECT DISTINCT z.Code AS Value, z.FullName AS Text
                  FROM hr_regionalzones z
                  INNER JOIN hr_city c ON c.RZoneCode = z.Code
                  INNER JOIN lcs_user_location ul ON ul.city_code = c.Code
                  WHERE ul.userid = @UserId
                  ORDER BY z.FullName ASC",
                new { UserId = currentUserId })).ToList());

            model.Cities = new List<SelectListItem> { new() { Value = "00", Text = "All Cities" } };
            model.Cities.AddRange((await connection.QueryAsync<SelectListItem>(
                @"SELECT c.Code AS Value, c.FullName AS Text
                  FROM hr_city c
                  INNER JOIN lcs_user_location ul ON ul.city_code = c.Code
                  WHERE ul.userid = @UserId
                    AND (@ZoneId = '00' OR c.RZoneCode = @ZoneId)
                  ORDER BY c.FullName ASC",
                new
                {
                    UserId = currentUserId,
                    ZoneId = string.IsNullOrWhiteSpace(model.ZoneId) ? "00" : model.ZoneId
                })).ToList());

            model.Divisions = new List<SelectListItem> { new() { Value = "0", Text = "Please Select" } };
            model.Divisions.AddRange((await connection.QueryAsync<SelectListItem>(
                @"SELECT BUID AS Value, Name AS Text
                  FROM lcs_setup.businessunit
                  WHERE IsDeleted = 0
                  ORDER BY Name ASC")).ToList());

            model.Departments = new List<SelectListItem> { new() { Value = "0", Text = "Please select Department" } };
            if (!string.IsNullOrWhiteSpace(model.DivisionId) && model.DivisionId != "0")
            {
                model.Departments.AddRange((await connection.QueryAsync<SelectListItem>(
                    @"SELECT PDID AS Value, PDName AS Text
                      FROM hr_parentdepartment
                      WHERE IsDeleted = 0
                        AND BUID = @DivisionId
                      ORDER BY PDName ASC",
                    new { model.DivisionId })).ToList());
            }

            model.SubDepartments = new List<SelectListItem> { new() { Value = "0", Text = "All" } };
            if (!string.IsNullOrWhiteSpace(model.DepartmentId) && model.DepartmentId != "0")
            {
                model.SubDepartments.AddRange((await connection.QueryAsync<SelectListItem>(
                    @"SELECT SDID AS Value, FullName AS Text
                      FROM hr_subdepartment
                      WHERE IsDeleted = 0
                        AND ParentID = @DepartmentId
                      ORDER BY FullName ASC",
                    new { model.DepartmentId })).ToList());
            }

            if (string.IsNullOrWhiteSpace(model.Mode))
            {
                model.Mode = "Department";
            }

            return model;
        }

        public async Task<(bool success, string message)> ProcessLeaveEncashmentAsync(LeaveProcessViewModel model, string currentUserId)
        {
            if (model.Year <= 0)
            {
                return (false, "Year is required.");
            }

            var isEmployeeMode = string.Equals(model.Mode, "Employee", StringComparison.OrdinalIgnoreCase);
            if (isEmployeeMode && string.IsNullOrWhiteSpace(model.EmployeeCode))
            {
                return (false, "Employee is required.");
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
                var salaryCfg = await SalaryConfig.LoadAsync(connection, transaction);

                if (model.CityCode != "00")
                {
                    var hasCityAccess = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*)
                          FROM lcs_user_location
                          WHERE userid = @UserId
                            AND city_code = @CityCode",
                        new
                        {
                            UserId = currentUserId,
                            model.CityCode
                        },
                        transaction);

                    if (hasCityAccess == 0)
                    {
                        throw new ArgumentException("You are not allowed to process leave encashment for the selected city.");
                    }
                }

                var employees = await LoadLeaveEncashmentEmployeesAsync(connection, transaction, model, currentUserId, salaryCfg);
                if (employees.Count == 0)
                {
                    return (false, "No record found");
                }

                await DeleteExistingLeaveEncashmentAsync(connection, transaction, model, currentUserId);

                var processedRows = new List<LeaveEncashmentInsertRow>();
                foreach (var employee in employees)
                {
                    var row = await BuildLeaveEncashmentRowAsync(connection, transaction, model.Year, employee, salaryCfg);
                    if (row != null)
                    {
                        processedRows.Add(row);
                    }
                }

                if (processedRows.Count == 0)
                {
                    throw new ArgumentException("Error while inserting data in the database.");
                }

                var inserted = await connection.ExecuteAsync(
                    @"REPLACE INTO hr_leave_encashment
                      (City, Emp_No, Appoint_Date, Name, FatherName, Designation, NIC_NO, Department,
                       leaves, BasicSalary, GrossSalary, Leave_Encashment, Total_Amount, Mode, CreatedOn,
                       Comments, LEMonth, LEYear)
                      VALUES
                      (@City, @EmpNo, @AppointDate, @Name, @FatherName, @Designation, @NicNo, @Department,
                       @Leaves, @BasicSalary, @GrossSalary, @LeaveEncashment, @TotalAmount, @Mode, @CreatedOn,
                       @Comments, @LeMonth, @LeYear)",
                    processedRows,
                    transaction,
                    commandTimeout: 600);

                if (inserted <= 0)
                {
                    throw new ArgumentException("Error while inserting data in the database.");
                }

                await transaction.CommitAsync();
                return (true, $"{inserted} Records process ");
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

        private static async Task<List<LeaveProcessEmployee>> LoadLeaveEncashmentEmployeesAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            LeaveProcessViewModel model,
            string currentUserId,
            SalaryConfig salaryCfg)
        {
            int leaveYearStartMonth = salaryCfg.LeaveYearStartMonth;
            int leaveYearStartDay   = salaryCfg.LeaveYearStartDay;
            var fromDate = new DateTime(leaveYearStartMonth <= 6 ? model.Year : model.Year - 1, leaveYearStartMonth, leaveYearStartDay);
            var toDate   = fromDate.AddYears(1).AddDays(-1);

            var query = new StringBuilder(
                @"SELECT DISTINCT
                      rz.FullName AS Zone,
                      c.Code AS City,
                      p.EMP_NO AS EmpNo,
                      p.APPOINT_DATE AS AppointDate,
                      p.NAME AS Name,
                      p.F_NAME AS FatherName,
                      j.FullName AS Designation,
                      p.NIC_NO AS NicNo,
                      dept.FullName AS Department,
                      dept.SDID AS DepartmentCode,
                      IF(DAY(p.APPOINT_DATE) > 15,
                         TIMESTAMPDIFF(MONTH, p.APPOINT_DATE, @ToDate),
                         TIMESTAMPDIFF(MONTH, p.APPOINT_DATE, @ToDate) + 1) AS MonthCount
                  FROM hr_employeepersonaldetail p
                  INNER JOIN hr_city c ON c.Code = p.P_CITY_CODE
                  INNER JOIN hr_regionalzones rz ON rz.Code = c.RZoneCode
                  INNER JOIN hr_employeedepartmentdetails empd ON empd.Emp_No = p.EMP_NO AND empd.ToDate IS NULL
                  INNER JOIN hr_subdepartment dept ON dept.SDID = empd.DeptCode
                  INNER JOIN hr_parentdepartment pd ON pd.PDID = dept.ParentID
                  INNER JOIN hr_employeejobdetails ej ON ej.Emp_No = p.EMP_NO AND ej.EffectiveTo IS NULL
                  INNER JOIN hr_jobs j ON j.Code = ej.JobCode
                  WHERE p.LEFT_DATE IS NULL
                    AND p.EMPLOYEE_TYPE IN ('001','003')
                    AND p.JobTypeId = 1
                    AND p.IsConfirmed = 1");

            var parameters = new DynamicParameters();
            parameters.Add("@ToDate", toDate);

            if (string.Equals(model.Mode, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND p.EMP_NO = @EmpNo");
                parameters.Add("@EmpNo", model.EmployeeCode.Trim());
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(model.ZoneId) && model.ZoneId != "00")
                {
                    query.Append(" AND rz.Code = @ZoneId");
                    parameters.Add("@ZoneId", model.ZoneId);
                }

                if (!string.IsNullOrWhiteSpace(model.CityCode) && model.CityCode != "00")
                {
                    query.Append(" AND p.P_CITY_CODE = @CityCode");
                    parameters.Add("@CityCode", model.CityCode);
                }
                else
                {
                    query.Append(" AND p.P_CITY_CODE IN (SELECT city_code FROM lcs_user_location WHERE Userid = @UserId)");
                    parameters.Add("@UserId", currentUserId);
                }

                if (!string.IsNullOrWhiteSpace(model.DivisionId) && model.DivisionId != "0")
                {
                    query.Append(" AND pd.BUID = @DivisionId");
                    parameters.Add("@DivisionId", model.DivisionId);
                }

                if (!string.IsNullOrWhiteSpace(model.DepartmentId) && model.DepartmentId != "0")
                {
                    query.Append(" AND pd.PDID = @DepartmentId");
                    parameters.Add("@DepartmentId", model.DepartmentId);
                }

                if (!string.IsNullOrWhiteSpace(model.SubDepartmentId) && model.SubDepartmentId != "0")
                {
                    query.Append(" AND dept.SDID = @SubDepartmentId");
                    parameters.Add("@SubDepartmentId", model.SubDepartmentId);
                }
            }

            query.Append(" HAVING MonthCount > 5");
            return (await connection.QueryAsync<LeaveProcessEmployee>(query.ToString(), parameters, transaction)).ToList();
        }

        private static async Task DeleteExistingLeaveEncashmentAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            LeaveProcessViewModel model,
            string currentUserId)
        {
            var query = new StringBuilder(
                @"DELETE l
                  FROM hr_leave_encashment l
                  INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = l.Emp_No
                  INNER JOIN hr_employeedepartmentdetails pd ON pd.Emp_No = p.EMP_NO AND pd.ToDate IS NULL
                  INNER JOIN hr_subdepartment sd ON sd.SDID = pd.DeptCode
                  INNER JOIN hr_parentdepartment dp ON dp.PDID = sd.ParentID
                  INNER JOIN hr_city c ON c.Code = l.City
                  WHERE LEMonth = @Month
                    AND LEYear = @Year");

            var parameters = new DynamicParameters();
            parameters.Add("@Month", 7);
            parameters.Add("@Year", model.Year);

            if (string.Equals(model.Mode, "Employee", StringComparison.OrdinalIgnoreCase))
            {
                query.Append(" AND l.Emp_No = @EmpNo");
                parameters.Add("@EmpNo", model.EmployeeCode.Trim());
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(model.ZoneId) && model.ZoneId != "00")
                {
                    query.Append(" AND c.RZoneCode = @ZoneId");
                    parameters.Add("@ZoneId", model.ZoneId);
                }

                if (!string.IsNullOrWhiteSpace(model.CityCode) && model.CityCode != "00")
                {
                    query.Append(" AND c.Code = @CityCode");
                    parameters.Add("@CityCode", model.CityCode);
                }
                else
                {
                    query.Append(" AND c.Code IN (SELECT city_code FROM lcs_user_location WHERE Userid = @UserId)");
                    parameters.Add("@UserId", currentUserId);
                }

                if (!string.IsNullOrWhiteSpace(model.DivisionId) && model.DivisionId != "0")
                {
                    query.Append(" AND dp.BUID = @DivisionId");
                    parameters.Add("@DivisionId", model.DivisionId);
                }

                if (!string.IsNullOrWhiteSpace(model.DepartmentId) && model.DepartmentId != "0")
                {
                    query.Append(" AND sd.ParentID = @DepartmentId");
                    parameters.Add("@DepartmentId", model.DepartmentId);
                }

                if (!string.IsNullOrWhiteSpace(model.SubDepartmentId) && model.SubDepartmentId != "0")
                {
                    query.Append(" AND sd.SDID = @SubDepartmentId");
                    parameters.Add("@SubDepartmentId", model.SubDepartmentId);
                }
            }

            await connection.ExecuteAsync(query.ToString(), parameters, transaction);
        }

        private static async Task<LeaveEncashmentInsertRow?> BuildLeaveEncashmentRowAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            LeaveProcessEmployee employee,
            SalaryConfig salaryCfg)
        {
            int leaveYearStartMonth = salaryCfg.LeaveYearStartMonth;
            int leaveYearStartDay   = salaryCfg.LeaveYearStartDay;
            var fromDate = new DateTime(leaveYearStartMonth <= 6 ? year : year - 1, leaveYearStartMonth, leaveYearStartDay);
            var toDate   = fromDate.AddYears(1).AddDays(-1);

            var grossSalary = await connection.ExecuteScalarAsync<decimal?>(
                @"SELECT CASE
                      WHEN @EmpNo IN ('00000000038860', '00000000042558') THEN
                          (SELECT TotalFixedGross FROM hr_employeepersonaldetail WHERE emp_no = @EmpNo)
                      ELSE FixGrossSalary(@EmpNo)
                  END",
                new { employee.EmpNo },
                transaction);

            var currentSalary = await connection.ExecuteScalarAsync<decimal?>(
                @"SELECT newbasicsalary
                  FROM hr_employeepersonaldetail
                  WHERE emp_no = @EmpNo
                    AND left_date IS NULL",
                new { employee.EmpNo },
                transaction);

            if (!grossSalary.HasValue || !currentSalary.HasValue)
            {
                return null;
            }

            var leaveSummary = await connection.QuerySingleOrDefaultAsync<LeaveSummary>(
                @"SELECT
                      ls.Total_Leaves AS TotalLeaves,
                      IFNULL(ab.Availed_Leaves, 0) AS AvailedLeaves
                  FROM hr_leavestructure ls
                  LEFT JOIN
                  (
                      SELECT
                          elr.Emp_No,
                          elr.LeaveCode,
                          SUM(DATEDIFF(elr.LeaveToDate, elr.LeaveFromDate) + 1) AS Availed_Leaves
                      FROM hr_employeepersonaldetail emd
                      INNER JOIN hr_employeeleaverequest elr ON emd.EMP_NO = elr.Emp_No
                      INNER JOIN hr_employeedepartmentdetails em ON em.Emp_No = emd.EMP_NO AND em.ToDate IS NULL
                      INNER JOIN hr_subdepartment depart ON depart.SDID = em.DeptCode
                      WHERE elr.Emp_No = @EmpNo
                        AND elr.LeaveFromDate BETWEEN @FromDate AND @ToDate
                      GROUP BY elr.Emp_No, elr.LeaveCode
                  ) ab ON ab.LeaveCode = ls.Code
                  WHERE ls.Code = '001'",
                new
                {
                    employee.EmpNo,
                    FromDate = fromDate,
                    ToDate = toDate
                },
                transaction);

            if (leaveSummary == null)
            {
                return null;
            }

            var perDaySalary = currentSalary.Value / salaryCfg.SalaryDaysDivisor;
            var totalLeaves = leaveSummary.TotalLeaves;
            var balanceLeave = totalLeaves - leaveSummary.AvailedLeaves;
            var totalMonths = employee.MonthCount;

            var cashableLeaves = 0;
            if (grossSalary.Value >= salaryCfg.LeaveEncashmentGrossThreshold)
            {
                if (employee.AppointDate < fromDate)
                {
                    var quota = totalMonths < salaryCfg.LeaveMonthsMinForCashout ? 0 : totalLeaves / 2;
                    cashableLeaves = balanceLeave >= quota ? quota : balanceLeave;
                }
                else
                {
                    totalLeaves = totalMonths * 2;
                    balanceLeave = totalLeaves - leaveSummary.AvailedLeaves;
                    var quota = totalMonths < salaryCfg.LeaveMonthsMinForCashout ? 0 : totalLeaves / 2;
                    cashableLeaves = balanceLeave >= quota ? quota : balanceLeave;
                }
            }
            else
            {
                if (employee.AppointDate < fromDate)
                {
                    cashableLeaves = balanceLeave;
                }
                else
                {
                    totalLeaves = totalMonths * 2;
                    balanceLeave = totalLeaves - leaveSummary.AvailedLeaves;
                    cashableLeaves = balanceLeave;
                }
            }

            var leaveEncashmentAmount = cashableLeaves * perDaySalary;
            return new LeaveEncashmentInsertRow
            {
                City = employee.City,
                EmpNo = employee.EmpNo,
                AppointDate = employee.AppointDate,
                Name = employee.Name,
                FatherName = employee.FatherName,
                Designation = employee.Designation,
                NicNo = employee.NicNo,
                Department = employee.Department,
                Leaves = cashableLeaves,
                BasicSalary = currentSalary.Value,
                GrossSalary = grossSalary.Value,
                LeaveEncashment = leaveEncashmentAmount,
                TotalAmount = leaveEncashmentAmount,
                Mode = "C",
                CreatedOn = DateTime.Now,
                Comments = $"Leave Encashment of {fromDate:yyyy-MM-dd}-{toDate:yyyy-MM-dd}",
                LeMonth = 7,
                LeYear = year
            };
        }

        private sealed class LeaveProcessEmployee
        {
            public string Zone { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public string EmpNo { get; set; } = string.Empty;
            public DateTime AppointDate { get; set; }
            public string Name { get; set; } = string.Empty;
            public string FatherName { get; set; } = string.Empty;
            public string Designation { get; set; } = string.Empty;
            public string NicNo { get; set; } = string.Empty;
            public string Department { get; set; } = string.Empty;
            public string DepartmentCode { get; set; } = string.Empty;
            public int MonthCount { get; set; }
        }

        private sealed class LeaveSummary
        {
            public int TotalLeaves { get; set; }
            public int AvailedLeaves { get; set; }
        }

        private sealed class LeaveEncashmentInsertRow
        {
            public string City { get; set; } = string.Empty;
            public string EmpNo { get; set; } = string.Empty;
            public DateTime AppointDate { get; set; }
            public string Name { get; set; } = string.Empty;
            public string FatherName { get; set; } = string.Empty;
            public string Designation { get; set; } = string.Empty;
            public string NicNo { get; set; } = string.Empty;
            public string Department { get; set; } = string.Empty;
            public int Leaves { get; set; }
            public decimal BasicSalary { get; set; }
            public decimal GrossSalary { get; set; }
            public decimal LeaveEncashment { get; set; }
            public decimal TotalAmount { get; set; }
            public string Mode { get; set; } = string.Empty;
            public DateTime CreatedOn { get; set; }
            public string Comments { get; set; } = string.Empty;
            public int LeMonth { get; set; }
            public int LeYear { get; set; }
        }
    }
}
