using System.Collections.Generic;
using System.Threading.Tasks;
using LCS_HR_MVC.Models.Closing;

namespace LCS_HR_MVC.Services
{
    public interface IClosingService
    {
        Task<IEnumerable<CloseProcessModel>> GetAllClosedProcessesAsync(string currentUserId);
        Task<bool> AddCloseProcessAsync(CloseProcessModel model, string currentUserId);
        Task<bool> DeleteCloseProcessAsync(string cityCode, int year, int month);
        
        Task<bool> UnlockSalaryAsync(UnlockSalaryViewModel model);
        Task<bool> CommissionUnlockAsync(CommissionUnlockViewModel model);
        
        Task<IEnumerable<dynamic>> GetZonesByUserAsync(string currentUserId);
        Task<IEnumerable<dynamic>> GetCitiesByZoneUserAsync(string zoneCode, string currentUserId);
    }
}