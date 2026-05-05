using System.Security.Claims;
using LCS_HR_MVC.Models.Closing;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class ClosingController : Controller
    {
        private readonly IClosingService _closingService;
        private readonly ILogger<ClosingController> _logger;

        public ClosingController(IClosingService closingService, ILogger<ClosingController> logger)
        {
            _closingService = closingService;
            _logger = logger;
        }

        #region Close Processes
        [HttpGet]
        public async Task<IActionResult> CloseProcesses(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            
            var data = await _closingService.GetAllClosedProcessesAsync(currentUserId);
            ViewBag.ProcessList = data;

            var zones = await _closingService.GetZonesByUserAsync(currentUserId);
            ViewBag.Zones = new SelectList(zones, "Value", "Text");

            var model = new CloseProcessModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseProcesses(CloseProcessModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("CloseProcesses");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.CityCode))
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _closingService.DeleteCloseProcessAsync(model.CityCode, model.Year, model.Month);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                        else TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("CloseProcesses");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.ProcessList = await _closingService.GetAllClosedProcessesAsync(currentUserId);
                    var zones = await _closingService.GetZonesByUserAsync(currentUserId);
                    ViewBag.Zones = new SelectList(zones, "Value", "Text");
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _closingService.AddCloseProcessAsync(model, currentUserId);
                    if (saved) TempData["SuccessMessage"] = "Record Saved Successfully";
                }
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CloseProcesses");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("CloseProcesses");
        }

        [HttpGet]
        public async Task<IActionResult> GetCitiesByZoneUser(string zoneCode)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var results = await _closingService.GetCitiesByZoneUserAsync(zoneCode, currentUserId);
            return Json(results);
        }
        #endregion

        #region Unlock Salary
        [HttpGet]
        public async Task<IActionResult> UnlockSalary()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var zones = await _closingService.GetZonesByUserAsync(currentUserId);
            ViewBag.Zones = new SelectList(zones, "Value", "Text");

            var model = new UnlockSalaryViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlockSalary(UnlockSalaryViewModel model, string action)
        {
            if (!ModelState.IsValid)
            {
                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
                var zones = await _closingService.GetZonesByUserAsync(currentUserId);
                ViewBag.Zones = new SelectList(zones, "Value", "Text");
                return View(model);
            }

            try
            {
                if (action == "Process")
                {
                    await _closingService.UnlockSalaryAsync(model);
                    TempData["SuccessMessage"] = "Salary Process Un-Lock Successfully!";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unlocking salary.");
                TempData["ErrorMessage"] = "An error occurred while processing.";
            }

            return RedirectToAction("UnlockSalary");
        }
        #endregion

        #region Commission Unlock
        [HttpGet]
        public async Task<IActionResult> CommissionUnlock()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var zones = await _closingService.GetZonesByUserAsync(currentUserId);
            ViewBag.Zones = new SelectList(zones, "Value", "Text");

            var model = new CommissionUnlockViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CommissionUnlock(CommissionUnlockViewModel model, string action)
        {
            if (!ModelState.IsValid)
            {
                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
                var zones = await _closingService.GetZonesByUserAsync(currentUserId);
                ViewBag.Zones = new SelectList(zones, "Value", "Text");
                return View(model);
            }

            try
            {
                if (action == "Process")
                {
                    await _closingService.CommissionUnlockAsync(model);
                    TempData["SuccessMessage"] = "Commission Process Un-Lock Successfully!";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unlocking commission.");
                TempData["ErrorMessage"] = "An error occurred while processing.";
            }

            return RedirectToAction("CommissionUnlock");
        }
        #endregion
    }
}