using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class CommissionController : Controller
    {
        private readonly ICommissionService _commissionService;
        private readonly ISetupService _setupService;
        private readonly ILogger<CommissionController> _logger;

        public CommissionController(ICommissionService commissionService, ISetupService setupService, ILogger<CommissionController> logger)
        {
            _commissionService = commissionService;
            _setupService = setupService;
            _logger = logger;
        }

        #region Employee Commission Details
        [HttpGet]
        public async Task<IActionResult> EmployeeCommDetails(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _commissionService.GetAllEmployeeCommissionsAsync(currentUserId);
            ViewBag.EmployeeCommissionList = data;

            var cities = await _setupService.GetAllCitiesAsync();
            ViewBag.Cities = new SelectList(cities, "Code", "FullName");

            var model = new EmployeeCommissionModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _commissionService.GetEmployeeCommissionByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeCommDetails(EmployeeCommissionModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeCommDetails");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _commissionService.DeleteEmployeeCommissionAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeCommDetails");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.EmployeeCommissionList = await _commissionService.GetAllEmployeeCommissionsAsync(currentUserId);
                    var cities = await _setupService.GetAllCitiesAsync();
                    ViewBag.Cities = new SelectList(cities, "Code", "FullName");
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _commissionService.AddEmployeeCommissionAsync(model, currentUserId);
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
                        bool updated = await _commissionService.UpdateEmployeeCommissionAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeeCommDetails action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeCommDetails");
        }

        [HttpGet]
        public async Task<IActionResult> SearchRoutes(string term, string cityCode)
        {
            if(string.IsNullOrEmpty(cityCode) || cityCode == "00") return Json(new List<dynamic>());
            var results = await _commissionService.SearchRoutesAsync(term, cityCode);
            return Json(results);
        }
        #endregion

        #region Tag Commission
        [HttpGet]
        public async Task<IActionResult> TagCommission()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var cities = await _setupService.GetAllCitiesAsync();
            ViewBag.Cities = new SelectList(cities, "Code", "FullName");
            
            var model = new TagCommissionViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TagCommission(TagCommissionViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("TagCommission");
            }

            if (!ModelState.IsValid)
            {
                var cities = await _setupService.GetAllCitiesAsync();
                ViewBag.Cities = new SelectList(cities, "Code", "FullName");
                return View(model);
            }

            try
            {
                var result = await _commissionService.ProcessTagCommissionAsync(model, currentUserId);
                if (result.success) TempData["SuccessMessage"] = result.message;
                else TempData["ErrorMessage"] = result.message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing TagCommission");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("TagCommission");
        }
        #endregion
    }
}