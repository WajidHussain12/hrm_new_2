using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class ExtrasController : Controller
    {
        private readonly IExtrasService _extrasService;
        private readonly ILogger<ExtrasController> _logger;

        public ExtrasController(IExtrasService extrasService, ILogger<ExtrasController> logger)
        {
            _extrasService = extrasService;
            _logger = logger;
        }

        #region Employee Extra
        [HttpGet]
        public async Task<IActionResult> EmployeeExtra(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _extrasService.GetAllEmployeeExtrasAsync(currentUserId);
            ViewBag.EmployeeExtraList = data;

            // Load Extra Types (ParentId = 0)
            var extraTypes = await _extrasService.GetExtraTypesAsync(0);
            ViewBag.ExtraTypes = new SelectList(extraTypes, "Value", "Text");

            var model = new EmployeeExtraModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _extrasService.GetEmployeeExtraByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeExtra(EmployeeExtraModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeExtra");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _extrasService.DeleteEmployeeExtraAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeExtra");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _extrasService.BulkUploadEmployeeExtrasAsync(model.BulkUploadFile, currentUserId);
                        if (result.successCount > 0) TempData["SuccessMessage"] = result.message;
                        else TempData["ErrorMessage"] = result.message;
                    }
                    else TempData["ErrorMessage"] = "Please select a valid Excel file.";
                    return RedirectToAction("EmployeeExtra");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.EmployeeExtraList = await _extrasService.GetAllEmployeeExtrasAsync(currentUserId);
                    var extraTypes = await _extrasService.GetExtraTypesAsync(0);
                    ViewBag.ExtraTypes = new SelectList(extraTypes, "Value", "Text");
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _extrasService.AddEmployeeExtraAsync(model, currentUserId);
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
                        bool updated = await _extrasService.UpdateEmployeeExtraAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeeExtra action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeExtra");
        }
        
        [HttpGet]
        public async Task<IActionResult> GetExtraSubTypes(int parentId)
        {
            var results = await _extrasService.GetExtraTypesAsync(parentId);
            return Json(results);
        }
        #endregion

        #region Employee Extra Fixed
        [HttpGet]
        public async Task<IActionResult> EmployeeExtraFixed(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _extrasService.GetAllEmployeeExtrasFixedAsync(currentUserId);
            ViewBag.EmployeeExtraFixedList = data;

            // Extra type fixed is generally parentId = 4
            var extraSubTypes = await _extrasService.GetExtraTypesAsync(4);
            ViewBag.ExtraSubTypes = new SelectList(extraSubTypes, "Value", "Text");

            var model = new EmployeeExtraFixedModel { FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _extrasService.GetEmployeeExtraFixedByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeExtraFixed(EmployeeExtraFixedModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeExtraFixed");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _extrasService.DeleteEmployeeExtraFixedAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeExtraFixed");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _extrasService.BulkUploadEmployeeExtrasFixedAsync(model.BulkUploadFile, currentUserId);
                        if (result.successCount > 0) TempData["SuccessMessage"] = result.message;
                        else TempData["ErrorMessage"] = result.message;
                    }
                    else TempData["ErrorMessage"] = "Please select a valid Excel file.";
                    return RedirectToAction("EmployeeExtraFixed");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.EmployeeExtraFixedList = await _extrasService.GetAllEmployeeExtrasFixedAsync(currentUserId);
                    var extraSubTypes = await _extrasService.GetExtraTypesAsync(4);
                    ViewBag.ExtraSubTypes = new SelectList(extraSubTypes, "Value", "Text");
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _extrasService.AddEmployeeExtraFixedAsync(model, currentUserId);
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
                        bool updated = await _extrasService.UpdateEmployeeExtraFixedAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeeExtraFixed action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeExtraFixed");
        }
        #endregion

        #region Emp AD Details
        [HttpGet]
        public async Task<IActionResult> EmpADDetails(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _extrasService.GetAllEmpADDetailsAsync(currentUserId);
            ViewBag.EmpADDetailsList = data;

            var model = new EmpADDetailsModel { EffectiveFrom = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _extrasService.GetEmpADDetailsByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmpADDetails(EmpADDetailsModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmpADDetails");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _extrasService.DeleteEmpADDetailsAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmpADDetails");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _extrasService.BulkUploadEmpADDetailsAsync(model.BulkUploadFile, currentUserId);
                        if (result.successCount > 0) TempData["SuccessMessage"] = result.message;
                        else TempData["ErrorMessage"] = result.message;
                    }
                    else TempData["ErrorMessage"] = "Please select a valid Excel file.";
                    return RedirectToAction("EmpADDetails");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.EmpADDetailsList = await _extrasService.GetAllEmpADDetailsAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _extrasService.AddEmpADDetailsAsync(model, currentUserId);
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
                        bool updated = await _extrasService.UpdateEmpADDetailsAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmpADDetails action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmpADDetails");
        }

        [HttpGet]
        public async Task<IActionResult> SearchADCodes(string term)
        {
            var results = await _extrasService.SearchADCodesAsync(term);
            return Json(results);
        }
        #endregion

        #region Extra Hours Approval
        [HttpGet]
        public async Task<IActionResult> ExtraHoursApproval()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            
            var cities = await _extrasService.GetCitiesAsync();
            ViewBag.Cities = new SelectList(cities, "Value", "Text");

            var model = new ExtraHoursApprovalViewModel();
            model.Extras = (await _extrasService.GetPendingExtraHoursAsync(currentUserId, null, null)).ToList();
            
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExtraHoursApproval(ExtraHoursApprovalViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Search")
                {
                    model.Extras = (await _extrasService.GetPendingExtraHoursAsync(currentUserId, model.CityCode, model.DeptCode)).ToList();
                    var cities = await _extrasService.GetCitiesAsync();
                    ViewBag.Cities = new SelectList(cities, "Value", "Text");
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
                        var result = await _extrasService.ProcessExtraHoursAsync(model.SelectedCodes, model.ApprovalStatus);
                        if (result.processed > 0)
                            TempData["SuccessMessage"] = $"{result.processed} Record(s) Updated Successfully.";
                        else
                            TempData["ErrorMessage"] = "Failed to update records.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ExtraHoursApproval action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("ExtraHoursApproval");
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentsByCity(string cityCode)
        {
            var results = await _extrasService.GetDepartmentsByCityAsync(cityCode);
            return Json(results);
        }
        #endregion
    }
}