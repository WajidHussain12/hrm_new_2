using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IAdminUserService
    {
        Task<IEnumerable<UserAdminModel>> GetAllUsersAsync();
        Task<UserAdminModel?> GetUserByIdAsync(string userId);
        Task<bool> AddUserAsync(UserAdminModel model, string currentUserId);
        Task<bool> UpdateUserAsync(UserAdminModel model, string currentUserId);
        Task<bool> DeleteUserAsync(string userId);
        Task<IEnumerable<dynamic>> SearchLocationsAsync(string term);
        Task<IEnumerable<dynamic>> SearchUsersAsync(string term);
        Task<bool> IsUserNameExistsAsync(string userName, string locationId, string? excludeUserId = null);
        Task<bool> IsLocationValidAsync(string locationId, string locationDescription);
        Task<bool> CheckReferencesAsync(string userId);
    }
}
