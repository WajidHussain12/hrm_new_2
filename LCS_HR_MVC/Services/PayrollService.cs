using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Models.Payroll;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService : IPayrollService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IConfiguration? _configuration;
        private readonly ILogger<PayrollService>? _logger;
        private readonly bool _overrideSalaryGeneration;

        private static string QualifyHrTable(string tableName) => $"lcs_hr.{tableName}";

        private static string CashConsignmentsTable => QualifyHrTable(AcTestTableNames.T_CashConsignments);
        private static string VasIncentiveDetailTable => QualifyHrTable(AcTestTableNames.T_VasIncentiveDetail);
        private static string CodConsignmentsTable => QualifyHrTable(AcTestTableNames.T_CodConsignments);
        private static string AllCodConsignmentTable => QualifyHrTable(AcTestTableNames.T_AllCodConsignment);
        private static string CodReturnShipmentsTable => QualifyHrTable(AcTestTableNames.T_CodReturnShipments);
        private static string CodCommissionTable => QualifyHrTable(AcTestTableNames.T_CodCommission);
        private static string OleCommissionTable => QualifyHrTable(AcTestTableNames.T_OleCommission);
        private static string RbiIncentiveDetailTable => QualifyHrTable(AcTestTableNames.T_RbiIncentiveDetail);
        private static string OleCommissionProcessTable => QualifyHrTable(AcTestTableNames.T_OleCommissionProcess);
        private static string CodReturnConsignmentsTable => QualifyHrTable(AcTestTableNames.T_CodReturnConsignments);
        private static string CodReturnCommissionTable => QualifyHrTable(AcTestTableNames.T_CodReturnCommission);
        private static string CodReturnCommissionProcessTable => QualifyHrTable(AcTestTableNames.T_CodReturnCommissionProc);
        private static string CommissionProcessTable => QualifyHrTable(AcTestTableNames.T_CommissionProcess);
        private static string CommissionProcessTableName => AcTestTableNames.T_CommissionProcess;
        private static string EmpCommAdjustmentTable => QualifyHrTable(AcTestTableNames.T_EmpCommAdjDtl);
        private static string AcknowledgmentTable => QualifyHrTable(AcTestTableNames.T_Acknowledgment);

        private static string BuildProcessedByInfo(string? createdBy, string? userName)
        {
            if (!string.IsNullOrWhiteSpace(userName))
                return $" — By: {createdBy} ({userName})";
            if (!string.IsNullOrWhiteSpace(createdBy))
                return $" — By: {createdBy}";
            return "";
        }

        /// <summary>
        /// Logs the MySQL CONNECTION_ID() along with context for DB diagnostics.
        /// Enables correlation between application logs and MySQL processlist.
        /// </summary>
        private async Task<long> LogConnectionIdAsync(
            MySqlConnection connection,
            string commissionType,
            string cityCode,
            string operation)
        {
            long connectionId = 0;
            try
            {
                connectionId = await connection.ExecuteScalarAsync<long>("SELECT CONNECTION_ID();");
                _logger?.LogInformation(
                    "[DbDiag] ConnId={ConnectionId} | Type={CommissionType} | City={CityCode} | Op={Operation} | DB={Database} | Server={Server}",
                    connectionId, commissionType, cityCode, operation,
                    connection.Database, connection.DataSource);
            }
            catch
            {
                // Non-critical — do not let diagnostics fail the operation.
            }
            return connectionId;
        }

        /// <summary>
        /// Logs completion of a DB operation with duration and row count.
        /// </summary>
        private void LogOperationComplete(
            string commissionType,
            string cityCode,
            string operation,
            long connectionId,
            TimeSpan duration,
            int rowsAffected)
        {
            _logger?.LogInformation(
                "[DbDiag] ConnId={ConnectionId} | Type={CommissionType} | City={CityCode} | Op={Operation} | Duration={DurationSec:F2}s | Rows={RowsAffected}",
                connectionId, commissionType, cityCode, operation, duration.TotalSeconds, rowsAffected);
        }

        public PayrollService(
            IDbConnectionFactory connectionFactory,
            IConfiguration? configuration = null,
            ILogger<PayrollService>? logger = null)
        {
            _connectionFactory = connectionFactory;
            _configuration = configuration;
            _logger = logger;
            _overrideSalaryGeneration = configuration?.GetValue<bool>("OverrideSalaryGeneration") ?? false;
        }

        public async Task<(bool success, string message)> ProcessLeavesAsync(AttendanceProcessViewModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (false, "Database error");
                await connection.OpenAsync();

                try
                {
                    await EnsureProcessesOpenAsync(connection, model.Year, model.Month, model.CityCode);
                }
                catch (ArgumentException ex)
                {
                    return (false, ex.Message);
                }

                int noOfRecordsAffected = await ProcessLeaveAttendanceCleanupAsync(connection, model);
                return (true, $"{noOfRecordsAffected} Leave Records Processed Successfully!");
            }
        }

        public async Task<(bool success, string message, IEnumerable<dynamic> errorRows)> ProcessAttendanceAsync(AttendanceProcessViewModel model, string currentUserId)
        {
            var errorRows = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (false, "Database error", errorRows);
                await connection.OpenAsync();

                try
                {
                    await EnsureProcessesOpenAsync(connection, model.Year, model.Month, model.CityCode);
                }
                catch (ArgumentException ex)
                {
                    return (false, ex.Message, errorRows);
                }

                DAL.ConnectionString = _connectionFactory.ConnectionString;
                StateHelper.userid = currentUserId;

                try
                {
                    await ProcessLeaveAttendanceCleanupAsync(connection, model);
                    await DeleteAttendanceProcessRecordsAsync(connection, model.Year, model.Month, model.CityCode);

                    using var attendanceCalculator = new AttendanceCalculator(
                        model.Year,
                        model.Month,
                        LogsDataMode.CityWise,
                        model.CityCode.Trim(),
                        false,
                        connection);

                    var resultSet = attendanceCalculator.ProcessAttendance();
                    if (!ValidateAdjustments(resultSet, out var wrongAdjustments))
                    {
                        return (
                            false,
                            "Attendance Process failed. There are some wrong adjustments passed.",
                            ConvertDataTableToDynamicRows(wrongAdjustments));
                    }

                    int insertedRows = await InsertAttendanceProcessRowsAsync(connection, resultSet);
                    return (true, $"{insertedRows} record(s) processed successfully.", errorRows);
                }
                catch (ArgumentException ex)
                {
                    return (false, ex.Message, errorRows);
                }
            }
        }

        private async Task EnsureProcessesOpenAsync(MySqlConnection connection, int year, int month, string cityCode)
        {
            // In test mode: skip hr_closeprocesses guard entirely.
            // Test runs are always allowed — the target month may already be
            // closed in production, but INSERTs go to _AC_Test tables anyway.
            if (AcTestTableNames.IsTestMode) return;

            var normalizedCityCode = cityCode?.Trim();

            int? currentClosed = await connection.ExecuteScalarAsync<int?>(
                @"SELECT 1
                  FROM hr_closeprocesses hc
                  WHERE hc.City = @CityCode
                    AND hc.Year = @Year
                    AND hc.Month = @Month
                  LIMIT 1",
                new
                {
                    CityCode = normalizedCityCode,
                    Year = year,
                    Month = month
                });

            if (currentClosed.HasValue)
            {
                throw new ArgumentException("Process is closed.");
            }

            int previousMonth = month - 1;
            int previousYear = year;
            if (previousMonth == 0)
            {
                previousMonth = 12;
                previousYear -= 1;
            }

            int? previousClosed = await connection.ExecuteScalarAsync<int?>(
                @"SELECT 1
                  FROM hr_closeprocesses hc
                  WHERE hc.City = @CityCode
                    AND hc.Year = @Year
                    AND hc.Month = @Month
                  LIMIT 1",
                new
                {
                    CityCode = normalizedCityCode,
                    Year = previousYear,
                    Month = previousMonth
                });

            if (!previousClosed.HasValue)
            {
                throw new ArgumentException("You must close previous month first.");
            }
        }

        private async Task<int> ProcessLeaveAttendanceCleanupAsync(MySqlConnection connection, AttendanceProcessViewModel model)
        {
            var (fromDate, toDate) = GetPayrollPeriod(model.Year, model.Month);

            const string getQuery = @"
                SELECT
                    p.EMP_NO AS EmpNo,
                    DATE(lr.LeaveFromDate) AS LeaveFromDate,
                    DATE(lr.LeaveToDate) AS LeaveToDate
                FROM hr_employeepersonaldetail p
                INNER JOIN hr_employeeleaverequest lr ON p.EMP_NO = lr.Emp_No
                WHERE DATE(lr.LeaveFromDate) BETWEEN @FromDate AND @ToDate
                  AND p.LEFT_DATE IS NULL
                  AND p.P_CITY_CODE = @CityCode
                ORDER BY p.EMP_NO;";

            var leaveWindows = (await connection.QueryAsync<LeaveCleanupWindow>(getQuery, new
            {
                FromDate = fromDate,
                ToDate = toDate,
                model.CityCode
            })).ToList();

            int affectedRows = 0;
            foreach (var leaveWindow in leaveWindows)
            {
                affectedRows += await connection.ExecuteAsync(
                    "DELETE FROM hr_employeeattendance WHERE emp_no = @EmpNo AND DATE(CHECKTIME) BETWEEN @LeaveFromDate AND @LeaveToDate",
                    new { leaveWindow.EmpNo, leaveWindow.LeaveFromDate, leaveWindow.LeaveToDate });

                affectedRows += await connection.ExecuteAsync(
                    "DELETE FROM hr_employeeattandenceadjust WHERE emp_no = @EmpNo AND DATE(adjustmentDate) BETWEEN @LeaveFromDate AND @LeaveToDate",
                    new { leaveWindow.EmpNo, leaveWindow.LeaveFromDate, leaveWindow.LeaveToDate });

                affectedRows += await connection.ExecuteAsync(
                    "DELETE FROM hr_mobileappattendence WHERE EmpNo = @EmpNo AND DATE(CreationDate) BETWEEN @LeaveFromDate AND @LeaveToDate",
                    new { leaveWindow.EmpNo, leaveWindow.LeaveFromDate, leaveWindow.LeaveToDate });
            }

            return affectedRows;
        }

        private async Task DeleteAttendanceProcessRecordsAsync(MySqlConnection connection, int year, int month, string cityCode)
        {
            const string deleteQuery = @"
                DELETE FROM hr_employeeattendanceprocess
                WHERE month = @Month
                  AND year = @Year
                  AND city = @CityCode";

            await connection.ExecuteAsync(deleteQuery, new { Month = month, Year = year, CityCode = cityCode });
        }

        private async Task<int> InsertAttendanceProcessRowsAsync(MySqlConnection connection, DataTable resultSet)
        {
            const string insertQuery = @"
                INSERT INTO hr_employeeattendanceprocess
                (
                    Year,
                    Month,
                    emp_no,
                    city,
                    Absents,
                    Sundays,
                    Holidays,
                    Leaves,
                    Late,
                    HalfDay,
                    AvgHrs,
                    ruleAbsents,
                    Notout,
                    Early,
                    adjustmentLate,
                    adjustmentAbsent,
                    adjustmentRAbsent,
                    Total_Ex_Hrs,
                    Ext_Hrs,
                    Ext_Days,
                    Missing_TimeIN,
                    Consective_Late,
                    CreatedBy,
                    Created_Date
                )
                VALUES
                (
                    @Year,
                    @Month,
                    @EmpNo,
                    @City,
                    @Absents,
                    @Sundays,
                    @Holidays,
                    @Leaves,
                    @Late,
                    @HalfDay,
                    @AvgHrs,
                    @RuleAbsents,
                    @NotOut,
                    @Early,
                    @AdjustmentLate,
                    @AdjustmentAbsent,
                    @AdjustmentRAbsent,
                    @TotalExHrs,
                    @ExtHrs,
                    @ExtDays,
                    @MissingTimeIn,
                    @ConsecutiveLate,
                    @CreatedBy,
                    @CreatedDate
                );";

            int rowsInserted = 0;
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                foreach (DataRow row in resultSet.Rows)
                {
                    rowsInserted += await connection.ExecuteAsync(insertQuery, new
                    {
                        Year = GetRowValue(row, "Year"),
                        Month = GetRowValue(row, "Month"),
                        EmpNo = GetRowValue(row, "emp_no"),
                        City = GetRowValue(row, "city"),
                        Absents = GetRowValue(row, "Absents"),
                        Sundays = GetRowValue(row, "Sundays"),
                        Holidays = GetRowValue(row, "Holidays"),
                        Leaves = GetRowValue(row, "Leaves"),
                        Late = GetRowValue(row, "Late"),
                        HalfDay = GetRowValue(row, "HalfDay"),
                        AvgHrs = GetRowValue(row, "AvgHrs"),
                        RuleAbsents = GetRowValue(row, "ruleAbsents"),
                        NotOut = GetRowValue(row, "Notout"),
                        Early = GetRowValue(row, "Early"),
                        AdjustmentLate = GetRowValue(row, "adjustmentLate"),
                        AdjustmentAbsent = GetRowValue(row, "adjustmentAbsent"),
                        AdjustmentRAbsent = GetRowValue(row, "adjustmentRAbsent"),
                        TotalExHrs = GetRowValue(row, "Total_Ex_Hrs"),
                        ExtHrs = GetRowValue(row, "Ext_Hrs"),
                        ExtDays = GetRowValue(row, "Ext_Days"),
                        MissingTimeIn = GetRowValue(row, "Missing_TimeIN"),
                        ConsecutiveLate = GetRowValue(row, "Consective_Late"),
                        CreatedBy = GetRowValue(row, "CreatedBy"),
                        CreatedDate = GetRowValue(row, "Created_Date")
                    }, transaction);
                }

                await transaction.CommitAsync();
                return rowsInserted;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static bool ValidateAdjustments(DataTable resultSet, out DataTable wrongAdjustments)
        {
            wrongAdjustments = resultSet.Clone();
            wrongAdjustments.Rows.Clear();

            foreach (DataRow row in resultSet.Rows)
            {
                int absents = Convert.ToInt32(row["Absents"]);
                int adjustmentAbsent = Convert.ToInt32(row["adjustmentAbsent"]);
                int ruleAbsents = Convert.ToInt32(row["ruleAbsents"]);
                int adjustmentRuleAbsent = Convert.ToInt32(row["adjustmentRAbsent"]);

                if (adjustmentAbsent > absents || adjustmentRuleAbsent > ruleAbsents)
                {
                    wrongAdjustments.ImportRow(row);
                }
            }

            RemoveColumnIfExists(wrongAdjustments, "city");
            RemoveColumnIfExists(wrongAdjustments, "Sundays");
            RemoveColumnIfExists(wrongAdjustments, "Holidays");
            RemoveColumnIfExists(wrongAdjustments, "Late");
            RemoveColumnIfExists(wrongAdjustments, "Notout");
            RemoveColumnIfExists(wrongAdjustments, "Early");
            RemoveColumnIfExists(wrongAdjustments, "adjustmentLate");
            RemoveColumnIfExists(wrongAdjustments, "CreatedBy");
            RemoveColumnIfExists(wrongAdjustments, "Created_Date");

            if (wrongAdjustments.Rows.Count == 0)
            {
                return true;
            }

            wrongAdjustments.Columns.Add(new DataColumn("ExtraAbsAdjPassed"));
            wrongAdjustments.Columns.Add(new DataColumn("ExtraRA_AdjPassed"));

            foreach (DataRow row in wrongAdjustments.Rows)
            {
                int extraAbsent = Math.Max(
                    0,
                    Convert.ToInt32(row["adjustmentAbsent"]) - Convert.ToInt32(row["Absents"]));

                int extraRuleAbsent = Math.Max(
                    0,
                    Convert.ToInt32(row["adjustmentRAbsent"]) - Convert.ToInt32(row["ruleAbsents"]));

                row["ExtraAbsAdjPassed"] = extraAbsent;
                row["ExtraRA_AdjPassed"] = extraRuleAbsent;
            }

            wrongAdjustments.Columns["Year"].SetOrdinal(0);
            wrongAdjustments.Columns["Month"].SetOrdinal(1);
            wrongAdjustments.Columns["emp_no"].SetOrdinal(2);
            wrongAdjustments.Columns["Absents"].SetOrdinal(3);
            wrongAdjustments.Columns["adjustmentAbsent"].SetOrdinal(4);
            wrongAdjustments.Columns["ExtraAbsAdjPassed"].SetOrdinal(5);
            wrongAdjustments.Columns["ruleAbsents"].SetOrdinal(6);
            wrongAdjustments.Columns["adjustmentRAbsent"].SetOrdinal(7);
            wrongAdjustments.Columns["ExtraRA_AdjPassed"].SetOrdinal(8);

            return false;
        }

        private static void RemoveColumnIfExists(DataTable table, string columnName)
        {
            if (table.Columns.Contains(columnName))
            {
                table.Columns.Remove(columnName);
            }
        }

        private static List<dynamic> ConvertDataTableToDynamicRows(DataTable table)
        {
            var rows = new List<dynamic>();
            foreach (DataRow row in table.Rows)
            {
                var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (DataColumn column in table.Columns)
                {
                    item[column.ColumnName] = row[column] == DBNull.Value ? null : row[column];
                }

                rows.Add(item);
            }

            return rows;
        }

        private static object? GetRowValue(DataRow row, string columnName)
        {
            return row[columnName] == DBNull.Value ? null : row[columnName];
        }

        private static (DateTime fromDate, DateTime toDate) GetPayrollPeriod(int year, int month)
        {
            return month - 1 == 0
                ? (new DateTime(year - 1, 12, 26), new DateTime(year, month, 25))
                : (new DateTime(year, month - 1, 26), new DateTime(year, month, 25));
        }

        public async Task<(bool success, string message)> ProcessCommissionAsync(CommissionProcessViewModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null)
                {
                    return (false, "Database error");
                }

                await connection.OpenAsync();
                var cpConnId = await LogConnectionIdAsync(connection, "CommissionProcess", model.CityCode, "ProcessCommissionAsync_Start");
                var cpOverallStart = System.Diagnostics.Stopwatch.StartNew();

                await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

                // Advisory lock serializes concurrent runs for the same city+period.
                // Two processes (manual + automation) both pass the "already processed" check
                // before either inserts, causing duplicate rows. GET_LOCK ensures only one
                // proceeds at a time; the second will then see the first's rows and exit cleanly.
                var lockName = $"comm_proc_{model.CityCode}_{model.Year}_{model.Month}";
                var lockAcquired = await connection.ExecuteScalarAsync<int>(
                    "SELECT GET_LOCK(@LockName, 60)",
                    new { LockName = lockName });

                if (lockAcquired != 1)
                    return (false, "Another commission process is already running for this city/period. Please try again shortly.");

                try
                {
                    using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                    try
                    {
                        await ProcessCommissionInternalWithWatchdogAsync(
      connection,
      transaction,
      model.Year,
      model.Month,
      model.CityCode,
      currentUserId,
      model.BillingStatusConfirmed,
      model.AttendanceStatusConfirmed,
      model.AllCommissionTypesConfirmed);
                        await transaction.CommitAsync();
                        cpOverallStart.Stop();
                        LogOperationComplete("CommissionProcess", model.CityCode, "ProcessCommission_TOTAL", cpConnId, cpOverallStart.Elapsed, 0);
                        return (true, "Commission Process Execute Successfully!");
                    }
                    catch (ArgumentException ex)
                    {
                        try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                        return (false, ex.Message);
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                        throw;
                    }
                }
                finally
                {
                    try { await connection.ExecuteAsync("SELECT RELEASE_LOCK(@LockName)", new { LockName = lockName }); } catch { }
                }
            }
        }

        public async Task<(bool success, string message)> ProcessSalariesAsync(SalariesProcessViewModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (false, "Database error");
                await connection.OpenAsync();
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    SalaryProcessRunResult result = await ExecuteSalaryProcessAsync(connection, transaction, model, currentUserId);
                    await transaction.CommitAsync();
                    return (true, $"{result.MessageCount} Pay Slip(s) generated.");
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
        }

        private static (int isCommission, int isNonCommission) GetSalaryProcessFlags(int commissionFilter)
        {
            return commissionFilter switch
            {
                1 => (1, 0),
                2 => (0, 1),
                _ => (1, 1)
            };
        }

        private async Task<List<string>> GetAlreadyProcessedSalaryDepartmentsAsync(
            MySqlConnection connection,
            IReadOnlyCollection<string> selectedDepartments,
            int month,
            int year,
            string cityCode,
            int isCommission,
            int isNonCommission)
        {
            if (selectedDepartments.Count == 0)
            {
                return new List<string>();
            }

            var departmentNames = await connection.QueryAsync<string>(
                @"SELECT DISTINCT
                      COALESCE(NULLIF(TRIM(sd.FullName), ''), CAST(status.Dept AS CHAR)) AS DepartmentName
                  FROM hr_deptsalaryprocessstatus status
                  LEFT JOIN hr_subdepartment sd ON sd.SDID = status.Dept
                  WHERE status.Month = @Month
                    AND status.Year = @Year
                    AND status.cityID = @CityCode
                    AND status.Dept IN @SelectedDepartments
                    AND (@IsCommission = 0 OR status.IsCommission = @IsCommission)
                    AND (@IsNonCommission = 0 OR status.IsNonCommission = @IsNonCommission)
                  ORDER BY DepartmentName",
                new
                {
                    Month = month,
                    Year = year,
                    CityCode = cityCode?.Trim(),
                    SelectedDepartments = selectedDepartments.ToArray(),
                    IsCommission = isCommission,
                    IsNonCommission = isNonCommission
                });

            return departmentNames
                .Where(static departmentName => !string.IsNullOrWhiteSpace(departmentName))
                .Select(static departmentName => departmentName.Trim())
                .ToList();
        }

        public async Task<IEnumerable<BulkAttendanceAdjustmentGridRow>> GetAttendanceDaysForAdjustmentAsync(int year, int month, string empNo)
        {
            var result = new List<BulkAttendanceAdjustmentGridRow>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return result;
                await connection.OpenAsync();

                var (fromDate, toDate) = GetPayrollPeriod(year, month);

                var dates = new List<DateTime>();
                for (var date = fromDate; date <= toDate; date = date.AddDays(1))
                {
                    if (date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        dates.Add(date);
                    }
                }

                const string hQuery = "SELECT DATE(HolidayDate) FROM hr_setup_holidays WHERE DATE(HolidayDate) BETWEEN @From AND @To";
                var holidays = await connection.QueryAsync<DateTime>(hQuery, new { From = fromDate.ToString("yyyy-MM-dd"), To = toDate.ToString("yyyy-MM-dd") });
                dates.RemoveAll(d => holidays.Contains(d.Date));

                const string lQuery = "SELECT DATE(LeaveFromDate), DATE(LeaveToDate) FROM hr_employeeleaverequest WHERE Emp_No=@EmpNo AND Status='A' AND (LeaveFromDate <= @To AND LeaveToDate >= @From)";
                var leaves = await connection.QueryAsync(lQuery, new { EmpNo = empNo, From = fromDate.ToString("yyyy-MM-dd"), To = toDate.ToString("yyyy-MM-dd") });

                foreach (var leave in leaves)
                {
                    DateTime start = Convert.ToDateTime(leave.LeaveFromDate);
                    DateTime end = Convert.ToDateTime(leave.LeaveToDate);
                    dates.RemoveAll(d => d.Date >= start.Date && d.Date <= end.Date);
                }

                foreach (var date in dates)
                {
                    result.Add(new BulkAttendanceAdjustmentGridRow { Date = date, IsSelected = false, AdjustType = "A" });
                }
            }

            return result;
        }

        public async Task<(bool success, string message)> SaveBulkAttendanceAdjustmentAsync(BulkAttendanceAdjustmentModel model, List<BulkAttendanceAdjustmentGridRow> gridData, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (false, "Database error");
                await connection.OpenAsync();

                var (fromDate, toDate) = GetPayrollPeriod(model.Year, model.Month);

                const string cityQuery = "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo";
                string cityCode = await connection.ExecuteScalarAsync<string>(cityQuery, new { EmpNo = model.EmpNo });
                if (string.IsNullOrEmpty(cityCode)) return (false, "Employee City not found.");

                try
                {
                    await EnsureProcessesOpenAsync(connection, model.Year, model.Month, cityCode);
                }
                catch (ArgumentException ex)
                {
                    return (false, ex.Message);
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        if (model.AutoAdjustment)
                        {
                            await connection.ExecuteAsync(
                                "DELETE FROM hr_employeeattandenceadjust WHERE emp_no=@EmpNo AND adjustmentDate BETWEEN @From AND @To AND adjustmentType IN ('A','RA')",
                                new { EmpNo = model.EmpNo, From = fromDate, To = toDate },
                                transaction);

                            await transaction.CommitAsync();
                            return (true, "Auto Adjustments cleared/processed successfully.");
                        }

                        if (gridData == null || gridData.Count == 0) return (false, "No grid data to adjust.");

                        foreach (var row in gridData)
                        {
                            if (!row.IsSelected)
                            {
                                continue;
                            }

                            int exists = await connection.ExecuteScalarAsync<int>(
                                "SELECT COUNT(*) FROM hr_employeeattandenceadjust WHERE emp_no=@EmpNo AND adjustmentDate=@AdjDate",
                                new { EmpNo = model.EmpNo, AdjDate = row.Date },
                                transaction);

                            if (exists > 0)
                            {
                                throw new ArgumentException($"Adjustment Already Passed on {row.Date:dd/MM/yyyy}");
                            }

                            const string insertQuery = @"
                                INSERT INTO hr_employeeattandenceadjust
                                (
                                    emp_no,
                                    adjustmentDate,
                                    adjustmentType,
                                    year,
                                    month,
                                    reason,
                                    CreatedBy,
                                    Created_Date,
                                    UpdatedBy,
                                    Updated_Date
                                )
                                VALUES
                                (
                                    @EmpNo,
                                    @Date,
                                    @Type,
                                    @Year,
                                    @Month,
                                    'Bulk Adjustment',
                                    @UserId,
                                    NOW(),
                                    @UserId,
                                    NOW()
                                )";

                            int adjMonth = row.Date.Day >= 26
                                ? (model.Month - 1 == 0 ? 12 : model.Month - 1)
                                : model.Month;

                            int adjYear = row.Date.Day >= 26 && model.Month - 1 == 0
                                ? model.Year - 1
                                : model.Year;

                            await connection.ExecuteAsync(insertQuery, new
                            {
                                EmpNo = model.EmpNo,
                                Date = row.Date,
                                Type = row.AdjustType,
                                Year = adjYear,
                                Month = adjMonth,
                                UserId = currentUserId
                            }, transaction);
                        }

                        await transaction.CommitAsync();
                        return (true, "Logs Adjusted Successfully");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        return (false, ex.Message);
                    }
                }
            }
        }

        #region Helpers
        public async Task<IEnumerable<dynamic>> GetZonesAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync("SELECT Code as Value, FullName as Text FROM hr_regionalzones");
            }
        }

        public async Task<IEnumerable<dynamic>> GetCitiesByZoneAsync(string zoneId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync(
                    "SELECT Code as Value, FullName as Text FROM hr_city WHERE (@zoneId = '00' OR RZoneCode=@zoneId)",
                    new { zoneId });
            }
        }

        public async Task<IEnumerable<dynamic>> GetDivisionsAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync("SELECT BUID as Value, Name as Text FROM lcs_setup.businessunit WHERE IsDeleted=0 ORDER BY BUID DESC");
            }
        }

        public async Task<IEnumerable<dynamic>> GetDepartmentsByDivisionAsync(int buId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync(
                    "SELECT PDID as Value, PDName as Text FROM hr_parentdepartment WHERE BUID=@buId AND IsDeleted=0 ORDER BY PDName ASC",
                    new { buId });
            }
        }

        public async Task<IEnumerable<dynamic>> GetSubDepartmentsByDepartmentAsync(int departmentId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync("SELECT SDID as Value, FullName as Text FROM hr_subdepartment WHERE ParentID=@departmentId AND IsDeleted=0", new { departmentId });
            }
        }

        private sealed class LeaveCleanupWindow
        {
            public string EmpNo { get; set; } = string.Empty;
            public DateTime LeaveFromDate { get; set; }
            public DateTime LeaveToDate { get; set; }
        }
        #endregion
    }
}
