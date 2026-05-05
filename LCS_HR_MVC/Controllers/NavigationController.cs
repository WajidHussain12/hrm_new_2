using LCS_HR_MVC.Models.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class NavigationController : Controller
    {
        [HttpGet]
        public IActionResult UnderDevelopment(string? title, string? legacyUrl)
        {
            var model = new LegacyPageViewModel
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Module" : title.Trim(),
                LegacyUrl = string.IsNullOrWhiteSpace(legacyUrl) ? string.Empty : legacyUrl.Trim()
            };

            ViewData["PageTitle"] = model.Title;
            return View(model);
        }
    }
}
