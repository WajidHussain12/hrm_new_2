using System.Collections.Generic;
using System.Threading.Tasks;
using LCS_HR_MVC.Models.Settlement;

namespace LCS_HR_MVC.Services
{
    public interface IAdvanceSalaryService
    {
        Task<IEnumerable<AdvanceSalaryModel>> GetAllAdvanceSalariesAsync(string currentUserId, string sortBy = "Code");
        Task<AdvanceSalaryModel?> GetAdvanceSalaryByIdAsync(string id);
        Task<bool> AddAdvanceSalaryAsync(AdvanceSalaryModel model, string currentUserId);
        Task<bool> UpdateAdvanceSalaryAsync(AdvanceSalaryModel model, string currentUserId);
        Task<bool> DeleteAdvanceSalaryAsync(string id);
        Task<(int successCount, string message)> BulkUploadAdvanceSalariesAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);

        Task<(int processed, int failed)> ProcessAdvanceSalaryApprovalsAsync(List<string> codes, string status, string currentUserId);
        
        Task<dynamic?> GetEmployeeSalaryInfoAsync(string empNo);
    }
}