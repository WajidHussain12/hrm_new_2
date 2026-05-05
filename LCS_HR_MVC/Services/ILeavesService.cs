using LCS_HR_MVC.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LCS_HR_MVC.Services
{
    public interface ILeavesService
    {
        Task<IEnumerable<LeaveRequestModel>> GetAllLeaveRequestsAsync(string currentUserId);
        Task<LeaveRequestModel?> GetLeaveRequestByIdAsync(string id);
        Task<bool> AddLeaveRequestAsync(LeaveRequestModel model, string currentUserId);
        Task<bool> UpdateLeaveRequestAsync(LeaveRequestModel model, string currentUserId);
        Task<bool> DeleteLeaveRequestAsync(string id);
        Task<(int successCount, string message)> BulkUploadLeaveRequestsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId);
        
        Task<IEnumerable<dynamic>> SearchLeaveCategoriesAsync(string term);
        Task<IEnumerable<dynamic>> SearchLeaveTypesAsync(string term);

        Task<IEnumerable<LeaveRequestModel>> GetPendingLeaveRequestsAsync(string currentUserId, DateTime? fromDate, DateTime? toDate);
        Task<(int approved, int failed)> ApproveLeaveRequestsAsync(List<string> requestCodes, string currentUserId, string currentUserName);
        Task<(int rejected, int failed)> RejectLeaveRequestsAsync(List<string> requestCodes, string currentUserId, string currentUserName);

        Task<IEnumerable<TakenLeaveModel>> GetAllTakenLeavesAsync();
        Task<bool> IsEmployeeOnProbationAsync(string empNo, int year);
        Task<bool> ActivateProbationPeriodAsync(string empNo, int year);
        Task<bool> AddTakenLeavesAsync(TakenLeaveModel model, string currentUserId);
        Task<bool> UpdateTakenLeaveAsync(TakenLeaveModel model, DateTime originalLeaveDate, string currentUserId);
        Task<bool> DeleteTakenLeaveAsync(string empNo, int year, DateTime leaveDate);
    }
}