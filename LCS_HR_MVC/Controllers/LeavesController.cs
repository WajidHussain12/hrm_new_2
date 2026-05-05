using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class LeavesController : Controller
    {
        private readonly ILeavesService _leavesService;
        private readonly ILogger<LeavesController> _logger;

        public LeavesController(ILeavesService leavesService, ILogger<LeavesController> logger)
        {
            _leavesService = leavesService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> EmployeeLeaveRequest(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _leavesService.GetAllLeaveRequestsAsync(currentUserId);
            ViewBag.LeaveRequestsList = data;

            var model = new LeaveRequestModel { RequestDate = DateTime.Now.Date, LeaveFromDate = DateTime.Now.Date, LeaveToDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _leavesService.GetLeaveRequestByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeLeaveRequest(LeaveRequestModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeLeaveRequest");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.RequestNo) || model.RequestNo == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _leavesService.DeleteLeaveRequestAsync(model.RequestNo);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmployeeLeaveRequest");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _leavesService.BulkUploadLeaveRequestsAsync(model.BulkUploadFile, currentUserId);
                        if (result.successCount > 0)
                        {
                            TempData["SuccessMessage"] = result.message;
                        }
                        else
                        {
                            TempData["ErrorMessage"] = result.message;
                        }
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Please select a valid Excel file.";
                    }
                    return RedirectToAction("EmployeeLeaveRequest");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.LeaveRequestsList = await _leavesService.GetAllLeaveRequestsAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _leavesService.AddLeaveRequestAsync(model, currentUserId);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.RequestNo) || model.RequestNo == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _leavesService.UpdateLeaveRequestAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeLeaveRequest action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeLeaveRequest");
        }

        [HttpGet]
        public async Task<IActionResult> SearchLeaveCategories(string term)
        {
            var results = await _leavesService.SearchLeaveCategoriesAsync(term);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> SearchLeaveTypes(string term)
        {
            var results = await _leavesService.SearchLeaveTypesAsync(term);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> EmployeeLeaveRequestApproval()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = new LeaveRequestApprovalViewModel
            {
                FromDate = DateTime.Now.Date,
                ToDate = DateTime.Now.Date
            };
            
            model.Requests = (await _leavesService.GetPendingLeaveRequestsAsync(currentUserId, model.FromDate, model.ToDate)).ToList();
            
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeLeaveRequestApproval(LeaveRequestApprovalViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var currentUserName = User.Identity?.Name ?? "Unknown";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeLeaveRequestApproval");
                }

                if (action == "Show")
                {
                    if (model.FromDate > model.ToDate)
                    {
                        TempData["ErrorMessage"] = "From Date cannot be greater than To Date.";
                    }
                    else
                    {
                        model.Requests = (await _leavesService.GetPendingLeaveRequestsAsync(currentUserId, model.FromDate, model.ToDate)).ToList();
                    }
                    return View(model);
                }

                if (model.SelectedRequestCodes == null || !model.SelectedRequestCodes.Any())
                {
                    TempData["ErrorMessage"] = "Please select Request(s) for approval/rejection!";
                    model.Requests = (await _leavesService.GetPendingLeaveRequestsAsync(currentUserId, model.FromDate, model.ToDate)).ToList();
                    return View(model);
                }

                if (action == "Approve")
                {
                    var result = await _leavesService.ApproveLeaveRequestsAsync(model.SelectedRequestCodes, currentUserId, currentUserName);
                    if (result.approved > 0)
                    {
                        TempData["SuccessMessage"] = $"Total {result.approved} Request(s) Approved Successfully.";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No Request(s) Approved.";
                    }
                }
                else if (action == "Reject")
                {
                    var result = await _leavesService.RejectLeaveRequestsAsync(model.SelectedRequestCodes, currentUserId, currentUserName);
                    if (result.rejected > 0)
                    {
                        TempData["SuccessMessage"] = $"Total {result.rejected} Request(s) Rejected Successfully.";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No Request(s) Rejected.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeLeaveRequestApproval action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            model.Requests = (await _leavesService.GetPendingLeaveRequestsAsync(currentUserId, model.FromDate, model.ToDate)).ToList();
            model.SelectedRequestCodes = new List<string>();
            return View(model);
        }
        [HttpGet]
        public async Task<IActionResult> EmployeeLeaves()
        {
            var data = await _leavesService.GetAllTakenLeavesAsync();
            ViewBag.TakenLeavesList = data;

            var yearsList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            for (int i = DateTime.Now.Year - 5; i <= DateTime.Now.Year + 1; i++)
            {
                yearsList.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(i.ToString(), i.ToString()));
            }
            ViewBag.YearsList = yearsList;

            var model = new TakenLeaveModel { LeaveFromDate = DateTime.Now.Date, LeaveToDate = DateTime.Now.Date, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeLeaves(TakenLeaveModel model, string action, string? originalLeaveDateStr)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeLeaves");
                }

                if (action == "ActivateProbation")
                {
                    if (string.IsNullOrEmpty(model.EmpNo))
                    {
                        TempData["ErrorMessage"] = "Employee Code is empty";
                    }
                    else
                    {
                        bool activated = await _leavesService.ActivateProbationPeriodAsync(model.EmpNo, model.Year);
                        if (activated) TempData["SuccessMessage"] = "Probation period activated successfully.";
                        else TempData["ErrorMessage"] = "Failed to activate probation.";
                    }
                    return RedirectToAction("EmployeeLeaves");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.EmpNo) || string.IsNullOrEmpty(originalLeaveDateStr))
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        if (DateTime.TryParse(originalLeaveDateStr, out DateTime origDate))
                        {
                            bool deleted = await _leavesService.DeleteTakenLeaveAsync(model.EmpNo, model.Year, origDate);
                            if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                            else TempData["ErrorMessage"] = "Error while deleting data in the database.";
                        }
                    }
                    return RedirectToAction("EmployeeLeaves");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.TakenLeavesList = await _leavesService.GetAllTakenLeavesAsync();
                    var years = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    for (int i = DateTime.Now.Year - 5; i <= DateTime.Now.Year + 1; i++)
                    {
                        years.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(i.ToString(), i.ToString()));
                    }
                    ViewBag.YearsList = years;
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _leavesService.AddTakenLeavesAsync(model, currentUserId);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.EmpNo) || string.IsNullOrEmpty(originalLeaveDateStr))
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        if (DateTime.TryParse(originalLeaveDateStr, out DateTime origDate))
                        {
                            bool updated = await _leavesService.UpdateTakenLeaveAsync(model, origDate, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeLeaves action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeLeaves");
        }

        [HttpGet]
        public async Task<IActionResult> IsEmployeeOnProbation(string empCode, int year)
        {
            bool isOnProbation = await _leavesService.IsEmployeeOnProbationAsync(empCode, year);
            return Json(new { d = isOnProbation ? 0 : 1 }); // 0 means probation is ON (hide fields), 1 means active
        }
    }
}