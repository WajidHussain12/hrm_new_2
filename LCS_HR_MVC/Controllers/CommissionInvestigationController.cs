using System.Security.Claims;
using Dapper;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class CommissionInvestigationController : Controller
    {
        private readonly ICommissionInvestigationService _service;
        private readonly IEmployeeService                _employeeService;
        private readonly ISetupService                   _setupService;
        private readonly ILogger<CommissionInvestigationController> _logger;

        public CommissionInvestigationController(
            ICommissionInvestigationService service,
            IEmployeeService                employeeService,
            ISetupService                   setupService,
            ILogger<CommissionInvestigationController> logger)
        {
            _service         = service;
            _employeeService = employeeService;
            _setupService    = setupService;
            _logger          = logger;
        }

        // ── Index — Search / List ─────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index(CommissionInvestigationFilter? filter)
        {
            var now = DateTime.Now;

            filter ??= new CommissionInvestigationFilter();

            // Defaults
            if (filter.Year  == 0) filter.Year  = now.Year;
            if (filter.Month == 0) filter.Month = now.Month;

            var vm = new CommissionInvestigationIndexVm
            {
                Filter          = filter,
                SearchPerformed = false,
            };

            try
            {
                vm.AvailableYears = await _service.GetAvailableYearsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load years dropdown for CommissionInvestigation Index");
                vm.ErrorMessage = "Years dropdown load karne mein masla: " + ex.Message;
            }

            // Only search when EmpNo is provided
            bool hasFilter = !string.IsNullOrWhiteSpace(filter.EmpNo);

            if (hasFilter)
            {
                try
                {
                    vm.SearchResults    = await _service.SearchEmployeesAsync(filter);
                    vm.SearchPerformed  = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Employee commission search failed for filter {@Filter}", filter);
                    vm.ErrorMessage    = "Search mein masla: " + ex.Message;
                    vm.SearchPerformed = true;
                }
            }

            return View(vm);
        }

        // ── Detail — Full Investigation ───────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Detail(string empNo, int year, int month, string cityCode)
        {
            if (string.IsNullOrWhiteSpace(empNo) || year < 2020 || month < 1 || month > 12 || string.IsNullOrWhiteSpace(cityCode))
            {
                TempData["ErrorMessage"] = "Invalid investigation parameters.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var vm = await _service.GetInvestigationAsync(empNo.Trim(), year, month, cityCode.Trim());
                return View(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Commission investigation failed for EmpNo={EmpNo} {Year}/{Month}", empNo, year, month);
                TempData["ErrorMessage"] = $"Investigation load karne mein masla: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // ── CRUD — Notes ─────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateNote(CreateNoteRequest request)
        {
            var createdBy = User.Identity?.Name
                         ?? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                         ?? "Unknown";

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage);
                TempData["ErrorMessage"] = "Note invalid: " + string.Join("; ", errors);
                return RedirectToDetail(request.EmpNo, request.Year, request.Month, request.CityCode);
            }

            try
            {
                var noteId = await _service.CreateNoteAsync(request, createdBy);
                TempData["SuccessMessage"] = $"Note #{noteId} successfully add kar diya gaya.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateNote failed for EmpNo={EmpNo}", request.EmpNo);
                TempData["ErrorMessage"] = "Note save karne mein masla: " + ex.Message;
            }

            return RedirectToDetail(request.EmpNo, request.Year, request.Month, request.CityCode);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateNoteStatus(
            int noteId, string newStatus, string empNo, int year, int month, string cityCode)
        {
            if (noteId <= 0 || string.IsNullOrWhiteSpace(newStatus))
            {
                TempData["ErrorMessage"] = "Invalid note update request.";
                return RedirectToDetail(empNo, year, month, cityCode);
            }

            var updatedBy = User.Identity?.Name
                          ?? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                          ?? "Unknown";

            try
            {
                var ok = await _service.UpdateNoteStatusAsync(noteId, newStatus, updatedBy);
                TempData[ok ? "SuccessMessage" : "ErrorMessage"] =
                    ok ? $"Note #{noteId} status '{newStatus}' kar diya gaya."
                       : $"Note #{noteId} update nahi hua (already deleted or not found).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateNoteStatus failed for NoteId={NoteId}", noteId);
                TempData["ErrorMessage"] = "Status update mein masla: " + ex.Message;
            }

            return RedirectToDetail(empNo, year, month, cityCode);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteNote(
            int noteId, string empNo, int year, int month, string cityCode)
        {
            if (noteId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid note ID.";
                return RedirectToDetail(empNo, year, month, cityCode);
            }

            var deletedBy = User.Identity?.Name
                          ?? User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                          ?? "Unknown";

            try
            {
                var ok = await _service.SoftDeleteNoteAsync(noteId, deletedBy);
                TempData[ok ? "SuccessMessage" : "ErrorMessage"] =
                    ok ? $"Note #{noteId} delete kar diya gaya."
                       : $"Note #{noteId} delete nahi hua (already deleted or not found).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteNote failed for NoteId={NoteId}", noteId);
                TempData["ErrorMessage"] = "Delete mein masla: " + ex.Message;
            }

            return RedirectToDetail(empNo, year, month, cityCode);
        }

        // ── API: Notes (partial refresh) ──────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetNotes(string empNo, int year, int month)
        {
            try
            {
                var notes = await _service.GetNotesAsync(empNo, year, month);
                return Json(notes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetNotes failed for EmpNo={EmpNo}", empNo);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── API: Route Code Suggestions ──────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetRouteCodeSuggestions(string empNo, string cityCode)
        {
            try
            {
                var vm = await _service.GetRouteCodeSuggestionsAsync(empNo.Trim(), cityCode.Trim());
                return Json(vm);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRouteCodeSuggestions failed for EmpNo={EmpNo}", empNo);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── API: Fix modals — dropdown data ──────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetFixDropdowns(string cityCode)
        {
            try
            {
                var codeTypes = await _employeeService.GetCourierCodeTypesAsync();
                var locations = await _employeeService.GetLocationsByCityCodeAsync(cityCode ?? "");
                return Json(new
                {
                    codeTypes = codeTypes.Select(x => new { value = x.Value, text = x.Text }),
                    locations = locations.Select(x => new { value = x.Value, text = x.Text })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetFixDropdowns failed for cityCode={CityCode}", cityCode);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── API: Fix — Assign Route Code (saves to S10; tries S6+S7 sync) ──────

        [HttpPost]
        public async Task<IActionResult> FixRouteCode([FromBody] FixRouteCodeRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.EmpNo) || string.IsNullOrWhiteSpace(req.RouteCode))
                return BadRequest(new { error = "EmpNo aur RouteCode required hain." });

            var userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                var model = new EmployeeRouteCodeModel
                {
                    Code         = "Auto Generated",
                    EmpNo        = req.EmpNo.Trim(),
                    EmployeeName = req.EmployeeName.Trim(),
                    RouteCode    = req.RouteCode.Trim().ToUpper(),
                    CityCode     = req.CityCode.Trim(),
                    LocationId   = req.LocationId,
                    CodeType     = req.CodeType,
                    FromDate     = req.FromDate ?? DateTime.Today,
                    ToDate       = req.ToDate,
                    Comments     = req.Comments ?? "",
                    IsRBIExclude = false
                };

                // ── Step 1: Save to S10 (Main HR) ──────────────────────────────
                bool savedS10 = await _employeeService.AddEmployeeRouteCodeAsync(model, userId);
                if (!savedS10)
                    return Json(new { success = false, error = "Route code save nahi hua. Duplicate record ya invalid data check karein." });

                // ── Step 2: Replicate to S6 (Billing) ──────────────────────────
                var s6Status = "unknown";
                try
                {
                    var s6Cs = _logger is not null
                        ? HttpContext.RequestServices.GetService<IConfiguration>()?.GetConnectionString("LHR_Billing")
                        : null;
                    s6Status = await ReplicateRouteCodeToServerAsync(s6Cs, model, userId, "lcs_hr") ? "ok" : "failed";
                }
                catch (Exception ex6)
                {
                    s6Status = $"error: {ex6.Message.Split('.')[0]}";
                    _logger.LogWarning(ex6, "FixRouteCode S6 sync failed for EmpNo={EmpNo}", req.EmpNo);
                }

                // ── Step 3: Replicate to S7 (MIS) ──────────────────────────────
                var s7Status = "unknown";
                try
                {
                    var s7Cs = HttpContext.RequestServices.GetService<IConfiguration>()?.GetConnectionString("MIS");
                    s7Status = await ReplicateRouteCodeToServerAsync(s7Cs, model, userId, "lcs_hr") ? "ok" : "failed";
                }
                catch (Exception ex7)
                {
                    s7Status = $"error: {ex7.Message.Split('.')[0]}";
                    _logger.LogWarning(ex7, "FixRouteCode S7 sync failed for EmpNo={EmpNo}", req.EmpNo);
                }

                return Json(new {
                    success  = true,
                    message  = $"Route Code '{req.RouteCode}' employee {req.EmpNo} ko Server 10 par assign ho gaya!",
                    s10      = "ok",
                    s6       = s6Status,
                    s7       = s7Status,
                    syncNote = s6Status == "ok" && s7Status == "ok"
                        ? "Teenon servers (S10, S6, S7) par sync ho gaya!"
                        : $"S10 ✓ | S6: {s6Status} | S7: {s7Status} — Failed servers par manually add karna hoga."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FixRouteCode failed for EmpNo={EmpNo}", req.EmpNo);
                return Json(new { success = false, error = ex.Message });
            }
        }

        private static async Task<bool> ReplicateRouteCodeToServerAsync(
            string? connectionString, EmployeeRouteCodeModel model, string userId, string dbName)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return false;

            // Replace database name in connection string to target the correct DB on that server
            var cs = System.Text.RegularExpressions.Regex.Replace(
                connectionString, @"database=\w+", $"database={dbName}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            using var conn = new MySql.Data.MySqlClient.MySqlConnection(cs);
            await conn.OpenAsync();

            // Check duplicate
            var dup = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM hr_employeeroutecode WHERE Emp_No=@EmpNo AND RouteCode=@RouteCode AND ToDate IS NULL",
                new { EmpNo = model.EmpNo, RouteCode = model.RouteCode });
            if (dup > 0) return true; // already exists — treat as ok

            // Get next Code (simple MAX+1)
            var maxCode = await conn.ExecuteScalarAsync<string>(
                "SELECT IFNULL(MAX(CAST(Code AS UNSIGNED)),0)+1 FROM hr_employeeroutecode") ?? "1";

            await conn.ExecuteAsync(@"INSERT INTO hr_employeeroutecode
                (Code, RouteCode, citycode, LocationId, Emp_No, FromDate, ToDate, Comments, CodeType, RBIExclude, createdby, Created_Date, UpdatedBy, Updated_Date)
                VALUES (@Code, @RouteCode, @CityCode, @LocationId, @EmpNo, @FromDate, @ToDate, @Comments, @CodeType, 0, @CreatedBy, NOW(), @CreatedBy, NOW())",
                new {
                    Code       = maxCode,
                    RouteCode  = model.RouteCode,
                    CityCode   = model.CityCode,
                    LocationId = model.LocationId,
                    EmpNo      = model.EmpNo,
                    FromDate   = model.FromDate,
                    ToDate     = model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value,
                    Comments   = (object?)model.Comments ?? DBNull.Value,
                    CodeType   = model.CodeType,
                    CreatedBy  = userId
                });
            return true;
        }

        // ── API: Fix — Commission Eligibility ────────────────────────────────

        [HttpPost]
        public async Task<IActionResult> FixEligibility([FromBody] FixEligibilityRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.EmpNo))
                return BadRequest(new { error = "EmpNo required hai." });

            var userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                var model = new CommissionEligibilityModel
                {
                    EmpNo                 = req.EmpNo.Trim(),
                    EmployeeDescription   = req.EmployeeName.Trim(),
                    OLE_Dispatch_Proper   = req.CommissionId2,
                    OLE_Transit_Dispatch  = req.CommissionId3,
                    OLE_Delivery_OPS      = req.CommissionId4
                };

                bool saved = await _setupService.SaveCommissionEligibilityAsync(model, userId);
                if (saved)
                    return Json(new { success = true, message = $"Employee {req.EmpNo} ki eligibility successfully save ho gayi!" });
                else
                    return Json(new { success = false, error = "Eligibility save nahi hui. Employee active hai aur name correct hai?" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FixEligibility failed for EmpNo={EmpNo}", req.EmpNo);
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── API: Reprocess Commission (single employee, single month) ────────

        [HttpPost]
        public async Task<IActionResult> ReprocessCommission([FromBody] ReprocessRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.EmpNo) || req.Year < 2020 || req.Month < 1 || req.Month > 12)
                return BadRequest(new { error = "EmpNo, Year, Month required hain." });

            var userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "system";

            try
            {
                // Delegate to PayrollService.ProcessSingleEmployeeCommissionAsync (same as CommissionVerification/Reprocess)
                var payrollService = HttpContext.RequestServices.GetService<IPayrollService>();
                if (payrollService == null)
                    return Json(new { success = false, error = "PayrollService not available." });

                var result = await payrollService.ProcessSingleEmployeeCommissionAsync(req.EmpNo.Trim(), req.Year, req.Month, userId);
                return Json(new {
                    success = result.Success,
                    message = result.Message,
                    year    = req.Year,
                    month   = req.Month
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReprocessCommission failed for EmpNo={EmpNo} {Year}/{Month}", req.EmpNo, req.Year, req.Month);
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Delete phantom NULL Cour_id commission record ────────────────────

        [HttpPost]
        public async Task<IActionResult> DeletePhantomCommissionRecord([FromBody] ReprocessRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.EmpNo) || req.Year < 2020 || req.Month < 1 || req.Month > 12)
                return BadRequest(new { error = "EmpNo, Year, Month required hain." });

            try
            {
                var config = HttpContext.RequestServices.GetService<IConfiguration>()
                          ?? throw new Exception("IConfiguration not available.");
                var cs = config.GetConnectionString("DefaultConnection")
                      ?? config.GetConnectionString("Main")
                      ?? throw new Exception("Main DB connection string not configured.");
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(cs);
                await conn.OpenAsync();

                // Only delete the record where Cour_id IS NULL for this emp/year/month
                // Safety check: only delete if a non-NULL Cour_id record ALSO exists for same period
                var realCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM lcs_hr.hr_commissionprocess WHERE emp_no=@E AND Year=@Y AND Month=@M AND Cour_id IS NOT NULL",
                    new { E = req.EmpNo.Trim(), Y = req.Year, M = req.Month });

                if (realCount == 0)
                    return Json(new { success = false, error = "Koi real (non-NULL Cour_id) record nahi mila — phantom delete karna safe nahi hai." });

                var deleted = await conn.ExecuteAsync(
                    "DELETE FROM lcs_hr.hr_commissionprocess WHERE emp_no=@E AND Year=@Y AND Month=@M AND Cour_id IS NULL LIMIT 5",
                    new { E = req.EmpNo.Trim(), Y = req.Year, M = req.Month });

                _logger.LogInformation("DeletePhantom: deleted {N} NULL Cour_id records for EmpNo={EmpNo} {Year}/{Month}",
                    deleted, req.EmpNo, req.Year, req.Month);
                return Json(new { success = true, deletedRows = deleted,
                    message = $"{deleted} phantom record(s) delete ho gaye. Page refresh karein." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeletePhantomCommissionRecord failed for {EmpNo} {Year}/{Month}", req.EmpNo, req.Year, req.Month);
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private RedirectToActionResult RedirectToDetail(string empNo, int year, int month, string cityCode) =>
            RedirectToAction(nameof(Detail), new { empNo, year, month, cityCode });
    }
}
