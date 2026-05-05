using LCS_HR_MVC.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LCS_HR_MVC.Services
{
    public interface ICommissionService
    {
        Task<IEnumerable<EmployeeCommissionModel>> GetAllEmployeeCommissionsAsync(string currentUserId);
        Task<EmployeeCommissionModel?> GetEmployeeCommissionByIdAsync(string id);
        Task<bool> AddEmployeeCommissionAsync(EmployeeCommissionModel model, string currentUserId);
        Task<bool> UpdateEmployeeCommissionAsync(EmployeeCommissionModel model, string currentUserId);
        Task<bool> DeleteEmployeeCommissionAsync(string id);
        
        Task<IEnumerable<dynamic>> SearchRoutesAsync(string term, string cityCode);

        Task<(bool success, string message)> ProcessTagCommissionAsync(TagCommissionViewModel model, string currentUserId);
    }
}