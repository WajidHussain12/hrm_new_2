using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class TestDataMigrationController : Controller
    {
        private readonly ITestDataMigrationService _migrationService;
        private readonly ILogger<TestDataMigrationController> _logger;

        public TestDataMigrationController(
            ITestDataMigrationService migrationService,
            ILogger<TestDataMigrationController> logger)
        {
            _migrationService = migrationService;
            _logger           = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? year, int? month)
        {
            var now   = DateTime.Now;
            var selYear  = year  ?? now.Year;
            var selMonth = month ?? now.Month;

            var model = await _migrationService.GetCurrentStatusAsync(selYear, selMonth);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartMigration(int year, int month, int rowLimit = 0,
            [FromForm(Name = "cities")] List<string>? cities = null)
        {
            if (year < 2020 || year > 2100 || month < 1 || month > 12)
            {
                TempData["ErrorMessage"] = "Invalid year or month.";
                return RedirectToAction(nameof(Index), new { year, month });
            }

            if (rowLimit < 0) rowLimit = 0;

            // Empty list = All Cities
            var cityFilter = cities?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList()
                             ?? new List<string>();

            try
            {
                var jobId = await _migrationService.StartMigrationAsync(year, month, rowLimit, cityFilter);
                var limitLabel = rowLimit == 0 ? "unlimited" : $"{rowLimit:N0} rows/table";
                var cityLabel  = cityFilter.Count > 0 ? $", {cityFilter.Count} cities" : ", all cities";
                TempData["SuccessMessage"] = $"Migration started ({limitLabel}{cityLabel}). Hangfire Job ID: {jobId}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start migration for {Year}/{Month}", year, month);
                TempData["ErrorMessage"] = $"Failed to start migration: {ex.Message}";
            }

            return RedirectToAction(nameof(Index), new { year, month });
        }

        [HttpGet]
        public async Task<IActionResult> Status(int year, int month)
        {
            if (year < 2020 || year > 2100 || month < 1 || month > 12)
                return BadRequest("Invalid year or month.");

            var model = await _migrationService.GetCurrentStatusAsync(year, month);
            return Json(model);
        }

        [HttpGet]
        public async Task<IActionResult> DestinationTables()
        {
            var tables = await _migrationService.GetDestinationTablesStatusAsync();
            return View(tables);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDestinationTables()
        {
            try
            {
                var (created, skipped, failed, messages) = await _migrationService.CreateDestinationTablesAsync();
                TempData["CreateResult"] = System.Text.Json.JsonSerializer.Serialize(messages);
                TempData["CreateSummary"] = $"Created: {created}, Skipped: {skipped}, Failed: {failed}";
                if (failed > 0)
                    TempData["CreateStatus"] = "failed";
                else if (created > 0)
                    TempData["CreateStatus"] = "success";
                else
                    TempData["CreateStatus"] = "skipped";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateDestinationTables failed");
                TempData["CreateSummary"] = $"Error: {ex.Message}";
                TempData["CreateStatus"] = "failed";
            }

            return RedirectToAction(nameof(DestinationTables));
        }
    }
}
