using LCS_HR_MVC.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LCS_HR_MVC.Services
{
    public interface IOvertimeService
    {
        Task<IEnumerable<EmployeeOvertimeModel>> GetAllEmployeeOvertimesAsync(string currentUserId);
        Task<EmployeeOvertimeModel?> GetEmployeeOvertimeByIdAsync(string id);
        Task<bool> AddEmployeeOvertimeAsync(EmployeeOvertimeModel model, string currentUserId);
        Task<bool> UpdateEmployeeOvertimeAsync(EmployeeOvertimeModel model, string currentUserId);
        Task<bool> DeleteEmployeeOvertimeAsync(string id);
    }
}