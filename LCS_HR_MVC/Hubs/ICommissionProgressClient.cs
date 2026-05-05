using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Hubs
{
    /// <summary>
    /// Strongly-typed SignalR client interface for CommissionProgressHub.
    /// Every method name here becomes the client-side event name — no magic strings.
    /// </summary>
    public interface ICommissionProgressClient
    {
        Task ProgressUpdate(AutomationProgressUpdate update);
        Task LogEntry(CommissionLogEntry entry);
        Task JobCompleted(string jobRunId, string summary);
    }
}
