using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface ITestDataMigrationService
    {
        /// <summary>Enqueues a Hangfire background job and returns the job ID.</summary>
        Task<string> StartMigrationAsync(int year, int month, int rowLimit, List<string>? cities = null);

        /// <summary>Actual migration work — invoked by Hangfire.</summary>
        Task RunMigrationAsync(int year, int month, int rowLimit, List<string>? cities = null);

        /// <summary>Returns current migration status (or Idle if never started).</summary>
        Task<MigrationStatusViewModel> GetCurrentStatusAsync(int year, int month);

        /// <summary>Returns all cities available for city-filter dropdown (from lcs_db.city).</summary>
        Task<List<CityOption>> GetAvailableCitiesAsync();

        /// <summary>Returns the current local existence + row-count status for every destination table.</summary>
        Task<List<DestinationTableCheck>> GetDestinationTablesStatusAsync();

        /// <summary>
        /// Creates any missing destination tables on the LOCAL server by copying DDL from the LIVE server.
        /// Already-existing tables are skipped. Returns counts and per-table messages.
        /// </summary>
        Task<(int created, int skipped, int failed, List<string> messages)> CreateDestinationTablesAsync();
    }
}
