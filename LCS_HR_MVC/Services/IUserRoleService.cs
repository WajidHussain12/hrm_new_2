using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IUserRoleService
    {
        Task<IEnumerable<UserRoleModel>> GetAllRolesAsync();
        Task<bool> IsDescriptionExistsAsync(string description, string? excludeRoleId = null);
        Task<bool> AddRoleAsync(UserRoleModel role);
        Task<bool> UpdateRoleAsync(UserRoleModel role);
        Task<bool> DeleteRoleAsync(string roleId);
        Task<string> GenerateNewRoleIdAsync();
    }
}
