using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    [Route("ac-test")]
    public class AcTestController : Controller
    {
        private readonly string _connectionString;
        private readonly ILogger<AcTestController> _logger;
        private readonly AcTestComparisonService _compService;

        public AcTestController(
            IConfiguration configuration,
            ILogger<AcTestController> logger,
            AcTestComparisonService compService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection not configured.");
            _logger     = logger;
            _compService = compService;
        }

        // ── GET /ac-test ──────────────────────────────────────────────────────

        [HttpGet("")]
        public async Task<IActionResult> Index(
            string? cityCode  = null,
            int?    year      = null,
            int?    month     = null,
            string? commType  = null,
            string? stationId = null,
            string? routeCode = null,
            string? empNo     = null)
        {
            var filter = new AcTestFilterRequest
            {
                CityCode       = cityCode,
                CommissionType = commType ?? "All",
                StationId      = stationId,
                RouteCode      = routeCode,
                EmpNo          = empNo
            };

            // Always default year/month so filter panel shows current period
            filter.Year  = year  ?? DateTime.Now.Year;
            filter.Month = month ?? DateTime.Now.Month;

            var vm = new AcTestComparisonViewModel
            {
                Filter         = filter,
                IsTestMode     = AcTestTableNames.IsTestMode,
                Cities         = await _compService.GetCitiesAsync(),
                FiltersApplied = filter.HasRequiredFilters
            };

            // Only run DB queries when city is selected
            if (filter.HasRequiredFilters)
                vm.Rows = await _compService.GetComparisonAsync(filter);

            return View(vm);
        }

        // ── GET /ac-test/city-stations ────────────────────────────────────────
        // AJAX: returns stations for a given city code (for Station dropdown)

        [HttpGet("city-stations")]
        public async Task<IActionResult> GetCityStations(string cityCode)
        {
            try
            {
                using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();

                var stations = await conn.QueryAsync(
                    "SELECT station_id AS value, station_name AS text " +
                    "FROM lcs_hr.hr_station " +
                    "WHERE city_code = @c ORDER BY station_name",
                    new { c = cityCode });

                return Json(stations);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[ACTest] GetCityStations failed for city {C}.", cityCode);
                return Json(Array.Empty<object>());
            }
        }

        // ── GET /ac-test/city-employees ───────────────────────────────────────
        // AJAX: returns employees who have commission in city/period (from REAL table)

        [HttpGet("city-employees")]
        public async Task<IActionResult> GetCityEmployees(
            string cityCode, int year, int month)
        {
            try
            {
                using var conn = new MySqlConnection(_connectionString);
                await conn.OpenAsync();

                var emps = await conn.QueryAsync(
                    "SELECT DISTINCT cp.emp_no AS value, " +
                    "CONCAT(e.emp_name, ' (', cp.emp_no, ')') AS text " +
                    "FROM lcs_hr.hr_commissionprocess cp " +
                    "JOIN lcs_hr.hr_employee e ON e.emp_no = cp.emp_no " +
                    "WHERE cp.citycode = @c " +
                    "  AND cp.year = @y AND cp.month = @m " +
                    "ORDER BY e.emp_name",
                    new { c = cityCode, y = year, m = month },
                    commandTimeout: 30);

                return Json(emps);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[ACTest] GetCityEmployees failed for city {C} {Y}/{M}.",
                    cityCode, year, month);
                return Json(Array.Empty<object>());
            }
        }

        // ── POST /ac-test/truncate ────────────────────────────────────────────

        [HttpPost("truncate"), ValidateAntiForgeryToken]
        public async Task<IActionResult> Truncate()
        {
            await AcTestTablesBootstrapper
                .TruncateAllTestTablesAsync(_connectionString, _logger);
            TempData["Success"] =
                "All AC_Test tables truncated. Ready for fresh test run.";
            return RedirectToAction(nameof(Index));
        }

        // ── POST /ac-test/recreate ────────────────────────────────────────────

        [HttpPost("recreate"), ValidateAntiForgeryToken]
        public async Task<IActionResult> Recreate()
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var t in AcTestTableNames.AllOutputTables)
            {
                var testT = t + AcTestTableNames.TestSuffix;
                try
                {
                    await conn.ExecuteAsync(
                        $"DROP TABLE IF EXISTS `lcs_hr`.`{testT}`");
                }
                catch { /* ignore */ }
            }

            AcTestTablesBootstrapper.Reset();
            await AcTestTablesBootstrapper
                .EnsureTestTablesAsync(_connectionString, _logger);

            TempData["Success"] =
                "All AC_Test tables recreated from real table DDL.";
            return RedirectToAction(nameof(Index));
        }
    }
}
