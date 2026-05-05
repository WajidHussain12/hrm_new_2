using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface ICommissionExecutionHistoryService
    {
        /// <summary>
        /// Inserts one row into hr_commission_execution_history.
        /// Creates the table on first call if it does not exist.
        /// Never throws — failures are logged and swallowed so callers are never interrupted.
        /// </summary>
        Task RecordAsync(CommissionExecutionRecord record);

        /// <summary>
        /// Returns all execution records for the given year/month, newest first.
        /// Used by GetReconciledHistoryAsync to populate AllExecutions per city×commType.
        /// </summary>
        Task<List<CommissionExecutionRecord>> GetByMonthAsync(int year, int month);
    }
}
