using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Text;
using LCS_HR_MVC.Data;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class MenuService : IMenuService
    {
        private const string ToggleOnlyUrl = "#";
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ILogger<MenuService>? _logger;
        
        // Dictionary mapping old webforms URLs stored in DB to new MVC routes
        private readonly Dictionary<string, string> _routeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Setup
            { "locations", "/Setup/GLLocations" },
            { "setup/Countries", "/Setup/Locations/Countries" },
            { "setup/City", "/Setup/Locations/Cities" },
            { "setup/Assign_Multiple_Locations", "/Setup/AssignMultipleLocations" },
            { "setup/Location_Coordinate_Update", "/Setup/LocationCoordinateUpdate" },
            { "setup/departments", "/Setup/Hr/Departments" },
            { "setup/EmployeeType", "/Setup/Hr/EmployeeTypes" },
            { "setup/LeaveStructure", "/Setup/Hr/LeaveStructures" },
            { "setup/CompanyAssets", "/Setup/CompanyAssets" },
            { "setup/AttendanceRule", "/Setup/Hr/AttendanceRules" },
            { "setup/EmployeeHead", "#" },
            { "setup/Devision", "/Setup/Hr/Divisions" },
            { "setup/Departments_Strength", "/Setup/Hr/DepartmentStrength" },
            { "setup/DefineHRHierarchy", "/Setup/Hr/DefineHRHierarchy" },
            { "setup/CommissionTypes", "/Setup/Payroll/CommissionRates" },
            { "setup/CommissionEligibility", "/Setup/Payroll/CommissionEligibility" },
            { "setup/TaxHead", "/Setup/Payroll/Taxes" },
            { "setup/TaxDetailEntry", "/Setup/TaxDetailEntry" },
            { "setup/allowance_deduction", "/Setup/AllowanceDeductions" },
            { "setup/RouteHead", "/Setup/Payroll/Routes" },
            { "setup/RouteDetailEntry", "/Setup/RouteDetailEntry" },
            { "setup/gazetted_holidays", "/Setup/Payroll/GazettedHolidays" },
            { "setup/Salarybank", "/Setup/Payroll/SalaryBanks" },
            { "setup/Jobs", "/Setup/Hr/Jobs" },
            { "setup/LoanTypes", "/Setup/Payroll/LoanTypes" },
            { "setup/Provinces", "/Setup/Locations/Provinces" },
            { "setup/RegionalZones", "/Setup/Locations/RegionalZones" },
            { "setup/Shifts", "/Setup/Payroll/Shifts" },
            { "setup/emp_glcode", "/Setup/EmpGlCode" },
            { "setup/EmployeeDetail", "/Employee/PersonalDetail" },
            { "Admin/UserRoles", "/Admin/UserRoles" },
            { "Admin/User", "/Admin/Users" },
            { "Admin/UserLocation", "/Setup/Locations/UserLocation" },
            { "Admin/userPrivileges", "/Admin/UserPrivileges" },
            
            // Transaction
            { "transaction/EmployeeTermination", "/Transaction/HR/EmployeeTermination" },
            { "transaction/EmployeeTrainingDetails", "/Transaction/HR/EmployeeTrainingDetails" },
            { "transaction/EmployeeShowCause", "/Transaction/HR/EmployeeShowCause" },
            { "transaction/emp_assets", "/Transaction/HR/EmployeeAssets" },
            { "transaction/emp_bank", "/Transaction/HR/EmployeeBankDetails" },
            { "transaction/emp_PromotionAwards", "/Transaction/HR/EmployeePromotionAwards" },
            { "transaction/EmployeeContracts", "/Transaction/HR/EmployeeContracts" },
            { "transaction/EmployeePayStructure", "/Transaction/EmployeePayStructure" },
            { "transaction/Increment", "/Transaction/HR/Increment" },
            { "transaction/MultipleJobs_approve", "/Transaction/HR/MultipleJobsApprove" },
            { "transaction/EmpIncrementApprovedUnApproved", "/Transaction/HR/IncrementApproval" },
            { "transaction/EmployeeDepartmentDetails", "/Transaction/EmployeeDetails/EmployeeDepartmentDetails" },
            { "transaction/EmployeeSalaryDetails", "/Transaction/EmployeeDetails/EmployeeSalaryDetails" },
            { "transaction/EmployeeFuelDetails", "#" },
            { "transaction/EmpADdetails", "/Transaction/EmployeeDetails/EmpADDetails" },
            { "transaction/emp_shifts", "/Transaction/EmployeeDetails/Shifts" },
            { "transaction/EmpJobDetails", "/Transaction/EmployeeDetails/EmployeeJobDetails" },
            { "transaction/EmployeeRoutCode", "/Transaction/EmployeeRoutCode" },
            { "transaction/EmployeeCommDetails", "/Transaction/EmployeeDetails/EmployeeCommDetails" },
            { "transaction/Employee_Part_time", "/Transaction/EmployeeDetails/EmployeePartTime" },
            { "transaction/Employee_Extras_Fixed", "/Transaction/EmployeeDetails/EmployeeExtraFixed" },
            { "transaction/TagFuelCard", "#" },
            { "transaction/EmployeeExtra", "/Transaction/EmployeeAdjustments/EmployeeExtra" },
            { "transaction/emp_overtime_detail", "/Transaction/EmployeeAdjustments/EmployeeOvertime" },
            { "transaction/EmployeeAttendanceAdjust", "/Transaction/EmployeeAttendanceAdjust" },
            { "transaction/BulkAbsentAdjustment", "/Transaction/EmployeeAdjustments/BulkAttendanceAdjustment" },
            { "transaction/CompleteAttendenceAdjustment", "/Transaction/CompleteAttendenceAdjustment" },
            { "transaction/ExtraHoursAproval", "/Transaction/EmployeeAdjustments/ExtraHoursApproval" },
            { "transaction/EmpAdvance_salary", "/Transaction/EmployeeAdjustments/EmpAdvanceSalary" },
            { "transaction/Penalty_Fine", "/Transaction/EmployeeAdjustments/PenaltyFine" },
            { "transaction/Advance_Salary_Approve", "/Transaction/EmployeeAdjustments/AdvanceSalaryApprove" },
            { "transaction/EmpLoanRequest", "/Transaction/EmployeeLoan/EmpLoanRequest" },
            { "transaction/EmployeeLoanApprove", "/Transaction/EmployeeLoan/EmployeeLoanApprove" },
            { "transaction/LoanDisbursed", "/Transaction/EmployeeLoan/LoanDisbursed" },
            { "transaction/LoanDeduction", "/Transaction/EmployeeLoan/LoanDeduction" },
            { "transaction/EmployeeLeaveRequest", "/Transaction/EmployeeRequest/EmployeeLeaveRequest" },
            { "transaction/EmployeeLeaveRequestApproval", "/Transaction/EmployeeRequest/EmployeeLeaveRequestApproval" },
            { "transaction/EmployeeRequestApproval", "/Transaction/EmployeeRequest/EmployeeLeaveRequestApproval" },
            { "transaction/EmployeeRequest", "/Transaction/EmployeeRequest/EmployeeLeaveRequest" },
            { "transaction/EmployeeLeaves", "/Leaves/EmployeeLeaves" },
            { "transaction/EmployeeMedicalSurvey", "/Employee/MedicalSurvey" },
            { "transaction/FuelPrices", "/Transaction/SalaryProcess/FuelPrices" },
            { "transaction/EmployeeAttendanceProccess", "/Transaction/SalaryProcess/EmployeeAttendanceProccess" },
            { "transaction/Bulk_Attendence_Adjustment", "/Payroll/BulkAttendanceAdjustment" },
            { "transaction/EmployeeAttendanceRe_Proccess", "/Transaction/SalaryProcess/EmployeeAttendanceProccess" },
            { "transaction/CommissionUnlock", "/Transaction/SalaryProcess/CommissionUnlock" },
            { "transaction/UnlockSalary", "/Transaction/SalaryProcess/UnlockSalary" },
            { "transaction/cod_commission", "/Transaction/SalaryProcess/CodCommission" },
            { "transaction/ReturnCOD_Commission", "/Transaction/SalaryProcess/ReturnCodCommission" },
                    { "Transaction/Cash_Commission", "/Transaction/SalaryProcess/CashCommission" },
                    { "Transaction/OverLand_Commission", "/Transaction/SalaryProcess/OverLandCommission" },
            { "transaction/TagCommission", "/Transaction/SalaryProcess/TagCommission" },
            { "transaction/commissionprocess", "/Transaction/SalaryProcess/CommissionProcess" },
            { "transaction/EmployeeCommissionProcess", "/Transaction/SalaryProcess/CommissionProcess" },
            { "transaction/salaries_process", "/Transaction/SalaryProcess/SalariesProcess" },
            { "transaction/Salaries_Process_Emp_Wise", "/Transaction/SalaryProcess/SalaryReprocess" },
            { "Transaction/CloseProcesses", "/Transaction/SalaryProcess/CloseProcesses" },
            { "transaction/DeathCompensation", "/Transaction/SalaryProcess/DeathCompensation" },
            { "transaction/salaries_vouchers", "/Transaction/SalaryProcess/SalaryVouchers" },
            { "transaction/LeaveProcess", "/Transaction/SalaryProcess/LeaveProcess" },
            { "transaction/ExcludeCodCN", "/Transaction/SalaryProcess/ExcludeCodCN" },
            { "transaction/Final_settlement", "/Settlement/FinalSettlement" },
            
            // Reports — Payroll
            { "reports/pay_slips", "/Report/Index?reportType=PaySlips" },
            { "reports/Pay_Slips_view", "/Report/Index?reportType=PaySlips" },
            { "reports/Salaries_details", "/Report/Index?reportType=SalariesDetails" },
            { "reports/SalaryBreakup", "/Report/Index?reportType=SalaryBreakup" },
            { "reports/CommissionBreakup", "/Report/Index?reportType=CommissionBreakup" },
            { "reports/SalariesInBank", "/Report/Index?reportType=SalariesInBank" },
            { "reports/SalariesInWallet", "/Report/Index?reportType=SalariesInChequeWallet" },
            { "reports/deduction_detail", "/Report/Index?reportType=DeductionDetail" },
            { "reports/SalarySummary", "/Report/Index?reportType=SalarySummary" },
            { "reports/Monthly_advSalary_detail", "/Report/Index?reportType=AdvanceSalaryDetail" },

            // Reports — HR
            { "reports/EmpList_withRouteCode", "/Report/Index?reportType=EmployeesList" },
            { "reports/employeeslist", "/Report/Index?reportType=EmployeesList" },
            { "reports/TerminationReport", "/Report/Index?reportType=TerminationReport" },
            { "reports/Attendance", "/Report/Index?reportType=AttendanceReport" },
            { "reports/AttendanceSummary", "/Report/Index?reportType=AttendanceSummary" },
            { "reports/TrainingRpt", "/Report/Index?reportType=TrainingReport" },
            { "reports/NewJoiningDetails", "/Report/Index?reportType=NewJoiningDetails" },
            { "reports/EmpLeaveStatusReport", "/Report/Index?reportType=LeaveStatusReport" },
            { "Reports/EmpPerDet_rpt", "/Report/Index?reportType=EmployeePersonalInfo" },
            { "reports/EmpPerInfoSummary", "/Report/Index?reportType=EmpPerInfoSummary" },
            { "reports/EmpWarningInfo", "/Report/Index?reportType=WarningInfo" },
            { "reports/EmpRouteDetail", "/Report/Index?reportType=RouteDetails" },
            { "reports/AttendanceTimeSpan", "/Report/Index?reportType=AttendanceTimeSpan" },
            { "reports/Deduction", "/Report/Index?reportType=DeductionReport" },
            { "reports/Allowance", "/Report/Index?reportType=ExtrasReport" },
            { "reports/HL_AttendanceRatio", "/Report/Index?reportType=HLAttendanceRatio" },
            { "reports/HumLeopardAttendance", "/Report/Index?reportType=HumLeopardAttendance" },
            { "reports/EmployeeFuelDetail", "/Report/Index?reportType=EmployeeFuelDetail" },
            { "reports/FuelCardTransaction", "/Report/Index?reportType=FuelCardTransaction" },
            { "Reports/EmpPayDetails", "/Report/Index?reportType=EmpPayDetails" },
            { "Reports/EmpReqDetails", "/Report/Index?reportType=VoiceOfEmployee" },
            { "Reports/EmpLoanDet_rpt", "/Report/Index?reportType=EmpLoanDetails" },
            { "reports/AttendanceSummaryChart", "/Report/Index?reportType=AttendanceSummaryChart" },
            { "reports/RBI_CN_Wise_Detail", "/Report/Index?reportType=RBICNWiseDetail" },
            { "reports/RBI_Deduction_Cn_Wise_Detail", "/Report/Index?reportType=RBIDeductionCNWiseDetail" },
            { "reports/Cash_CN_Wise_Detail", "/Report/Index?reportType=CashCNWiseDetail" },
            { "reports/COD_Return_Cn_Wise_Detail", "/Report/Index?reportType=CODReturnCNWiseDetail" },
            { "reports/Vas_Cn_Wise_Detail", "/Report/Index?reportType=VASCNWiseDetail" },
            { "reports/COD_Pickup_Cn_Wise_Detail", "/Report/Index?reportType=CODPickupCNWiseDetail" },
            { "reports/AdvanceSalaryVoucher", "/Report/Index?reportType=AdvanceSalaryVoucher" },
            { "reports/salary_check", "/Report/Index?reportType=SalaryTracking" },
            { "reports/Salaries_Summary_Accounts", "/Report/Index?reportType=SalariesSummaryAccounts" },
            { "reports/Commission_detail", "/Report/Index?reportType=CommissionDetail" },
            { "Reports/Monthly_Loan_detail", "/Report/Index?reportType=LoanDetail" },
            { "reports/EmpGratuity", "/Report/Index?reportType=GratuityReport" },

            // Support
            { "support/DataUploadingUtility", "/Support/DataUploadingUtility" },
            { "support/DbBackUp", "/Support/DbBackUp" },
            { "support/ResultSetExporter", "/Support/ResultSetExporter" },
            { "support/ErrorLogs", "/Support/ErrorLogs" },
            { "support/AuditViewer", "/Support/AuditViewer" },
            { "support/AttLogViewer", "/Support/AttLogViewer" },
            { "support/DocSamples", "/Support/DocSamples" },
            { "support/Softwares", "/Support/Softwares" },
            { "support/HR_Docs", "/Support/HR_Docs" },
            { "ChangePasswordOnExpire", "/Account/ChangePassword" },
            { "Account/ChangePasswordOnExpire", "/Account/ChangePassword" },

            // Automation
            { "automation/commission", "/Automation/Commission" }
        };

        public MenuService(IDbConnectionFactory connectionFactory, ILogger<MenuService>? logger = null)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        private static string NormalizeLegacyUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || url == "#")
            {
                return string.Empty;
            }
            
            url = url.Replace(".aspx", "", StringComparison.OrdinalIgnoreCase);
            
            if (url.StartsWith("~/"))
            {
                url = url.Substring(2);
            }
            else if (url.StartsWith("/"))
            {
                url = url.Substring(1);
            }

            return url.Trim();
        }

        private string CleanUrl(string? url, string? description, bool allowPlaceholder)
        {
            var normalizedUrl = NormalizeLegacyUrl(url);
            if (string.IsNullOrEmpty(normalizedUrl))
            {
                return allowPlaceholder
                    ? BuildPlaceholderUrl(description, url)
                    : ToggleOnlyUrl;
            }

            if (_routeMap.TryGetValue(normalizedUrl, out var mappedUrl) && !string.IsNullOrWhiteSpace(mappedUrl) && mappedUrl != "#")
            {
                return mappedUrl;
            }

            return allowPlaceholder
                ? BuildPlaceholderUrl(description, url)
                : ToggleOnlyUrl;
        }

        private static string BuildPlaceholderUrl(string? description, string? legacyUrl)
        {
            var title = Uri.EscapeDataString(string.IsNullOrWhiteSpace(description) ? "Page" : description.Trim());
            var legacy = Uri.EscapeDataString(legacyUrl?.Trim() ?? string.Empty);
            return $"/Navigation/UnderDevelopment?title={title}&legacyUrl={legacy}";
        }

        private static string HtmlEncode(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }

        public async Task<string> GetMenuHtmlAsync(string userRole)
        {
            try
            {
                return await GetMenuHtmlFromDatabaseAsync(userRole);
            }
            catch (MySqlException ex)
            {
                _logger?.LogError(ex, "Menu database connection failed. Rendering fallback navigation.");
                return BuildFallbackMenuHtml();
            }
            catch (ArgumentException ex) when (IsDatabaseConnectionException(ex))
            {
                _logger?.LogError(ex, "Menu database host/connection string is invalid. Rendering fallback navigation.");
                return BuildFallbackMenuHtml();
            }
            catch (InvalidOperationException ex) when (IsDatabaseConnectionException(ex))
            {
                _logger?.LogError(ex, "Menu database connection is not available. Rendering fallback navigation.");
                return BuildFallbackMenuHtml();
            }
            catch (TimeoutException ex)
            {
                _logger?.LogError(ex, "Menu database connection timed out. Rendering fallback navigation.");
                return BuildFallbackMenuHtml();
            }
        }

        private async Task<string> GetMenuHtmlFromDatabaseAsync(string userRole)
        {
            var menuHtml = new StringBuilder();

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return BuildFallbackMenuHtml();
                await connection.OpenAsync();

                string lvl1Query = "SELECT * FROM lcs_menu where active = '1' order by sort asc";
                using (var cmdLvl1 = new MySqlCommand(lvl1Query, connection))
                using (var readerLvl1 = await cmdLvl1.ExecuteReaderAsync())
                {
                    var menuLevel1 = new DataTable();
                    menuLevel1.Load(readerLvl1);

                    if (menuLevel1.Rows.Count > 0)
                    {
                        foreach (DataRow dr in menuLevel1.Rows)
                        {
                            var menuId = dr[0].ToString();
                            var menuDesc = dr["description"].ToString();
                            var menuUrl = CleanUrl(dr["url"].ToString(), menuDesc, allowPlaceholder: false);

                            menuHtml.Append($"<li class='dropdown'><a data-toggle='dropdown' class='dropdown-toggle' href='{HtmlEncode(menuUrl)}'>{HtmlEncode(menuDesc)}</a>");

                            string lvl2Query = $"SELECT * FROM lcs_submenu where MenuID='{menuId}' and active=1 order by sort asc";
                            using (var cmdLvl2 = new MySqlCommand(lvl2Query, connection))
                            using (var readerLvl2 = await cmdLvl2.ExecuteReaderAsync())
                            {
                                var menuLevel2 = new DataTable();
                                menuLevel2.Load(readerLvl2);

                                if (menuLevel2.Rows.Count > 0)
                                {
                                    menuHtml.Append("<ul class='dropdown-menu multilevel' role='menu'>");
                                    foreach (DataRow dr2 in menuLevel2.Rows)
                                    {
                                        var subMenuId = dr2[1].ToString();
                                        var subMenuDesc = dr2["description"].ToString();
                                        var subMenuUrl = CleanUrl(dr2["url"].ToString(), subMenuDesc, allowPlaceholder: false);

                                        menuHtml.Append($"<li class='dropdown-submenu mul'><a href='{HtmlEncode(subMenuUrl)}'>{HtmlEncode(subMenuDesc)}</a>");

                                        string lvl3Query = $@"SELECT sm.url, sm.Description FROM lcs_submenu_det sm 
                                                              INNER JOIN lcs_roles_privileges r ON sm.MenuID = r.MenuID AND sm.SubmenuID = r.SubMenuID and sm.submenudetid=r.submenudetid 
                                                              WHERE sm.MenuID = '{menuId}' and sm.SubMenuID='{subMenuId}' AND sm.active = '1' AND r.RoleID = '{userRole}' AND r.can_View = 1 ORDER BY sm.sort ASC";
                                        
                                        using (var cmdLvl3 = new MySqlCommand(lvl3Query, connection))
                                        using (var dread = await cmdLvl3.ExecuteReaderAsync())
                                        {
                                            if (dread.HasRows)
                                            {
                                                var seenDetails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                                var level3Html = new StringBuilder();

                                                while (await dread.ReadAsync())
                                                {
                                                    var detDesc = dread["Description"].ToString();
                                                    var rawDetUrl = dread["url"].ToString();
                                                    var normalizedDetUrl = NormalizeLegacyUrl(rawDetUrl);
                                                    var detailKey = $"{detDesc?.Trim()}|{normalizedDetUrl}";

                                                    if (!seenDetails.Add(detailKey))
                                                    {
                                                        continue;
                                                    }

                                                    var detUrl = CleanUrl(rawDetUrl, detDesc, allowPlaceholder: true);
                                                    level3Html.Append($"<li><a href='{HtmlEncode(detUrl)}'>{HtmlEncode(detDesc)}</a></li>");
                                                }

                                                if (level3Html.Length > 0)
                                                {
                                                    menuHtml.Append("<ul class='dropdown-menu'>");
                                                    menuHtml.Append(level3Html);
                                                    menuHtml.Append("</ul>");
                                                }
                                            }
                                        }
                                        menuHtml.Append("</li>");
                                    }
                                    menuHtml.Append("</ul>");
                                }
                            }
                            menuHtml.Append("</li>");
                        }
                    }
                }
            }

            return menuHtml.ToString();
        }

        private static string BuildFallbackMenuHtml()
        {
            var menuHtml = new StringBuilder();
            menuHtml.Append("<li><a href='/Home/Index'>Home</a></li>");
            menuHtml.Append("<li><a href='/Automation/Commission'>Automation Commission</a></li>");
            menuHtml.Append("<li><a href='/Account/Logout'>Logout</a></li>");
            return menuHtml.ToString();
        }

        private static bool IsDatabaseConnectionException(Exception ex)
        {
            if (ex is MySqlException)
            {
                return true;
            }

            string message = ex.Message ?? string.Empty;
            return message.Contains("Unable to connect", StringComparison.OrdinalIgnoreCase)
                || message.Contains("host name or IP address is invalid", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || IsDatabaseConnectionException(ex.InnerException);
        }
    }
}
