using System.Security.Claims;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Models.Payroll;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class PayrollController : Controller
    {
        private readonly IPayrollService _payrollService;
        private readonly ISetupService _setupService;
        private readonly ILogger<PayrollController> _logger;
        private readonly ICommissionExecutionHistoryService _executionHistory;

        public PayrollController(IPayrollService payrollService, ISetupService setupService, ILogger<PayrollController> logger, ICommissionExecutionHistoryService executionHistory)
        {
            _payrollService = payrollService;
            _setupService = setupService;
            _logger = logger;
            _executionHistory = executionHistory;
        }

        #region Employee Attendance Process
        [HttpGet]
        public async Task<IActionResult> EmployeeAttendanceProccess()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var cities = await _setupService.GetAllCitiesAsync();
            ViewBag.Cities = new SelectList(cities, "Code", "FullName");

            var model = new AttendanceProcessViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeAttendanceProccess(AttendanceProcessViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("EmployeeAttendanceProccess");
            }

            if (!ModelState.IsValid)
            {
                var cities = await _setupService.GetAllCitiesAsync();
                ViewBag.Cities = new SelectList(cities, "Code", "FullName");
                return View(model);
            }

            try
            {
                if (action == "ProcessLeaves")
                {
                    var result = await _payrollService.ProcessLeavesAsync(model, currentUserId);
                    if (result.success) TempData["SuccessMessage"] = result.message;
                    else TempData["ErrorMessage"] = result.message;
                }
                else if (action == "ProcessAttendance")
                {
                    var result = await _payrollService.ProcessAttendanceAsync(model, currentUserId);
                    if (result.success)
                    {
                        TempData["SuccessMessage"] = result.message;
                    }
                    else
                    {
                        if (result.errorRows != null && result.errorRows.Any())
                        {
                            TempData["ErrorMessage"] = result.message + " Check exported errors.";
                            // In a real scenario, we would trigger an EPPlus export here and return a FileResult.
                        }
                        else
                        {
                            TempData["ErrorMessage"] = result.message;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing EmployeeAttendanceProccess");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeAttendanceProccess");
        }
        #endregion

        #region Bulk Attendance Adjustment
        [HttpGet]
        public async Task<IActionResult> BulkAttendanceAdjustment()
        {
            var model = new BulkAttendanceAdjustmentModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAttendanceAdjustment(BulkAttendanceAdjustmentModel model, string action, List<BulkAttendanceAdjustmentGridRow> gridData)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("BulkAttendanceAdjustment");
                }

                if (action == "Fetch")
                {
                    if (string.IsNullOrEmpty(model.EmpNo))
                    {
                        TempData["ErrorMessage"] = "Employee No is required to fetch logs.";
                    }
                    else
                    {
                        ViewBag.GridData = await _payrollService.GetAttendanceDaysForAdjustmentAsync(model.Year, model.Month, model.EmpNo);
                    }
                    return View(model);
                }

                if (action == "Save")
                {
                    if (string.IsNullOrEmpty(model.EmpNo))
                    {
                        TempData["ErrorMessage"] = "Employee is required.";
                        return View(model);
                    }

                    var result = await _payrollService.SaveBulkAttendanceAdjustmentAsync(model, gridData, currentUserId);
                    if (result.success)
                    {
                        TempData["SuccessMessage"] = result.message;
                        return RedirectToAction("BulkAttendanceAdjustment");
                    }
                    else
                    {
                        TempData["ErrorMessage"] = result.message;
                    }
                }
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing BulkAttendanceAdjustment");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            ViewBag.GridData = gridData ?? new List<BulkAttendanceAdjustmentGridRow>();
            return View(model);
        }
        #endregion

        #region Fuel Prices
        [HttpGet]
        public async Task<IActionResult> FuelPrices(string? code, string? searchField, string? searchText)
        {
            var model = await _payrollService.GetFuelPricesPageAsync(GetWorkingDate(), code, searchField, searchText);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FuelPrices(FuelPricesViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("FuelPrices");
            }

            if (action == "Search")
            {
                return RedirectToAction("FuelPrices", new
                {
                    searchField = model.SearchField,
                    searchText = model.SearchText
                });
            }

            if ((action == "Save" || action == "Update") && !ModelState.IsValid)
            {
                return await RenderFuelPricesViewAsync(model);
            }

            try
            {
                (bool success, string message) result = action switch
                {
                    "Save" => await _payrollService.SaveFuelPriceAsync(model, currentUserId),
                    "Update" => await _payrollService.UpdateFuelPriceAsync(model, currentUserId),
                    "Delete" => await _payrollService.DeleteFuelPriceAsync(model.Code, currentUserId),
                    _ => (false, "Unsupported action.")
                };

                if (result.success)
                {
                    TempData["SuccessMessage"] = result.message;
                    return RedirectToAction("FuelPrices", new
                    {
                        searchField = model.SearchField,
                        searchText = model.SearchText
                    });
                }

                TempData["ErrorMessage"] = result.message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Fuel Prices");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return await RenderFuelPricesViewAsync(model);
        }
        #endregion

        #region Commission Process
        [HttpGet]
        public async Task<IActionResult> CommissionProcess()
        {
            await PopulateCommissionProcessViewBagAsync();
            var model = new CommissionProcessViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CommissionProcess(CommissionProcessViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("CommissionProcess");
                }

                if (!ModelState.IsValid)
                {
                    await PopulateCommissionProcessViewBagAsync();
                    return View(model);
                }

                if (!model.BillingStatusConfirmed || !model.AttendanceStatusConfirmed || !model.AllCommissionTypesConfirmed)
                {
                    TempData["ErrorMessage"] = "Please acknowledge all checkbox confirmations before proceeding.";
                    await PopulateCommissionProcessViewBagAsync();
                    return View(model);
                }

                var _startComm = DateTime.Now;
                var result = await _payrollService.ProcessCommissionAsync(model, currentUserId);
                if (result.success)
                {
                    TempData["SuccessMessage"] = result.message;
                }
                else if (result.message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["WarningMessage"] = result.message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.message;
                }

                var _doneComm = DateTime.Now;
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "CommissionProcess",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = result.success ? "Completed"
                                      : result.message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase) ? "AlreadyProcessed"
                                      : "Failed",
                    StartedAt         = _startComm,
                    CompletedAt       = _doneComm,
                    DurationMs        = (int)(_doneComm - _startComm).TotalMilliseconds,
                    ErrorMessage      = result.success || result.message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase)
                                        ? null : result.message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Commission Process");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("CommissionProcess");
        }
        #endregion

        #region Exclude COD CN
        [HttpGet]
        public async Task<IActionResult> ExcludeCodCN()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            if (!string.Equals(currentUserId, "163", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "You are not allowed to access this page.";
            }

            var model = await _payrollService.GetExcludeCodCnPageAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExcludeCodCN(ExcludeCodCnViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("ExcludeCodCN");
            }

            try
            {
                if (model.UploadFile == null || model.UploadFile.Length == 0)
                {
                    TempData["ErrorMessage"] = "Please Upload File In CSV Formate.";
                    return View(await _payrollService.GetExcludeCodCnPageAsync());
                }

                using var stream = model.UploadFile.OpenReadStream();
                var result = await _payrollService.ProcessExcludeCodCnUploadAsync(
                    stream,
                    model.UploadFile.FileName,
                    model.UploadFile.Length,
                    currentUserId,
                    HttpContext.RequestAborted);

                if (result.InsertedCount > 0)
                {
                    TempData["SuccessMessage"] = "Record Saved Successfully";
                }
                else if (result.SkippedCount > 0)
                {
                    TempData["ErrorMessage"] = "All uploaded CN numbers already exist.";
                }

                return View(result);
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Exclude COD CN");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return View(await _payrollService.GetExcludeCodCnPageAsync());
        }
        #endregion

        #region Cash Commission
        [HttpGet]
        public async Task<IActionResult> CashCommission()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = await _payrollService.GetCashCommissionPageAsync(GetWorkingDate(), currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CashCommission(CashCommissionViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("CashCommission");
            }

            if (!ModelState.IsValid)
            {
                return await RenderCashCommissionViewAsync(model, currentUserId);
            }

            var _start = DateTime.Now;
            try
            {
                var result = await _payrollService.ProcessCashCommissionAsync(model, currentUserId);
                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else if (result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["WarningMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }

                model.CashSourceRowsRetrieved = result.CashSourceRowsRetrieved;
                model.VasSourceRowsRetrieved = result.VasSourceRowsRetrieved;
                model.CashRowsInserted = result.CashRowsInserted;
                model.VasRowsInserted = result.VasRowsInserted;
                model.StationCount = result.StationCount;
                model.FromDate = result.FromDate;
                model.ToDate = result.ToDate;

                var _done = DateTime.Now;
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "CashCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = result.Success ? "Completed"
                                      : result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase) ? "AlreadyProcessed"
                                      : "Failed",
                    RowsProcessed     = result.CashRowsInserted + result.VasRowsInserted,
                    StartedAt         = _start,
                    CompletedAt       = _done,
                    DurationMs        = (int)(_done - _start).TotalMilliseconds,
                    ErrorMessage      = result.Success || result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase)
                                        ? null : result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Cash Commission");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "CashCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = "Failed",
                    StartedAt         = _start,
                    CompletedAt       = DateTime.Now,
                    DurationMs        = (int)(DateTime.Now - _start).TotalMilliseconds,
                    ErrorMessage      = ex.Message
                });
            }

            return await RenderCashCommissionViewAsync(model, currentUserId);
        }

        [HttpGet]
        public async Task<IActionResult> GetCashCommissionCities(string zoneId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var page = await _payrollService.GetCashCommissionPageAsync(GetWorkingDate(), currentUserId, new CashCommissionViewModel
            {
                ZoneId = zoneId
            });

            return Json(page.Cities
                .Where(static item => item.Value != "0")
                .Select(static item => new { value = item.Value, text = item.Text }));
        }
        #endregion

        #region OverLand Commission
        [HttpGet]
        public async Task<IActionResult> OverLandCommission()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = await _payrollService.GetOverLandCommissionPageAsync(GetWorkingDate(), currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OverLandCommission(OverLandCommissionViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("OverLandCommission");
            }

            if (!ModelState.IsValid)
            {
                return await RenderOverLandCommissionViewAsync(model, currentUserId);
            }

            var _startOle = DateTime.Now;
            try
            {
                var result = await _payrollService.ProcessOverLandCommissionAsync(model, currentUserId);
                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else if (result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["WarningMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }

                model.OleRowsInserted = result.OleRowsInserted;
                model.RbiRowsInserted = result.RbiRowsInserted;
                model.ProcessRowsInserted = result.ProcessRowsInserted;
                model.StationCount = result.StationCount;
                model.LocationCount = result.LocationCount;
                model.FromDate = result.FromDate;
                model.ToDate = result.ToDate;

                var _doneOle = DateTime.Now;
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "OverLandCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = result.Success ? "Completed"
                                      : result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase) ? "AlreadyProcessed"
                                      : "Failed",
                    RowsProcessed     = result.ProcessRowsInserted,
                    StartedAt         = _startOle,
                    CompletedAt       = _doneOle,
                    DurationMs        = (int)(_doneOle - _startOle).TotalMilliseconds,
                    ErrorMessage      = result.Success || result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase)
                                        ? null : result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OverLand Commission");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "OverLandCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = "Failed",
                    StartedAt         = _startOle,
                    CompletedAt       = DateTime.Now,
                    DurationMs        = (int)(DateTime.Now - _startOle).TotalMilliseconds,
                    ErrorMessage      = ex.Message
                });
            }

            return await RenderOverLandCommissionViewAsync(model, currentUserId);
        }

        [HttpGet]
        public async Task<IActionResult> GetOverLandCommissionCities(string zoneId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var page = await _payrollService.GetOverLandCommissionPageAsync(GetWorkingDate(), currentUserId, new OverLandCommissionViewModel
            {
                ZoneId = zoneId
            });

            return Json(page.Cities
                .Where(static item => item.Value != "0")
                .Select(static item => new { value = item.Value, text = item.Text }));
        }
        #endregion

        #region COD Commission
        [HttpGet]
        public async Task<IActionResult> CodCommission()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = await _payrollService.GetCodCommissionPageAsync(GetWorkingDate(), currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CodCommission(CodCommissionViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("CodCommission");
            }

            if (!ModelState.IsValid)
            {
                return await RenderCodCommissionViewAsync(model, currentUserId);
            }

            var _startCod = DateTime.Now;
            try
            {
                var result = await _payrollService.ProcessCodCommissionAsync(model, currentUserId);
                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else if (result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["WarningMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }

                model.ConsignmentRowsInserted = result.ConsignmentRowsInserted;
                model.ActivityRowsInserted = result.ActivityRowsInserted;
                model.ReturnShipmentRowsInserted = result.ReturnShipmentRowsInserted;
                model.CommissionRowsInserted = result.CommissionRowsInserted;
                model.StationCount = result.StationCount;
                model.FromDate = result.FromDate;
                model.ToDate = result.ToDate;

                var _doneCod = DateTime.Now;
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "CodCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = result.Success ? "Completed"
                                      : result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase) ? "AlreadyProcessed"
                                      : "Failed",
                    RowsProcessed     = result.CommissionRowsInserted,
                    StartedAt         = _startCod,
                    CompletedAt       = _doneCod,
                    DurationMs        = (int)(_doneCod - _startCod).TotalMilliseconds,
                    ErrorMessage      = result.Success || result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase)
                                        ? null : result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing COD Commission");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "CodCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = "Failed",
                    StartedAt         = _startCod,
                    CompletedAt       = DateTime.Now,
                    DurationMs        = (int)(DateTime.Now - _startCod).TotalMilliseconds,
                    ErrorMessage      = ex.Message
                });
            }

            return await RenderCodCommissionViewAsync(model, currentUserId);
        }

        [HttpGet]
        public async Task<IActionResult> GetCodCommissionCities(string zoneId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var page = await _payrollService.GetCodCommissionPageAsync(GetWorkingDate(), currentUserId, new CodCommissionViewModel
            {
                ZoneId = zoneId
            });

            return Json(page.Cities
                .Where(static item => item.Value != "0")
                .Select(static item => new { value = item.Value, text = item.Text }));
        }
        #endregion

        #region Return COD Commission
        [HttpGet]
        public async Task<IActionResult> ReturnCodCommission()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = await _payrollService.GetReturnCodCommissionPageAsync(GetWorkingDate(), currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnCodCommission(ReturnCodCommissionViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("ReturnCodCommission");
            }

            if (!ModelState.IsValid)
            {
                return await RenderReturnCodCommissionViewAsync(model, currentUserId);
            }

            var _startRcod = DateTime.Now;
            try
            {
                var result = await _payrollService.ProcessReturnCodCommissionAsync(model, currentUserId);
                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else if (result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["WarningMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }

                model.ConsignmentRowsInserted = result.ConsignmentRowsInserted;
                model.CommissionRowsInserted = result.CommissionRowsInserted;
                model.ProcessRowsInserted = result.ProcessRowsInserted;
                model.StationCount = result.StationCount;
                model.LocationCount = result.LocationCount;
                model.FromDate = result.FromDate;
                model.ToDate = result.ToDate;

                var _doneRcod = DateTime.Now;
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "ReturnCodCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = result.Success ? "Completed"
                                      : result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase) ? "AlreadyProcessed"
                                      : "Failed",
                    RowsProcessed     = result.ProcessRowsInserted,
                    StartedAt         = _startRcod,
                    CompletedAt       = _doneRcod,
                    DurationMs        = (int)(_doneRcod - _startRcod).TotalMilliseconds,
                    ErrorMessage      = result.Success || result.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase)
                                        ? null : result.Message
                });
            }
            catch (ArgumentException ex)
            {
                var isAlready = ex.Message.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase);
                if (isAlready)
                    TempData["WarningMessage"] = ex.Message;
                else
                    TempData["ErrorMessage"] = ex.Message;
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "ReturnCodCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = isAlready ? "AlreadyProcessed" : "Failed",
                    StartedAt         = _startRcod,
                    CompletedAt       = DateTime.Now,
                    DurationMs        = (int)(DateTime.Now - _startRcod).TotalMilliseconds,
                    ErrorMessage      = isAlready ? null : ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Return COD Commission");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
                await _executionHistory.RecordAsync(new CommissionExecutionRecord
                {
                    ExecutionSource   = "Manual",
                    Year              = model.Year,
                    Month             = model.Month,
                    CityCode          = model.CityCode,
                    CommissionType    = "ReturnCodCommission",
                    TriggeredBy       = User.Identity?.Name ?? currentUserId,
                    TriggeredByUserId = currentUserId,
                    Status            = "Failed",
                    StartedAt         = _startRcod,
                    CompletedAt       = DateTime.Now,
                    DurationMs        = (int)(DateTime.Now - _startRcod).TotalMilliseconds,
                    ErrorMessage      = ex.Message
                });
            }

            return await RenderReturnCodCommissionViewAsync(model, currentUserId);
        }

        [HttpGet]
        public async Task<IActionResult> GetReturnCodCommissionCities(string zoneId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var page = await _payrollService.GetReturnCodCommissionPageAsync(GetWorkingDate(), currentUserId, new ReturnCodCommissionViewModel
            {
                ZoneId = zoneId
            });

            return Json(page.Cities
                .Where(static item => item.Value != "00")
                .Select(static item => new { value = item.Value, text = item.Text }));
        }
        #endregion

        #region Death Compensation
        [HttpGet]
        public async Task<IActionResult> DeathCompensation()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var currentLocationId = User.Claims.FirstOrDefault(c => c.Type == "LocationID")?.Value;
            var model = await _payrollService.GetDeathCompensationPageAsync(GetWorkingDate(), currentUserId, currentLocationId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeathCompensation(DeathCompensationViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var currentLocationId = User.Claims.FirstOrDefault(c => c.Type == "LocationID")?.Value;

            if (action == "Reset")
            {
                return RedirectToAction("DeathCompensation");
            }

            if (!ModelState.IsValid)
            {
                return await RenderDeathCompensationViewAsync(model, currentUserId, currentLocationId);
            }

            try
            {
                var result = await _payrollService.ProcessDeathCompensationAsync(model, currentUserId, currentLocationId);
                if (result.Success)
                {
                    TempData["SuccessMessage"] = result.Message;
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message;
                }

                model.GeneratedCount = result.PayslipCount;
                model.VoucherStatusInserted = result.VoucherStatusInserted;
                model.VoucherStatusUpdated = result.VoucherStatusUpdated;
                model.ProcessedEmployees = result.ProcessedEmployees;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Death Compensation");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return await RenderDeathCompensationViewAsync(model, currentUserId, currentLocationId);
        }

        [HttpGet]
        public async Task<IActionResult> GetDeathCompensationCities(string zoneId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var currentLocationId = User.Claims.FirstOrDefault(c => c.Type == "LocationID")?.Value;
            var page = await _payrollService.GetDeathCompensationPageAsync(GetWorkingDate(), currentUserId, currentLocationId, new DeathCompensationViewModel
            {
                ZoneId = zoneId
            });

            return Json(page.Cities
                .Where(static item => item.Value != "0")
                .Select(static item => new { value = item.Value, text = item.Text }));
        }
        #endregion

        #region Leave Process
        [HttpGet]
        public async Task<IActionResult> LeaveProcess()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = await _payrollService.GetLeaveProcessPageAsync(GetWorkingDate(), currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveProcess(LeaveProcessViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("LeaveProcess");
            }

            if (!ModelState.IsValid)
            {
                return await RenderLeaveProcessViewAsync(model, currentUserId);
            }

            try
            {
                var result = await _payrollService.ProcessLeaveEncashmentAsync(model, currentUserId);
                if (result.success)
                {
                    TempData["SuccessMessage"] = result.message;
                    return RedirectToAction("LeaveProcess");
                }

                TempData["ErrorMessage"] = result.message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Leave Process");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return await RenderLeaveProcessViewAsync(model, currentUserId);
        }
        #endregion

        #region Salary Reprocess
        [HttpGet]
        public async Task<IActionResult> SalaryReprocess()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = await _payrollService.GetSalaryReprocessPageAsync(GetWorkingDate(), currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalaryReprocess(SalaryReprocessViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("SalaryReprocess");
            }

            if (!ModelState.IsValid)
            {
                return await RenderSalaryReprocessViewAsync(model, currentUserId);
            }

            try
            {
                var result = await _payrollService.ProcessSalaryReprocessAsync(model, currentUserId);
                if (result.success)
                {
                    TempData["SuccessMessage"] = result.message;
                    return RedirectToAction("SalaryReprocess");
                }

                TempData["ErrorMessage"] = result.message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Salary Reprocess");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return await RenderSalaryReprocessViewAsync(model, currentUserId);
        }
        #endregion

        #region Employee Commission Reprocess
        [HttpGet]
        public IActionResult EmployeeCommissionReprocess()
        {
            var model = new LCS_HR_MVC.Models.Payroll.SingleEmployeeCommissionViewModel
            {
                Year  = GetWorkingDate().Year,
                Month = GetWorkingDate().Month
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmployeeCommissionReprocess(
            LCS_HR_MVC.Models.Payroll.SingleEmployeeCommissionViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
                return RedirectToAction("EmployeeCommissionReprocess");

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var result = await _payrollService.ProcessSingleEmployeeCommissionAsync(
                    model.EmpNo, model.Year, model.Month, currentUserId);

                if (result.Success)
                    TempData["SuccessMessage"] = result.Message;
                else
                    TempData["ErrorMessage"] = result.Message;
            }
            catch (ArgumentException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing single-employee commission reprocess for {EmpNo}", model.EmpNo);
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("EmployeeCommissionReprocess");
        }
        #endregion

        #region Salary Vouchers
        [HttpGet]
        public async Task<IActionResult> SalaryVouchers()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var model = await _payrollService.GetSalaryVouchersPageAsync(GetWorkingDate(), currentUserId);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalaryVouchers(SalaryVouchersViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            if (action == "Reset")
            {
                return RedirectToAction("SalaryVouchers");
            }

            if (!ModelState.IsValid)
            {
                return await RenderSalaryVouchersViewAsync(model, currentUserId);
            }

            try
            {
                var result = await _payrollService.GenerateSalaryVouchersAsync(model, currentUserId);
                if (result.success)
                {
                    TempData["SuccessMessage"] = result.message;
                    return RedirectToAction("SalaryVouchers");
                }

                TempData["ErrorMessage"] = result.message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Salary Vouchers");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return await RenderSalaryVouchersViewAsync(model, currentUserId);
        }
        #endregion

        #region Salaries Process
        [HttpGet]
        public async Task<IActionResult> SalariesProcess()
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var cities = await _setupService.GetAllCitiesAsync();
            ViewBag.Cities = new SelectList(cities, "Code", "FullName");

            var zones = await _payrollService.GetZonesAsync();
            ViewBag.Zones = new SelectList(zones, "Value", "Text");

            var divisions = await _payrollService.GetDivisionsAsync();
            ViewBag.Divisions = new SelectList(divisions, "Value", "Text");

            var model = new SalariesProcessViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalariesProcess(SalariesProcessViewModel model, string action)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";

            try
            {
                if (action == "Reset")
                {
                    return RedirectToAction("SalariesProcess");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.Cities = new SelectList(await _setupService.GetAllCitiesAsync(), "Code", "FullName");
                    ViewBag.Zones = new SelectList(await _payrollService.GetZonesAsync(), "Value", "Text");
                    ViewBag.Divisions = new SelectList(await _payrollService.GetDivisionsAsync(), "Value", "Text");
                    return View(model);
                }

                if (action == "ProcessSalaries")
                {
                    if (!model.BillingStatusConfirmed || !model.AttendanceStatusConfirmed || !model.CommissionStatusConfirmed || !model.OneTimeActivityConfirmed)
                    {
                        TempData["ErrorMessage"] = "Please acknowledge all confirmation checkboxes before proceeding.";
                        ViewBag.Cities = new SelectList(await _setupService.GetAllCitiesAsync(), "Code", "FullName");
                        ViewBag.Zones = new SelectList(await _payrollService.GetZonesAsync(), "Value", "Text");
                        ViewBag.Divisions = new SelectList(await _payrollService.GetDivisionsAsync(), "Value", "Text");
                        return View(model);
                    }

                    var result = await _payrollService.ProcessSalariesAsync(model, currentUserId);
                    if (result.success) TempData["SuccessMessage"] = result.message;
                    else TempData["ErrorMessage"] = result.message;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Salaries Process");
                TempData["ErrorMessage"] = "An error occurred while processing the request.";
            }

            return RedirectToAction("SalariesProcess");
        }

        [HttpGet]
        public async Task<IActionResult> GetCitiesByZone(string zoneId)
        {
            var results = await _payrollService.GetCitiesByZoneAsync(zoneId);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartmentsByDivision(int buId)
        {
            var results = await _payrollService.GetDepartmentsByDivisionAsync(buId);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetSubDepartmentsByDepartment(int departmentId)
        {
            var results = await _payrollService.GetSubDepartmentsByDepartmentAsync(departmentId);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetSubDepartmentsByDivision(int buId)
        {
            var parents = await _payrollService.GetDepartmentsByDivisionAsync(buId);
            var subDepts = new List<dynamic>();
            foreach(var parent in parents)
            {
                int pId = Convert.ToInt32(parent.Value);
                var subs = await _payrollService.GetSubDepartmentsByDepartmentAsync(pId);
                subDepts.AddRange(subs);
            }
            return Json(subDepts);
        }

        [HttpGet]
        public async Task<IActionResult> GetSalaryReprocessSubDepartments(string cityCode)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var results = await _payrollService.GetSalaryReprocessSubDepartmentsAsync(cityCode, currentUserId);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetSalaryReprocessCities(string zoneId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var page = await _payrollService.GetSalaryReprocessPageAsync(GetWorkingDate(), currentUserId, new SalaryReprocessViewModel
            {
                ZoneId = zoneId
            });

            return Json(page.Cities
                .Where(static item => item.Value != "0")
                .Select(static item => new { value = item.Value, text = item.Text }));
        }

        [HttpGet]
        public async Task<IActionResult> GetSalaryReprocessEmployees(string cityCode, string subDepartmentId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var results = await _payrollService.GetSalaryReprocessEmployeesAsync(cityCode, subDepartmentId, currentUserId);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetSalaryVoucherCities(string zoneId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var page = await _payrollService.GetSalaryVouchersPageAsync(GetWorkingDate(), currentUserId, new SalaryVouchersViewModel
            {
                ZoneId = zoneId
            });

            return Json(page.Cities
                .Where(static item => item.Value != "0" && item.Value != "ALL")
                .Select(static item => new { value = item.Value, text = item.Text }));
        }

        [HttpGet]
        public async Task<IActionResult> GetSalaryVoucherSubDepartments(string cityCode)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var results = await _payrollService.GetSalaryVoucherSubDepartmentsAsync(cityCode, currentUserId);
            return Json(results);
        }

        [HttpGet]
        public async Task<IActionResult> GetSalaryVoucherEmployees(string zoneId, string cityCode, string subDepartmentId)
        {
            var currentUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? "0";
            var results = await _payrollService.GetSalaryVoucherEmployeesAsync(zoneId, cityCode, subDepartmentId, currentUserId);
            return Json(results);
        }

        private async Task PopulateCommissionProcessViewBagAsync()
        {
            ViewBag.Cities = new SelectList(await _setupService.GetAllCitiesAsync(), "Code", "FullName");
            ViewBag.Zones = new SelectList(await _payrollService.GetZonesAsync(), "Value", "Text");
        }

        private async Task<IActionResult> RenderFuelPricesViewAsync(FuelPricesViewModel model)
        {
            var page = await _payrollService.GetFuelPricesPageAsync(
                GetWorkingDate(),
                model.IsEditMode ? model.Code : null,
                model.SearchField,
                model.SearchText);

            page.Code = model.Code;
            page.TypeCode = model.TypeCode;
            page.FromDate = model.FromDate;
            page.ToDate = model.ToDate;
            page.Price = model.Price;
            page.Comments = model.Comments;
            page.IsEditMode = model.IsEditMode;
            return View("FuelPrices", page);
        }

        private async Task<IActionResult> RenderCodCommissionViewAsync(CodCommissionViewModel model, string currentUserId)
        {
            var page = await _payrollService.GetCodCommissionPageAsync(GetWorkingDate(), currentUserId, model);
            page.ConsignmentRowsInserted = model.ConsignmentRowsInserted;
            page.ActivityRowsInserted = model.ActivityRowsInserted;
            page.ReturnShipmentRowsInserted = model.ReturnShipmentRowsInserted;
            page.CommissionRowsInserted = model.CommissionRowsInserted;
            page.StationCount = model.StationCount;
            page.FromDate = model.FromDate;
            page.ToDate = model.ToDate;
            return View("CodCommission", page);
        }

        private async Task<IActionResult> RenderCashCommissionViewAsync(CashCommissionViewModel model, string currentUserId)
        {
            var page = await _payrollService.GetCashCommissionPageAsync(GetWorkingDate(), currentUserId, model);
            page.BillingStatus = model.BillingStatus;
            page.CashSourceRowsRetrieved = model.CashSourceRowsRetrieved;
            page.VasSourceRowsRetrieved = model.VasSourceRowsRetrieved;
            page.CashRowsInserted = model.CashRowsInserted;
            page.VasRowsInserted = model.VasRowsInserted;
            page.StationCount = model.StationCount;
            page.FromDate = model.FromDate;
            page.ToDate = model.ToDate;
            return View("CashCommission", page);
        }

        private async Task<IActionResult> RenderOverLandCommissionViewAsync(OverLandCommissionViewModel model, string currentUserId)
        {
            var page = await _payrollService.GetOverLandCommissionPageAsync(GetWorkingDate(), currentUserId, model);
            page.BillingStatus = model.BillingStatus;
            page.AttendanceStatus = model.AttendanceStatus;
            page.OleRowsInserted = model.OleRowsInserted;
            page.RbiRowsInserted = model.RbiRowsInserted;
            page.ProcessRowsInserted = model.ProcessRowsInserted;
            page.StationCount = model.StationCount;
            page.LocationCount = model.LocationCount;
            page.FromDate = model.FromDate;
            page.ToDate = model.ToDate;
            return View("OverLandCommission", page);
        }

        private async Task<IActionResult> RenderReturnCodCommissionViewAsync(ReturnCodCommissionViewModel model, string currentUserId)
        {
            var page = await _payrollService.GetReturnCodCommissionPageAsync(GetWorkingDate(), currentUserId, model);
            page.ConsignmentRowsInserted = model.ConsignmentRowsInserted;
            page.CommissionRowsInserted = model.CommissionRowsInserted;
            page.ProcessRowsInserted = model.ProcessRowsInserted;
            page.StationCount = model.StationCount;
            page.LocationCount = model.LocationCount;
            page.FromDate = model.FromDate;
            page.ToDate = model.ToDate;
            page.BillingStatus = model.BillingStatus;
            return View("ReturnCodCommission", page);
        }

        private async Task<IActionResult> RenderDeathCompensationViewAsync(DeathCompensationViewModel model, string currentUserId, string? currentLocationId)
        {
            var page = await _payrollService.GetDeathCompensationPageAsync(GetWorkingDate(), currentUserId, currentLocationId, model);
            page.GeneratedCount = model.GeneratedCount;
            page.VoucherStatusInserted = model.VoucherStatusInserted;
            page.VoucherStatusUpdated = model.VoucherStatusUpdated;
            page.ProcessedEmployees = model.ProcessedEmployees;
            return View("DeathCompensation", page);
        }

        private async Task<IActionResult> RenderLeaveProcessViewAsync(LeaveProcessViewModel model, string currentUserId)
        {
            var page = await _payrollService.GetLeaveProcessPageAsync(GetWorkingDate(), currentUserId, model);
            page.EmployeeDescription = model.EmployeeDescription;
            page.EmployeeCode = model.EmployeeCode;
            page.Mode = model.Mode;
            return View("LeaveProcess", page);
        }

        private async Task<IActionResult> RenderSalaryReprocessViewAsync(SalaryReprocessViewModel model, string currentUserId)
        {
            var page = await _payrollService.GetSalaryReprocessPageAsync(GetWorkingDate(), currentUserId, model);
            page.SelectedEmployeeIds = model.SelectedEmployeeIds;
            page.BillingStatusConfirmed = model.BillingStatusConfirmed;
            page.AttendanceStatusConfirmed = model.AttendanceStatusConfirmed;
            page.CommissionStatusConfirmed = model.CommissionStatusConfirmed;
            page.OneTimeActivityConfirmed = model.OneTimeActivityConfirmed;
            return View("SalaryReprocess", page);
        }

        private async Task<IActionResult> RenderSalaryVouchersViewAsync(SalaryVouchersViewModel model, string currentUserId)
        {
            var page = await _payrollService.GetSalaryVouchersPageAsync(GetWorkingDate(), currentUserId, model);
            page.SelectedEmployeeIds = model.SelectedEmployeeIds;
            return View("SalaryVouchers", page);
        }

        private DateTime GetWorkingDate()
        {
            var sessionValue = HttpContext.Session.GetString("workingdate");
            return DateTime.TryParse(sessionValue, out var workingDate)
                ? workingDate
                : DateTime.Now;
        }
        #endregion
    }
}
