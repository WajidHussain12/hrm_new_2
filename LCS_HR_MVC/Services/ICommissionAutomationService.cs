using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface ICommissionAutomationService
    {
        Task<string> StartAutomationAsync(int year, int month, string triggeredBy, string userId);
        Task ExecuteAutomationJobAsync(string jobRunId, int year, int month, string userId);
        Task TriggerScheduledAsync();
        Task<CommissionAutomationDashboardViewModel> GetDashboardAsync(int year, int month);
        Task<CommissionBaseDataValidationResult> ValidateBaseDataAsync(int year, int month);
        Task<CommissionHistoryViewModel> GetReconciledHistoryAsync(int year, int month);
    }
}
