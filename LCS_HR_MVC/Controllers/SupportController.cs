using System.Security.Claims;
using LCS_HR_MVC.Models.Support;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class SupportController : Controller
    {
        private readonly ISupportService _supportService;
        private readonly ILogger<SupportController> _logger;

        public SupportController(ISupportService supportService, ILogger<SupportController> logger)
        {
            _supportService = supportService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> DbBackUp(string? latestFileName)
        {
            ViewData["PageTitle"] = "Data Base Back Up";
            var model = await _supportService.GetDbBackUpPageAsync(latestFileName);
            return View(model);
        }

        [HttpPost]
        [ActionName("DbBackUp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DbBackUpPost(string action)
        {
            if (string.Equals(action, "Reset", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(DbBackUp));
            }

            try
            {
                var currentUserName = User.Identity?.Name ?? "User";
                var fileName = await _supportService.CreateDatabaseBackupAsync(currentUserName);
                TempData["SuccessMessage"] = "Process Completed Successfully.";
                return RedirectToAction(nameof(DbBackUp), new { latestFileName = fileName });
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating database backup.");
                TempData["ErrorMessage"] = "Unknown Error";
            }

            return RedirectToAction(nameof(DbBackUp));
        }

        [HttpGet]
        public async Task<IActionResult> DownloadBackup(string fileName)
        {
            var file = await _supportService.GetBackupFileAsync(fileName);
            if (file == null)
            {
                return NotFound();
            }

            return File(file.Content, file.ContentType, file.FileName);
        }

        [HttpGet]
        public IActionResult ResultSetExporter()
        {
            ViewData["PageTitle"] = "Excel Exporter";
            return View(new ResultSetExporterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResultSetExporter(ResultSetExporterViewModel model, string action)
        {
            ViewData["PageTitle"] = "Excel Exporter";

            if (string.Equals(action, "Reset", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(ResultSetExporter));
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var file = await _supportService.ExportResultSetAsync(model);
                return File(file.Content, file.ContentType, file.FileName);
            }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting result set.");
                ModelState.AddModelError(string.Empty, "Unknown Error");
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ErrorLogs([FromQuery] ErrorLogsQueryModel query)
        {
            ViewData["PageTitle"] = "Error Logs";
            var model = await _supportService.GetErrorLogsPageAsync(query);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> AuditViewer([FromQuery] AuditViewerQueryModel query)
        {
            ViewData["PageTitle"] = "Audit Viewer for Developers";
            var model = await _supportService.GetAuditViewerPageAsync(query);
            return View(model);
        }

        [HttpGet]
        public IActionResult DataUploadingUtility()
        {
            ViewData["PageTitle"] = "Data Upload Utiltity";
            return View(new DataUploadingUtilityViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DataUploadingUtility(DataUploadingUtilityViewModel model, string action)
        {
            ViewData["PageTitle"] = "Data Upload Utiltity";

            if (string.Equals(action, "Reset", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(DataUploadingUtility));
            }

            try
            {
                if (string.Equals(action, "CheckConnection", StringComparison.OrdinalIgnoreCase))
                {
                    var isValid = await _supportService.ValidateConnectionAsync(model.ConnectionString);
                    model.LastConnectionCheckPassed = isValid;
                    if (isValid)
                    {
                        TempData["SuccessMessage"] = "Connected Successfully to server.";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "Invalid connection string.Please check user credentials.";
                    }

                    return View(model);
                }

                var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
                var result = await _supportService.UploadDataAsync(model, currentUserId);
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction(nameof(DataUploadingUtility));
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing upload utility.");
                TempData["ErrorMessage"] = "Unknown Error";
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> AttLogViewer()
        {
            ViewData["PageTitle"] = "Attendance Log Viewer (For Karachi)";
            var model = await _supportService.GetAttendanceLogViewerPageAsync(GetWorkingDate());
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AttLogViewer(AttendanceLogViewerViewModel model, string action)
        {
            ViewData["PageTitle"] = "Attendance Log Viewer (For Karachi)";

            if (string.Equals(action, "Reset", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(AttLogViewer));
            }

            model = await _supportService.GetAttendanceLogViewerPageAsync(GetWorkingDate(), model);
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var file = await _supportService.ExportAttendanceLogAsync(model);
                return File(file.Content, file.ContentType, file.FileName);
            }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting attendance log for employee {EmployeeNo}.", model.EmpNo);
                ModelState.AddModelError(string.Empty, "Unknown Error");
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> SearchAttendanceLogEmployees(string term)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var employees = await _supportService.SearchAttendanceLogEmployeesAsync(term ?? string.Empty, currentUserId);
            return Json(employees);
        }

        [HttpGet]
        public async Task<IActionResult> DocSamples()
        {
            ViewData["PageTitle"] = "Document Samples";
            var model = await _supportService.GetFileLibraryAsync("Docs");
            return View("FileLibrary", model);
        }

        [HttpGet]
        public async Task<IActionResult> Softwares()
        {
            ViewData["PageTitle"] = "Download Softwares";
            var model = await _supportService.GetFileLibraryAsync("Softwares");
            return View("FileLibrary", model);
        }

        [HttpGet]
        public async Task<IActionResult> HR_Docs()
        {
            ViewData["PageTitle"] = "HR Documents";
            var model = await _supportService.GetFileLibraryAsync("HR_Docs");
            return View("FileLibrary", model);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadLibraryFile(string libraryKey, string fileName)
        {
            var file = await _supportService.GetLibraryFileAsync(libraryKey, fileName);
            if (file == null)
            {
                return NotFound();
            }

            return File(file.Content, file.ContentType, file.FileName);
        }

        private DateTime GetWorkingDate()
        {
            var sessionValue = HttpContext.Session.GetString("workingdate");
            return DateTime.TryParse(sessionValue, out var workingDate)
                ? workingDate
                : DateTime.Now;
        }
    }
}
