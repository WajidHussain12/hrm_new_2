using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class CommissionVerificationController : Controller
    {
        private readonly ICommissionVerificationService _verificationService;
        private readonly IPayrollService                _payrollService;
        private readonly ILogger<CommissionVerificationController> _logger;

        private const int PageSize = 50;

        public CommissionVerificationController(
            ICommissionVerificationService verificationService,
            IPayrollService                payrollService,
            ILogger<CommissionVerificationController> logger)
        {
            _verificationService = verificationService;
            _payrollService      = payrollService;
            _logger              = logger;
        }

        // ── GET: /CommissionVerification ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Index(
            int? year, int? month,
            string cityCode             = "",
            string commissionTypeFilter = "All",
            int page                    = 1,
            int page2                   = 1)
        {
            var now = DateTime.Now;
            var y   = (year  >= 2020 && year  <= 2100) ? year!.Value  : now.Year;
            var m   = (month >= 1    && month <= 12)   ? month!.Value : now.Month;
            if (page  < 1) page  = 1;
            if (page2 < 1) page2 = 1;

            var filter = new CommissionVerificationFilter
            {
                Year                 = y,
                Month                = m,
                CityCode             = cityCode,
                CommissionTypeFilter = commissionTypeFilter
            };

            var (from, to) = GetPeriod(y, m);

            var vm = new CommissionVerificationViewModel
            {
                Filter          = filter,
                AvailableYears  = await _verificationService.GetAvailableYearsAsync(),
                AvailableCities = await _verificationService.GetCitiesAsync(),
                IsDefaultView   = true,
                CurrentPage     = page,
                PageSize        = PageSize,
                ProcessedPage   = page2,
                PeriodFrom      = from,
                PeriodTo        = to
            };

            try
            {
                var (rows, total) = await _verificationService.GetAllCommissionsPagedAsync(filter, page, PageSize);
                vm.AllCommissionRows = rows;
                vm.TotalAllCount     = total;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommissionVerification/Index CN view failed.");
                vm.ErrorMessage = "Failed to load commission data. Please try again.";
            }

            try
            {
                var (pRows, pTotal) = await _verificationService.GetProcessedCommissionsPagedAsync(filter, page2, PageSize);
                vm.ProcessedRows       = pRows;
                vm.TotalProcessedCount = pTotal;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommissionVerification/Index processed view failed.");
            }

            return View(vm);
        }

        // ── POST: /CommissionVerification/Search ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Search(CommissionVerificationFilter filter)
        {
            var (from, to) = GetPeriod(filter.Year, filter.Month);

            var vm = new CommissionVerificationViewModel
            {
                Filter          = filter,
                AvailableYears  = await _verificationService.GetAvailableYearsAsync(),
                AvailableCities = await _verificationService.GetCitiesAsync(),
                SearchPerformed = true,
                IsDefaultView   = false,
                PeriodFrom      = from,
                PeriodTo        = to
            };

            if (filter.Year < 2020 || filter.Year > 2100 || filter.Month < 1 || filter.Month > 12)
            {
                vm.ErrorMessage = "Invalid year or month.";
                return View("Index", vm);
            }

            if (string.IsNullOrWhiteSpace(filter.EmpNoSearch) &&
                string.IsNullOrWhiteSpace(filter.EmpNameSearch))
            {
                vm.ErrorMessage = "Please enter an employee number or name to search.";
                return View("Index", vm);
            }

            try
            {
                vm.MatchedEmployees = await _verificationService.SearchEmployeesAsync(filter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommissionVerification/Search failed.");
                vm.ErrorMessage = "Search failed. Please try again.";
            }

            return View("Index", vm);
        }

        // ── GET: /CommissionVerification/Detail ──────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Detail(
            string empNo, int year, int month, string cityCode = "",
            string commissionTypeFilter = "All",
            string deliveryFilter       = "All")
        {
            if (string.IsNullOrWhiteSpace(empNo))
                return RedirectToAction(nameof(Index));

            var (from, to) = GetPeriod(year, month);

            var vm = new CommissionVerificationViewModel
            {
                Filter = new CommissionVerificationFilter
                {
                    Year                 = year,
                    Month                = month,
                    CityCode             = cityCode,
                    CommissionTypeFilter = commissionTypeFilter,
                    DeliveryFilter       = deliveryFilter
                },
                AvailableYears  = await _verificationService.GetAvailableYearsAsync(),
                AvailableCities = await _verificationService.GetCitiesAsync(),
                SearchPerformed = true,
                IsDefaultView   = false,
                PeriodFrom      = from,
                PeriodTo        = to
            };

            try
            {
                var (summary, cns, missingCns) = await _verificationService.GetEmployeeDetailAsync(
                    empNo, year, month, cityCode);

                vm.SelectedEmployee = summary;
                vm.CnDetails        = ApplyDetailFilters(cns, commissionTypeFilter, deliveryFilter);
                vm.MissingCns       = missingCns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommissionVerification/Detail failed for {EmpNo}.", empNo);
                vm.ErrorMessage = "Failed to load employee detail. Please try again.";
            }

            return View("Index", vm);
        }

        // ── POST: /CommissionVerification/Reprocess ──────────────────────────────
        // Employee-scoped commission reprocess (calls Step 5 for single employee).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reprocess(string empNo, int year, int month, string cityCode = "")
        {
            if (string.IsNullOrWhiteSpace(empNo))
                return RedirectToAction(nameof(Index));

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

            try
            {
                var result = await _payrollService.ProcessSingleEmployeeCommissionAsync(empNo, year, month, userId);

                TempData["ReprocessResult"]  = result.Success ? "success" : "error";
                TempData["ReprocessMessage"] = result.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommissionVerification/Reprocess failed for {EmpNo}.", empNo);
                TempData["ReprocessResult"]  = "error";
                TempData["ReprocessMessage"] = "Reprocess failed. Please try again.";
            }

            return RedirectToAction(nameof(Detail), new { empNo, year, month, cityCode });
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static (DateTime From, DateTime To) GetPeriod(int year, int month)
        {
            var from = new DateTime(year, month, 21).AddMonths(-1);
            var to   = new DateTime(year, month, 20);
            return (from, to);
        }

        private static List<CnCommissionRow> ApplyDetailFilters(
            List<CnCommissionRow> cns, string commTypeFilter, string deliveryFilter)
        {
            var result = cns.AsEnumerable();

            if (commTypeFilter != "All")
                result = result.Where(r => r.CommissionType == commTypeFilter);

            if (deliveryFilter == "OnTime")
                result = result.Where(r => r.CommissionType != "COD" || r.IsOnTime);
            else if (deliveryFilter == "Delayed")
                result = result.Where(r => r.CommissionType == "COD" && !r.IsOnTime);

            return result.ToList();
        }
    }
}
