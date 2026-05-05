using System.Security.Claims;
using LCS_HR_MVC.Models.Settlement;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class SettlementController : Controller
    {
        private readonly ISettlementService _settlementService;
        private readonly ILogger<SettlementController> _logger;

        public SettlementController(ISettlementService settlementService, ILogger<SettlementController> logger)
        {
            _settlementService = settlementService;
            _logger = logger;
        }

        #region Employee Termination
        [HttpGet]
        public async Task<IActionResult> EmployeeTermination(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _settlementService.GetAllEmployeeTerminationsAsync(currentUserId);
            ViewBag.TerminationList = data;

            var model = new EmployeeTerminationModel { TerminationDate = DateTime.Now.Date, LeavingReason = "00", Settlement = "N" };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _settlementService.GetEmployeeTerminationByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeTermination(EmployeeTerminationModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeTermination");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _settlementService.DeleteEmployeeTerminationAsync(model.Code, currentUserId);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeTermination");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _settlementService.BulkUploadTerminationsAsync(model.BulkUploadFile, currentUserId);
                        if (result.successCount > 0) TempData["SuccessMessage"] = result.message;
                        else TempData["ErrorMessage"] = result.message;
                    }
                    else TempData["ErrorMessage"] = "Please select a valid Excel file.";
                    return RedirectToAction("EmployeeTermination");
                }

                if (!ModelState.IsValid || model.LeavingReason == "00")
                {
                    if (model.LeavingReason == "00") ModelState.AddModelError("LeavingReason", "Please Select Reason.");
                    ViewBag.TerminationList = await _settlementService.GetAllEmployeeTerminationsAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _settlementService.AddEmployeeTerminationAsync(model, currentUserId);
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
                        bool updated = await _settlementService.UpdateEmployeeTerminationAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeeTermination action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeTermination");
        }
        #endregion

        #region Final Settlement
        [HttpGet]
        public IActionResult FinalSettlement()
        {
            var model = new FinalSettlementModel();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalSettlement(FinalSettlementModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("FinalSettlement");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                if (action == "Save")
                {
                    if(model.WorkingDays1 == 0 && model.WorkingDays2 == 0)
                    {
                        TempData["ErrorMessage"] = "Please provide working days.";
                        return View(model);
                    }

                    var result = await _settlementService.ProcessFinalSettlementAsync(model, currentUserId);
                    if (result.success) TempData["SuccessMessage"] = result.message;
                    else TempData["ErrorMessage"] = result.message;
                }
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FinalSettlement.");
                TempData["ErrorMessage"] = "An error occurred while processing.";
            }

            return RedirectToAction("FinalSettlement");
        }

        [HttpGet]
        public async Task<IActionResult> SearchSettlementEmployees(string term)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var employees = await _settlementService.SearchSettlementEmployeesAsync(term ?? string.Empty, currentUserId);
            return Json(employees);
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeResignData(string empNo)
        {
            var result = await _settlementService.GetEmployeeResignDataAsync(empNo);
            return Json(result);
        }
        #endregion
    }
}
