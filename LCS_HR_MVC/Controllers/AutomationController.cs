using System.Security.Claims;
using Dapper;
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

            var model = await _automationService.GetDashboardAsync(selectedYear, selectedMonth);
            model.JobRunId = GetLatestDashboardJobRunId(model);
            ApplyDashboardCacheHeaders(model);

            // Do not filter Entries by jobRunId.
            // Resume creates new job_run_id only for incomplete work,
            // while completed city steps remain under previous job_run_id values.
            // Dashboard must show full month-level progress.

            return View(model);
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

            // Server-side pre-flight guard — prevents automation from starting if base data is missing.
            // The JS pre-check normally catches this first; this is the authoritative safety net.
            var validation = await _automationService.ValidateBaseDataAsync(year, month);
            if (!validation.IsValid)
            {
                TempData["ValidationFailed"] = System.Text.Json.JsonSerializer.Serialize(validation);
                return RedirectToAction(nameof(Commission), new { year, month });
            }

            var triggeredBy   = User.Identity?.Name ?? "Unknown";
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

        private void ApplyDashboardCacheHeaders(CommissionAutomationDashboardViewModel model)
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            if (model.IsRunning || model.Entries.Any(entry => entry.Status == "Running"))
            {
                // Safety net for missed SignalR events or stale browser DOM.
                // While automation is running, force a server-render refresh every 30 seconds.
                Response.Headers["Refresh"] = "30";
            }
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
                using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot create database connection.");
                await connection.OpenAsync();

                return await connection.ExecuteScalarAsync<string?>(
                    @"SELECT job_run_id
                      FROM lcs_hr.hr_commission_automation_log
                      WHERE year = @Year
                        AND month = @Month
                        AND status = 'Running'
                      ORDER BY updated_at DESC, id DESC
                      LIMIT 1;",
                    new { Year = year, Month = month });
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
            var model = await _automationService.GetDashboardAsync(year, month);
            model.JobRunId = GetLatestDashboardJobRunId(model);
            return Json(model);
        }

        [HttpGet]
        public async Task<IActionResult> History(int? year, int? month)
        {
            var now = DateTime.Now;
            var selectedYear  = year  ?? now.Year;
            var selectedMonth = month ?? now.Month;

            if (selectedYear < 2020 || selectedYear > 2100 || selectedMonth < 1 || selectedMonth > 12)
            {
                selectedYear  = now.Year;
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
