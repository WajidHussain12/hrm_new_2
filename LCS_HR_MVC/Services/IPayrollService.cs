using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Models.Payroll;

namespace LCS_HR_MVC.Services
{
    public interface IPayrollService
    {
        Task<(bool success, string message, IEnumerable<dynamic> errorRows)> ProcessAttendanceAsync(AttendanceProcessViewModel model, string currentUserId);
        
        // Stubs for future processes
        Task<(bool success, string message)> ProcessLeavesAsync(AttendanceProcessViewModel model, string currentUserId);
        Task<(bool success, string message)> ProcessCommissionAsync(CommissionProcessViewModel model, string currentUserId);
        
        Task<(bool success, string message)> ProcessSalariesAsync(SalariesProcessViewModel model, string currentUserId);

        Task<FuelPricesViewModel> GetFuelPricesPageAsync(System.DateTime workingDate, string? code = null, string? searchField = null, string? searchText = null);
        Task<(bool success, string message)> SaveFuelPriceAsync(FuelPricesViewModel model, string currentUserId);
        Task<(bool success, string message)> UpdateFuelPriceAsync(FuelPricesViewModel model, string currentUserId);
        Task<(bool success, string message)> DeleteFuelPriceAsync(string code, string currentUserId);

        Task<ExcludeCodCnViewModel> GetExcludeCodCnPageAsync();
        Task<ExcludeCodCnViewModel> ProcessExcludeCodCnUploadAsync(Stream fileStream, string fileName, long fileSize, string currentUserId, System.Threading.CancellationToken cancellationToken = default);
        Task<CashCommissionViewModel> GetCashCommissionPageAsync(System.DateTime workingDate, string currentUserId, CashCommissionViewModel? existingModel = null);
        Task<CashCommissionProcessResult> ProcessCashCommissionAsync(CashCommissionViewModel model, string currentUserId, Func<int, int, Task>? onProgress = null);
        Task<CashCommissionPreviewResult> PreviewCashCommissionAsync(int year, int month, string cityCode, string currentUserId, bool billingStatus = false);
        Task<CodCommissionViewModel> GetCodCommissionPageAsync(System.DateTime workingDate, string currentUserId, CodCommissionViewModel? existingModel = null);
        Task<CodCommissionProcessResult> ProcessCodCommissionAsync(CodCommissionViewModel model, string currentUserId, Func<int, int, Task>? onProgress = null);
        Task<OverLandCommissionViewModel> GetOverLandCommissionPageAsync(System.DateTime workingDate, string currentUserId, OverLandCommissionViewModel? existingModel = null);
        Task<OverLandCommissionProcessResult> ProcessOverLandCommissionAsync(OverLandCommissionViewModel model, string currentUserId, Func<int, int, Task>? onProgress = null);
        Task<OverLandCommissionPreviewResult> PreviewOverLandCommissionAsync(int year, int month, string cityCode, string currentUserId, bool billingStatus = false, bool attendanceStatus = false);
        Task<ReturnCodCommissionViewModel> GetReturnCodCommissionPageAsync(System.DateTime workingDate, string currentUserId, ReturnCodCommissionViewModel? existingModel = null);
        Task<ReturnCodCommissionProcessResult> ProcessReturnCodCommissionAsync(ReturnCodCommissionViewModel model, string currentUserId, Func<int, int, Task>? onProgress = null);
        Task<FinalCommissionProcessResult> ProcessFinalCommissionAsync(FinalCommissionProcessViewModel model, string userId, Func<int, int, Task>? onProgress = null);
        Task<SingleEmployeeCommissionResult> ProcessSingleEmployeeCommissionAsync(string empNo, int year, int month, string currentUserId);
        Task<DeathCompensationViewModel> GetDeathCompensationPageAsync(System.DateTime workingDate, string currentUserId, string? defaultLocationId, DeathCompensationViewModel? existingModel = null);
        Task<DeathCompensationProcessResult> ProcessDeathCompensationAsync(DeathCompensationViewModel model, string currentUserId, string? defaultLocationId);
        Task<LeaveProcessViewModel> GetLeaveProcessPageAsync(System.DateTime workingDate, string currentUserId, LeaveProcessViewModel? existingModel = null);
        Task<(bool success, string message)> ProcessLeaveEncashmentAsync(LeaveProcessViewModel model, string currentUserId);
        Task<SalaryReprocessViewModel> GetSalaryReprocessPageAsync(System.DateTime workingDate, string currentUserId, SalaryReprocessViewModel? existingModel = null);
        Task<IEnumerable<dynamic>> GetSalaryReprocessSubDepartmentsAsync(string cityCode, string currentUserId);
        Task<IEnumerable<SalaryProcessEmployeeOption>> GetSalaryReprocessEmployeesAsync(string cityCode, string subDepartmentId, string currentUserId);
        Task<(bool success, string message)> ProcessSalaryReprocessAsync(SalaryReprocessViewModel model, string currentUserId);
        Task<SalaryVouchersViewModel> GetSalaryVouchersPageAsync(System.DateTime workingDate, string currentUserId, SalaryVouchersViewModel? existingModel = null);
        Task<IEnumerable<dynamic>> GetSalaryVoucherSubDepartmentsAsync(string cityCode, string currentUserId);
        Task<IEnumerable<SalaryProcessEmployeeOption>> GetSalaryVoucherEmployeesAsync(string zoneId, string cityCode, string subDepartmentId, string currentUserId);
        Task<(bool success, string message)> GenerateSalaryVouchersAsync(SalaryVouchersViewModel model, string currentUserId);

        Task<IEnumerable<BulkAttendanceAdjustmentGridRow>> GetAttendanceDaysForAdjustmentAsync(int year, int month, string empNo);
        Task<(bool success, string message)> SaveBulkAttendanceAdjustmentAsync(BulkAttendanceAdjustmentModel model, List<BulkAttendanceAdjustmentGridRow> gridData, string currentUserId);

        Task<IEnumerable<dynamic>> GetZonesAsync();
        Task<IEnumerable<dynamic>> GetCitiesByZoneAsync(string zoneId);
        Task<IEnumerable<dynamic>> GetDivisionsAsync();
        Task<IEnumerable<dynamic>> GetDepartmentsByDivisionAsync(int buId);
        Task<IEnumerable<dynamic>> GetSubDepartmentsByDepartmentAsync(int departmentId);
    }
}
