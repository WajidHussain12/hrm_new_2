using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class AttendanceManagementController : Controller
    {
        private readonly IAttendanceManagementService _svc;
        private readonly ILogger<AttendanceManagementController> _logger;

        public AttendanceManagementController(
            IAttendanceManagementService svc,
            ILogger<AttendanceManagementController> logger)
        {
            _svc    = svc;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════
        //  INDEX — main attendance management page
        // ════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> Index(
            AttendanceManagementFilter? filter,
            CancellationToken ct)
        {
            var userId = CurrentUserId();
            filter ??= new AttendanceManagementFilter();

            // Clamp month/year to valid range
            if (filter.Year  < 2000) filter.Year  = DateTime.Now.Year;
            if (filter.Month < 1 || filter.Month > 12) filter.Month = DateTime.Now.Month;
            if (filter.Page  < 1) filter.Page = 1;
            filter.PageSize = 50;

            ViewData["PageTitle"] = "Employee Attendance";

            try
            {
                var vm = await _svc.GetAttendancePageAsync(filter, userId, ct);
                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading attendance management page.");
                TempData["ErrorMessage"] = "Error loading attendance data: " + ex.Message;
                var vm = await _svc.GetAttendancePageAsync(filter, userId, ct);
                return View(vm);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(AttendanceManagementFilter filter)
        {
            // Redirect to GET with filter bound as route values
            return RedirectToAction(nameof(Index), new
            {
                filter.EmpNo,
                filter.Year,
                filter.Month,
                filter.CityCode,
                filter.AttSource,
                filter.AttStatus,
                filter.FromDate,
                filter.ToDate,
                filter.Page
            });
        }

        // ════════════════════════════════════════════════════════════════════
        //  AJAX — employee autocomplete
        // ════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> SearchEmployees(string term, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
                return Json(Array.Empty<object>());

            var userId  = CurrentUserId();
            var results = await _svc.SearchEmployeesAsync(term.Trim(), userId, ct);
            return Json(results);
        }

        // ════════════════════════════════════════════════════════════════════
        //  CREATE ADJUSTMENT
        // ════════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult CreateAdjustment()
        {
            ViewData["PageTitle"] = "Add Attendance Adjustment";
            return PartialView("_AdjustmentForm", new AttendanceAdjustmentModel
            {
                AdjustmentType = "A",
                AdjustmentDate = DateTime.Today
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAdjustment(
            AttendanceAdjustmentModel model, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields.";
                return RedirectToAction(nameof(Index));
            }

            var userId = CurrentUserId();
            var (ok, msg) = await _svc.AddAdjustmentAsync(model, userId, ct);

            if (ok) TempData["SuccessMessage"] = msg;
            else    TempData["ErrorMessage"]   = msg;

            return RedirectToAction(nameof(Index), new
            {
                Year  = model.AdjustmentDate?.Year  ?? DateTime.Now.Year,
                Month = model.AdjustmentDate?.Month ?? DateTime.Now.Month,
            });
        }

        // ════════════════════════════════════════════════════════════════════
        //  EDIT ADJUSTMENT
        // ════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> EditAdjustment(string empNo, string date, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(empNo) || string.IsNullOrWhiteSpace(date))
                return Json(new { success = false, message = "Invalid request." });

            if (!DateTime.TryParse(date, out var parsedDate))
                return Json(new { success = false, message = "Invalid date." });

            var model = await _svc.GetAdjustmentAsync(empNo, parsedDate, ct);
            if (model == null)
                return Json(new { success = false, message = "Adjustment record not found." });

            return Json(new
            {
                success  = true,
                empNo    = model.EmpNo,
                empName  = model.EmpName,
                date     = parsedDate.ToString("yyyy-MM-dd"),
                adjType  = model.AdjustmentType,
                reason   = model.Reason
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAdjustment(
            AttendanceAdjustmentModel model, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please fill in all required fields.";
                return RedirectToAction(nameof(Index));
            }

            var userId = CurrentUserId();
            var (ok, msg) = await _svc.UpdateAdjustmentAsync(model, userId, ct);

            if (ok) TempData["SuccessMessage"] = msg;
            else    TempData["ErrorMessage"]   = msg;

            return RedirectToAction(nameof(Index), new
            {
                Year  = model.AdjustmentDate?.Year  ?? DateTime.Now.Year,
                Month = model.AdjustmentDate?.Month ?? DateTime.Now.Month,
            });
        }

        // ════════════════════════════════════════════════════════════════════
        //  DELETE ADJUSTMENT
        // ════════════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAdjustment(
            string empNo, string date, int year, int month, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(empNo) || string.IsNullOrWhiteSpace(date))
            {
                TempData["ErrorMessage"] = "Invalid request.";
                return RedirectToAction(nameof(Index));
            }

            if (!DateTime.TryParse(date, out var parsedDate))
            {
                TempData["ErrorMessage"] = "Invalid date.";
                return RedirectToAction(nameof(Index));
            }

            var (ok, msg) = await _svc.DeleteAdjustmentAsync(empNo, parsedDate, ct);

            if (ok) TempData["SuccessMessage"] = msg;
            else    TempData["ErrorMessage"]   = msg;

            return RedirectToAction(nameof(Index), new { year, month });
        }

        // ════════════════════════════════════════════════════════════════════
        //  AJAX — update check-in / check-out time
        // ════════════════════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCheckTime(
            string empNo, string date, string checkType,
            string newTime, string source, CancellationToken ct)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return Json(new { success = false, message = "Invalid date." });

            if (!TimeSpan.TryParse(newTime, out var parsedTime))
                return Json(new { success = false, message = "Invalid time format (HH:mm)." });

            var userId = CurrentUserId();
            var (ok, msg) = await _svc.UpdateCheckTimeAsync(
                empNo, parsedDate, checkType, parsedTime, source, userId, ct);

            return Json(new { success = ok, message = msg });
        }

        // ════════════════════════════════════════════════════════════════════
        //  HELPER
        // ════════════════════════════════════════════════════════════════════

        private string CurrentUserId() =>
            User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
    }
}
