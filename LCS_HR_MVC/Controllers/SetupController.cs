using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class SetupController : Controller
    {
        private readonly ISetupService _setupService;
        private readonly ITaxSetupService _taxSetupService;
        private readonly IRouteSetupService _routeSetupService;
        private readonly ILogger<SetupController> _logger;

        public SetupController(ISetupService setupService, ITaxSetupService taxSetupService, IRouteSetupService routeSetupService, ILogger<SetupController> logger)
        {
            _setupService = setupService;
            _taxSetupService = taxSetupService;
            _routeSetupService = routeSetupService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Routes()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _routeSetupService.GetAllRoutesAsync(currentUserId);
            return View(data);
        }

        [HttpGet]
        public async Task<IActionResult> RouteDetailEntry(string? code, string? city)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
            var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            cityItems.AddRange(cities);
            ViewBag.CitiesList = cityItems;

            if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(city))
            {
                var model = await _routeSetupService.GetRouteByCodeAndCityAsync(code, city);
                if (model != null)
                {
                    ViewBag.OldCityCode = city;
                    if (model.Details.Count == 0)
                    {
                        model.Details.Add(new RouteDetailModel());
                    }
                    return View(model);
                }
            }

            var newModel = new RouteModel { RouteCode = "Auto Generated", FromDate = DateTime.Now };
            newModel.Details.Add(new RouteDetailModel());
            return View(newModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RouteDetailEntry(RouteModel model, string action, string oldCityCode)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("RouteDetailEntry");
                }

                if (action == "Back")
                {
                    return RedirectToAction("Routes");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.RouteCode) || model.RouteCode == "Auto Generated" || model.CityCode == "00")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _routeSetupService.DeleteRouteAsync(model.RouteCode, oldCityCode);
                        if (deleted)
                        {
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                            return RedirectToAction("Routes");
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                        }
                    }
                    return RedirectToAction("RouteDetailEntry", new { code = model.RouteCode, city = oldCityCode });
                }

                if (model.CityCode == "00" || string.IsNullOrEmpty(model.CityCode)) ModelState.AddModelError("CityCode", "Please select a city");

                if (model.RouteCode != "Auto Generated" && model.RouteCode.Length != 5)
                {
                    ModelState.AddModelError("RouteCode", "Route Code must be exactly 5 digits.");
                }

                if (!ModelState.IsValid)
                {
                    var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
                    var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
                    cityItems.AddRange(cities);
                    ViewBag.CitiesList = cityItems;
                    ViewBag.OldCityCode = oldCityCode;
                    return View(model);
                }

                // Grid Validation
                if (model.Details == null || model.Details.Count == 0 || (model.Details.Count == 1 && string.IsNullOrEmpty(model.Details[0].AreaName)))
                {
                    TempData["ErrorMessage"] = "None of the fields in the grid can be left empty.";
                    if (model.Details == null) model.Details = new List<RouteDetailModel>();
                    if (model.Details.Count == 0) model.Details.Add(new RouteDetailModel());

                    var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
                    var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
                    cityItems.AddRange(cities);
                    ViewBag.CitiesList = cityItems;
                    ViewBag.OldCityCode = oldCityCode;
                    return View(model);
                }

                if (action == "Save")
                {
                    bool exists = await _routeSetupService.IsRouteCodeExistsAsync(model.RouteCode, model.CityCode);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Route Code already defined for this city.";
                    }
                    else
                    {
                        bool descExists = await _routeSetupService.IsRouteDescriptionExistsAsync(model.Description, model.CityCode);
                        if (descExists)
                        {
                            TempData["ErrorMessage"] = "Route Description already exists in database for this city.";
                        }
                        else
                        {
                            bool saved = await _routeSetupService.SaveRouteAsync(model, currentUserId);
                            if (saved)
                            {
                                TempData["SuccessMessage"] = $"Route No {model.RouteCode} Saved Successfully";
                                return RedirectToAction("Routes");
                            }
                            else
                            {
                                TempData["ErrorMessage"] = "Error while inserting data in the database.";
                            }
                        }
                    }
                }
                else if (action == "Update")
                {
                    bool descExists = await _routeSetupService.IsRouteDescriptionExistsAsync(model.Description, model.CityCode, model.RouteCode);
                    if (descExists)
                    {
                        TempData["ErrorMessage"] = "Route Description already exists in database for this city.";
                    }
                    else
                    {
                        bool updated = await _routeSetupService.UpdateRouteAsync(model, oldCityCode, currentUserId);
                        if (updated)
                        {
                            TempData["SuccessMessage"] = $"Route No. {model.RouteCode} updated Successfully";
                            return RedirectToAction("Routes");
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing RouteDetailEntry action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            var reCities = await _setupService.GetCitiesByUserAsync(currentUserId);
            var reCityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            reCityItems.AddRange(reCities);
            ViewBag.CitiesList = reCityItems;
            ViewBag.OldCityCode = oldCityCode;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Taxes()
        {
            var data = await _taxSetupService.GetAllTaxesAsync();
            return View(data);
        }

        [HttpGet]
        public async Task<IActionResult> TaxDetailEntry(string? code)
        {
            if (!string.IsNullOrEmpty(code))
            {
                var model = await _taxSetupService.GetTaxByCodeAsync(code);
                if (model != null)
                {
                    if (model.Details.Count == 0)
                    {
                        model.Details.Add(new TaxDetailModel());
                    }
                    return View(model);
                }
            }

            var newModel = new TaxHeadModel();
            newModel.Details.Add(new TaxDetailModel());
            return View(newModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TaxDetailEntry(TaxHeadModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("TaxDetailEntry");
                }

                if (action == "Back")
                {
                    return RedirectToAction("Taxes");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _taxSetupService.DeleteTaxAsync(model.Code);
                        if (deleted)
                        {
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                            return RedirectToAction("Taxes");
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                        }
                    }
                    return View(model);
                }

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                // Grid Validation
                if (model.Details == null || model.Details.Count == 0 || (model.Details.Count == 1 && model.Details[0].LimitFrom == 0 && model.Details[0].LimitTo == 0))
                {
                    TempData["ErrorMessage"] = "Grid is empty.";
                    if (model.Details == null) model.Details = new List<TaxDetailModel>();
                    if (model.Details.Count == 0) model.Details.Add(new TaxDetailModel());
                    return View(model);
                }

                // Check ranges and values
                foreach (var detail in model.Details)
                {
                    if (detail.LimitFrom >= detail.LimitTo)
                    {
                        TempData["ErrorMessage"] = "Limit from cannot be greater or equal to Limit to.";
                        return View(model);
                    }
                    if (detail.PctAmount >= 100 || detail.PctAmount < 0)
                    {
                        TempData["ErrorMessage"] = "Percent Amount should be smaller than 100 and greater or equal to 0.";
                        return View(model);
                    }
                    if (detail.FixAmount > 0 && detail.FixAmount >= detail.LimitFrom)
                    {
                        // From old logic: Fix amount cannot be smaller or equal to Limit from. Wait, old logic says "if (FixAmount >= LimitFrom) error". Which means fix amount must be less than limit from.
                        TempData["ErrorMessage"] = "Fix amount cannot be greater or equal to Limit from.";
                        return View(model);
                    }
                }

                // Check for overlapping slab ranges across all brackets
                for (int i = 0; i < model.Details.Count; i++)
                {
                    for (int j = i + 1; j < model.Details.Count; j++)
                    {
                        var a = model.Details[i];
                        var b = model.Details[j];
                        if (a.LimitFrom < b.LimitTo && b.LimitFrom < a.LimitTo)
                        {
                            TempData["ErrorMessage"] = $"Tax bracket ranges overlap: [{a.LimitFrom:N0} - {a.LimitTo:N0}] and [{b.LimitFrom:N0} - {b.LimitTo:N0}].";
                            return View(model);
                        }
                    }
                }

                // Calculate DateFrom and DateTo based on TaxYear if they are null or not set properly
                if (model.TaxYear.HasValue)
                {
                    model.DateFrom = new DateTime(model.TaxYear.Value, 7, 1);
                    model.DateTo = new DateTime(model.TaxYear.Value + 1, 7, 1).AddDays(-1);
                }

                if (action == "Save")
                {
                    bool exists = await _taxSetupService.IsTaxYearExistsAsync(model.TaxYear.Value);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = $"Tax for the year {model.TaxYear.Value} already has been defined.";
                    }
                    else
                    {
                        bool saved = await _taxSetupService.SaveTaxAsync(model, currentUserId);
                        if (saved)
                        {
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                            return RedirectToAction("Taxes");
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                        }
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool exists = await _taxSetupService.IsTaxYearExistsAsync(model.TaxYear.Value, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = $"Tax for the year {model.TaxYear.Value} already has been defined.";
                        }
                        else
                        {
                            bool updated = await _taxSetupService.UpdateTaxAsync(model, currentUserId);
                            if (updated)
                            {
                                TempData["SuccessMessage"] = "Record updated successfully.";
                                return RedirectToAction("Taxes");
                            }
                            else
                            {
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing TaxDetailEntry action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Countries()
        {
            var countries = await _setupService.GetAllCountriesAsync();
            ViewBag.CountriesList = countries;
            return View(new CountryModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Countries(CountryModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Countries");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid country to delete.";
                    }
                    else
                    {
                        // Note: A check for references should ideally exist here matching LCS.CheckReferences
                        bool deleted = await _setupService.DeleteCountryAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database";
                    }
                    return RedirectToAction("Countries");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.CountriesList = await _setupService.GetAllCountriesAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsCountryExistsAsync(model.FullName, model.ShortName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Country already exists";
                    }
                    else
                    {
                        bool saved = await _setupService.AddCountryAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid country to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsCountryExistsAsync(model.FullName, model.ShortName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Country already exists";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateCountryAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Countries action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Countries");
        }
        [HttpGet]
        public async Task<IActionResult> Provinces()
        {
            var provinces = await _setupService.GetAllProvincesAsync();
            ViewBag.ProvincesList = provinces;

            var countries = await _setupService.GetAllCountriesAsync();
            var countryItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("<- Please Select ->", "00") };
            countryItems.AddRange(countries.Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(c.FullName, c.Code)));
            ViewBag.CountriesList = countryItems;

            return View(new ProvinceModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Provinces(ProvinceModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Provinces");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid province to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteProvinceAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database";
                    }
                    return RedirectToAction("Provinces");
                }

                if (model.CountryCode == "00")
                {
                    ModelState.AddModelError("CountryCode", "Please select a country.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.ProvincesList = await _setupService.GetAllProvincesAsync();
                    var countries = await _setupService.GetAllCountriesAsync();
                    var countryItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("<- Please Select ->", "00") };
                    countryItems.AddRange(countries.Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(c.FullName, c.Code)));
                    ViewBag.CountriesList = countryItems;
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsProvinceExistsAsync(model.CountryCode, model.FullName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Province already exists.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddProvinceAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid province to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsProvinceExistsAsync(model.CountryCode, model.FullName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Province already exists.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateProvinceAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Provinces action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Provinces");
        }
        [HttpGet]
        public async Task<IActionResult> Cities()
        {
            var cities = await _setupService.GetAllCitiesAsync();
            ViewBag.CitiesList = cities;

            var countries = await _setupService.GetAllCountriesAsync();
            var countryItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("<- Please Select ->", "00") };
            countryItems.AddRange(countries.Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(c.FullName, c.Code)));
            ViewBag.CountriesList = countryItems;

            var zones = await _setupService.GetZonesAsync();
            var zoneItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("<- Please Select ->", "00") };
            zoneItems.AddRange(zones);
            ViewBag.ZonesList = zoneItems;

            return View(new CityModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cities(CityModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Cities");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid city to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteCityAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("Cities");
                }

                if (model.CountryCode == "00") ModelState.AddModelError("CountryCode", "Please select a country.");
                if (model.ProvinceCode == "00" || string.IsNullOrEmpty(model.ProvinceCode)) ModelState.AddModelError("ProvinceCode", "Please select a province.");
                if (model.ZoneCode == "00") ModelState.AddModelError("ZoneCode", "Please select a zone.");

                if (!ModelState.IsValid)
                {
                    ViewBag.CitiesList = await _setupService.GetAllCitiesAsync();

                    var countries = await _setupService.GetAllCountriesAsync();
                    var countryItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("<- Please Select ->", "00") };
                    countryItems.AddRange(countries.Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(c.FullName, c.Code)));
                    ViewBag.CountriesList = countryItems;

                    var zones = await _setupService.GetZonesAsync();
                    var zoneItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("<- Please Select ->", "00") };
                    zoneItems.AddRange(zones);
                    ViewBag.ZonesList = zoneItems;

                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsCityExistsAsync(model.FullName, model.ShortName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddCityAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid city to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsCityExistsAsync(model.FullName, model.ShortName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateCityAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Cities action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Cities");
        }

        [HttpGet]
        public async Task<IActionResult> GetProvinces(string countryCode)
        {
            var provinces = await _setupService.GetProvincesByCountryAsync(countryCode);
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a province", "00") };
            items.AddRange(provinces);
            return Json(items);
        }
        [HttpGet]
        public async Task<IActionResult> Departments()
        {
            var depts = await _setupService.GetAllDepartmentsAsync();
            ViewBag.DepartmentsList = depts;

            var companies = await _setupService.GetCompaniesAsync();
            var compItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
            compItems.AddRange(companies);
            ViewBag.CompaniesList = compItems;

            var bus = await _setupService.GetBusinessUnitsAsync();
            var buItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            buItems.AddRange(bus);
            ViewBag.DivisionsList = buItems;

            var parents = await _setupService.GetParentDepartmentsAsync();
            var parentItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Select Parent Department", "00") };
            parentItems.AddRange(parents);
            ViewBag.ParentsList = parentItems;

            return View(new DepartmentModel { SDID = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Departments(DepartmentModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Departments");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.SDID) || model.SDID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid department to delete.";
                    }
                    else
                    {
                        // Needs to check if IsParent and PDID references if required
                        bool deleted = await _setupService.DeleteDepartmentAsync(model.SDID, model.PDID);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database";
                    }
                    return RedirectToAction("Departments");
                }

                if (model.CID == "0") ModelState.AddModelError("CID", "*");
                if (model.BUID == "00") ModelState.AddModelError("BUID", "*");
                if (!model.IsParent && model.PDID == "00") ModelState.AddModelError("PDID", "Select Parent Department");

                if (!ModelState.IsValid)
                {
                    ViewBag.DepartmentsList = await _setupService.GetAllDepartmentsAsync();

                    var companies = await _setupService.GetCompaniesAsync();
                    var compItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
                    compItems.AddRange(companies);
                    ViewBag.CompaniesList = compItems;

                    var bus = await _setupService.GetBusinessUnitsAsync();
                    var buItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    buItems.AddRange(bus);
                    ViewBag.DivisionsList = buItems;

                    var parents = await _setupService.GetParentDepartmentsAsync();
                    var parentItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Select Parent Department", "00") };
                    parentItems.AddRange(parents);
                    ViewBag.ParentsList = parentItems;

                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsDepartmentExistsAsync(model.SdeptName, model.ShortSDname, model.CID, model.BUID, model.IsParent, model.PDID);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Department already exists";
                    }
                    else
                    {
                        bool saved = await _setupService.AddDepartmentAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.SDID) || model.SDID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid department to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsDepartmentExistsAsync(model.SdeptName, model.ShortSDname, model.CID, model.BUID, model.IsParent, model.PDID, model.SDID);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Department already exists";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateDepartmentAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record Update Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Departments action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Departments");
        }
        [HttpGet]
        public async Task<IActionResult> Divisions()
        {
            var divisions = await _setupService.GetAllDivisionsAsync();
            ViewBag.DivisionsList = divisions;
            return View(new DivisionModel { BUID = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Divisions(DivisionModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Divisions");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.DivisionsList = await _setupService.GetAllDivisionsAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsDivisionExistsAsync(model.FullName, model.ShortName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddDivisionAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.BUID) || model.BUID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid division to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsDivisionExistsAsync(model.FullName, model.ShortName, model.BUID);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateDivisionAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Divisions action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Divisions");
        }
        [HttpGet]
        public async Task<IActionResult> Jobs()
        {
            var jobs = await _setupService.GetAllJobsAsync();
            ViewBag.JobsList = jobs;

            var parents = await _setupService.GetParentDepartmentsAsync();
            var parentItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
            parentItems.AddRange(parents);
            ViewBag.ParentsList = parentItems;

            return View(new JobModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Jobs(JobModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Jobs");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid designation to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteJobAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("Jobs");
                }

                if (model.ParentDeptId == "0") ModelState.AddModelError("ParentDeptId", "Required");
                if (model.SubDeptId == "0" || string.IsNullOrEmpty(model.SubDeptId)) ModelState.AddModelError("SubDeptId", "Required");

                if (!ModelState.IsValid)
                {
                    ViewBag.JobsList = await _setupService.GetAllJobsAsync();

                    var parents = await _setupService.GetParentDepartmentsAsync();
                    var parentItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
                    parentItems.AddRange(parents);
                    ViewBag.ParentsList = parentItems;

                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsJobExistsAsync(model.FullName, model.ShortName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddJobAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid designation to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsJobExistsAsync(model.FullName, model.ShortName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateJobAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Jobs action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Jobs");
        }

        [HttpGet]
        public async Task<IActionResult> GetSubDepartmentsByParent(string parentId)
        {
            var subs = await _setupService.GetSubDepartmentsByParentAsync(parentId);
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "0") };
            items.AddRange(subs);
            return Json(items);
        }

        [HttpGet]
        public async Task<IActionResult> EmployeeTypes()
        {
            var types = await _setupService.GetAllEmployeeTypesAsync();
            ViewBag.EmployeeTypesList = types;
            return View(new EmployeeTypeModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeTypes(EmployeeTypeModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("EmployeeTypes");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid employee type to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteEmployeeTypeAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("EmployeeTypes");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.EmployeeTypesList = await _setupService.GetAllEmployeeTypesAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsEmployeeTypeExistsAsync(model.FullName, model.ShortName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddEmployeeTypeAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid employee type to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsEmployeeTypeExistsAsync(model.FullName, model.ShortName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateEmployeeTypeAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeTypes action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("EmployeeTypes");
        }

        [HttpGet]
        public async Task<IActionResult> RegionalZones()
        {
            var zones = await _setupService.GetAllRegionalZonesAsync();
            ViewBag.RegionalZonesList = zones;
            return View(new RegionalZoneModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegionalZones(RegionalZoneModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("RegionalZones");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid regional zone to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteRegionalZoneAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("RegionalZones");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.RegionalZonesList = await _setupService.GetAllRegionalZonesAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsRegionalZoneExistsAsync(model.FullName, model.ShortName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddRegionalZoneAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid regional zone to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsRegionalZoneExistsAsync(model.FullName, model.ShortName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateRegionalZoneAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing RegionalZones action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("RegionalZones");
        }
        [HttpGet]
        public async Task<IActionResult> SalaryBanks()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var banks = await _setupService.GetAllSalaryBanksAsync(currentUserId);
            ViewBag.SalaryBanksList = banks;

            var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
            var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            cityItems.AddRange(cities);
            ViewBag.CitiesList = cityItems;

            var bankItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a bank", "00") };
            ViewBag.BanksList = bankItems;

            return View(new SalaryBankModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalaryBanks(SalaryBankModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("SalaryBanks");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid bank to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteSalaryBankAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("SalaryBanks");
                }

                if (model.CityId == "00" || string.IsNullOrEmpty(model.CityId)) ModelState.AddModelError("CityId", "Required");
                if (model.BankGlCode == "00" || string.IsNullOrEmpty(model.BankGlCode)) ModelState.AddModelError("BankGlCode", "Required");

                if (!ModelState.IsValid)
                {
                    ViewBag.SalaryBanksList = await _setupService.GetAllSalaryBanksAsync(currentUserId);
                    var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
                    var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
                    cityItems.AddRange(cities);
                    ViewBag.CitiesList = cityItems;

                    var bankItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a bank", "00") };
                    if (model.CityId != "00" && !string.IsNullOrEmpty(model.CityId))
                    {
                        bankItems.AddRange(await _setupService.GetBanksByCityAsync(model.CityId));
                    }
                    ViewBag.BanksList = bankItems;

                    return View(model);
                }

                if (action == "Save")
                {
                    bool exists = await _setupService.IsSalaryBankExistsAsync(model.CityId);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "A Bank entry for this City already exists in database. Note: Each city can have only a single bank entry.";
                    }
                    else
                    {
                        // To get the Bank description for the selected GL Code
                        var banks = await _setupService.GetBanksByCityAsync(model.CityId);
                        var bankDesc = banks.FirstOrDefault(b => b.Value == model.BankGlCode)?.Text ?? string.Empty;

                        bool saved = await _setupService.AddSalaryBankAsync(model, currentUserId, bankDesc);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid bank to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsSalaryBankExistsAsync(model.CityId, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "A Bank entry for this City already exists in database. Note: Each city can have only a single bank entry.";
                        }
                        else
                        {
                            var banks = await _setupService.GetBanksByCityAsync(model.CityId);
                            var bankDesc = banks.FirstOrDefault(b => b.Value == model.BankGlCode)?.Text ?? string.Empty;

                            bool updated = await _setupService.UpdateSalaryBankAsync(model, currentUserId, bankDesc);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SalaryBanks action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("SalaryBanks");
        }

        [HttpGet]
        public async Task<IActionResult> GetBanksByCity(string cityCode)
        {
            var banks = await _setupService.GetBanksByCityAsync(cityCode);
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a bank", "00") };
            items.AddRange(banks);
            return Json(items);
        }

        [HttpGet]
        public async Task<IActionResult> LoanTypes()
        {
            var types = await _setupService.GetAllLoanTypesAsync();
            ViewBag.LoanTypesList = types;
            return View(new LoanTypeModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoanTypes(LoanTypeModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("LoanTypes");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid loan type to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteLoanTypeAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("LoanTypes");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.LoanTypesList = await _setupService.GetAllLoanTypesAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsLoanTypeExistsAsync(model.FullName, model.ShortName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddLoanTypeAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid loan type to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsLoanTypeExistsAsync(model.FullName, model.ShortName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Full Name or Short Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateLoanTypeAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing LoanTypes action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("LoanTypes");
        }
        [HttpGet]
        public async Task<IActionResult> Shifts()
        {
            var shifts = await _setupService.GetAllShiftsAsync();
            ViewBag.ShiftsList = shifts;
            return View(new ShiftModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Shifts(ShiftModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Shifts");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid shift to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteShiftAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("Shifts");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.ShiftsList = await _setupService.GetAllShiftsAsync();
                    return View(model);
                }

                if (TimeSpan.Parse(model.StartTime) >= TimeSpan.Parse(model.EndTime))
                {
                    TempData["ErrorMessage"] = "Start Time should be less than End Time.";
                    return RedirectToAction("Shifts");
                }
                if (TimeSpan.Parse(model.BeginIn) >= TimeSpan.Parse(model.EndIn))
                {
                    TempData["ErrorMessage"] = "Begin In should be less than End In.";
                    return RedirectToAction("Shifts");
                }
                if (TimeSpan.Parse(model.BeginOut) >= TimeSpan.Parse(model.EndOut))
                {
                    TempData["ErrorMessage"] = "Begin Out should be less than End Out.";
                    return RedirectToAction("Shifts");
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsShiftExistsAsync(model.Name);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Shift already exists.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddShiftAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid shift to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsShiftExistsAsync(model.Name, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Shift already exists.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateShiftAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Shifts action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("Shifts");
        }
        [HttpGet]
        public async Task<IActionResult> AllowanceDeductions()
        {
            var data = await _setupService.GetAllAllowanceDeductionsAsync();
            ViewBag.AllowanceDeductionsList = data;

            var types = await _setupService.GetAllowanceCodeTypesAsync();
            var typeItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            typeItems.AddRange(types);
            ViewBag.CodeTypesList = typeItems;

            return View(new AllowanceDeductionModel { ID = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AllowanceDeductions(AllowanceDeductionModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("AllowanceDeductions");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.ID) || model.ID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteAllowanceDeductionAsync(model.ID);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("AllowanceDeductions");
                }

                if (model.CodeID == "00") ModelState.AddModelError("CodeID", "Required!");

                if (!ModelState.IsValid)
                {
                    ViewBag.AllowanceDeductionsList = await _setupService.GetAllAllowanceDeductionsAsync();
                    var types = await _setupService.GetAllowanceCodeTypesAsync();
                    var typeItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    typeItems.AddRange(types);
                    ViewBag.CodeTypesList = typeItems;
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                // To populate the CodeType string based on selected CodeID
                var allTypes = await _setupService.GetAllowanceCodeTypesAsync();
                model.CodeType = allTypes.FirstOrDefault(t => t.Value == model.CodeID)?.Text ?? string.Empty;

                if (action == "Save")
                {
                    bool saved = await _setupService.AddAllowanceDeductionAsync(model, currentUserId);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.ID) || model.ID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _setupService.UpdateAllowanceDeductionAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AllowanceDeductions action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("AllowanceDeductions");
        }
        [HttpGet]
        public async Task<IActionResult> AllowanceDeductionDetails()
        {
            var data = await _setupService.GetAllAllowanceDeductionDetailsAsync();
            ViewBag.AllowanceDeductionDetailsList = data;

            var types = await _setupService.GetAllowanceTypesAsync();
            var typeItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            typeItems.AddRange(types);
            ViewBag.AllowanceTypesList = typeItems;

            var rates = await _setupService.GetCommissionPolicyRatesAsync();
            var rateItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            rateItems.AddRange(rates);
            ViewBag.CommissionRatesList = rateItems;

            var paymentModes = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
            {
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00"),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("C", "C"),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("F", "F"),
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("B", "B")
            };
            ViewBag.PaymentModesList = paymentModes;

            return View(new AllowanceDeductionDetailModel { ID = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AllowanceDeductionDetails(AllowanceDeductionDetailModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("AllowanceDeductionDetails");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.ID) || model.ID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteAllowanceDeductionDetailAsync(model.ID);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("AllowanceDeductionDetails");
                }

                if (model.TypeID == "00") ModelState.AddModelError("TypeID", "Required!");
                if (model.PaymentMode == "00") ModelState.AddModelError("PaymentMode", "Required!");

                if (!ModelState.IsValid)
                {
                    ViewBag.AllowanceDeductionDetailsList = await _setupService.GetAllAllowanceDeductionDetailsAsync();
                    
                    var types = await _setupService.GetAllowanceTypesAsync();
                    var typeItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    typeItems.AddRange(types);
                    ViewBag.AllowanceTypesList = typeItems;

                    var rates = await _setupService.GetCommissionPolicyRatesAsync();
                    var rateItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    rateItems.AddRange(rates);
                    ViewBag.CommissionRatesList = rateItems;

                    ViewBag.PaymentModesList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
                    {
                        new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00"),
                        new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("C", "C"),
                        new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("F", "F"),
                        new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("B", "B")
                    };
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool saved = await _setupService.AddAllowanceDeductionDetailAsync(model, currentUserId);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.ID) || model.ID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool updated = await _setupService.UpdateAllowanceDeductionDetailAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AllowanceDeductionDetails action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("AllowanceDeductionDetails");
        }

        [HttpGet]
        public async Task<IActionResult> GetAllowanceDeductionDetailData(string id)
        {
            var model = await _setupService.GetAllowanceDeductionDetailByIdAsync(id);
            return Json(model);
        }
        [HttpGet]
        public async Task<IActionResult> GradeAllowances()
        {
            var data = await _setupService.GetAllGradeAllowancesAsync();
            ViewBag.GradeAllowancesList = data;
            return View(new GradeAllowanceModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GradeAllowances(GradeAllowanceModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("GradeAllowances");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteGradeAllowanceAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("GradeAllowances");
                }

                if (model.ApplyTo == "Department" && string.IsNullOrEmpty(model.DepartmentCode))
                {
                    ModelState.AddModelError("DepartmentDescription", "Please select a valid department.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.GradeAllowancesList = await _setupService.GetAllGradeAllowancesAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsGradeAllowanceExistsAsync(model.Type, model.FullName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Entry already exists.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddGradeAllowanceAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsGradeAllowanceExistsAsync(model.Type, model.FullName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Entry already exists.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateGradeAllowanceAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing GradeAllowances action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("GradeAllowances");
        }

        [HttpGet]
        public async Task<IActionResult> SearchDepartments(string term)
        {
            var results = await _setupService.SearchDepartmentsAsync(term);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetGradeAllowanceData(string code)
        {
            var model = await _setupService.GetGradeAllowanceByCodeAsync(code);
            return Json(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetGlCodeByShortName(string shortName)
        {
            var glCode = await _setupService.GetGlCodeByShortNameAsync(shortName);
            return Json(new { glCode });
        }
        [HttpGet]
        public async Task<IActionResult> CompanyAssets()
        {
            var data = await _setupService.GetAllCompanyAssetsAsync();
            ViewBag.CompanyAssetsList = data;

            var types = await _setupService.GetAssetTypesAsync();
            var typeItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
            typeItems.AddRange(types);
            ViewBag.AssetTypesList = typeItems;

            return View(new CompanyAssetModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompanyAssets(CompanyAssetModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("CompanyAssets");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid asset to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteCompanyAssetAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("CompanyAssets");
                }

                if (model.Type == "00" || string.IsNullOrEmpty(model.Type)) ModelState.AddModelError("Type", "Required");

                if (!ModelState.IsValid)
                {
                    ViewBag.CompanyAssetsList = await _setupService.GetAllCompanyAssetsAsync();
                    
                    var types = await _setupService.GetAssetTypesAsync();
                    var typeItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please Select", "00") };
                    typeItems.AddRange(types);
                    ViewBag.AssetTypesList = typeItems;

                    // Reload labels if type was selected
                    if (model.Type != "00" && !string.IsNullOrEmpty(model.Type))
                    {
                        model.DynamicLabels = await _setupService.GetAssetStructureLabelsAsync(model.Type);
                    }

                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsCompanyAssetExistsAsync(model.Name);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Asset name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddCompanyAssetAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid asset to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsCompanyAssetExistsAsync(model.Name, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Asset name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateCompanyAssetAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CompanyAssets action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("CompanyAssets");
        }

        [HttpGet]
        public async Task<IActionResult> GetAssetLabels(string typeCode)
        {
            var labels = await _setupService.GetAssetStructureLabelsAsync(typeCode);
            return Json(labels);
        }
        [HttpGet]
        public async Task<IActionResult> AssetStructures()
        {
            var data = await _setupService.GetAllAssetStructuresAsync();
            ViewBag.AssetStructuresList = data;
            return View(new AssetStructureModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssetStructures(AssetStructureModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("AssetStructures");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid asset property to delete.";
                    }
                    else
                    {
                        // TODO: Add Reference Check for hr_assetstructure if needed
                        bool deleted = await _setupService.DeleteAssetStructureAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("AssetStructures");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.AssetStructuresList = await _setupService.GetAllAssetStructuresAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsAssetStructureExistsAsync(model.Description);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Asset property already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddAssetStructureAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid asset property to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsAssetStructureExistsAsync(model.Description, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Asset property already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateAssetStructureAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AssetStructures action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("AssetStructures");
        }
        [HttpGet]
        public async Task<IActionResult> AttendanceRules()
        {
            var data = await _setupService.GetAllAttendanceRulesAsync();
            ViewBag.AttendanceRulesList = data;
            return View(new AttendanceRuleModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AttendanceRules(AttendanceRuleModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("AttendanceRules");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteAttendanceRuleAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("AttendanceRules");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.AttendanceRulesList = await _setupService.GetAllAttendanceRulesAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool exists = await _setupService.IsAttendanceRuleExistsAsync(model.LeaveName);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Leave Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddAttendanceRuleAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsAttendanceRuleExistsAsync(model.LeaveName, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Leave Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateAttendanceRuleAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AttendanceRules action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("AttendanceRules");
        }
        [HttpGet]
        public async Task<IActionResult> CommissionRates()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var data = await _setupService.GetAllCommissionRatesAsync(currentUserId);
            ViewBag.CommissionRatesList = data;

            var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
            var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            cityItems.AddRange(cities);
            ViewBag.CitiesList = cityItems;

            return View(new CommissionRateModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CommissionRates(CommissionRateModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("CommissionRates");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteCommissionRateAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("CommissionRates");
                }

                if (model.Citycode == "00") ModelState.AddModelError("Citycode", "Required");

                if (!ModelState.IsValid)
                {
                    ViewBag.CommissionRatesList = await _setupService.GetAllCommissionRatesAsync(currentUserId);
                    var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
                    var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
                    cityItems.AddRange(cities);
                    ViewBag.CitiesList = cityItems;
                    return View(model);
                }

                if (action == "Save")
                {
                    bool exists = await _setupService.IsCommissionRateExistsAsync(model.Citycode);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "City already exists in database.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddCommissionRateAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to update.";
                    }
                    else
                    {
                        bool exists = await _setupService.IsCommissionRateExistsAsync(model.Citycode, model.Code);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "City already exists in database.";
                        }
                        else
                        {
                            bool updated = await _setupService.UpdateCommissionRateAsync(model, currentUserId);
                            if (updated)
                                TempData["SuccessMessage"] = "Record updated successfully.";
                            else
                                TempData["ErrorMessage"] = "Error while updating data in the database.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CommissionRates action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("CommissionRates");
        }
        [HttpGet]
        public async Task<IActionResult> CommissionEligibility(string? empNo)
        {
            var data = await _setupService.GetAllCommissionEligibilitiesAsync();
            ViewBag.CommissionEligibilityList = data;

            var model = new CommissionEligibilityModel();

            if (!string.IsNullOrEmpty(empNo))
            {
                var existing = await _setupService.GetCommissionEligibilityByEmpNoAsync(empNo);
                if (existing != null)
                {
                    model = existing;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CommissionEligibility(CommissionEligibilityModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("CommissionEligibility");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.CommissionEligibilityList = await _setupService.GetAllCommissionEligibilitiesAsync();
                    return View(model);
                }

                if (action == "Save" || action == "Update")
                {
                    bool saved = await _setupService.SaveCommissionEligibilityAsync(model, currentUserId);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error while inserting data in the database.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CommissionEligibility action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("CommissionEligibility");
        }

        [HttpGet]
        public IActionResult EmpGlCode()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmpGlCode(string action)
        {
            if (action == "Reset")
                return RedirectToAction("EmpGlCode");

            try
            {
                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
                var (count, message) = await _setupService.GenerateEmpGlCodesAsync(currentUserId);
                if (count > 0)
                    TempData["SuccessMessage"] = message;
                else
                    TempData["ErrorMessage"] = message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating employee GL codes");
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("EmpGlCode");
        }

        [HttpGet]
        public async Task<IActionResult> SearchActiveEmployees(string term)
        {
            var emps = await _setupService.SearchActiveEmployeesAsync(term);
            return Json(emps);
        }

        [HttpGet]
        public async Task<IActionResult> SearchAssets(string term)
        {
            var assets = await _setupService.GetAllCompanyAssetsAsync();
            var filtered = assets.Where(a => a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || a.Code.Contains(term, StringComparison.OrdinalIgnoreCase))
                                 .Take(15)
                                 .Select(a => new { label = $"{a.Name} | {a.Code}", desc = a.Name, value = a.Code });
            return Json(filtered);
        }
        [HttpGet]
        public async Task<IActionResult> LeaveStructures()
        {
            var data = await _setupService.GetAllLeaveStructuresAsync();
            ViewBag.LeaveStructuresList = data;
            return View(new LeaveStructureModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveStructures(LeaveStructureModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("LeaveStructures");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteLeaveStructureAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("LeaveStructures");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.LeaveStructuresList = await _setupService.GetAllLeaveStructuresAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool saved = await _setupService.AddLeaveStructureAsync(model, currentUserId);
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
                        bool updated = await _setupService.UpdateLeaveStructureAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing LeaveStructures action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("LeaveStructures");
        }
        [HttpGet]
        public async Task<IActionResult> DepartmentStrength()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = new DepartmentStrengthViewModel();
            
            var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
            var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            cityItems.AddRange(cities);
            ViewBag.CitiesList = cityItems;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DepartmentStrength(DepartmentStrengthViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Load")
                {
                    if (model.CityCode != "00")
                    {
                        model.Strengths = (await _setupService.GetDepartmentStrengthsByCityAsync(model.CityCode)).ToList();
                    }
                }
                else if (action == "Save")
                {
                    if (model.CityCode == "00" || string.IsNullOrEmpty(model.CityCode))
                    {
                        TempData["ErrorMessage"] = "Please select a city.";
                    }
                    else if (model.Strengths != null && model.Strengths.Any())
                    {
                        bool allSaved = true;
                        foreach (var item in model.Strengths)
                        {
                            bool saved = await _setupService.UpdateDepartmentStrengthAsync(model.CityCode, item.PDID, item.SDID, item.Strength, currentUserId);
                            if (!saved) allSaved = false;
                        }

                        if (allSaved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Some records failed to save.";
                    }
                    
                    // Reload
                    model.Strengths = (await _setupService.GetDepartmentStrengthsByCityAsync(model.CityCode)).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing DepartmentStrength action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            var cities = await _setupService.GetCitiesByUserAsync(currentUserId);
            var cityItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            cityItems.AddRange(cities);
            ViewBag.CitiesList = cityItems;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GazettedHolidays()
        {
            var data = await _setupService.GetAllGazettedHolidaysAsync();
            ViewBag.GazettedHolidaysList = data;
            return View(new GazettedHolidayModel { Code = "Auto Generated", FromDate = DateTime.Now.Date, ToDate = DateTime.Now.Date });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GazettedHolidays(GazettedHolidayModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("GazettedHolidays");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteGazettedHolidayAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database.";
                    }
                    return RedirectToAction("GazettedHolidays");
                }

                if (!model.IsAllLocations && (model.LocationID == "00" || string.IsNullOrEmpty(model.LocationID)))
                {
                    ModelState.AddModelError("LocationDescription", "Please select a location.");
                }

                if (model.ToDate < model.FromDate)
                {
                    ModelState.AddModelError("ToDate", "To date cannot be smaller than From date.");
                }

                if (model.FromDate?.Month != model.ToDate?.Month)
                {
                    ModelState.AddModelError("FromDate", "Date range must be within same month.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.GazettedHolidaysList = await _setupService.GetAllGazettedHolidaysAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                if (action == "Save")
                {
                    bool saved = await _setupService.AddGazettedHolidayAsync(model, currentUserId);
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
                        bool updated = await _setupService.UpdateGazettedHolidayAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated successfully.";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing GazettedHolidays action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("GazettedHolidays");
        }
        [HttpGet]
        public async Task<IActionResult> DefineHRHierarchy()
        {
            var model = new HRHierarchyViewModel();

            var bus = await _setupService.GetBusinessUnitsAsync();
            var buItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Select Division", "0") };
            buItems.AddRange(bus);
            ViewBag.DivisionsList = buItems;

            var deptItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Select Department", "00") };
            ViewBag.DepartmentsList = deptItems;

            var subItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("All Departments", "00") };
            ViewBag.SubDepartmentsList = subItems;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DefineHRHierarchy([FromBody] HRHierarchyViewModel model)
        {
            try
            {
                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
                
                if (model.Employees != null && model.Employees.Any())
                {
                    // Update all to have the ReportTo from the parent model
                    foreach (var emp in model.Employees)
                    {
                        emp.ReportTo = model.HODEmployeeCode;
                    }

                    int rows = await _setupService.InsertEmpHierarchyAsync(model.Employees, currentUserId);
                    if (rows > 0 || !string.IsNullOrEmpty(model.Employees.First().DeleteHirerchyEmpIDS))
                    {
                        return Json(new { success = true, message = "Record Saved Successfully" });
                    }
                }
                
                return Json(new { success = false, message = "No valid records to save." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing DefineHRHierarchy");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentsByBU(string buId)
        {
            if (!int.TryParse(buId, out var parsedBuId))
            {
                return Json(new List<object>());
            }

            var departments = await _setupService.GetParentDepartmentsByIDAsync(1, parsedBuId);
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
            {
                new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Select Department", "00")
            };
            items.AddRange(departments);

            return Json(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetReportedToEmployees(string reportToId, string deptId, string subDeptId)
        {
            var reported = await _setupService.GetReportedEmployeesAsync(reportToId);
            var available = await _setupService.GetEmployeesBySubDepartmentAsync(deptId, subDeptId);
            return Json(new { obj1 = reported, obj2 = available });
        }

        // ─── GL Locations ────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GLLocations()
        {
            var list = await _setupService.GetAllGLLocationsAsync();
            ViewBag.GLLocationsList = list;
            return View(new LCS_HR_MVC.Models.GLLocationModel { Code = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GLLocations(LCS_HR_MVC.Models.GLLocationModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
            try
            {
                if (action == "Reset")
                    return RedirectToAction("GLLocations");

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.Code) || model.Code == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid record to delete.";
                    }
                    else
                    {
                        bool deleted = await _setupService.DeleteGLLocationAsync(model.Code);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted successfully.";
                        else
                            TempData["ErrorMessage"] = "This record has been referenced in another table and cannot be deleted.";
                    }
                    return RedirectToAction("GLLocations");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.GLLocationsList = await _setupService.GetAllGLLocationsAsync();
                    return View(model);
                }

                if (action == "Save")
                {
                    bool exists = await _setupService.IsGLLocationExistsAsync(model.Description);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Location already exists.";
                    }
                    else
                    {
                        bool saved = await _setupService.AddGLLocationAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    bool exists = await _setupService.IsGLLocationExistsAsync(model.Description, model.Code);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Location already exists.";
                    }
                    else
                    {
                        bool updated = await _setupService.UpdateGLLocationAsync(model, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing GLLocations action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction("GLLocations");
        }

        // ─── Assign Multiple Locations ──────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> AssignMultipleLocations(string? empNo)
        {
            var model = new LCS_HR_MVC.Models.AssignMultipleLocationsViewModel();
            model.AllLocations = (await _setupService.GetSetupLocationsAsync()).ToList();
            if (!string.IsNullOrEmpty(empNo))
            {
                model.EmpNo = empNo;
                model.AssignedLocationIds = await _setupService.GetAssignedLocationsByEmpAsync(empNo);
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignMultipleLocations(LCS_HR_MVC.Models.AssignMultipleLocationsViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
            try
            {
                if (action == "Reset")
                    return RedirectToAction("AssignMultipleLocations");

                if (string.IsNullOrEmpty(model.EmpNo))
                {
                    TempData["ErrorMessage"] = "Please select an employee.";
                    model.AllLocations = (await _setupService.GetSetupLocationsAsync()).ToList();
                    return View(model);
                }

                if (action == "Save")
                {
                    if (model.AssignedLocationIds == null) model.AssignedLocationIds = new List<int>();
                    bool saved = await _setupService.SaveAssignedLocationsAsync(model.EmpNo, model.AssignedLocationIds, currentUserId);
                    if (saved)
                        TempData["SuccessMessage"] = "Save Successfully!";
                    else
                        TempData["ErrorMessage"] = "Error occurred while saving data.";
                    return RedirectToAction("AssignMultipleLocations", new { empNo = model.EmpNo });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AssignMultipleLocations action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }
            model.AllLocations = (await _setupService.GetSetupLocationsAsync()).ToList();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetAssignedLocations(string empNo)
        {
            var ids = await _setupService.GetAssignedLocationsByEmpAsync(empNo);
            return Json(ids);
        }

        // ─── Location Coordinate Update ─────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> LocationCoordinateUpdate()
        {
            var model = new LCS_HR_MVC.Models.LocationCoordinateViewModel();
            model.Locations = (await _setupService.GetLocationsWithCoordinatesAsync()).ToList();

            var zones = await _setupService.GetZonesAsync();
            var zoneItems = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
                { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a zone", "00") };
            zoneItems.AddRange(zones);
            ViewBag.ZonesList = zoneItems;

            ViewBag.CitiesList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
                { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            ViewBag.LocationsList = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
                { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a location", "00") };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LocationCoordinateUpdate(LCS_HR_MVC.Models.LocationCoordinateViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
            try
            {
                if (action == "Reset")
                    return RedirectToAction("LocationCoordinateUpdate");

                if (action == "Update")
                {
                    if (model.SelectedLocationId == 0 || string.IsNullOrEmpty(model.SelectedCityCode) || model.SelectedCityCode == "00")
                    {
                        TempData["ErrorMessage"] = "Please select a zone, city, and location.";
                    }
                    else
                    {
                        bool updated = await _setupService.UpdateLocationCoordinatesAsync(
                            model.SelectedLocationId, model.SelectedCityCode, model.Latitude, model.Longitude, currentUserId);
                        if (updated)
                            TempData["SuccessMessage"] = "Record updated Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while updating data in the database.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing LocationCoordinateUpdate action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }
            return RedirectToAction("LocationCoordinateUpdate");
        }

        [HttpGet]
        public async Task<IActionResult> GetCitiesByZone(string zoneCode)
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
                { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a city", "00") };
            if (!string.IsNullOrEmpty(zoneCode) && zoneCode != "00")
            {
                var cities = await _setupService.GetCitiesByZoneAsync(zoneCode);
                items.AddRange(cities);
            }
            return Json(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetLocationsByCity(string cityId)
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
                { new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem("Please select a location", "00") };
            if (!string.IsNullOrEmpty(cityId) && cityId != "00")
            {
                var locations = await _setupService.GetLocationsByCityAsync(cityId);
                items.AddRange(locations);
            }
            return Json(items);
        }
    }
}
