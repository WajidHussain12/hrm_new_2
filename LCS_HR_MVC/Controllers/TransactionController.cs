using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class TransactionController : Controller
    {
        private readonly IEmployeeService _employeeService;
        private readonly ILogger<TransactionController> _logger;

        public TransactionController(IEmployeeService employeeService, ILogger<TransactionController> logger)
        {
            _employeeService = employeeService;
            _logger = logger;
        }

        // Equivalent to Transaction_Hrms_Employee_Salary
        [HttpGet]
        public async Task<IActionResult> EmployeeSalaryDetails()
        {
            var model = new EmployeeSalaryListViewModel
            {
                SalaryDetails = await _employeeService.GetAllEmployeeSalariesAsync()
            };
            
            ViewBag.AllowancesList = await _employeeService.GetAllowancesForSalaryAsync();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeSalaryDetails(EmployeeSalaryListViewModel model, string action, string? FixBasicSalary)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeSalaryDetails");
                }
                
                if (action == "Save" && !string.IsNullOrEmpty(model.NewSalaryDetail.EmpNo))
                {
                    if (!string.IsNullOrEmpty(FixBasicSalary) && decimal.TryParse(FixBasicSalary, out decimal basicSalary))
                    {
                        // Use Standard Breakup Logic
                        bool saved = await _employeeService.InsertStandardSalaryBreakupAsync(model.NewSalaryDetail.EmpNo, basicSalary, currentUserId);
                        if (saved)
                        {
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Failed to save record.";
                        }
                    }
                    else
                    {
                         TempData["ErrorMessage"] = "Invalid Salary Amount.";
                    }
                }
                else if (action == "Add" && !string.IsNullOrEmpty(model.NewSalaryDetail.EmpNo))
                {
                    // Adding individual custom allowance row
                    if (string.IsNullOrEmpty(model.NewSalaryDetail.ADCode) || model.NewSalaryDetail.ADCode == "0")
                    {
                        ModelState.AddModelError("NewSalaryDetail.ADCode", "Allowance is required");
                    }
                    
                    if (ModelState.IsValid)
                    {
                        bool saved = await _employeeService.AddEmployeeSalaryAsync(model.NewSalaryDetail, currentUserId);
                        if (saved)
                        {
                            TempData["SuccessMessage"] = "Record Added Successfully";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeSalaryDetails action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            model.SalaryDetails = await _employeeService.GetAllEmployeeSalariesAsync();
            ViewBag.AllowancesList = await _employeeService.GetAllowancesForSalaryAsync();

            return View(model);
        }
        [HttpGet]
        public async Task<IActionResult> EmployeeJobDetails(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeeJobDetailsAsync(currentUserId);
            ViewBag.JobDetailsList = data;

            var model = new EmployeeJobDetailModel { EffectiveFrom = DateTime.Now.Date, DeptFromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeeJobDetailByCodeAsync(code);
                if (existing != null) model = existing;
            }

            var setupService = HttpContext.RequestServices.GetService<ISetupService>();
            if (setupService != null)
            {
                var bus = await setupService.GetBusinessUnitsAsync();
                var buItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                buItems.AddRange(bus);
                ViewBag.DivisionsList = buItems;

                var deptItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                ViewBag.DepartmentsList = deptItems;

                var subItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                ViewBag.SubDepartmentsList = subItems;

                var desigItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                ViewBag.DesignationsList = desigItems;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeJobDetails(EmployeeJobDetailModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var isAdmin = User.Claims.FirstOrDefault(c => c.Type == "RoleDescription")?.Value == "Administrator";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeJobDetails");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeeJobDetailAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmployeeJobDetails");
                }

                if (model.BUID == "00") ModelState.AddModelError("BUID", "Required!");
                if (model.ParentDeptId == "00") ModelState.AddModelError("ParentDeptId", "Required!");
                if (model.SubDeptId == "00") ModelState.AddModelError("SubDeptId", "Required!");
                if (model.JobCode == "00") ModelState.AddModelError("JobCode", "Required!");

                if (!ModelState.IsValid)
                {
                    ViewBag.JobDetailsList = await _employeeService.GetAllEmployeeJobDetailsAsync(currentUserId);
                    var setupService = HttpContext.RequestServices.GetService<ISetupService>();
                    if (setupService != null)
                    {
                        var bus = await setupService.GetBusinessUnitsAsync();
                        var buItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                        buItems.AddRange(bus);
                        ViewBag.DivisionsList = buItems;

                        ViewBag.DepartmentsList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                        ViewBag.SubDepartmentsList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                        ViewBag.DesignationsList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    }
                    return View(model);
                }

                DateTime serverDate = DateTime.Now; // Should get from context if matching `StateHelper.workingdate` exactly

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeeJobDetailAsync(model, currentUserId, serverDate, isAdmin);
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
                        bool updated = await _employeeService.UpdateEmployeeJobDetailAsync(model, currentUserId, serverDate, isAdmin);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeJobDetails action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("EmployeeJobDetails");
        }

        [HttpGet]
        public async Task<IActionResult> GetParentDepartmentsByBU(string buId)
        {
            var depts = await _employeeService.GetParentDepartmentsByBUAsync(buId);
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            items.AddRange(depts);
            return Json(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetSubDepartmentsAndJobsByParent(string parentId)
        {
            var subs = await _employeeService.GetSubDepartmentsByParentAsync(parentId);
            var subItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            subItems.AddRange(subs);

            var jobs = await _employeeService.GetDesignationsByParentAsync(parentId);
            var jobItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            jobItems.AddRange(jobs);

            return Json(new { subs = subItems, jobs = jobItems });
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeBU(string empNo)
        {
            var result = await _employeeService.GetEmployeeDetailByCodeAsync(empNo);
            return Json(result);
        }
        [HttpGet]
        public async Task<IActionResult> EmployeeDepartmentDetails(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeeDepartmentDetailsAsync(currentUserId);
            ViewBag.DepartmentDetailsList = data;

            var model = new EmployeeDepartmentDetailModel { FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeeDepartmentDetailByCodeAsync(code);
                if (existing != null) model = existing;
            }

            var setupService = HttpContext.RequestServices.GetService<ISetupService>();
            if (setupService != null)
            {
                var cities = await setupService.GetCitiesByUserAsync(currentUserId);
                var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                cityItems.AddRange(cities);
                ViewBag.CitiesList = cityItems;

                var bus = await setupService.GetBusinessUnitsAsync();
                var buItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
                buItems.AddRange(bus);
                ViewBag.DivisionsList = buItems;

                var deptItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                ViewBag.DepartmentsList = deptItems;

                var subItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                ViewBag.SubDepartmentsList = subItems;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeDepartmentDetails(EmployeeDepartmentDetailModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var isAdmin = User.Claims.FirstOrDefault(c => c.Type == "RoleDescription")?.Value == "Administrator";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeDepartmentDetails");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeeDepartmentDetailAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmployeeDepartmentDetails");
                }

                if (model.CityCode == "00") ModelState.AddModelError("CityCode", "Required!");
                if (model.BUID == "0" || model.BUID == "00") ModelState.AddModelError("BUID", "Required!");
                if (model.ParentDeptId == "00") ModelState.AddModelError("ParentDeptId", "Required!");
                if (model.SubDeptId == "00") ModelState.AddModelError("SubDeptId", "Required!");

                if (!ModelState.IsValid)
                {
                    ViewBag.DepartmentDetailsList = await _employeeService.GetAllEmployeeDepartmentDetailsAsync(currentUserId);
                    var setupService = HttpContext.RequestServices.GetService<ISetupService>();
                    if (setupService != null)
                    {
                        var cities = await setupService.GetCitiesByUserAsync(currentUserId);
                        var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                        cityItems.AddRange(cities);
                        ViewBag.CitiesList = cityItems;

                        var bus = await setupService.GetBusinessUnitsAsync();
                        var buItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
                        buItems.AddRange(bus);
                        ViewBag.DivisionsList = buItems;

                        ViewBag.DepartmentsList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                        ViewBag.SubDepartmentsList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    }
                    return View(model);
                }

                // Check Department Strength
                string strengthWarning = await _employeeService.GetDepartmentStrengthValidationMessageAsync(model.ParentDeptId, model.SubDeptId, model.CityCode);
                if (!string.IsNullOrEmpty(strengthWarning))
                {
                    TempData["ErrorMessage"] = strengthWarning;
                    // Proceeding to allow exception block to catch or just returning based on legacy rule (it throws exception so it blocks)
                    // We'll mimic throw ArgumentException to jump to catch block
                    throw new ArgumentException(strengthWarning);
                }

                DateTime serverDate = DateTime.Now; // Mocking StateHelper.workingdate

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeeDepartmentDetailAsync(model, currentUserId, serverDate, isAdmin);
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
                        bool updated = await _employeeService.UpdateEmployeeDepartmentDetailAsync(model, currentUserId, serverDate, isAdmin);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeDepartmentDetails action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("EmployeeDepartmentDetails");
        }
        [HttpGet]
        public async Task<IActionResult> EmployeeContracts(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeeContractsAsync(currentUserId);
            ViewBag.ContractsList = data;

            var model = new EmployeeContractModel { FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeeContractByCodeAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeContracts(EmployeeContractModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeContracts");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeeContractAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmployeeContracts");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.ContractsList = await _employeeService.GetAllEmployeeContractsAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeeContractAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateEmployeeContractAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeContracts action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("EmployeeContracts");
        }
        [HttpGet]
        public async Task<IActionResult> EmployeeBankDetails(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeeBankDetailsAsync(currentUserId);
            ViewBag.BankDetailsList = data;

            var banks = await _employeeService.GetBankListAsync();
            var bankItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
            bankItems.AddRange(banks);
            ViewBag.BanksList = bankItems;

            var model = new EmployeeBankDetailModel { FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeeBankDetailByCodeAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeBankDetails(EmployeeBankDetailModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeBankDetails");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeeBankDetailAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmployeeBankDetails");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _employeeService.BulkUploadEmployeeBankDetailsAsync(model.BulkUploadFile, currentUserId);
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
                        TempData["ErrorMessage"] = "Please select a valid CSV file.";
                    }
                    return RedirectToAction("EmployeeBankDetails");
                }

                if (model.BankName == "0") ModelState.AddModelError("BankName", "Required");

                if (!ModelState.IsValid)
                {
                    ViewBag.BankDetailsList = await _employeeService.GetAllEmployeeBankDetailsAsync(currentUserId);
                    var banks = await _employeeService.GetBankListAsync();
                    var bankItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
                    bankItems.AddRange(banks);
                    ViewBag.BanksList = bankItems;
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeeBankDetailAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateEmployeeBankDetailAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeBankDetails action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("EmployeeBankDetails");
        }
        [HttpGet]
        public async Task<IActionResult> EmployeePayStructure(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeePayStructuresAsync(currentUserId);
            ViewBag.PayStructureList = data;

            var model = new EmployeePayStructureModel { PayStrucDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeePayStructureByCodeAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeePayStructure(EmployeePayStructureModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeePayStructure");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeePayStructureAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmployeePayStructure");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.PayStructureList = await _employeeService.GetAllEmployeePayStructuresAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeePayStructureAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateEmployeePayStructureAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeePayStructure action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("EmployeePayStructure");
        }
        #region Employee Assets
        [HttpGet]
        public async Task<IActionResult> EmployeeAssets(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetEmployeeAssetsAsync(""); // Initial load show all or limit in view
            ViewBag.AssetList = data;

            var model = new EmployeeAssetModel { FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeeAssetByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeAssets(EmployeeAssetModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeAssets");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeeAssetAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeAssets");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.AssetList = await _employeeService.GetEmployeeAssetsAsync("");
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeeAssetAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateEmployeeAssetAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeeAsset action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeAssets");
        }
        #endregion
        #region Employee Training Details
        [HttpGet]
        public async Task<IActionResult> EmployeeTrainingDetails(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeeTrainingsAsync(currentUserId);
            ViewBag.TrainingList = data;

            var buId = "1"; // Assuming a default BU or fetched based on user profile
            var pDepts = await _employeeService.GetParentDepartmentsByBUAsync(buId);
            ViewBag.PDepts = new SelectList(pDepts, "Value", "Text");

            var setupService = HttpContext.RequestServices.GetService<ISetupService>();
            if (setupService != null)
            {
                var countries = await setupService.GetAllCountriesAsync();
                ViewBag.Countries = new SelectList(countries, "Code", "FullName");
            }
            else
            {
                ViewBag.Countries = new SelectList(new List<dynamic>(), "Code", "FullName");
            }

            var model = new EmployeeTrainingModel { FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeeTrainingByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeTrainingDetails(EmployeeTrainingModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeTrainingDetails");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeeTrainingAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeTrainingDetails");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.TrainingList = await _employeeService.GetAllEmployeeTrainingsAsync(currentUserId);
                    ViewBag.PDepts = new SelectList(await _employeeService.GetParentDepartmentsByBUAsync("1"), "Value", "Text");
                    
                    var setupService = HttpContext.RequestServices.GetService<ISetupService>();
                    if (setupService != null)
                    {
                        ViewBag.Countries = new SelectList(await setupService.GetAllCountriesAsync(), "Code", "FullName");
                    }
                    else
                    {
                        ViewBag.Countries = new SelectList(new List<dynamic>(), "Code", "FullName");
                    }
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeeTrainingAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateEmployeeTrainingAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeeTrainingDetails action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeTrainingDetails");
        }
        #endregion

        #region Employee Show Cause
        [HttpGet]
        public async Task<IActionResult> EmployeeShowCause(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeeShowCausesAsync(currentUserId);
            ViewBag.ShowCauseList = data;

            var model = new EmployeeShowCauseModel { IssueDate = DateTime.Now.Date, ReplyDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeeShowCauseByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeShowCause(EmployeeShowCauseModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeShowCause");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeeShowCauseAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeeShowCause");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.ShowCauseList = await _employeeService.GetAllEmployeeShowCausesAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeeShowCauseAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateEmployeeShowCauseAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeeShowCause action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeShowCause");
        }
        #endregion
        #region Employee Promotion Awards
        [HttpGet]
        public async Task<IActionResult> EmployeePromotionAwards(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeePromotionAwardsAsync(currentUserId);
            ViewBag.PromotionAwardsList = data;

            var model = new EmployeePromotionAwardModel { AnnouncementDate = DateTime.Now.Date, FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeePromotionAwardByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeePromotionAwards(EmployeePromotionAwardModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeePromotionAwards");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeePromotionAwardAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeePromotionAwards");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.PromotionAwardsList = await _employeeService.GetAllEmployeePromotionAwardsAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeePromotionAwardAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateEmployeePromotionAwardAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeePromotionAwards action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeePromotionAwards");
        }
        #endregion

        #region Employee Part Time
        [HttpGet]
        public async Task<IActionResult> EmployeePartTime(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllEmployeePartTimesAsync(currentUserId);
            ViewBag.PartTimeList = data;

            var model = new EmployeePartTimeModel { FromDate = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetEmployeePartTimeByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeePartTime(EmployeePartTimeModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var isAdmin = User.Claims.FirstOrDefault(c => c.Type == "RoleDescription")?.Value == "Administrator";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeePartTime");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteEmployeePartTimeAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("EmployeePartTime");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.PartTimeList = await _employeeService.GetAllEmployeePartTimesAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddEmployeePartTimeAsync(model, currentUserId, isAdmin);
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
                        bool updated = await _employeeService.UpdateEmployeePartTimeAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing EmployeePartTime action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeePartTime");
        }
        #endregion
        #region Multiple Jobs Approve
        [HttpGet]
        public async Task<IActionResult> MultipleJobsApprove(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllMultipleJobsApproveAsync(currentUserId);
            ViewBag.MultipleJobsList = data;

            var model = new MultipleJobsApproveModel { Date = DateTime.Now.Date };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetMultipleJobsApproveByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MultipleJobsApprove(MultipleJobsApproveModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("MultipleJobsApprove");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteMultipleJobsApproveAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("MultipleJobsApprove");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.MultipleJobsList = await _employeeService.GetAllMultipleJobsApproveAsync(currentUserId);
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddMultipleJobsApproveAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateMultipleJobsApproveAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing MultipleJobsApprove action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("MultipleJobsApprove");
        }
        #endregion

        #region Increment
        [HttpGet]
        public async Task<IActionResult> Increment(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _employeeService.GetAllIncrementsAsync(currentUserId);
            ViewBag.IncrementList = data;

            var setupService = HttpContext.RequestServices.GetService<ISetupService>();
            if (setupService != null)
            {
                var pDepts = await setupService.GetParentDepartmentsByIDAsync(1, 1);
                ViewBag.PDepts = new SelectList(pDepts, "Value", "Text");
            }
            else
            {
                ViewBag.PDepts = new SelectList(new List<dynamic>(), "Value", "Text");
            }

            var model = new IncrementModel { FromDate = DateTime.Now.Date, Mode = "E", Type = "I" };
            if (!string.IsNullOrEmpty(code))
            {
                var existing = await _employeeService.GetIncrementByIdAsync(code);
                if (existing != null) model = existing;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Increment(IncrementModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Increment");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _employeeService.DeleteIncrementAsync(model.Code);
                        if (deleted) TempData["SuccessMessage"] = "Record deleted successfully.";
                    }
                    return RedirectToAction("Increment");
                }

                if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile != null)
                    {
                        var result = await _employeeService.BulkUploadIncrementsAsync(model.BulkUploadFile, currentUserId);
                        if (result.successCount > 0) TempData["SuccessMessage"] = result.message;
                        else TempData["ErrorMessage"] = result.message;
                    }
                    else TempData["ErrorMessage"] = "Please select a valid CSV file.";
                    return RedirectToAction("Increment");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.IncrementList = await _employeeService.GetAllIncrementsAsync(currentUserId);
                    
                    var setupService = HttpContext.RequestServices.GetService<ISetupService>();
                    if (setupService != null)
                    {
                        ViewBag.PDepts = new SelectList(await setupService.GetParentDepartmentsByIDAsync(1, 1), "Value", "Text");
                    }
                    else
                    {
                        ViewBag.PDepts = new SelectList(new List<dynamic>(), "Value", "Text");
                    }
                    return View(model);
                }

                if (action == "Save")
                {
                    bool saved = await _employeeService.AddIncrementAsync(model, currentUserId);
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
                        bool updated = await _employeeService.UpdateIncrementAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing Increment action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("Increment");
        }
        #endregion

        #region Increment Approval
        [HttpGet]
        public async Task<IActionResult> IncrementApproval()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            
            var setupService = HttpContext.RequestServices.GetService<ISetupService>();
            if (setupService != null)
            {
                ViewBag.Cities = new SelectList(await setupService.GetAllCitiesAsync(), "Code", "FullName");
                ViewBag.Depts = new SelectList(await setupService.GetAllSubDepartmentsAsync(), "SDID", "FullName");
            }
            
            var model = new IncrementApprovalViewModel();
            var data = await _employeeService.GetPendingIncrementsAsync(currentUserId, model.CityCode, model.DepartmentId);
            model.Increments = data.ToList();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IncrementApproval(IncrementApprovalViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Search")
                {
                    var data = await _employeeService.GetPendingIncrementsAsync(currentUserId, model.CityCode, model.DepartmentId);
                    model.Increments = data.ToList();
                    
                    var setupService = HttpContext.RequestServices.GetService<ISetupService>();
                    if (setupService != null)
                    {
                        ViewBag.Cities = new SelectList(await setupService.GetAllCitiesAsync(), "Code", "FullName");
                        ViewBag.Depts = new SelectList(await setupService.GetAllSubDepartmentsAsync(), "SDID", "FullName");
                    }
                    return View(model);
                }

                if (action == "Process")
                {
                    // Filter out elements that don't have status changes (if default is 1, keep 2/3)
                    var toProcess = model.Increments.Where(x => x.SelectedStatusId == 2 || x.SelectedStatusId == 3).ToList();
                    
                    if (toProcess.Count == 0)
                    {
                        TempData["ErrorMessage"] = "No status changes selected.";
                    }
                    else
                    {
                        bool success = await _employeeService.ProcessIncrementApprovalsAsync(toProcess, currentUserId);
                        if (success)
                        {
                            TempData["SuccessMessage"] = "Increments processed successfully.";
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Failed to process increments.";
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
                _logger.LogError(ex, "Error processing IncrementApproval action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("IncrementApproval");
        }
        #endregion

        #region Employee Route Code
        [HttpGet]
        public async Task<IActionResult> EmployeeRoutCode(string? code)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            EmployeeRouteCodeModel model;
            if (!string.IsNullOrEmpty(code))
            {
                model = await _employeeService.GetEmployeeRouteCodeByCodeAsync(code) ?? new EmployeeRouteCodeModel();
            }
            else
            {
                model = new EmployeeRouteCodeModel();
            }
            ViewBag.RouteCodeList = await _employeeService.GetAllEmployeeRouteCodesAsync(currentUserId);
            ViewBag.CourierCodeTypes = await _employeeService.GetCourierCodeTypesAsync();
            if (!string.IsNullOrEmpty(model.CityCode))
                ViewBag.Locations = await _employeeService.GetLocationsByCityCodeAsync(model.CityCode);
            else
                ViewBag.Locations = new List<SelectListItem>();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeRoutCode(EmployeeRouteCodeModel model, string action)
        {
            if (action == "Reset") return RedirectToAction("EmployeeRoutCode");

            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            try
            {
                bool result;
                if (action == "Save")
                    result = await _employeeService.AddEmployeeRouteCodeAsync(model, currentUserId);
                else if (action == "Update")
                    result = await _employeeService.UpdateEmployeeRouteCodeAsync(model, currentUserId);
                else if (action == "Delete")
                    result = await _employeeService.DeleteEmployeeRouteCodeAsync(model.Code);
                else
                    return RedirectToAction("EmployeeRoutCode");

                TempData[result ? "SuccessMessage" : "ErrorMessage"] = result
                    ? $"Record {(action == "Save" ? "Saved" : action == "Update" ? "Updated" : "Deleted")} Successfully"
                    : "Operation failed.";
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in EmployeeRoutCode action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }
            return RedirectToAction("EmployeeRoutCode");
        }

        [HttpGet]
        public async Task<IActionResult> GetLocationsByCityCode(string cityCode)
        {
            var locations = await _employeeService.GetLocationsByCityCodeAsync(cityCode);
            return Json(locations.Select(l => new { value = l.Value, text = l.Text }));
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeCityInfo(string empNo)
        {
            var info = await _employeeService.GetEmployeeCityInfoAsync(empNo);
            return Json(info);
        }

        [HttpGet]
        public async Task<IActionResult> GetRouteDescription(string routeCode)
        {
            var desc = await _employeeService.GetRouteDescriptionAsync(routeCode);
            return Json(new { description = desc });
        }
        #endregion

        #region Employee Shift Detail
        [HttpGet]
        public async Task<IActionResult> EmployeeShifts(string? id)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            EmployeeShiftDetailModel model;
            if (!string.IsNullOrEmpty(id))
            {
                model = await _employeeService.GetEmployeeShiftDetailByIdAsync(id) ?? new EmployeeShiftDetailModel();
            }
            else
            {
                model = new EmployeeShiftDetailModel();
            }
            ViewBag.ShiftDetailList = await _employeeService.GetAllEmployeeShiftDetailsAsync(currentUserId);
            ViewBag.ActiveShifts = await _employeeService.GetActiveShiftsAsync();
            var isAdmin = User.Claims.FirstOrDefault(c => c.Type == "RoleDescription")?.Value == "Administrator";
            ViewBag.IsAdmin = isAdmin;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeShifts(EmployeeShiftDetailModel model, string action)
        {
            if (action == "Reset") return RedirectToAction("EmployeeShifts");

            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var isAdmin = User.Claims.FirstOrDefault(c => c.Type == "RoleDescription")?.Value == "Administrator";
            try
            {
                bool result;
                if (action == "Save")
                    result = await _employeeService.AddEmployeeShiftDetailAsync(model, currentUserId, isAdmin);
                else if (action == "Update")
                    result = await _employeeService.UpdateEmployeeShiftDetailAsync(model, currentUserId);
                else if (action == "Delete")
                    result = await _employeeService.DeleteEmployeeShiftDetailAsync(model.Id);
                else if (action == "BulkUpload")
                {
                    if (model.BulkUploadFile == null)
                    {
                        TempData["ErrorMessage"] = "Please select an Excel file to upload.";
                        return RedirectToAction("EmployeeShifts");
                    }
                    var (count, message) = await _employeeService.BulkUploadEmployeeShiftsAsync(model.BulkUploadFile, currentUserId);
                    TempData[count > 0 ? "SuccessMessage" : "ErrorMessage"] = message;
                    return RedirectToAction("EmployeeShifts");
                }
                else
                    return RedirectToAction("EmployeeShifts");

                TempData[result ? "SuccessMessage" : "ErrorMessage"] = result
                    ? $"Record {(action == "Save" ? "Saved" : action == "Update" ? "Updated" : "Deleted")} Successfully"
                    : "Operation failed.";
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in EmployeeShifts action: {Action}", action);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }
            return RedirectToAction("EmployeeShifts");
        }
        #endregion

        #region Employee Search AJAX Helpers

        [HttpGet]
        public async Task<IActionResult> SearchEmployees(string term)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var employees = await _employeeService.GetAllEmployeesAsync(currentUserId);
            var results = employees
                .Where(e => e.EmpNo.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(20)
                .Select(e => new { value = e.EmpNo, label = $"{e.EmpNo} - {e.Name}" });
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeNameByNo(string empNo)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var emp = await _employeeService.GetEmployeePersonalDetailByEmpNoAsync(empNo);
            if (emp == null) return Json(new { name = "" });
            return Json(new { name = emp.Name });
        }

        #endregion

        #region Employee Attendance Adjustment

        [HttpGet]
        public async Task<IActionResult> EmployeeAttendanceAdjust(string? empNo, string? date)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = new AttendanceAdjustmentModel();
            if (!string.IsNullOrEmpty(empNo) && !string.IsNullOrEmpty(date) && DateTime.TryParse(date, out DateTime adjDate))
            {
                var existing = await _employeeService.GetAttendanceAdjustmentAsync(empNo, adjDate);
                if (existing != null) model = existing;
            }
            ViewBag.AdjustmentList = await _employeeService.GetAttendanceAdjustmentsAsync(currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeAttendanceAdjust(AttendanceAdjustmentModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
                return RedirectToAction("EmployeeAttendanceAdjust");

            if (action == "Save")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.AdjustmentList = await _employeeService.GetAttendanceAdjustmentsAsync(currentUserId);
                    return View(model);
                }
                var (success, message) = await _employeeService.AddAttendanceAdjustmentAsync(model, currentUserId);
                TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
                return RedirectToAction("EmployeeAttendanceAdjust");
            }

            if (action == "Update")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.AdjustmentList = await _employeeService.GetAttendanceAdjustmentsAsync(currentUserId);
                    return View(model);
                }
                var (success, message) = await _employeeService.UpdateAttendanceAdjustmentAsync(model, currentUserId);
                TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
                return RedirectToAction("EmployeeAttendanceAdjust");
            }

            if (action == "Delete")
            {
                var (success, message) = await _employeeService.DeleteAttendanceAdjustmentAsync(model.EmpNo, model.AdjustmentDate!.Value);
                TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
                return RedirectToAction("EmployeeAttendanceAdjust");
            }

            return RedirectToAction("EmployeeAttendanceAdjust");
        }

        #endregion

        #region Complete Attendance Adjustment (Bulk Present/Absent Mark)

        [HttpGet]
        public IActionResult CompleteAttendenceAdjustment()
        {
            var model = new BulkPresentMarkModel
            {
                Year = DateTime.Now.Year,
                Month = DateTime.Now.Month,
                FromDate = DateTime.Today,
                ToDate = DateTime.Today
            };
            var absentModel = new BulkAbsentMarkModel
            {
                Year = DateTime.Now.Year,
                Month = DateTime.Now.Month
            };
            ViewBag.AbsentModel = absentModel;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteAttendenceAdjustment(BulkPresentMarkModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "MarkPresent")
            {
                var (success, message) = await _employeeService.BulkMarkPresentAsync(
                    model.File!, model.Year, model.Month, model.IsDateWise,
                    model.FromDate ?? DateTime.Today, model.ToDate ?? DateTime.Today,
                    currentUserId);
                TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
                return RedirectToAction("CompleteAttendenceAdjustment");
            }

            if (action == "MarkAbsent")
            {
                var absentYear = int.TryParse(Request.Form["AbsentYear"], out int ay) ? ay : model.Year;
                var absentMonth = int.TryParse(Request.Form["AbsentMonth"], out int am) ? am : model.Month;
                var absentFile = Request.Form.Files["AbsentFile"];
                var (success, message) = await _employeeService.BulkMarkAbsentAsync(absentFile!, absentYear, absentMonth, currentUserId);
                TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
                return RedirectToAction("CompleteAttendenceAdjustment");
            }

            return RedirectToAction("CompleteAttendenceAdjustment");
        }

        #endregion
    }
}
