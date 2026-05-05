using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IUserPrivilegeService
    {
        Task<IEnumerable<PrivilegeItem>> GetPrivilegesAsync(string roleId);
        Task<bool> UpdatePrivilegesAsync(string roleId, IEnumerable<PrivilegeItem> privileges);
    }
}
