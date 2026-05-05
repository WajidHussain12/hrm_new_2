using System.Collections.Generic;
using System.Threading.Tasks;
using LCS_HR_MVC.Models.Settlement;

namespace LCS_HR_MVC.Services
{
    public interface ISettlementService
    {
        Task<IEnumerable<EmployeeTerminationModel>> GetAllEmployeeTerminationsAsync(string currentUserId);
        Task<EmployeeTerminationModel?> GetEmployeeTerminationByIdAsync(string id);
        Task<bool> AddEmployeeTerminationAsync(EmployeeTerminationModel model, string currentUserId);
        Task<bool> UpdateEmployeeTerminationAsync(EmployeeTerminationModel model, string currentUserId);
        Task<bool> DeleteEmployeeTerminationAsync(string id, string currentUserId);
        Task<(int successCount, string message)> BulkUploadTerminationsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        Task<(bool success, string message)> ProcessFinalSettlementAsync(FinalSettlementModel model, string currentUserId);
        Task<FinalSettlementPreviewResult> PreviewFinalSettlementAsync(FinalSettlementModel model, string currentUserId);
        Task<FinalSettlementPreviewResult> ReplayFinalSettlementAsync(FinalSettlementModel model, string currentUserId);
        Task<dynamic?> GetEmployeeResignDataAsync(string empNo);
        Task<IEnumerable<dynamic>> SearchSettlementEmployeesAsync(string term, string currentUserId);
    }
}
