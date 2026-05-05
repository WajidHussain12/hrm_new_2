using System.Globalization;
using System.Text;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class AttendanceManagementService : IAttendanceManagementService
    {
        private readonly IDbConnectionFactory _db;
        private readonly ILogger<AttendanceManagementService> _logger;

        public AttendanceManagementService(IDbConnectionFactory db, ILogger<AttendanceManagementService> logger)
        {
            _db     = db;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  MAIN PAGE — combined records + stats + dropdowns
        // ════════════════════════════════════════════════════════════════════════

        public async Task<AttendanceManagementViewModel> GetAttendancePageAsync(
            AttendanceManagementFilter filter,
            string userId,
            CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection() as MySqlConnection
                             ?? throw new InvalidOperationException("Cannot open DB connection.");
            await conn.OpenAsync(ct);

            var vm = new AttendanceManagementViewModel { Filter = filter };

            // ── Dropdowns ────────────────────────────────────────────────────
            vm.YearList  = BuildYearList(filter.Year);
            vm.MonthList = BuildMonthList(filter.Month);
            vm.CityList  = await BuildCityListAsync(conn, userId, filter.CityCode);
            vm.SourceList = new List<SelectListItem>
            {
                new("All Sources",    "",           string.IsNullOrEmpty(filter.AttSource)),
                new("Biometric",      "Biometric",  filter.AttSource == "Biometric"),
                new("Mobile App",     "Mobile App", filter.AttSource == "Mobile App"),
                new("Adjustment",     "Adjustment", filter.AttSource == "Adjustment"),
            };
            vm.StatusList = new List<SelectListItem>
            {
                new("All Statuses",    "", string.IsNullOrEmpty(filter.AttStatus)),
                new("Present",         "Present",  filter.AttStatus == "Present"),
                new("Absent",          "Absent",   filter.AttStatus == "Absent"),
                new("Work From Home",  "WFH",      filter.AttStatus == "WFH"),
                new("On Leave",        "Leave",    filter.AttStatus == "Leave"),
                new("Holiday",         "Holiday",  filter.AttStatus == "Holiday"),
                new("Weekend",         "Weekend",  filter.AttStatus == "Weekend"),
                new("Late",            "Late",     filter.AttStatus == "Late"),
            };

            // ── Build the combined attendance SQL ────────────────────────────
            var (dataSql, countSql, param) = BuildAttendanceSql(filter, userId);

            // count
            vm.TotalRecords = await conn.ExecuteScalarAsync<int>(countSql, param, commandTimeout: 120);

            if (vm.TotalRecords == 0)
            {
                vm.Stats = new AttendanceSummaryStats();
                return vm;
            }

            // paged records
            var rows = (await conn.QueryAsync<AttendanceRawRow>(dataSql, param, commandTimeout: 120)).ToList();

            vm.Records = rows.Select(MapRow).ToList();

            // ── Summary stats ────────────────────────────────────────────────
            vm.Stats = ComputeStats(vm.Records);

            return vm;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SQL BUILDER
        // ════════════════════════════════════════════════════════════════════════

        private static (string dataSql, string countSql, DynamicParameters param) BuildAttendanceSql(
            AttendanceManagementFilter f,
            string userId)
        {
            var p = new DynamicParameters();
            p.Add("@UserId", userId);
            p.Add("@Year",   f.Year);
            p.Add("@Month",  f.Month);

            // Optional per-employee or date-range override
            bool hasEmp      = !string.IsNullOrWhiteSpace(f.EmpNo);
            bool hasFromDate = f.FromDate.HasValue;
            bool hasToDate   = f.ToDate.HasValue;

            if (hasEmp)       p.Add("@EmpNo",    f.EmpNo!.Trim());
            if (hasFromDate)  p.Add("@FromDate",  f.FromDate!.Value.Date);
            if (hasToDate)    p.Add("@ToDate",    f.ToDate!.Value.Date);

            // Additional city filter
            bool hasCity = !string.IsNullOrWhiteSpace(f.CityCode);
            if (hasCity) p.Add("@CityCode", f.CityCode);

            // ── Core UNION subquery ──────────────────────────────────────────
            // Combines: (1) Biometric check-in/out  (2) Mobile-app punches
            //           (3) Manual adjustments
            // Then GROUPs by (emp_no, att_date) so a day with both a biometric
            // punch AND an adjustment appears as a single merged row.
            var coreSql = @"
            SELECT
                raw.emp_no,
                raw.att_date,
                COALESCE(
                    MAX(CASE WHEN raw.att_source = 'Biometric'  THEN raw.check_in  END),
                    MAX(CASE WHEN raw.att_source = 'Mobile App' THEN raw.check_in  END)
                ) AS check_in,
                COALESCE(
                    MAX(CASE WHEN raw.att_source = 'Biometric'  THEN raw.check_out END),
                    MAX(CASE WHEN raw.att_source = 'Mobile App' THEN raw.check_out END)
                ) AS check_out,
                CASE
                    WHEN MAX(CASE WHEN raw.att_source = 'Biometric'  THEN 1 ELSE 0 END) = 1 THEN 'Biometric'
                    WHEN MAX(CASE WHEN raw.att_source = 'Mobile App' THEN 1 ELSE 0 END) = 1 THEN 'Mobile App'
                    ELSE MAX(CASE WHEN raw.att_source NOT IN ('Biometric','Mobile App') THEN raw.att_source END)
                END AS att_source,
                MAX(raw.is_wfh)     AS is_wfh,
                MAX(raw.adj_type)   AS adj_type,
                MAX(raw.adj_reason) AS adj_reason,
                MAX(raw.lat)        AS lat,
                MAX(raw.lng)        AS lng
            FROM (
                /* ── Biometric (ZKTeco / thumb reader) ────────────────── */
                SELECT
                    a.emp_no,
                    DATE(a.CHECKTIME)                                            AS att_date,
                    TIME(MIN(CASE WHEN a.CHECKTYPE='I' THEN a.CHECKTIME END))    AS check_in,
                    TIME(MAX(CASE WHEN a.CHECKTYPE='O' THEN a.CHECKTIME END))    AS check_out,
                    'Biometric'                                                  AS att_source,
                    BIT_OR(IFNULL(a.IsAllowWFH, 0))                             AS is_wfh,
                    NULL                                                         AS adj_type,
                    NULL                                                         AS adj_reason,
                    MIN(CASE WHEN a.CHECKTYPE='I' THEN a.LATITUDE  END)          AS lat,
                    MIN(CASE WHEN a.CHECKTYPE='I' THEN a.LONGITUDE END)          AS lng
                FROM hr_employeeattendance a
                WHERE a.Month = @Month
                  AND a.Year  = @Year
                  AND a.Status <> 'D'
                GROUP BY a.emp_no, DATE(a.CHECKTIME)

                UNION ALL

                /* ── Mobile App (HumLeopard) ───────────────────────────── */
                /* CheckType column: 'I' = check-in, 'O' = check-out        */
                SELECT
                    m.EmpNo,
                    DATE(m.AttendenceDate),
                    TIME(MIN(CASE WHEN m.CheckType = 'I' THEN m.CreationDate END)) AS check_in,
                    TIME(MAX(CASE WHEN m.CheckType = 'O' THEN m.CreationDate END)) AS check_out,
                    'Mobile App',
                    MAX(CASE WHEN m.IsWFH = 1 THEN 1 ELSE 0 END),
                    NULL,
                    NULL,
                    MIN(CASE WHEN m.CheckType = 'I' AND m.Lat  REGEXP '^-?[0-9]+(\\.[0-9]+)?$' THEN CAST(m.Lat  AS DECIMAL(12,7)) ELSE NULL END),
                    MIN(CASE WHEN m.CheckType = 'I' AND m.Long REGEXP '^-?[0-9]+(\\.[0-9]+)?$' THEN CAST(m.Long AS DECIMAL(12,7)) ELSE NULL END)
                FROM hr_mobileappattendence m
                WHERE YEAR(m.AttendenceDate) = @Year
                  AND MONTH(m.AttendenceDate) = @Month
                GROUP BY m.EmpNo, DATE(m.AttendenceDate)

                UNION ALL

                /* ── Manual Adjustments ────────────────────────────────── */
                SELECT
                    adj.emp_no,
                    DATE(adj.adjustmentDate),
                    NULL,
                    NULL,
                    CASE adj.adjustmentType
                        WHEN 'A'  THEN 'Adjustment'
                        WHEN 'RA' THEN 'Adjustment'
                        WHEN 'L'  THEN 'Adjustment'
                        WHEN 'E'  THEN 'Adjustment'
                        ELSE           'Adjustment'
                    END,
                    0,
                    adj.adjustmentType,
                    adj.reason,
                    NULL,
                    NULL
                FROM hr_employeeattandenceadjust adj
                WHERE adj.year  = @Year
                  AND adj.month = @Month
            ) raw
            GROUP BY raw.emp_no, raw.att_date";

            // ── Outer select wraps in employee / city / leave / holiday ──────
            var outerWhere = new StringBuilder("WHERE p.EMP_STATUS <> 'I'");

            if (hasEmp)  outerWhere.Append(" AND r.emp_no = @EmpNo");
            if (hasCity) outerWhere.Append(" AND p.P_CITY_CODE = @CityCode");

            if (hasFromDate) outerWhere.Append(" AND r.att_date >= @FromDate");
            if (hasToDate)   outerWhere.Append(" AND r.att_date <= @ToDate");

            // source filter
            if (!string.IsNullOrWhiteSpace(f.AttSource))
            {
                p.Add("@AttSource", f.AttSource);
                outerWhere.Append(" AND r.att_source = @AttSource");
            }

            // status filter (translated to SQL-level conditions)
            if (!string.IsNullOrWhiteSpace(f.AttStatus))
            {
                outerWhere.Append(f.AttStatus switch
                {
                    "Present"  => " AND r.att_source IN ('Biometric','Mobile App') AND (r.adj_type IS NULL OR r.adj_type NOT IN ('A')) AND DAYOFWEEK(r.att_date) NOT IN (1,7) AND r.is_wfh = 0",
                    "WFH"      => " AND r.is_wfh = 1",
                    "Absent"   => " AND r.adj_type = 'A'",
                    "Leave"    => " AND lr.ELReq_No IS NOT NULL",
                    "Holiday"  => " AND h.fromdate IS NOT NULL",
                    "Weekend"  => " AND DAYOFWEEK(r.att_date) IN (1,7)",
                    "Late"     => " AND r.adj_type = 'L'",
                    _          => ""
                });
            }

            var outerSelectColumns = @"
                r.emp_no                                            AS EmpNo,
                p.NAME                                              AS EmpName,
                c.FullName                                          AS City,
                sd.FullName                                         AS Department,
                r.att_date                                          AS AttDate,
                DAYNAME(r.att_date)                                 AS DayName,
                r.check_in                                          AS CheckIn,
                r.check_out                                         AS CheckOut,
                r.att_source                                        AS AttSource,
                r.is_wfh                                            AS IsWFH,
                r.adj_type                                          AS AdjType,
                r.adj_reason                                        AS AdjReason,
                r.lat                                               AS Latitude,
                r.lng                                               AS Longitude,
                CASE WHEN DAYOFWEEK(r.att_date) IN (1,7) THEN 1 ELSE 0 END AS IsWeekend,
                CASE WHEN h.fromdate IS NOT NULL           THEN 1 ELSE 0 END AS IsHoliday,
                CASE WHEN lr.ELReq_No IS NOT NULL         THEN 1 ELSE 0 END AS IsLeave,
                lr.LeaveCode                                        AS LeaveCode,
                ls.FullName                                         AS LeaveCategory,
                lr.Reason                                           AS LeaveReason,
                lr.Status                                           AS LeaveStatus";

            var outerJoin = @"
            INNER JOIN hr_employeepersonaldetail p  ON p.EMP_NO     = r.emp_no
            INNER JOIN hr_city                   c  ON c.Code       = p.P_CITY_CODE
            INNER JOIN lcs_user_location         ul ON ul.city_code = p.P_CITY_CODE AND ul.userid = @UserId
            INNER JOIN hr_employeedepartmentdetails dd ON dd.Emp_No = r.emp_no AND dd.ToDate IS NULL
            INNER JOIN hr_subdepartment          sd ON sd.SDID      = dd.DeptCode
            LEFT  JOIN hr_gazetted_holidays      h  ON r.att_date BETWEEN DATE(h.fromdate) AND DATE(h.todate)
            LEFT  JOIN hr_employeeleaverequest   lr ON lr.Emp_No = r.emp_no
                                                   AND r.att_date BETWEEN DATE(lr.LeaveFromDate) AND DATE(lr.LeaveToDate)
                                                   AND lr.Status = 'A'
            LEFT  JOIN hr_leavestructure         ls ON ls.Code = lr.LeaveCode";

            int offset = (Math.Max(1, f.Page) - 1) * f.PageSize;
            p.Add("@PageSize", f.PageSize);
            p.Add("@Offset",   offset);

            var dataSql = $@"
                SELECT {outerSelectColumns}
                FROM ({coreSql}) r
                {outerJoin}
                {outerWhere}
                ORDER BY r.att_date DESC, p.NAME ASC
                LIMIT @PageSize OFFSET @Offset;";

            var countSql = $@"
                SELECT COUNT(*)
                FROM ({coreSql}) r
                INNER JOIN hr_employeepersonaldetail p  ON p.EMP_NO     = r.emp_no
                INNER JOIN hr_city                   c  ON c.Code       = p.P_CITY_CODE
                INNER JOIN lcs_user_location         ul ON ul.city_code = p.P_CITY_CODE AND ul.userid = @UserId
                INNER JOIN hr_employeedepartmentdetails dd ON dd.Emp_No = r.emp_no AND dd.ToDate IS NULL
                INNER JOIN hr_subdepartment          sd ON sd.SDID      = dd.DeptCode
                LEFT  JOIN hr_gazetted_holidays      h  ON r.att_date BETWEEN DATE(h.fromdate) AND DATE(h.todate)
                LEFT  JOIN hr_employeeleaverequest   lr ON lr.Emp_No = r.emp_no
                                                       AND r.att_date BETWEEN DATE(lr.LeaveFromDate) AND DATE(lr.LeaveToDate)
                                                       AND lr.Status = 'A'
                {outerWhere};";

            return (dataSql, countSql, p);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ROW MAPPING (Dapper raw → domain model)
        // ════════════════════════════════════════════════════════════════════════

        private static AttendanceDailyRow MapRow(AttendanceRawRow r)
        {
            return new AttendanceDailyRow
            {
                EmpNo         = r.EmpNo,
                EmpName       = r.EmpName,
                City          = r.City,
                Department    = r.Department,
                AttDate       = r.AttDate,
                DayName       = r.DayName,
                CheckIn       = r.CheckIn.HasValue  && r.CheckIn  != TimeSpan.Zero ? r.CheckIn  : null,
                CheckOut      = r.CheckOut.HasValue && r.CheckOut != TimeSpan.Zero ? r.CheckOut : null,
                AttSource     = r.AttSource ?? "None",
                IsWFH         = r.IsWFH != 0,
                IsWeekend     = r.IsWeekend != 0,
                IsHoliday     = r.IsHoliday != 0,
                IsLeave       = r.IsLeave != 0,
                LeaveCode     = r.LeaveCode,
                LeaveCategory = r.LeaveCategory,
                LeaveReason   = r.LeaveReason,
                LeaveStatus   = r.LeaveStatus,
                AdjType       = r.AdjType,
                AdjReason     = r.AdjReason,
                Latitude      = r.Latitude,
                Longitude     = r.Longitude,
            };
        }

        private static AttendanceSummaryStats ComputeStats(List<AttendanceDailyRow> rows)
        {
            var s = new AttendanceSummaryStats { TotalRecords = rows.Count };
            foreach (var r in rows)
            {
                switch (r.AttendanceStatus)
                {
                    case "Present":        s.TotalPresent++;  break;
                    case "Work From Home": s.TotalWFH++;      break;
                    case "Absent":         s.TotalAbsent++;   break;
                    case "On Leave":       s.TotalLeave++;    break;
                    case "Holiday":        s.TotalHoliday++;  break;
                    case "Weekend":        s.TotalWeekend++;  break;
                    case "Late":           s.TotalLate++;     break;
                }
                switch (r.AttSource)
                {
                    case "Biometric":  s.TotalBiometric++; break;
                    case "Mobile App": s.TotalMobile++;    break;
                    case "Adjustment": s.TotalManual++;    break;
                }
            }
            return s;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  EMPLOYEE SEARCH (autocomplete)
        // ════════════════════════════════════════════════════════════════════════

        public async Task<IEnumerable<dynamic>> SearchEmployeesAsync(
            string term, string userId, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection() as MySqlConnection
                             ?? throw new InvalidOperationException("Cannot open DB connection.");
            await conn.OpenAsync(ct);

            const string sql = @"
                SELECT DISTINCT e.EMP_NO, e.NAME
                FROM hr_employeepersonaldetail e
                INNER JOIN lcs_user_location ul ON ul.city_code = e.P_CITY_CODE
                WHERE ul.userid = @UserId
                  AND e.EMP_STATUS <> 'I'
                  AND (e.NAME LIKE @Term OR e.EMP_NO LIKE @Term)
                ORDER BY e.NAME
                LIMIT 50;";

            var rows = await conn.QueryAsync(sql, new { UserId = userId, Term = $"{term}%" });
            return rows.Select(r => new
            {
                label = $"{r.NAME} | {r.EMP_NO}",
                value = (string)r.EMP_NO,
                desc  = (string)r.NAME
            });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  CRUD — ADJUSTMENTS
        // ════════════════════════════════════════════════════════════════════════

        public async Task<AttendanceAdjustmentModel?> GetAdjustmentAsync(
            string empNo, DateTime date, CancellationToken ct = default)
        {
            using var conn = _db.CreateConnection() as MySqlConnection
                             ?? throw new InvalidOperationException("Cannot open DB connection.");
            await conn.OpenAsync(ct);

            const string sql = @"
                SELECT adj.emp_no AS EmpNo, p.NAME AS EmpName,
                       adj.adjustmentDate AS AdjustmentDate,
                       adj.adjustmentType AS AdjustmentType,
                       adj.reason         AS Reason
                FROM hr_employeeattandenceadjust adj
                INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = adj.emp_no
                WHERE adj.emp_no = @EmpNo AND DATE(adj.adjustmentDate) = DATE(@Date)
                LIMIT 1;";

            return await conn.QueryFirstOrDefaultAsync<AttendanceAdjustmentModel>(
                sql, new { EmpNo = empNo, Date = date });
        }

        public async Task<(bool success, string message)> AddAdjustmentAsync(
            AttendanceAdjustmentModel model, string userId, CancellationToken ct = default)
        {
            if (model.AdjustmentDate == null)
                return (false, "Adjustment date is required.");
            if (model.AdjustmentDate.Value.DayOfWeek == DayOfWeek.Sunday)
                return (false, "Adjustment cannot be created for Sunday.");

            using var conn = (MySqlConnection)_db.CreateConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // Check process closed
                if (await IsProcessClosedAsync(conn, tx, model.EmpNo, model.AdjustmentDate.Value))
                    return (false, "This month's processes are locked. Cannot add adjustment.");

                // Check duplicate
                var dup = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM hr_employeeattandenceadjust WHERE emp_no=@EmpNo AND DATE(adjustmentDate)=DATE(@Date)",
                    new { EmpNo = model.EmpNo, Date = model.AdjustmentDate.Value }, tx);
                if (dup > 0)
                    return (false, "An adjustment already exists for this employee on this date.");

                // Check employee exists
                var empExists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM hr_employeepersonaldetail WHERE emp_no=@EmpNo AND EMP_STATUS<>'I'",
                    new { EmpNo = model.EmpNo }, tx);
                if (empExists == 0)
                    return (false, "Employee not found.");

                await conn.ExecuteAsync(@"
                    INSERT INTO hr_employeeattandenceadjust
                        (emp_no, adjustmentDate, adjustmentType, year, month, reason, createdby, CreatedDate, updatedby, Updated_Date)
                    VALUES
                        (@EmpNo, @Date, @Type, @Year, @Month, @Reason, @UserId, NOW(), @UserId, NOW())",
                    new
                    {
                        EmpNo   = model.EmpNo,
                        Date    = model.AdjustmentDate.Value,
                        Type    = model.AdjustmentType,
                        Year    = model.AdjustmentDate.Value.Year,
                        Month   = model.AdjustmentDate.Value.Month,
                        Reason  = model.Reason ?? "",
                        UserId  = userId
                    }, tx);

                await tx.CommitAsync(ct);
                return (true, "Adjustment saved successfully.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string message)> UpdateAdjustmentAsync(
            AttendanceAdjustmentModel model, string userId, CancellationToken ct = default)
        {
            if (model.AdjustmentDate == null)
                return (false, "Adjustment date is required.");
            if (model.AdjustmentDate.Value.DayOfWeek == DayOfWeek.Sunday)
                return (false, "Adjustment cannot be on a Sunday.");

            using var conn = (MySqlConnection)_db.CreateConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                if (await IsProcessClosedAsync(conn, tx, model.EmpNo, model.AdjustmentDate.Value))
                    return (false, "This month's processes are locked. Cannot update adjustment.");

                var origDate = DateTime.Parse(model.OriginalDate ?? model.AdjustmentDate.Value.ToString("yyyy-MM-dd"));

                await conn.ExecuteAsync(@"
                    UPDATE hr_employeeattandenceadjust
                    SET adjustmentDate = @Date,
                        adjustmentType = @Type,
                        year           = @Year,
                        month          = @Month,
                        reason         = @Reason,
                        updatedby      = @UserId,
                        Updated_Date   = NOW()
                    WHERE emp_no = @EmpNo AND DATE(adjustmentDate) = DATE(@OrigDate)",
                    new
                    {
                        EmpNo    = model.EmpNo,
                        Date     = model.AdjustmentDate.Value,
                        Type     = model.AdjustmentType,
                        Year     = model.AdjustmentDate.Value.Year,
                        Month    = model.AdjustmentDate.Value.Month,
                        Reason   = model.Reason ?? "",
                        UserId   = userId,
                        OrigDate = origDate
                    }, tx);

                await tx.CommitAsync(ct);
                return (true, "Adjustment updated successfully.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string message)> DeleteAdjustmentAsync(
            string empNo, DateTime date, CancellationToken ct = default)
        {
            using var conn = (MySqlConnection)_db.CreateConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                if (await IsProcessClosedAsync(conn, tx, empNo, date))
                    return (false, "This month's processes are locked. Cannot delete adjustment.");

                await conn.ExecuteAsync(
                    "DELETE FROM hr_employeeattandenceadjust WHERE emp_no=@EmpNo AND DATE(adjustmentDate)=DATE(@Date)",
                    new { EmpNo = empNo, Date = date }, tx);

                await tx.CommitAsync(ct);
                return (true, "Adjustment deleted successfully.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                return (false, ex.Message);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  UPDATE CHECK TIME
        // ════════════════════════════════════════════════════════════════════════

        public async Task<(bool success, string message)> UpdateCheckTimeAsync(
            string empNo, DateTime date, string checkType, TimeSpan newTime,
            string source, string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(empNo))
                return (false, "Employee number is required.");
            if (checkType != "I" && checkType != "O")
                return (false, "Invalid check type. Must be 'I' (In) or 'O' (Out).");

            // Build new datetime = original date + new time
            var newDateTime = date.Date.Add(newTime);

            using var conn = (MySqlConnection)_db.CreateConnection();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                int affected = 0;

                if (string.Equals(source, "Biometric", StringComparison.OrdinalIgnoreCase))
                {
                    // hr_employeeattendance: update the CHECKTIME for matching CHECKTYPE rows
                    affected = await conn.ExecuteAsync(@"
                        UPDATE hr_employeeattendance
                        SET    CHECKTIME = @NewDateTime
                        WHERE  emp_no    = @EmpNo
                          AND  DATE(CHECKTIME) = @Date
                          AND  CHECKTYPE = @CheckType
                          AND  Status   <> 'D'",
                        new { NewDateTime = newDateTime, EmpNo = empNo, Date = date.Date, CheckType = checkType },
                        tx);
                }
                else if (string.Equals(source, "Mobile App", StringComparison.OrdinalIgnoreCase))
                {
                    // hr_mobileappattendence: update CreationDate for matching CheckType row
                    affected = await conn.ExecuteAsync(@"
                        UPDATE hr_mobileappattendence
                        SET    CreationDate   = @NewDateTime
                        WHERE  EmpNo          = @EmpNo
                          AND  DATE(AttendenceDate) = @Date
                          AND  CheckType      = @CheckType",
                        new { NewDateTime = newDateTime, EmpNo = empNo, Date = date.Date, CheckType = checkType },
                        tx);
                }
                else
                {
                    return (false, $"Cannot update time for source '{source}'.");
                }

                if (affected == 0)
                    return (false, "No matching record found to update.");

                await tx.CommitAsync(ct);
                return (true, $"Check-{(checkType == "I" ? "In" : "Out")} time updated to {newTime:hh\\:mm}.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                return (false, ex.Message);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════

        private static async Task<bool> IsProcessClosedAsync(
            MySqlConnection conn, MySqlTransaction tx, string empNo, DateTime date)
        {
            var city = await conn.ExecuteScalarAsync<string?>(
                "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE emp_no=@EmpNo LIMIT 1",
                new { EmpNo = empNo }, tx);
            if (string.IsNullOrEmpty(city)) return false;

            var cnt = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM hr_closeprocesses WHERE City=@City AND Year=@Year AND Month=@Month",
                new { City = city, date.Year, date.Month }, tx);
            return cnt > 0;
        }

        private static List<SelectListItem> BuildYearList(int selectedYear)
        {
            int now = DateTime.Now.Year;
            return Enumerable.Range(now - 5, 7)
                .Select(y => new SelectListItem(y.ToString(), y.ToString(), y == selectedYear))
                .ToList();
        }

        private static List<SelectListItem> BuildMonthList(int selectedMonth)
        {
            return Enumerable.Range(1, 12)
                .Select(m => new SelectListItem(
                    CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m),
                    m.ToString(),
                    m == selectedMonth))
                .ToList();
        }

        private static async Task<List<SelectListItem>> BuildCityListAsync(
            MySqlConnection conn, string userId, string? selected)
        {
            const string sql = @"
                SELECT c.Code AS Value, c.FullName AS Text
                FROM hr_city c
                INNER JOIN lcs_user_location ul ON ul.city_code = c.Code
                WHERE ul.userid = @UserId
                ORDER BY c.FullName;";

            var cities = (await conn.QueryAsync<SelectListItem>(sql, new { UserId = userId })).ToList();
            cities.Insert(0, new SelectListItem("All Cities", ""));
            foreach (var item in cities)
                item.Selected = item.Value == selected;
            return cities;
        }

        // ── Dapper intermediate mapping class ────────────────────────────────────
        private sealed class AttendanceRawRow
        {
            public string    EmpNo         { get; set; } = "";
            public string    EmpName       { get; set; } = "";
            public string    City          { get; set; } = "";
            public string    Department    { get; set; } = "";
            public DateTime  AttDate       { get; set; }
            public string    DayName       { get; set; } = "";
            public TimeSpan? CheckIn       { get; set; }
            public TimeSpan? CheckOut      { get; set; }
            public string?   AttSource     { get; set; }
            public int       IsWFH         { get; set; }
            public int       IsWeekend     { get; set; }
            public int       IsHoliday     { get; set; }
            public int       IsLeave       { get; set; }
            public string?   LeaveCode     { get; set; }
            public string?   LeaveCategory { get; set; }
            public string?   LeaveReason   { get; set; }
            public string?   LeaveStatus   { get; set; }
            public string?   AdjType       { get; set; }
            public string?   AdjReason     { get; set; }
            public double?   Latitude      { get; set; }
            public double?   Longitude     { get; set; }
        }
    }
}
