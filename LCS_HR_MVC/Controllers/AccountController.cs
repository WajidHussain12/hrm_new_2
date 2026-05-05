using System.Security.Claims;
using LCS_HR_MVC.Models.ViewModels;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;

        public AccountController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            var model = new LoginViewModel();

            // Read cookies for pre-filling username
            if (Request.Cookies.TryGetValue("user", out string? userCookie))
            {
                model.Username = userCookie;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var user = await _authService.AuthenticateUserAsync(model.Username, model.Password);

                if (user == null)
                {
                    ModelState.AddModelError("", "User Does Not Exists or Invalid Password");
                    return View(model);
                }

                // Authentication Successful
                // Create Claims Identity
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Role, user.UserRole),
                    new Claim("RoleDescription", user.RoleDescription),
                    new Claim("LocationID", user.LocCode)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Set session details
                HttpContext.Session.SetString("userid", user.UserId.ToString());
                HttpContext.Session.SetString("username", user.Name);
                HttpContext.Session.SetString("usercities", string.Join(",", user.UserCities));
                HttpContext.Session.SetString("workingdate", user.WorkingDate.ToString("yyyy-MM-dd"));

                // Handle cookies like old code
                Response.Cookies.Append("name", user.Name, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7) });
                Response.Cookies.Append("user", model.Username, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7) });

                // Check Close Process
                if (user.WorkingDate.Day == 11)
                {
                    await _authService.PerformCloseProcessAsync(user.WorkingDate);
                }

                // Redirect logic
                if (user.RoleDescription == "Administrator")
                {
                    return RedirectToAction("Index", "Admin"); // Note: Assuming an Admin controller exists or will exist
                }
                
                // Expiry Date Logic
                if (DateTime.Now.Date >= new DateTime(2025, 3, 5))
                {
                    bool isUpdated = await _authService.IsPasswordUpdatedThisMonthAsync(user.UserId);
                    if (!isUpdated)
                    {
                        return RedirectToAction("ChangePasswordOnExpire", "Account");
                    }
                }

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                
                return RedirectToAction("Index", "Home");
            }
            catch (ArgumentException ex)
            {
                model.ErrorMessage = ex.Message;
                return View(model);
            }
            catch (Exception ex)
            {
                model.ErrorMessage = $"Error: {ex.Message}";
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            var model = new ChangePasswordViewModel
            {
                Username = User.Identity?.Name ?? string.Empty,
                IsExpired = false
            };
            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            model.Username = User.Identity?.Name ?? string.Empty;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login");
            }

            var hashedOldPassword = Utilities.SecurityHelper.HashPassword(model.OldPassword);
            var hashedNewPassword = Utilities.SecurityHelper.HashPassword(model.NewPassword);

            bool isCurrentValid = await _authService.CheckCurrentPasswordAsync(userId, hashedOldPassword);
            if (!isCurrentValid)
            {
                model.ErrorMessage = "Current password is incorrect. Please enter old password!";
                return View(model);
            }

            bool isSameAsOld = await _authService.CheckCurrentPasswordAsync(userId, hashedNewPassword);
            if (isSameAsOld)
            {
                model.ErrorMessage = "New password cannot be the same as the current password. Please choose a different password!";
                return View(model);
            }

            bool isUpdated = await _authService.UpdatePasswordAsync(userId, hashedNewPassword);
            if (isUpdated)
            {
                // If the user is on the expired page, log them out or redirect to Home.
                // The old system abandoned session and redirected to Home, but standard practice is to force re-login.
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                HttpContext.Session.Clear();
                
                TempData["SuccessMessage"] = "Password Changed Successfully. Kindly login again.";
                return RedirectToAction("Login");
            }

            model.ErrorMessage = "Error updating password.";
            return View(model);
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePasswordOnExpire()
        {
            var model = new ChangePasswordViewModel
            {
                Username = User.Identity?.Name ?? string.Empty,
                IsExpired = true
            };
            return View("ChangePassword", model);
        }
        
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> ChangePasswordOnExpire(ChangePasswordViewModel model)
        {
            model.IsExpired = true;
            return ChangePassword(model);
        }
    }
}
