using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Text;
using LCS_HR_MVC.Models.Support;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class SupportService
    {
        public Task<AttendanceLogViewerViewModel> GetAttendanceLogViewerPageAsync(DateTime workingDate, AttendanceLogViewerViewModel? existingModel = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var selectedYear = existingModel?.Year ?? workingDate.Year;
            if (selectedYear < workingDate.Year - 5 || selectedYear > workingDate.Year)
            {
                selectedYear = workingDate.Year;
            }

            var maxMonth = selectedYear < workingDate.Year ? 12 : workingDate.Month;
            var selectedMonth = existingModel?.Month ?? workingDate.Month;
            if (selectedMonth < 1 || selectedMonth > maxMonth)
            {
                selectedMonth = Math.Min(workingDate.Month, maxMonth);
            }

            var model = existingModel ?? new AttendanceLogViewerViewModel();
            model.Year = selectedYear;
            model.Month = selectedMonth;
            model.WorkingYear = workingDate.Year;
            model.WorkingMonth = workingDate.Month;
            model.WorkingMonthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(workingDate.Month);
            model.SourceFilePath = GetAttendanceLogSourcePath();
            model.SourceFileExists = File.Exists(model.SourceFilePath);
            model.Years = Enumerable.Range(workingDate.Year - 5, 6)
                .Select(year => new SelectListItem(year.ToString(CultureInfo.InvariantCulture), year.ToString(CultureInfo.InvariantCulture)))
                .ToList();
            model.Months = Enumerable.Range(1, maxMonth)
                .Select(month => new SelectListItem(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month), month.ToString(CultureInfo.InvariantCulture)))
                .ToList();

            return Task.FromResult(model);
        }

        public async Task<IEnumerable<dynamic>> SearchAttendanceLogEmployeesAsync(string term, string currentUserId, CancellationToken cancellationToken = default)
        {
            var results = new List<dynamic>();
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return results;
            }

            await connection.OpenAsync(cancellationToken);

            const string query = @"
SELECT DISTINCT
    e.EMP_NO,
    e.NAME,
    COALESCE(dept.ShortName, '') AS DepartmentShortName
FROM hr_employeepersonaldetail e
INNER JOIN hr_employeedepartmentdetails deptDet
    ON e.EMP_NO = deptDet.Emp_No
INNER JOIN hr_subdepartment dept
    ON deptDet.DeptCode = dept.SDID
INNER JOIN lcs_user_location lul
    ON lul.city_code = e.P_CITY_CODE
WHERE deptDet.ToDate IS NULL
  AND e.EMP_STATUS <> 'I'
  AND lul.userid = @UserId
  AND (e.NAME LIKE @Term OR e.EMP_NO LIKE @Term)
ORDER BY e.NAME
LIMIT 100;";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@UserId", currentUserId);
            command.Parameters.AddWithValue("@Term", $"{term}%");

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var empNo = reader["EMP_NO"]?.ToString() ?? string.Empty;
                var employeeName = reader["NAME"]?.ToString() ?? string.Empty;
                var departmentShortName = reader["DepartmentShortName"]?.ToString() ?? string.Empty;
                var labelPrefix = string.IsNullOrWhiteSpace(departmentShortName) ? string.Empty : $"{departmentShortName}~";

                results.Add(new
                {
                    label = $"{labelPrefix}{employeeName} | {empNo}",
                    value = empNo,
                    desc = employeeName
                });
            }

            return results;
        }

        public async Task<SupportDownloadFile> ExportAttendanceLogAsync(AttendanceLogViewerViewModel model, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!OperatingSystem.IsWindows())
            {
                throw new ArgumentException("Attendance log export is only supported on Windows.");
            }

            if (string.IsNullOrWhiteSpace(model.EmpNo))
            {
                throw new ArgumentException("Employee is required.");
            }

            var sourcePath = GetAttendanceLogSourcePath();
            if (!File.Exists(sourcePath))
            {
                throw new ArgumentException($"Attendance log source file was not found at '{sourcePath}'.");
            }

            var attendanceId = await GetAttendanceEmployeeIdAsync(model.EmpNo.Trim(), cancellationToken);
            if (string.IsNullOrWhiteSpace(attendanceId))
            {
                throw new ArgumentException("Employee attendance ID not found.");
            }

            var accessConnectionString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={sourcePath};Persist Security Info=True";
            var rows = new List<DateTime>();

            try
            {
                using var connection = new OleDbConnection(accessConnectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
select CHECKTIME
from CHECKINOUT
where DateValue(CHECKTIME) >= ?
  and DateValue(CHECKTIME) <= ?
  and USERID = ?
order by CHECKTIME";

                var fromDate = new DateTime(model.Year, model.Month, 1).AddDays(-1);
                var toDate = new DateTime(model.Year, model.Month, DateTime.DaysInMonth(model.Year, model.Month));

                var fromParameter = command.Parameters.Add("@dateFrom", OleDbType.Date);
                fromParameter.Value = fromDate;

                var toParameter = command.Parameters.Add("@toDate", OleDbType.Date);
                toParameter.Value = toDate;

                var userParameter = command.Parameters.Add("@userId", OleDbType.BigInt);
                userParameter.Value = long.Parse(attendanceId, CultureInfo.InvariantCulture);

                using var reader = command.ExecuteReader();
                while (reader != null && reader.Read())
                {
                    if (reader[0] != DBNull.Value)
                    {
                        rows.Add(Convert.ToDateTime(reader[0], CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (FormatException)
            {
                throw new ArgumentException("Employee attendance ID is invalid.");
            }
            catch (OleDbException ex)
            {
                _logger.LogError(ex, "Error reading attendance log file {SourcePath}.", sourcePath);
                throw new ArgumentException("Unable to read attendance log source file.");
            }

            if (rows.Count == 0)
            {
                throw new ArgumentException("No attendance log found.");
            }

            var csv = new StringBuilder();
            csv.AppendLine("CHECKTIME");
            foreach (var row in rows)
            {
                csv.AppendLine(row.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }

            return new SupportDownloadFile
            {
                Content = Encoding.UTF8.GetBytes(csv.ToString()),
                FileName = $"AttendanceLog_{model.EmpNo.Trim()}_{model.Year}{model.Month:00}.csv",
                ContentType = "text/csv"
            };
        }

        private async Task<string> GetAttendanceEmployeeIdAsync(string empNo, CancellationToken cancellationToken)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return string.Empty;
            }

            await connection.OpenAsync(cancellationToken);

            const string query = @"
SELECT perDet.attandanceid
FROM hr_employeepersonaldetail perDet
WHERE perDet.EMP_NO = @EmpNo
LIMIT 1;";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@EmpNo", empNo);
            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result == null || result == DBNull.Value
                ? string.Empty
                : result.ToString() ?? string.Empty;
        }
    }
}
