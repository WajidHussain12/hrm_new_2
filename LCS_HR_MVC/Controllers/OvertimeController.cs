using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class OvertimeController : Controller
    {
        private readonly IOvertimeService _overtimeService;
        private readonly ILogger<OvertimeController> _logger;

        public OvertimeController(IOvertimeService overtimeService, ILogger<OvertimeController> logger)
        {
            _overtimeService = overtimeService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> EmployeeOvertime(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _overtimeService.GetAllEmployeeOvertimesAsync(currentUserId);
            ViewBag.OvertimeList = data;

            var model = new EmployeeOvertimeModel { Date = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _overtimeService.GetEmployeeOvertimeByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeOvertime(EmployeeOvertimeModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeOvertime");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _overtimeService.DeleteEmployeeOvertimeAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeOvertime");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.OvertimeList = await _overtimeService.GetAllEmployeeOvertimesAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _overtimeService.AddEmployeeOvertimeAsync(model, currentUserId);
                    if (saved) TempData["SuccessMessage"] = "Record Saved Successfully";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _overtimeService.UpdateEmployeeOvertimeAsync(model, currentUserId);
                        if (updated) TempData["SuccessMessage"] = "Record updated successfully.";
                    }
                }
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeOvertime action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeOvertime");
        }
    }
}