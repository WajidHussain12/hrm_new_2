using System.Security.Claims;
using LCS_HR_MVC.Models.Settlement;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class AdvanceSalaryController : Controller
    {
        private readonly IAdvanceSalaryService _advanceSalaryService;
        private readonly ILogger<AdvanceSalaryController> _logger;

        public AdvanceSalaryController(IAdvanceSalaryService advanceSalaryService, ILogger<AdvanceSalaryController> logger)
        {
            _advanceSalaryService = advanceSalaryService;
            _logger = logger;
        }

        #region Employee Advance Salary
        [HttpGet]
        public async Task<IActionResult> EmpAdvanceSalary(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _advanceSalaryService.GetAllAdvanceSalariesAsync(currentUserId);
            ViewBag.AdvanceList = data;

            var model = new AdvanceSalaryModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _advanceSalaryService.GetAdvanceSalaryByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmpAdvanceSalary(AdvanceSalaryModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmpAdvanceSalary");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _advanceSalaryService.DeleteAdvanceSalaryAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmpAdvanceSalary");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.AdvanceList = await _advanceSalaryService.GetAllAdvanceSalariesAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _advanceSalaryService.AddAdvanceSalaryAsync(model, currentUserId);
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
                        bool updated = await _advanceSalaryService.UpdateAdvanceSalaryAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmpAdvanceSalary action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmpAdvanceSalary");
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeSalaryInfo(string empNo)
        {
            var result = await _advanceSalaryService.GetEmployeeSalaryInfoAsync(empNo);
            return Json(result);
        }
        #endregion

        #region Advance Salary Approve
        [HttpGet]
        public async Task<IActionResult> AdvanceSalaryApprove()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = new AdvanceSalaryApprovalViewModel();
            model.Requests = (await _advanceSalaryService.GetAllAdvanceSalariesAsync(currentUserId, model.SortBy)).ToList();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdvanceSalaryApprove(AdvanceSalaryApprovalViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Sort")
                {
                    model.Requests = (await _advanceSalaryService.GetAllAdvanceSalariesAsync(currentUserId, model.SortBy)).ToList();
                    return View(model);
                }

                if (action == "Process")
                {
                    if (model.SelectedCodes == null || !model.SelectedCodes.Any())
                    {
                        TempData["ErrorMessage"] = "You have to select at least one record.";
                    }
                    else
                    {
                        var result = await _advanceSalaryService.ProcessAdvanceSalaryApprovalsAsync(model.SelectedCodes, model.ApprovalStatus, currentUserId);
                        if (result.processed > 0)
                            TempData["SuccessMessage"] = $"{result.processed} record(s) out of {model.SelectedCodes.Count} selected record(s) have been updated successfully.";
                        else
                            TempData["ErrorMessage"] = "No records updated.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AdvanceSalaryApprove.");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            model.Requests = (await _advanceSalaryService.GetAllAdvanceSalariesAsync(currentUserId, model.SortBy)).ToList();
            model.SelectedCodes = new List<string>();
            return View(model);
        }
        #endregion
    }
}