using LCS_HR_MVC.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Services
{
    public interface IEmployeeService
    {
        Task<IEnumerable<EmployeeSalaryModel>> GetAllEmployeeSalariesAsync();
        Task<IEnumerable<SelectListItem>> GetAllowancesForSalaryAsync();
        Task<bool> AddEmployeeSalaryAsync(EmployeeSalaryModel model, string currentUserId);
        Task<bool> InsertStandardSalaryBreakupAsync(string empNo, decimal basicSalary, string currentUserId);

        Task<IEnumerable<EmployeeJobDetailModel>> GetAllEmployeeJobDetailsAsync(string currentUserId);
        Task<EmployeeJobDetailModel?> GetEmployeeJobDetailByCodeAsync(string code);
        Task<bool> AddEmployeeJobDetailAsync(EmployeeJobDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin);
        Task<bool> UpdateEmployeeJobDetailAsync(EmployeeJobDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin);
        Task<bool> DeleteEmployeeJobDetailAsync(string code);
        Task<IEnumerable<SelectListItem>> GetParentDepartmentsByBUAsync(string buId);
        Task<IEnumerable<SelectListItem>> GetSubDepartmentsByParentAsync(string parentId);
        Task<IEnumerable<SelectListItem>> GetDesignationsByParentAsync(string parentId);
        Task<dynamic?> GetEmployeeDetailByCodeAsync(string empNo);

        Task<IEnumerable<EmployeeDepartmentDetailModel>> GetAllEmployeeDepartmentDetailsAsync(string currentUserId);
        Task<EmployeeDepartmentDetailModel?> GetEmployeeDepartmentDetailByCodeAsync(string code);
        Task<bool> AddEmployeeDepartmentDetailAsync(EmployeeDepartmentDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin);
        Task<bool> UpdateEmployeeDepartmentDetailAsync(EmployeeDepartmentDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin);
        Task<bool> DeleteEmployeeDepartmentDetailAsync(string code);
        Task<string> GetDepartmentStrengthValidationMessageAsync(string parentDeptId, string subDeptId, string cityCode);

        Task<IEnumerable<EmployeeContractModel>> GetAllEmployeeContractsAsync(string currentUserId);
        Task<EmployeeContractModel?> GetEmployeeContractByCodeAsync(string code);
        Task<bool> AddEmployeeContractAsync(EmployeeContractModel model, string currentUserId);
        Task<bool> UpdateEmployeeContractAsync(EmployeeContractModel model, string currentUserId);
        Task<bool> DeleteEmployeeContractAsync(string code);

        Task<IEnumerable<EmployeeBankDetailModel>> GetAllEmployeeBankDetailsAsync(string currentUserId);
        Task<IEnumerable<SelectListItem>> GetBankListAsync();
        Task<EmployeeBankDetailModel?> GetEmployeeBankDetailByCodeAsync(string code);
        Task<bool> AddEmployeeBankDetailAsync(EmployeeBankDetailModel model, string currentUserId);
        Task<bool> UpdateEmployeeBankDetailAsync(EmployeeBankDetailModel model, string currentUserId);
        Task<bool> DeleteEmployeeBankDetailAsync(string code);
        Task<(int successCount, string message)> BulkUploadEmployeeBankDetailsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        Task<IEnumerable<EmployeePayStructureModel>> GetAllEmployeePayStructuresAsync(string currentUserId);
        Task<EmployeePayStructureModel?> GetEmployeePayStructureByCodeAsync(string code);
        Task<bool> AddEmployeePayStructureAsync(EmployeePayStructureModel model, string currentUserId);
        Task<bool> UpdateEmployeePayStructureAsync(EmployeePayStructureModel model, string currentUserId);

        Task<IEnumerable<EmployeeAssetModel>> GetEmployeeAssetsAsync(string empNo);
        Task<EmployeeAssetModel?> GetEmployeeAssetByIdAsync(string code);
        Task<bool> AddEmployeeAssetAsync(EmployeeAssetModel model, string currentUserId);
        Task<bool> UpdateEmployeeAssetAsync(EmployeeAssetModel model, string currentUserId);
        Task<bool> DeleteEmployeeAssetAsync(string code);
        Task<bool> DeleteEmployeePayStructureAsync(string code);
        Task<IEnumerable<EmployeeTrainingModel>> GetAllEmployeeTrainingsAsync(string currentUserId);
        Task<EmployeeTrainingModel?> GetEmployeeTrainingByIdAsync(string code);
        Task<bool> AddEmployeeTrainingAsync(EmployeeTrainingModel model, string currentUserId);
        Task<bool> UpdateEmployeeTrainingAsync(EmployeeTrainingModel model, string currentUserId);
        Task<bool> DeleteEmployeeTrainingAsync(string code);

        Task<IEnumerable<EmployeeShowCauseModel>> GetAllEmployeeShowCausesAsync(string currentUserId);
        Task<EmployeeShowCauseModel?> GetEmployeeShowCauseByIdAsync(string code);
        Task<bool> AddEmployeeShowCauseAsync(EmployeeShowCauseModel model, string currentUserId);
        Task<bool> UpdateEmployeeShowCauseAsync(EmployeeShowCauseModel model, string currentUserId);
        Task<bool> DeleteEmployeeShowCauseAsync(string code);

        Task<IEnumerable<EmployeePromotionAwardModel>> GetAllEmployeePromotionAwardsAsync(string currentUserId);
        Task<EmployeePromotionAwardModel?> GetEmployeePromotionAwardByIdAsync(string code);
        Task<bool> AddEmployeePromotionAwardAsync(EmployeePromotionAwardModel model, string currentUserId);
        Task<bool> UpdateEmployeePromotionAwardAsync(EmployeePromotionAwardModel model, string currentUserId);
        Task<bool> DeleteEmployeePromotionAwardAsync(string code);

        Task<IEnumerable<EmployeePartTimeModel>> GetAllEmployeePartTimesAsync(string currentUserId);
        Task<EmployeePartTimeModel?> GetEmployeePartTimeByIdAsync(string code);
        Task<bool> AddEmployeePartTimeAsync(EmployeePartTimeModel model, string currentUserId, bool isAdmin);
        Task<bool> UpdateEmployeePartTimeAsync(EmployeePartTimeModel model, string currentUserId);
        Task<bool> DeleteEmployeePartTimeAsync(string code);

        Task<IEnumerable<MultipleJobsApproveModel>> GetAllMultipleJobsApproveAsync(string currentUserId);
        Task<MultipleJobsApproveModel?> GetMultipleJobsApproveByIdAsync(string code);
        Task<bool> AddMultipleJobsApproveAsync(MultipleJobsApproveModel model, string currentUserId);
        Task<bool> UpdateMultipleJobsApproveAsync(MultipleJobsApproveModel model, string currentUserId);
        Task<bool> DeleteMultipleJobsApproveAsync(string code);

        Task<IEnumerable<IncrementModel>> GetAllIncrementsAsync(string currentUserId);
        Task<IncrementModel?> GetIncrementByIdAsync(string code);
        Task<bool> AddIncrementAsync(IncrementModel model, string currentUserId);
        Task<bool> UpdateIncrementAsync(IncrementModel model, string currentUserId);
        Task<bool> DeleteIncrementAsync(string code);
        Task<(int successCount, string message)> BulkUploadIncrementsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        Task<IEnumerable<IncrementApprovalModel>> GetPendingIncrementsAsync(string currentUserId, string? cityCode, string? deptId);
        Task<bool> ProcessIncrementApprovalsAsync(List<IncrementApprovalModel> incrementsToProcess, string currentUserId);
        Task<IReadOnlyList<IncrementApprovalPreviewModel>> PreviewIncrementApprovalsAsync(List<IncrementApprovalModel> incrementsToProcess, string currentUserId);

        // Employee Route Code
        Task<dynamic?> GetEmployeeCityInfoAsync(string empNo);
        Task<string?> GetRouteDescriptionAsync(string routeCode);
        Task<IEnumerable<EmployeeRouteCodeModel>> GetAllEmployeeRouteCodesAsync(string currentUserId);
        Task<EmployeeRouteCodeModel?> GetEmployeeRouteCodeByCodeAsync(string code);
        Task<IEnumerable<SelectListItem>> GetCourierCodeTypesAsync();
        Task<IEnumerable<SelectListItem>> GetLocationsByCityCodeAsync(string cityCode);
        Task<bool> AddEmployeeRouteCodeAsync(EmployeeRouteCodeModel model, string currentUserId);
        Task<bool> UpdateEmployeeRouteCodeAsync(EmployeeRouteCodeModel model, string currentUserId);
        Task<bool> DeleteEmployeeRouteCodeAsync(string code);

        // Employee Shift Detail
        Task<IEnumerable<EmployeeShiftDetailModel>> GetAllEmployeeShiftDetailsAsync(string currentUserId);
        Task<EmployeeShiftDetailModel?> GetEmployeeShiftDetailByIdAsync(string id);
        Task<IEnumerable<SelectListItem>> GetActiveShiftsAsync();
        Task<bool> AddEmployeeShiftDetailAsync(EmployeeShiftDetailModel model, string currentUserId, bool isAdmin);
        Task<bool> UpdateEmployeeShiftDetailAsync(EmployeeShiftDetailModel model, string currentUserId);
        Task<bool> DeleteEmployeeShiftDetailAsync(string id);
        Task<(int successCount, string message)> BulkUploadEmployeeShiftsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        // Employee Personal Detail
        Task<IEnumerable<EmployeeListItemModel>> GetAllEmployeesAsync(string currentUserId);
        Task<EmployeePersonalDetailModel?> GetEmployeePersonalDetailByEmpNoAsync(string empNo);
        Task<(bool success, string empNo, string message)> AddEmployeePersonalDetailAsync(EmployeePersonalDetailModel model, string currentUserId);
        Task<bool> UpdateEmployeePersonalDetailAsync(EmployeePersonalDetailModel model, string currentUserId);
        Task<bool> DeleteEmployeePersonalDetailAsync(string empNo);

        // Employee Experience
        Task<IEnumerable<EmployeeExperienceModel>> GetEmployeeExperiencesAsync(string empNo);
        Task<EmployeeExperienceModel?> GetEmployeeExperienceBySnAsync(string empNo, int sno);
        Task<bool> AddEmployeeExperienceAsync(EmployeeExperienceModel model, string currentUserId);
        Task<bool> UpdateEmployeeExperienceAsync(EmployeeExperienceModel model, string currentUserId);
        Task<bool> DeleteEmployeeExperienceAsync(string empNo, int sno);

        // Employee Education
        Task<IEnumerable<EmployeeEducationModel>> GetEmployeeEducationsAsync(string empNo);
        Task<EmployeeEducationModel?> GetEmployeeEducationBySnAsync(string empNo, int sno);
        Task<bool> AddEmployeeEducationAsync(EmployeeEducationModel model, string currentUserId);
        Task<bool> UpdateEmployeeEducationAsync(EmployeeEducationModel model, string currentUserId);
        Task<bool> DeleteEmployeeEducationAsync(string empNo, int sno);

        // Employee Medical History
        Task<IEnumerable<EmployeeMedicalHistoryModel>> GetEmployeeMedicalHistoriesAsync(string empNo);
        Task<EmployeeMedicalHistoryModel?> GetEmployeeMedicalHistoryBySnAsync(string empNo, int sno);
        Task<bool> AddEmployeeMedicalHistoryAsync(EmployeeMedicalHistoryModel model, string currentUserId);
        Task<bool> UpdateEmployeeMedicalHistoryAsync(EmployeeMedicalHistoryModel model, string currentUserId);
        Task<bool> DeleteEmployeeMedicalHistoryAsync(string empNo, int sno);

        // Employee Medical Survey
        Task<EmployeeMedicalSurveyModel?> GetEmployeeMedicalSurveyAsync(string empNo);
        Task<bool> SaveEmployeeMedicalSurveyAsync(EmployeeMedicalSurveyModel model, string currentUserId);

        // Attendance Adjustment
        Task<IEnumerable<AttendanceAdjustmentModel>> GetAttendanceAdjustmentsAsync(string currentUserId);
        Task<AttendanceAdjustmentModel?> GetAttendanceAdjustmentAsync(string empNo, DateTime date);
        Task<(bool success, string message)> AddAttendanceAdjustmentAsync(AttendanceAdjustmentModel model, string currentUserId);
        Task<(bool success, string message)> UpdateAttendanceAdjustmentAsync(AttendanceAdjustmentModel model, string currentUserId);
        Task<(bool success, string message)> DeleteAttendanceAdjustmentAsync(string empNo, DateTime date);
        Task<(bool success, string message)> BulkMarkPresentAsync(IFormFile file, int year, int month, bool isDateWise, DateTime fromDate, DateTime toDate, string currentUserId);
        Task<(bool success, string message)> BulkMarkAbsentAsync(IFormFile file, int year, int month, string currentUserId);
    }
}
