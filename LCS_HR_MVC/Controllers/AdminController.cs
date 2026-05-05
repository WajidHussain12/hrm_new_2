using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly IUserRoleService _userRoleService;
        private readonly IAdminUserService _adminUserService;
        private readonly IUserLocationService _userLocationService;
        private readonly IUserPrivilegeService _userPrivilegeService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IUserRoleService userRoleService, 
            IAdminUserService adminUserService, 
            IUserLocationService userLocationService, 
            IUserPrivilegeService userPrivilegeService,
            ILogger<AdminController> logger)
        {
            _userRoleService = userRoleService;
            _adminUserService = adminUserService;
            _userLocationService = userLocationService;
            _userPrivilegeService = userPrivilegeService;
            _logger = logger;
        }

        // Dashboard for Admin
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> UserPrivileges(string? roleId)
        {
            var model = new UserPrivilegeViewModel();
            var roles = await _userRoleService.GetAllRolesAsync();
            var roleItems = new List<SelectListItem> { new SelectListItem("<- Please Select ->", "00") };
            roleItems.AddRange(roles.Select(r => new SelectListItem(r.Description, r.RoleID)));
            ViewBag.RolesList = roleItems;

            if (!string.IsNullOrEmpty(roleId) && roleId != "00")
            {
                var roleDescription = roles.FirstOrDefault(r => r.RoleID == roleId)?.Description;
                if (roleDescription == "Administrator")
                {
                    TempData["ErrorMessage"] = "Administrator is not Allowed";
                }
                else
                {
                    model.RoleID = roleId;
                    model.Privileges = (await _userPrivilegeService.GetPrivilegesAsync(roleId)).ToList();
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserPrivileges(UserPrivilegeViewModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("UserPrivileges");
                }

                if (string.IsNullOrEmpty(model.RoleID) || model.RoleID == "00")
                {
                    TempData["ErrorMessage"] = "Please select a valid role.";
                    return RedirectToAction("UserPrivileges");
                }

                if (action == "Update")
                {
                    bool saved = await _userPrivilegeService.UpdatePrivilegesAsync(model.RoleID, model.Privileges);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error occurred while saving data in database.";

                    return RedirectToAction("UserPrivileges", new { roleId = model.RoleID });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing UserPrivileges action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("UserPrivileges", new { roleId = model.RoleID });
        }

        [HttpGet]
        public async Task<IActionResult> UserRoles()
        {
            var roles = await _userRoleService.GetAllRolesAsync();
            ViewBag.Roles = roles;
            return View(new UserRoleModel { RoleID = "Auto Generated" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserRoles(UserRoleModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("UserRoles");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.RoleID) || model.RoleID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid role to delete.";
                    }
                    else
                    {
                        bool deleted = await _userRoleService.DeleteRoleAsync(model.RoleID);
                        if (deleted)
                            TempData["SuccessMessage"] = "Record deleted Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while deleting data in the database";
                    }
                    return RedirectToAction("UserRoles");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.Roles = await _userRoleService.GetAllRolesAsync();
                    return View(model);
                }

                if (action == "Save")
                {
                    bool exists = await _userRoleService.IsDescriptionExistsAsync(model.Description);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "Description already exists in database.";
                    }
                    else
                    {
                        bool saved = await _userRoleService.AddRoleAsync(model);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.RoleID) || model.RoleID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid role to update.";
                    }
                    else
                    {
                        bool exists = await _userRoleService.IsDescriptionExistsAsync(model.Description, model.RoleID);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "Description already exists in database.";
                        }
                        else
                        {
                            bool updated = await _userRoleService.UpdateRoleAsync(model);
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
                _logger.LogError(ex, "Error processing UserRoles action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction("UserRoles");
        }

        [HttpGet]
        public async Task<IActionResult> Users(string? id = null)
        {
            var model = new UserAdminModel();
            
            if (!string.IsNullOrEmpty(id))
            {
                var user = await _adminUserService.GetUserByIdAsync(id);
                if (user != null)
                {
                    model = user;
                }
            }

            await PopulateUserViewBagAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Users(UserAdminModel model, string action)
        {
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("Users");
                }

                if (action == "Delete")
                {
                    if (string.IsNullOrEmpty(model.UserID) || model.UserID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid user to delete.";
                    }
                    else
                    {
                        bool hasRef = await _adminUserService.CheckReferencesAsync(model.UserID);
                        if (hasRef)
                        {
                            TempData["ErrorMessage"] = "This Record has been referenced in another table";
                        }
                        else
                        {
                            bool deleted = await _adminUserService.DeleteUserAsync(model.UserID);
                            if (deleted)
                                TempData["SuccessMessage"] = "Record deleted Successfully";
                            else
                                TempData["ErrorMessage"] = "Error while deleting data in the database";
                        }
                    }
                    return RedirectToAction("Users");
                }

                // File validation
                if (model.SignatureFile != null && model.SignatureFile.Length > 3000000)
                {
                    ModelState.AddModelError("SignatureFile", "File Size should be less than 3 MB.");
                }

                if (action == "Save" && string.IsNullOrEmpty(model.Password))
                {
                    ModelState.AddModelError("Password", "Password is required for new user.");
                }

                if (!ModelState.IsValid)
                {
                    await PopulateUserViewBagAsync();
                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

                bool isLocValid = await _adminUserService.IsLocationValidAsync(model.LocationID, model.LocationDescription);
                if (!isLocValid)
                {
                    TempData["ErrorMessage"] = "Location ID or Account description does not exist in database.";
                    await PopulateUserViewBagAsync();
                    return View(model);
                }

                if (model.ExpiryDate.Date < DateTime.Today)
                {
                    TempData["ErrorMessage"] = "Expiry date cannot be in the past.";
                    await PopulateUserViewBagAsync();
                    return View(model);
                }

                if (action == "Save")
                {
                    bool exists = await _adminUserService.IsUserNameExistsAsync(model.UserName, model.LocationID);
                    if (exists)
                    {
                        TempData["ErrorMessage"] = "User Name already exists in database.";
                    }
                    else
                    {
                        bool saved = await _adminUserService.AddUserAsync(model, currentUserId);
                        if (saved)
                            TempData["SuccessMessage"] = "Record Saved Successfully";
                        else
                            TempData["ErrorMessage"] = "Error while inserting data in the database.";
                    }
                }
                else if (action == "Update")
                {
                    if (string.IsNullOrEmpty(model.UserID) || model.UserID == "Auto Generated")
                    {
                        TempData["ErrorMessage"] = "Select a valid user to update.";
                    }
                    else
                    {
                        bool exists = await _adminUserService.IsUserNameExistsAsync(model.UserName, model.LocationID, model.UserID);
                        if (exists)
                        {
                            TempData["ErrorMessage"] = "User Name already exists in database.";
                        }
                        else
                        {
                            bool updated = await _adminUserService.UpdateUserAsync(model, currentUserId);
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
                _logger.LogError(ex, "Error processing Users action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            await PopulateUserViewBagAsync();
            return View(model);
        }

        private async Task PopulateUserViewBagAsync()
        {
            var users = await _adminUserService.GetAllUsersAsync();
            ViewBag.UsersList = users;

            var roles = await _userRoleService.GetAllRolesAsync();
            var roleItems = new List<SelectListItem> { new SelectListItem("<- Please Select ->", "00") };
            roleItems.AddRange(roles.Select(r => new SelectListItem(r.Description, r.RoleID)));
            ViewBag.RolesList = roleItems;
        }

        [HttpGet]
        public async Task<IActionResult> UserLocation(string? userId)
        {
            var userRole = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (userRole != "Administrator")
            {
                TempData["ErrorMessage"] = "Access denied. This page is restricted to Administrators only.";
                return RedirectToAction("Index", "Home");
            }
            var model = new UserLocationViewModel();
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _adminUserService.GetUserByIdAsync(userId);
                if (user != null)
                {
                    model.UserID = user.UserID;
                    model.UserDescription = user.FullName;
                    model.Locations = (await _userLocationService.GetUserLocationsAsync(userId)).ToList();
                }
            }
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UserLocation(UserLocationViewModel model, string action)
        {
            var userRole = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (userRole != "Administrator")
            {
                TempData["ErrorMessage"] = "Access denied. This page is restricted to Administrators only.";
                return RedirectToAction("Index", "Home");
            }
            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("UserLocation");
                }

                if (string.IsNullOrEmpty(model.UserID))
                {
                    TempData["ErrorMessage"] = "Please select a user first.";
                    return View(model);
                }

                if (action == "Save")
                {
                    var codes = model.Locations.Where(x => !string.IsNullOrEmpty(x.Code)).Select(x => x.Code).ToList();
                    bool saved = await _userLocationService.UpdateUserLocationsAsync(model.UserID, codes);
                    if (saved)
                        TempData["SuccessMessage"] = "Record Saved Successfully";
                    else
                        TempData["ErrorMessage"] = "Error occurred while saving data in database.";

                    return RedirectToAction("UserLocation", new { userId = model.UserID });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing UserLocation action: {Action}", action);
                TempData["ErrorMessage"] = ex.Message;
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllLocations()
        {
            var results = await _userLocationService.GetAllLocationsAsync();
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers(string term)
        {
            var results = await _adminUserService.SearchUsersAsync(term);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetLocations(string term)
        {
            var results = await _adminUserService.SearchLocationsAsync(term);
            return Json(results);
        }
    }
}