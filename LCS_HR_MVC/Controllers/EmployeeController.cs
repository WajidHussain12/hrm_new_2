using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class EmployeeController : Controller
    {
        private readonly IEmployeeService _employeeService;
        private readonly ISetupService _setupService;
        private readonly ILogger<EmployeeController> _logger;

        public EmployeeController(IEmployeeService employeeService, ISetupService setupService, ILogger<EmployeeController> logger)
        {
            _employeeService = employeeService;
            _setupService = setupService;
            _logger = logger;
        }

        private string CurrentUserId => User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
        private bool IsAdmin => User.Claims.FirstOrDefault(c => c.Type == "RoleDescription")?.Value == "Administrator";

        #region Employee Personal Detail

        [HttpGet]
        public async Task<IActionResult> PersonalDetail(string? id)
        {
            var model = new EmployeePersonalDetailModel();
            if (!string.IsNullOrEmpty(id))
            {
                var existing = await _employeeService.GetEmployeePersonalDetailByEmpNoAsync(id);
                if (existing != null) model = existing;
            }

            await LoadPersonalDetailDropDowns(model);
            ViewBag.EmployeeList = await _employeeService.GetAllEmployeesAsync(CurrentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PersonalDetail(EmployeePersonalDetailModel model, string action)
        {
            if (action == "Reset")
                return RedirectToAction("PersonalDetail");

            if (action == "Save")
            {
                if (!ModelState.IsValid)
                {
                    await LoadPersonalDetailDropDowns(model);
                    ViewBag.EmployeeList = await _employeeService.GetAllEmployeesAsync(CurrentUserId);
                    return View(model);
                }

                var (success, newEmpNo, message) = await _employeeService.AddEmployeePersonalDetailAsync(model, CurrentUserId);
                if (success)
                {
                    TempData["SuccessMessage"] = message;
                    return RedirectToAction("PersonalDetail", new { id = newEmpNo });
                }
                TempData["ErrorMessage"] = message;
                await LoadPersonalDetailDropDowns(model);
                ViewBag.EmployeeList = await _employeeService.GetAllEmployeesAsync(CurrentUserId);
                return View(model);
            }

            if (action == "Update")
            {
                if (!ModelState.IsValid)
                {
                    await LoadPersonalDetailDropDowns(model);
                    ViewBag.EmployeeList = await _employeeService.GetAllEmployeesAsync(CurrentUserId);
                    return View(model);
                }

                bool updated = await _employeeService.UpdateEmployeePersonalDetailAsync(model, CurrentUserId);
                if (updated)
                {
                    TempData["SuccessMessage"] = "Record Updated Successfully";
                    return RedirectToAction("PersonalDetail", new { id = model.EmpNo });
                }
                TempData["ErrorMessage"] = "Error updating record.";
                await LoadPersonalDetailDropDowns(model);
                ViewBag.EmployeeList = await _employeeService.GetAllEmployeesAsync(CurrentUserId);
                return View(model);
            }

            if (action == "Delete")
            {
                bool deleted = await _employeeService.DeleteEmployeePersonalDetailAsync(model.EmpNo);
                if (deleted)
                    TempData["SuccessMessage"] = "Record deleted successfully";
                else
                    TempData["ErrorMessage"] = "Cannot delete: record is referenced by other data.";
                return RedirectToAction("PersonalDetail");
            }

            return RedirectToAction("PersonalDetail");
        }

        private async Task LoadPersonalDetailDropDowns(EmployeePersonalDetailModel model)
        {
            ViewBag.Countries = await _setupService.GetCountriesSelectAsync();
            ViewBag.Cities = !string.IsNullOrEmpty(model.PCountryCode) && model.PCountryCode != "00"
                ? await _setupService.GetCitiesByCountryAsync(model.PCountryCode)
                : new List<SelectListItem>();
            ViewBag.Locations = model.PCityCode != "00" && !string.IsNullOrEmpty(model.PCityCode)
                ? await _employeeService.GetLocationsByCityCodeAsync(model.PCityCode)
                : new List<SelectListItem>();
            ViewBag.EmployeeTypes = await _setupService.GetEmployeeTypesSelectAsync();
            ViewBag.JobTypes = await _setupService.GetJobTypesAsync();
            ViewBag.ThirdParties = await _setupService.GetThirdPartiesAsync();
            ViewBag.Divisions = await _setupService.GetDivisionsSelectAsync();
            ViewBag.Companies = await _setupService.GetCompaniesAsync();
            ViewBag.BusinessUnits = await _setupService.GetBusinessUnitsAsync();
            ViewBag.ParentDepts = await _setupService.GetParentDepartmentsAsync();
            ViewBag.Designations = !string.IsNullOrEmpty(model.DepartmentCode) && model.DepartmentCode != "0"
                ? await _employeeService.GetDesignationsByParentAsync(model.DepartmentCode)
                : new List<SelectListItem>();
        }

        // AJAX: Get cities by country
        [HttpGet]
        public async Task<IActionResult> GetCitiesByCountry(string countryCode)
        {
            var cities = await _setupService.GetCitiesByCountryAsync(countryCode);
            return Json(cities.Select(c => new { value = c.Value, text = c.Text }));
        }

        // AJAX: Get locations by city code
        [HttpGet]
        public async Task<IActionResult> GetLocationsByCity(string cityCode)
        {
            var locations = await _employeeService.GetLocationsByCityCodeAsync(cityCode);
            return Json(locations.Select(l => new { value = l.Value, text = l.Text }));
        }

        // AJAX: Get designations by department
        [HttpGet]
        public async Task<IActionResult> GetDesignationsByDept(string deptId)
        {
            var designations = await _employeeService.GetDesignationsByParentAsync(deptId);
            return Json(designations.Select(d => new { value = d.Value, text = d.Text }));
        }

        #endregion

        #region Employee Experience

        [HttpGet]
        public async Task<IActionResult> Experience(string empNo, int? sno)
        {
            if (string.IsNullOrEmpty(empNo))
                return RedirectToAction("PersonalDetail");

            EmployeeExperienceModel model;
            if (sno.HasValue)
            {
                model = await _employeeService.GetEmployeeExperienceBySnAsync(empNo, sno.Value) ?? new EmployeeExperienceModel { EmpNo = empNo };
            }
            else
            {
                model = new EmployeeExperienceModel { EmpNo = empNo };
                var emp = await _employeeService.GetEmployeePersonalDetailByEmpNoAsync(empNo);
                if (emp != null) model.EmpName = emp.Name;
            }

            ViewBag.ExperienceList = await _employeeService.GetEmployeeExperiencesAsync(empNo);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Experience(EmployeeExperienceModel model, string action)
        {
            if (action == "Reset")
                return RedirectToAction("Experience", new { empNo = model.EmpNo });

            if (action == "Save")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.ExperienceList = await _employeeService.GetEmployeeExperiencesAsync(model.EmpNo);
                    return View(model);
                }
                await _employeeService.AddEmployeeExperienceAsync(model, CurrentUserId);
                TempData["SuccessMessage"] = "Record Saved Successfully";
                return RedirectToAction("Experience", new { empNo = model.EmpNo });
            }

            if (action == "Update")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.ExperienceList = await _employeeService.GetEmployeeExperiencesAsync(model.EmpNo);
                    return View(model);
                }
                await _employeeService.UpdateEmployeeExperienceAsync(model, CurrentUserId);
                TempData["SuccessMessage"] = "Record Updated Successfully";
                return RedirectToAction("Experience", new { empNo = model.EmpNo });
            }

            if (action == "Delete")
            {
                await _employeeService.DeleteEmployeeExperienceAsync(model.EmpNo, model.Sno);
                TempData["SuccessMessage"] = "Record deleted successfully";
                return RedirectToAction("Experience", new { empNo = model.EmpNo });
            }

            return RedirectToAction("Experience", new { empNo = model.EmpNo });
        }

        #endregion

        #region Employee Education

        [HttpGet]
        public async Task<IActionResult> Education(string empNo, int? sno)
        {
            if (string.IsNullOrEmpty(empNo))
                return RedirectToAction("PersonalDetail");

            EmployeeEducationModel model;
            if (sno.HasValue)
            {
                model = await _employeeService.GetEmployeeEducationBySnAsync(empNo, sno.Value) ?? new EmployeeEducationModel { EmpNo = empNo };
            }
            else
            {
                model = new EmployeeEducationModel { EmpNo = empNo };
                var emp = await _employeeService.GetEmployeePersonalDetailByEmpNoAsync(empNo);
                if (emp != null) model.EmpName = emp.Name;
            }

            ViewBag.EducationList = await _employeeService.GetEmployeeEducationsAsync(empNo);
            ViewBag.Countries = await _setupService.GetCountriesSelectAsync();
            ViewBag.Cities = model.CountryCode != "00"
                ? await _setupService.GetCitiesByCountryAsync(model.CountryCode)
                : new List<SelectListItem>();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Education(EmployeeEducationModel model, string action)
        {
            if (action == "Reset")
                return RedirectToAction("Education", new { empNo = model.EmpNo });

            if (action == "Save")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.EducationList = await _employeeService.GetEmployeeEducationsAsync(model.EmpNo);
                    ViewBag.Countries = await _setupService.GetCountriesSelectAsync();
                    ViewBag.Cities = new List<SelectListItem>();
                    return View(model);
                }
                await _employeeService.AddEmployeeEducationAsync(model, CurrentUserId);
                TempData["SuccessMessage"] = "Record Saved Successfully";
                return RedirectToAction("Education", new { empNo = model.EmpNo });
            }

            if (action == "Update")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.EducationList = await _employeeService.GetEmployeeEducationsAsync(model.EmpNo);
                    ViewBag.Countries = await _setupService.GetCountriesSelectAsync();
                    ViewBag.Cities = new List<SelectListItem>();
                    return View(model);
                }
                await _employeeService.UpdateEmployeeEducationAsync(model, CurrentUserId);
                TempData["SuccessMessage"] = "Record Updated Successfully";
                return RedirectToAction("Education", new { empNo = model.EmpNo });
            }

            if (action == "Delete")
            {
                await _employeeService.DeleteEmployeeEducationAsync(model.EmpNo, model.Sno);
                TempData["SuccessMessage"] = "Record deleted successfully";
                return RedirectToAction("Education", new { empNo = model.EmpNo });
            }

            return RedirectToAction("Education", new { empNo = model.EmpNo });
        }

        #endregion

        #region Employee Medical History

        [HttpGet]
        public async Task<IActionResult> MedicalHistory(string empNo, int? sno)
        {
            if (string.IsNullOrEmpty(empNo))
                return RedirectToAction("PersonalDetail");

            EmployeeMedicalHistoryModel model;
            if (sno.HasValue)
            {
                model = await _employeeService.GetEmployeeMedicalHistoryBySnAsync(empNo, sno.Value) ?? new EmployeeMedicalHistoryModel { EmpNo = empNo };
            }
            else
            {
                model = new EmployeeMedicalHistoryModel { EmpNo = empNo };
                var emp = await _employeeService.GetEmployeePersonalDetailByEmpNoAsync(empNo);
                if (emp != null) model.EmpName = emp.Name;
            }

            ViewBag.MedicalList = await _employeeService.GetEmployeeMedicalHistoriesAsync(empNo);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MedicalHistory(EmployeeMedicalHistoryModel model, string action)
        {
            if (action == "Reset")
                return RedirectToAction("MedicalHistory", new { empNo = model.EmpNo });

            if (action == "Save")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.MedicalList = await _employeeService.GetEmployeeMedicalHistoriesAsync(model.EmpNo);
                    return View(model);
                }
                await _employeeService.AddEmployeeMedicalHistoryAsync(model, CurrentUserId);
                TempData["SuccessMessage"] = "Record Saved Successfully";
                return RedirectToAction("MedicalHistory", new { empNo = model.EmpNo });
            }

            if (action == "Update")
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.MedicalList = await _employeeService.GetEmployeeMedicalHistoriesAsync(model.EmpNo);
                    return View(model);
                }
                await _employeeService.UpdateEmployeeMedicalHistoryAsync(model, CurrentUserId);
                TempData["SuccessMessage"] = "Record Updated Successfully";
                return RedirectToAction("MedicalHistory", new { empNo = model.EmpNo });
            }

            if (action == "Delete")
            {
                await _employeeService.DeleteEmployeeMedicalHistoryAsync(model.EmpNo, model.Sno);
                TempData["SuccessMessage"] = "Record deleted successfully";
                return RedirectToAction("MedicalHistory", new { empNo = model.EmpNo });
            }

            return RedirectToAction("MedicalHistory", new { empNo = model.EmpNo });
        }

        #endregion

        #region Employee Medical Survey

        [HttpGet]
        public async Task<IActionResult> MedicalSurvey(string? empNo)
        {
            var model = new EmployeeMedicalSurveyModel();
            if (!string.IsNullOrEmpty(empNo))
            {
                var survey = await _employeeService.GetEmployeeMedicalSurveyAsync(empNo);
                if (survey != null) model = survey;
                // Ensure at least 10 rows in family list
                while (model.FamilyMembers.Count < 10)
                    model.FamilyMembers.Add(new MedicalSurveyFamilyMember());
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MedicalSurvey(EmployeeMedicalSurveyModel model, string action)
        {
            if (action == "Search")
            {
                return RedirectToAction("MedicalSurvey", new { empNo = model.EmpNo });
            }

            if (action == "Save")
            {
                bool saved = await _employeeService.SaveEmployeeMedicalSurveyAsync(model, CurrentUserId);
                if (saved)
                    TempData["SuccessMessage"] = "Medical survey saved successfully";
                else
                    TempData["ErrorMessage"] = "Error saving medical survey.";
                return RedirectToAction("MedicalSurvey", new { empNo = model.EmpNo });
            }

            if (action == "Reset")
                return RedirectToAction("MedicalSurvey");

            return RedirectToAction("MedicalSurvey");
        }

        #endregion
    }
}
