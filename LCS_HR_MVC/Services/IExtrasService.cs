using LCS_HR_MVC.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LCS_HR_MVC.Services
{
    public interface IExtrasService
    {
        // Employee Extras
        Task<IEnumerable<EmployeeExtraModel>> GetAllEmployeeExtrasAsync(string currentUserId);
        Task<EmployeeExtraModel?> GetEmployeeExtraByIdAsync(string id);
        Task<bool> AddEmployeeExtraAsync(EmployeeExtraModel model, string currentUserId);
        Task<bool> UpdateEmployeeExtraAsync(EmployeeExtraModel model, string currentUserId);
        Task<bool> DeleteEmployeeExtraAsync(string id);
        Task<(int successCount, string message)> BulkUploadEmployeeExtrasAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);
        
        // Employee Extras Fixed
        Task<IEnumerable<EmployeeExtraFixedModel>> GetAllEmployeeExtrasFixedAsync(string currentUserId);
        Task<EmployeeExtraFixedModel?> GetEmployeeExtraFixedByIdAsync(string id);
        Task<bool> AddEmployeeExtraFixedAsync(EmployeeExtraFixedModel model, string currentUserId);
        Task<bool> UpdateEmployeeExtraFixedAsync(EmployeeExtraFixedModel model, string currentUserId);
        Task<bool> DeleteEmployeeExtraFixedAsync(string id);
        Task<(int successCount, string message)> BulkUploadEmployeeExtrasFixedAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        // Employee AD Details
        Task<IEnumerable<EmpADDetailsModel>> GetAllEmpADDetailsAsync(string currentUserId);
        Task<EmpADDetailsModel?> GetEmpADDetailsByIdAsync(string id);
        Task<bool> AddEmpADDetailsAsync(EmpADDetailsModel model, string currentUserId);
        Task<bool> UpdateEmpADDetailsAsync(EmpADDetailsModel model, string currentUserId);
        Task<bool> DeleteEmpADDetailsAsync(string id);
        Task<(int successCount, string message)> BulkUploadEmpADDetailsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        // Extra Hours Approval
        Task<IEnumerable<EmployeeExtraModel>> GetPendingExtraHoursAsync(string currentUserId, string? cityCode, string? deptCode);
        Task<(int processed, int failed)> ProcessExtraHoursAsync(List<string> codes, int statusId);
        
        // Helpers
        Task<IEnumerable<dynamic>> GetExtraTypesAsync(int parentId = 0);
        Task<IEnumerable<dynamic>> SearchADCodesAsync(string term);
        Task<IEnumerable<dynamic>> GetCitiesAsync();
        Task<IEnumerable<dynamic>> GetDepartmentsByCityAsync(string cityCode);
    }
}