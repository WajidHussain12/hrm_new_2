using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class AutomationController : Controller
    {
        private readonly ICommissionAutomationService _automationService;
        private readonly ILogger<AutomationController> _logger;

        public AutomationController(
            ICommissionAutomationService automationService,
            ILogger<AutomationController> logger)
        {
            _automationService = automationService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Commission(int? year, int? month)
        {
            var now = DateTime.Now;
            var selectedYear = year ?? now.Year;
            var selectedMonth = month ?? now.Month;

            var model = await _automationService.GetDashboardAsync(selectedYear, selectedMonth);
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
                TempData["SuccessMessage"] = $"Automation started. Job ID: {jobRunId}";
                return RedirectToAction(nameof(Commission), new { year, month });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start commission automation for {Year}/{Month}", year, month);
                TempData["ErrorMessage"] = $"Failed to start automation: {ex.Message}";
                return RedirectToAction(nameof(Commission), new { year, month });
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
