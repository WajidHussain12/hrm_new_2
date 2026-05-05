using System.Text;
using Dapper;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Data
{
    /// <summary>
    /// Builds filtered row-count comparisons between real and _AC_Test tables.
    /// Each table has its own city/year/month column mapping.
    /// All queries wrapped in try/catch — falls back to total COUNT(*) if
    /// the expected column does not exist.
    /// </summary>
    public class AcTestComparisonService
    {
        private readonly string _connectionString;
        private readonly ILogger<AcTestComparisonService> _logger;

        // ── Per-table query spec ───────────────────────────────────────────────
        private sealed record TableQuerySpec(
            string  RealTable,
            string  Group,
            string? CityCol,          // direct city column name, or null if StationJoinCol
            string? YearExpr,         // "Year" or "YEAR(billing_date)"
            string? MonthExpr,        // "Month" or "MONTH(billing_date)"
            string? RouteColName    = null, // column for optional route filter
            string? EmpNoColName    = null, // column for optional emp_no filter
            string? StationJoinCol  = null  // use hr_station subquery for city (e.g. "StationId")
        );

        private static readonly TableQuerySpec[] _specs =
        {
            // ── Cash ─────────────────────────────────────────────────────────
            new(AcTestTableNames.CashConsignments,
                "Cash",
                CityCol:       "station_city_code",
                YearExpr:      "YEAR(billing_date)",
                MonthExpr:     "MONTH(billing_date)",
                RouteColName:  "cour_id"),

            new(AcTestTableNames.VasIncentiveDetail,
                "Cash",
                CityCol:   "city_code",
                YearExpr:  "YEAR(created_date)",
                MonthExpr: "MONTH(created_date)"),

            // ── COD ──────────────────────────────────────────────────────────
            new(AcTestTableNames.CodConsignments,
                "COD",
                CityCol:      "Arivl_Dest",
                YearExpr:     "Cyear",
                MonthExpr:    "CMonth",
                RouteColName: "COURIER_ID"),

            new(AcTestTableNames.AllCodConsignment,
                "COD",
                CityCol:   "Arivl_Dest",
                YearExpr:  "YEAR(arrival_date)",
                MonthExpr: "MONTH(arrival_date)"),

            new(AcTestTableNames.CodReturnShipments,
                "COD",
                CityCol:        null,
                YearExpr:       "ComYear",
                MonthExpr:      "ComMonth",
                StationJoinCol: "StationId"),

            new(AcTestTableNames.CodCommission,
                "COD",
                CityCol:        null,
                YearExpr:       "CYear",
                MonthExpr:      "CMonth",
                RouteColName:   "COURIER_ID",
                StationJoinCol: "StationId"),

            // ── OverLand ──────────────────────────────────────────────────────
            new(AcTestTableNames.OleCommission,
                "OverLand",
                CityCol:      "citycode",
                YearExpr:     "Year",
                MonthExpr:    "Month",
                RouteColName: "CourierId"),

            new(AcTestTableNames.RbiIncentiveDetail,
                "OverLand",
                CityCol:      "CityCode",
                YearExpr:     "Year",
                MonthExpr:    "Month",
                RouteColName: "CourierId"),

            new(AcTestTableNames.OleCommissionProcess,
                "OverLand",
                CityCol:   "CityCode",
                YearExpr:  "Year",
                MonthExpr: "Month"),

            // ── ReturnCOD ─────────────────────────────────────────────────────
            new(AcTestTableNames.CodReturnConsignments,
                "ReturnCOD",
                CityCol:      "Arivl_Dest",
                YearExpr:     "YEAR(arrival_date)",
                MonthExpr:    "MONTH(arrival_date)",
                RouteColName: "COURIER_ID"),

            new(AcTestTableNames.CodReturnCommission,
                "ReturnCOD",
                CityCol:        null,
                YearExpr:       "ComYear",
                MonthExpr:      "ComMonth",
                RouteColName:   "COURIER_ID",
                StationJoinCol: "StationId"),

            new(AcTestTableNames.CodReturnCommissionProc,
                "ReturnCOD",
                CityCol:   "CityCode",
                YearExpr:  "Year",
                MonthExpr: "Month"),

            // ── Master ────────────────────────────────────────────────────────
            new(AcTestTableNames.CommissionProcess,
                "Master",
                CityCol:      "citycode",
                YearExpr:     "year",
                MonthExpr:    "month",
                EmpNoColName: "emp_no"),

            new(AcTestTableNames.EmpCommAdjDtl,
                "Master",
                CityCol:      "City_Code",
                YearExpr:     "Year",
                MonthExpr:    "Month",
                EmpNoColName: "Emp_No"),

            // ── Log ───────────────────────────────────────────────────────────
            new(AcTestTableNames.Acknowledgment,
                "Log",
                CityCol:   "CityCode",
                YearExpr:  "Year",
                MonthExpr: "Month"),
        };

        public AcTestComparisonService(
            IConfiguration configuration,
            ILogger<AcTestComparisonService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection not configured.");
            _logger = logger;
        }

        // ── Public: filtered row counts ───────────────────────────────────────

        public async Task<List<AcTestTableComparisonRow>> GetComparisonAsync(
            AcTestFilterRequest filter)
        {
            // Do NOT run queries without required filters (city is mandatory)
            if (!filter.HasRequiredFilters)
                return new();

            var result = new List<AcTestTableComparisonRow>();

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            // Apply CommissionType group filter (server-side)
            var specsToQuery = (filter.CommissionType is null or "All")
                ? _specs
                : _specs.Where(s => s.Group == filter.CommissionType).ToArray();

            foreach (var spec in specsToQuery)
            {
                var testTable = spec.RealTable + AcTestTableNames.TestSuffix;
                var row = new AcTestTableComparisonRow
                {
                    RealTable       = spec.RealTable,
                    TestTable       = testTable,
                    CommissionGroup = spec.Group,
                };

                row.RealExists = await TableExistsAsync(conn, spec.RealTable);
                row.TestExists = await TableExistsAsync(conn, testTable);

                var (whereClause, param) = BuildWhere(spec, filter);

                // Real table count
                if (row.RealExists)
                {
                    var (c, isFiltered) = await CountSafeAsync(
                        conn, spec.RealTable, whereClause, param);
                    row.RealRows   = c;
                    row.IsFiltered = isFiltered;
                }

                // Test table count (identical WHERE, different table name)
                if (row.TestExists)
                {
                    var (c, _) = await CountSafeAsync(
                        conn, testTable, whereClause, param);
                    row.TestRows = c;
                }

                result.Add(row);
            }

            return result;
        }

        /// <summary>
        /// Returns distinct city codes + names from hr_city.
        /// Tries multiple common column name patterns for city name.
        /// </summary>
        public async Task<List<(string Code, string Name)>> GetCitiesAsync()
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            // Try common name column patterns in order
            var nameColCandidates = new[] { "Description", "CityName", "Name", "City_Name" };

            foreach (var nameCol in nameColCandidates)
            {
                try
                {
                    var sql = $"SELECT Code AS C, `{nameCol}` AS N " +
                              "FROM lcs_hr.hr_city " +
                              "WHERE Code IS NOT NULL AND Code != '' " +
                              "ORDER BY Code";
                    var rows = await conn.QueryAsync(sql);
                    var list = rows
                        .Select(r => (
                            Code: (string)(r.C ?? ""),
                            Name: (string)(r.N ?? "")))
                        .Where(x => !string.IsNullOrEmpty(x.Code))
                        .ToList();
                    if (list.Count > 0)
                    {
                        _logger.LogDebug(
                            "[ACTest] hr_city name column resolved: {Col}", nameCol);
                        return list;
                    }
                }
                catch { /* try next candidate */ }
            }

            // Final fallback: code used as both code and name
            try
            {
                var rows = await conn.QueryAsync(
                    "SELECT Code AS C, Code AS N FROM lcs_hr.hr_city " +
                    "WHERE Code IS NOT NULL AND Code != '' ORDER BY Code");
                return rows
                    .Select(r => (Code: (string)(r.C ?? ""), Name: (string)(r.N ?? "")))
                    .Where(x => !string.IsNullOrEmpty(x.Code))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ACTest] GetCitiesAsync: failed to load cities.");
                return new();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private (string WhereClause, object? Params) BuildWhere(
            TableQuerySpec spec, AcTestFilterRequest filter)
        {
            if (!filter.HasRequiredFilters)
                return ("", null);

            var sb = new StringBuilder();

            // City condition
            if (spec.StationJoinCol != null)
            {
                // Join via hr_station to resolve city code → station_id
                sb.Append($"`{spec.StationJoinCol}` IN " +
                    "(SELECT station_id FROM lcs_hr.hr_station " +
                    "WHERE city_code = @CityCode)");
            }
            else if (spec.CityCol != null)
            {
                sb.Append($"`{spec.CityCol}` = @CityCode");
            }

            // Year condition
            if (spec.YearExpr != null)
            {
                if (sb.Length > 0) sb.Append(" AND ");
                sb.Append($"{spec.YearExpr} = @Year");
            }

            // Month condition
            if (spec.MonthExpr != null)
            {
                if (sb.Length > 0) sb.Append(" AND ");
                sb.Append($"{spec.MonthExpr} = @Month");
            }

            // Optional RouteCode
            if (!string.IsNullOrWhiteSpace(filter.RouteCode) && spec.RouteColName != null)
                sb.Append($" AND `{spec.RouteColName}` = @RouteCode");

            // Optional EmpNo
            if (!string.IsNullOrWhiteSpace(filter.EmpNo) && spec.EmpNoColName != null)
                sb.Append($" AND `{spec.EmpNoColName}` = @EmpNo");

            var p = new
            {
                CityCode  = filter.CityCode!,
                Year      = filter.Year!.Value,
                Month     = filter.Month!.Value,
                RouteCode = filter.RouteCode ?? "",
                EmpNo     = filter.EmpNo ?? ""
            };

            return (sb.ToString(), p);
        }

        private async Task<(long Count, bool IsFiltered)> CountSafeAsync(
            MySqlConnection conn, string table, string? whereClause, object? param)
        {
            // No WHERE clause → return total count, IsFiltered = false
            if (string.IsNullOrEmpty(whereClause))
            {
                try
                {
                    return (await conn.ExecuteScalarAsync<long>(
                        $"SELECT COUNT(*) FROM `lcs_hr`.`{table}`"), false);
                }
                catch { return (-1, false); }
            }

            // Try filtered count
            try
            {
                var n = await conn.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM `lcs_hr`.`{table}` WHERE {whereClause}",
                    param,
                    commandTimeout: 30);
                return (n, true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "[ACTest] Filtered count failed for {T} — falling back to total.",
                    table);
                // Fallback: total count, mark IsFiltered = false so view shows 〰
                try
                {
                    return (await conn.ExecuteScalarAsync<long>(
                        $"SELECT COUNT(*) FROM `lcs_hr`.`{table}`"), false);
                }
                catch { return (-1, false); }
            }
        }

        private static async Task<bool> TableExistsAsync(
            MySqlConnection conn, string table)
        {
            try
            {
                var cnt = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t",
                    new { t = table });
                return cnt > 0;
            }
            catch { return false; }
        }
    }
}
