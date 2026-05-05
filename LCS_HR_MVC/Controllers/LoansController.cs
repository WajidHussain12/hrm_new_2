using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class LoansController : Controller
    {
        private readonly ILoansService _loansService;
        private readonly ILogger<LoansController> _logger;

        public LoansController(ILoansService loansService, ILogger<LoansController> logger)
        {
            _loansService = loansService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> EmpLoanRequest(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _loansService.GetAllLoanRequestsAsync(currentUserId);
            ViewBag.LoanRequestsList = data;

            var model = new LoanRequestModel { RequestDate = DateTime.Now.Date, StartDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _loansService.GetLoanRequestByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmpLoanRequest(LoanRequestModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var currentUserName = User.Identity?.Name ?? "Unknown";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmpLoanRequest");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _loansService.DeleteLoanRequestAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmpLoanRequest");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _loansService.BulkUploadLoanRequestsAsync(model.BulkUploadFile, currentUserId);
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
                    return RedirectToAction("EmpLoanRequest");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.LoanRequestsList = await _loansService.GetAllLoanRequestsAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _loansService.AddLoanRequestAsync(model, currentUserId, currentUserName);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _loansService.UpdateLoanRequestAsync(model, currentUserId, currentUserName);
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
                _logger.LogError(ex, "Error processing EmpLoanRequest action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmpLoanRequest");
        }

        [HttpGet]
        public async Task<IActionResult> SearchLoans(string term)
        {
            var results = await _loansService.SearchLoansAsync(term);
            return Json(results);
        }
        [HttpGet]
        public async Task<IActionResult> EmployeeLoanApprove()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = new LoanRequestApprovalViewModel();
            model.Requests = (await _loansService.GetPendingLoanRequestsAsync(currentUserId)).ToList();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeLoanApprove(LoanRequestApprovalViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var currentUserName = User.Identity?.Name ?? "Unknown";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeLoanApprove");
                }

                if (action == "Search")
                {
                    // Basic client-side filtering via JS is preferred, but returning full list here
                    model.Requests = (await _loansService.GetPendingLoanRequestsAsync(currentUserId)).ToList();
                    return View(model);
                }

                if (model.SelectedRequestCodes == null || !model.SelectedRequestCodes.Any())
                {
                    TempData["ErrorMessage"] = "You have to select at least one record for appropriate action.";
                    model.Requests = (await _loansService.GetPendingLoanRequestsAsync(currentUserId)).ToList();
                    return View(model);
                }

                if (model.ApprovalStatus == "A")
                {
                    var result = await _loansService.ProcessLoanRequestsAsync(model.SelectedRequestCodes, "A", currentUserId, currentUserName);
                    if (result.processed > 0)
                    {
                        TempData["SuccessMessage"] = $"{result.processed} record(s) out of {model.SelectedRequestCodes.Count} selected record(s) have been updated successfully.";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No records updated.";
                    }
                }
                else if (model.ApprovalStatus == "R")
                {
                    var result = await _loansService.ProcessLoanRequestsAsync(model.SelectedRequestCodes, "R", currentUserId, currentUserName);
                    if (result.processed > 0)
                    {
                        TempData["SuccessMessage"] = $"{result.processed} record(s) out of {model.SelectedRequestCodes.Count} selected record(s) have been updated successfully.";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No records updated.";
                    }
                }
                else
                {
                    // Pending
                    var result = await _loansService.ProcessLoanRequestsAsync(model.SelectedRequestCodes, "P", currentUserId, currentUserName);
                    if (result.processed > 0)
                    {
                        TempData["SuccessMessage"] = $"{result.processed} record(s) out of {model.SelectedRequestCodes.Count} selected record(s) have been updated successfully.";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No records updated.";
                    }
                }
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeLoanApprove action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            model.Requests = (await _loansService.GetPendingLoanRequestsAsync(currentUserId)).ToList();
            model.SelectedRequestCodes = new List<string>();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> LoanDisbursed(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _loansService.GetAllLoanDisbursedAsync(currentUserId);
            ViewBag.LoanDisbursedList = data;

            var model = new LoanDisbursedModel { DisbursedDate = DateTime.Now.Date, DeductionStartDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _loansService.GetLoanDisbursedByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoanDisbursed(LoanDisbursedModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var currentUserName = User.Identity?.Name ?? "Unknown";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("LoanDisbursed");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _loansService.DeleteLoanDisbursedAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("LoanDisbursed");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _loansService.BulkUploadLoanDisbursedAsync(model.BulkUploadFile, currentUserId);
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
                    return RedirectToAction("LoanDisbursed");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.LoanDisbursedList = await _loansService.GetAllLoanDisbursedAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _loansService.AddLoanDisbursedAsync(model, currentUserId, currentUserName);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _loansService.UpdateLoanDisbursedAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing LoanDisbursed action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("LoanDisbursed");
        }

        [HttpGet]
        public async Task<IActionResult> GetApprovedLoanRequestData(string lrNo)
        {
            var result = await _loansService.GetApprovedLoanRequestDataAsync(lrNo);
            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> LoanDeduction(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _loansService.GetAllLoanDeductionsAsync(currentUserId);
            ViewBag.LoanDeductionList = data;

            var model = new LoanDeductionModel { DeductionDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _loansService.GetLoanDeductionByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoanDeduction(LoanDeductionModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("LoanDeduction");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _loansService.DeleteLoanDeductionAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("LoanDeduction");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _loansService.BulkUploadLoanDeductionsAsync(model.BulkUploadFile, currentUserId);
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
                    return RedirectToAction("LoanDeduction");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.LoanDeductionList = await _loansService.GetAllLoanDeductionsAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _loansService.AddLoanDeductionAsync(model, currentUserId);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _loansService.UpdateLoanDeductionAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing LoanDeduction action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("LoanDeduction");
        }

        [HttpGet]
        public async Task<IActionResult> GetLoanDisbursedData(string ldNo)
        {
            var result = await _loansService.GetLoanDisbursedDataAsync(ldNo);
            return Json(result);
        }
    }
}