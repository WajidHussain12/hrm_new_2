using System.Security.Claims;
using LCS_HR_MVC.Models.Report;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private static readonly HashSet<string> SupportedReportTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "PaySlips",
            "SalariesDetails",
            "SalaryBreakup",
            "CommissionBreakup",
            "EmployeesList",
            "TerminationReport",
            "AttendanceReport",
            "AttendanceSummary",
            "TrainingReport",
            "NewJoiningDetails",
            "LeaveStatusReport",
            "EmployeePersonalInfo",
            "EmpPerInfoSummary",
            "WarningInfo",
            "RouteDetails",
            "AttendanceTimeSpan",
            "SalariesInBank",
            "SalariesInChequeWallet",
            "DeductionDetail",
            "SalarySummary",
            "AdvanceSalaryDetail",
            "DeductionReport",
            "ExtrasReport",
            "HLAttendanceRatio",
            "HumLeopardAttendance",
            "EmployeeFuelDetail",
            "FuelCardTransaction",
            "EmpPayDetails",
            "VoiceOfEmployee",
            "EmpLoanDetails",
            "AttendanceSummaryChart",
            "RBICNWiseDetail",
            "RBIDeductionCNWiseDetail",
            "CashCNWiseDetail",
            "CODReturnCNWiseDetail",
            "VASCNWiseDetail",
            "CODPickupCNWiseDetail",
            "AdvanceSalaryVoucher",
            "SalaryTracking",
            "SalariesSummaryAccounts",
            "CommissionDetail",
            "LoanDetail",
            "GratuityReport"
        };

        private readonly IReportService _reportService;
        private readonly ISetupService _setupService;
        private readonly IPayrollService _payrollService;
        private readonly ILogger<ReportController> _logger;

        public ReportController(IReportService reportService, ISetupService setupService, IPayrollService payrollService, ILogger<ReportController> logger)
        {
            _reportService = reportService;
            _setupService = setupService;
            _payrollService = payrollService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? reportType)
        {
            var model = new ReportViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            if (!string.IsNullOrWhiteSpace(reportType) && SupportedReportTypes.Contains(reportType))
            {
                model.ReportType = SupportedReportTypes.First(x => x.Equals(reportType, StringComparison.OrdinalIgnoreCase));
            }

            await PopulateDropdownsAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(ReportViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Generate")
                {
                    var data = await _reportService.GetReportDataAsync(model, currentUserId);
                    model.ReportData = data;
                    if (data.Count > 0)
                    {
                        model.Columns = data[0].Keys.ToList();
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No records found matching the criteria.";
                    }
                }
                else if (action == "ExportExcel")
                {
                    var data = await _reportService.GetReportDataAsync(model, currentUserId);
                    if (data.Count > 0)
                    {
                        var fileBytes = _reportService.GenerateExcelReport(data, model.ReportType);
                        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{model.ReportType}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
                    }
                    else
                    {
                        TempData["ErrorMessage"] = "No records found to export.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing report");
                TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
            }

            await PopulateDropdownsAsync();
            return View(model);
        }

        private async Task PopulateDropdownsAsync()
        {
            var zones = await _payrollService.GetZonesAsync();
            ViewBag.Zones = new SelectList(zones, "Value", "Text");

            var cities = await _setupService.GetAllCitiesAsync();
            ViewBag.Cities = new SelectList(cities, "Code", "FullName");

            var departments = await _reportService.GetHrDepartmentsAsync();
            ViewBag.DepartmentsList = new SelectList(departments, "Value", "Text");
        }
    }
}
