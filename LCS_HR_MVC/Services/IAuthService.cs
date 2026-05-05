using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface IAuthService
    {
        Task<UserModel?> AuthenticateUserAsync(string username, string password);
        Task<bool> IsPasswordUpdatedThisMonthAsync(int userId);
        Task PerformCloseProcessAsync(DateTime workingDate);
        
        Task<bool> CheckCurrentPasswordAsync(int userId, string hashedPassword);
        Task<bool> UpdatePasswordAsync(int userId, string newHashedPassword);
    }
}
