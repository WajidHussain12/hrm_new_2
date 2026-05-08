using System.Security.Claims;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class AutomationController : Controller
    {
        private readonly ICommissionAutomationService _automationService;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ILogger<AutomationController> _logger;

        public AutomationController(
            ICommissionAutomationService automationService,
            IDbConnectionFactory connectionFactory,
            ILogger<AutomationController> logger)
        {
            _automationService = automationService;
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Commission(int? year, int? month, string? jobRunId)
        {
            var now = DateTime.Now;
            var selectedYear = year ?? now.Year;
            var selectedMonth = month ?? now.Month;

            try
            {
                var model = await _automationService.GetDashboardAsync(selectedYear, selectedMonth);
                model.JobRunId = GetLatestDashboardJobRunId(model);
                ApplyAutomationButtonState(model);
                ApplyDashboardCacheHeaders(model);
                return View(model);
            }
            catch (MySqlException ex)
            {
                return DatabaseConnectionProblem(selectedYear, selectedMonth, ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryDatabaseConnection(int year, int month)
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot create database connection.");
                await connection.OpenAsync();

                var activeJobRunId = await FindActiveRunningJobRunIdAsync(year, month);
                if (string.IsNullOrWhiteSpace(activeJobRunId))
                {
                    try
                    {
                        var jobRunId = await _automationService.StartAutomationAsync(year, month, "AutoRecovery", "210");
                        TempData["SuccessMessage"] = $"Database connection established. Automation resumed. Job ID: {jobRunId}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto resume failed after database reconnect for {Year}/{Month}", year, month);
                        TempData["ErrorMessage"] = $"Database connection established, but auto resume failed: {ex.Message}";
                    }
                }
                else
                {
                    TempData["SuccessMessage"] = $"Database connection established. Automation is already running. Current Job ID: {activeJobRunId}";
                }

                return RedirectToAction(nameof(Commission), new { year, month });
            }
            catch (MySqlException ex)
            {
                return DatabaseConnectionProblem(year, month, ex);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartCommission(int year, int month)
        {
            if (year < 2020 || year > 2100 || month < 1 || month > 12)
            {
                TempData["ErrorMessage"] = "Invalid year or month.";
                return RedirectToAction(nameof(Commission), new { year, month });
            }

            string? activeJobRunId = await FindActiveRunningJobRunIdAsync(year, month);
            if (!string.IsNullOrWhiteSpace(activeJobRunId))
            {
                TempData["SuccessMessage"] = $"Automation already running. Current Job ID: {activeJobRunId}";
                return RedirectToAction(nameof(Commission), new { year, month });
            }

            var validation = await _automationService.ValidateBaseDataAsync(year, month);
            if (!validation.IsValid)
            {
                TempData["ValidationFailed"] = System.Text.Json.JsonSerializer.Serialize(validation);
                return RedirectToAction(nameof(Commission), new { year, month });
            }

            var triggeredBy = User.Identity?.Name ?? "Unknown";
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                var jobRunId = await _automationService.StartAutomationAsync(year, month, triggeredBy, currentUserId);
                TempData["SuccessMessage"] = $"Automation request accepted. Job ID: {jobRunId}";
                return RedirectToAction(nameof(Commission), new { year, month });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start commission automation for {Year}/{Month}", year, month);
                TempData["ErrorMessage"] = $"Failed to start automation: {ex.Message}";
                return RedirectToAction(nameof(Commission), new { year, month });
            }
        }

        private IActionResult DatabaseConnectionProblem(int year, int month, Exception ex)
        {
            _logger.LogError(ex, "Automation database connection failed for {Year}/{Month}", year, month);
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            ViewData["Year"] = year;
            ViewData["Month"] = month;
            ViewData["PeriodLabel"] = new DateTime(year, month, 1).ToString("MMMM yyyy");
            ViewData["ErrorTitle"] = "Database Connection Not Established";
            ViewData["ErrorMessage"] = "The automation dashboard could not connect to the database server. Your progress is preserved. Please retry after the database or network is available.";
            ViewData["TechnicalMessage"] = ex.Message;
            return View("DatabaseConnectionError");
        }

        private void ApplyDashboardCacheHeaders(CommissionAutomationDashboardViewModel model)
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            if (model.Entries.Any(entry => entry.Status == "Running"))
            {
                Response.Headers["Refresh"] = "30";
            }
        }

        private static void ApplyAutomationButtonState(CommissionAutomationDashboardViewModel model)
        {
            bool hasRunningEntry = model.Entries.Any(entry => entry.Status == "Running");
            model.IsRunning = hasRunningEntry;
        }

        private static string? GetLatestDashboardJobRunId(CommissionAutomationDashboardViewModel model)
        {
            return model.Entries
                .OrderByDescending(entry => entry.Status == "Running")
                .ThenByDescending(entry => entry.UpdatedAt)
                .ThenByDescending(entry => entry.Id)
                .Select(entry => entry.JobRunId)
                .FirstOrDefault(jobRunId => !string.IsNullOrWhiteSpace(jobRunId))
                ?? model.JobRunId;
        }

        private async Task<string?> FindActiveRunningJobRunIdAsync(int year, int month)
        {
            try
            {
                var model = await _automationService.GetDashboardAsync(year, month);
                return model.Entries
                    .Where(entry => entry.Status == "Running")
                    .OrderByDescending(entry => entry.UpdatedAt)
                    .ThenByDescending(entry => entry.Id)
                    .Select(entry => entry.JobRunId)
                    .FirstOrDefault(jobRunId => !string.IsNullOrWhiteSpace(jobRunId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not check active commission automation job for {Year}/{Month}. Continuing with normal start flow.",
                    year,
                    month);
                return null;
            }
        }

        [HttpGet]
        public async Task<IActionResult> ValidateBaseData(int year, int month)
        {
            if (year < 2020 || year > 2100 || month < 1 || month > 12)
                return BadRequest("Invalid year or month.");

            var result = await _automationService.ValidateBaseDataAsync(year, month);
            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> Status(int year, int month)
        {
            try
            {
                var model = await _automationService.GetDashboardAsync(year, month);
                model.JobRunId = GetLatestDashboardJobRunId(model);
                ApplyAutomationButtonState(model);
                return Json(model);
            }
            catch (MySqlException ex)
            {
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Json(new { databaseConnectionError = true, message = "Database connection not established.", detail = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> History(int? year, int? month)
        {
            var now = DateTime.Now;
            var selectedYear = year ?? now.Year;
            var selectedMonth = month ?? now.Month;

            if (selectedYear < 2020 || selectedYear > 2100 || selectedMonth < 1 || selectedMonth > 12)
            {
                selectedYear = now.Year;
                selectedMonth = now.Month;
            }

            var model = await _automationService.GetReconciledHistoryAsync(selectedYear, selectedMonth);
            return View(model);
        }

        [HttpGet]
        public IActionResult WorkingFlow()
        {
            return View();
        }

        [HttpGet]
        public IActionResult CommissionGuide()
        {
            return View();
        }

        [HttpGet]
        public IActionResult CommissionFlowExplained()
        {
            return View();
        }
    }
}
