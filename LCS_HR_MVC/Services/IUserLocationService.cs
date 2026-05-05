using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IUserLocationService
    {
        Task<IEnumerable<LocationItem>> GetUserLocationsAsync(string userId);
        Task<IEnumerable<LocationItem>> GetAllLocationsAsync();
        Task<bool> UpdateUserLocationsAsync(string userId, IEnumerable<string> locationCodes);
    }
}
