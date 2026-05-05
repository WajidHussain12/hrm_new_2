using System.Security.Claims;
using LCS_HR_MVC.Models.Penalty;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class PenaltyController : Controller
    {
        private readonly IPenaltyService _penaltyService;
        private readonly ISetupService _setupService;
        private readonly ILogger<PenaltyController> _logger;

        public PenaltyController(IPenaltyService penaltyService, ISetupService setupService, ILogger<PenaltyController> logger)
        {
            _penaltyService = penaltyService;
            _setupService = setupService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> PenaltyFine(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            
            var data = await _penaltyService.GetAllPenaltyFinesAsync(currentUserId);
            ViewBag.PenaltyList = data;

            var penaltyTypes = await _penaltyService.GetPenaltyTypesAsync();
            ViewBag.PenaltyTypes = new SelectList(penaltyTypes, "Value", "Text");

            var divisions = await _penaltyService.GetDivisionsAsync();
            ViewBag.Divisions = new SelectList(divisions, "Value", "Text");

            var model = new PenaltyFineModel { FineDate = DateTime.Now.Date, Mode = "E" };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _penaltyService.GetPenaltyFineByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PenaltyFine(PenaltyFineModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("PenaltyFine");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.ID) || model.ID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _penaltyService.DeletePenaltyFineAsync(model.ID);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("PenaltyFine");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _penaltyService.BulkUploadPenaltyFineAsync(model.BulkUploadFile, currentUserId);
                        if (result.successCount > 0) TempData["SuccessMessage"] = result.message;
                        else TempData["ErrorMessage"] = result.message;
                    }
                    else TempData["ErrorMessage"] = "Please select a valid CSV file.";
                    return RedirectToAction("PenaltyFine");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.PenaltyList = await _penaltyService.GetAllPenaltyFinesAsync(currentUserId);
                    ViewBag.PenaltyTypes = new SelectList(await _penaltyService.GetPenaltyTypesAsync(), "Value", "Text");
                    ViewBag.Divisions = new SelectList(await _penaltyService.GetDivisionsAsync(), "Value", "Text");
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _penaltyService.AddPenaltyFineAsync(model, currentUserId);
                    if (saved) TempData["SuccessMessage"] = "Record Saved Successfully";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.ID) || model.ID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _penaltyService.UpdatePenaltyFineAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing PenaltyFine action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("PenaltyFine");
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentsByDivision(int divisionId)
        {
            var results = await _penaltyService.GetDepartmentsByDivisionAsync(divisionId);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetSubDepartmentsByDepartment(int departmentId)
        {
            var results = await _penaltyService.GetSubDepartmentsByDepartmentAsync(departmentId);
            return Json(results);
        }
    }
}