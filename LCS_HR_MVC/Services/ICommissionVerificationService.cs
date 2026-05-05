using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    public interface ICommissionVerificationService
    {
        /// <summary>Returns available years for the filter dropdown.</summary>
        Task<List<int>> GetAvailableYearsAsync();

        /// <summary>Returns all cities (Code, city_name) for the filter dropdown.</summary>
        Task<List<(string Code, string Name)>> GetCitiesAsync();

        /// <summary>
        /// Returns all CN-level commission rows (Cash + COD) for the given month,
        /// sorted by date DESC, with pagination.
        /// Commission period: 21st of previous month → 20th of selected month.
        /// </summary>
        Task<(List<CommissionFlatRow> Rows, int TotalCount)> GetAllCommissionsPagedAsync(
            CommissionVerificationFilter filter, int page, int pageSize);

        /// <summary>
        /// Searches employees matching the name/empNo search terms.
        /// Returns up to 50 matches (no CN detail loaded).
        /// </summary>
        Task<List<EmployeeCommissionSummary>> SearchEmployeesAsync(CommissionVerificationFilter filter);

        /// <summary>
        /// Returns full commission detail for a single employee:
        /// summary cards + CN-level rows (Cash + COD) + missing CNs (commission=0).
        /// </summary>
        Task<(EmployeeCommissionSummary Summary, List<CnCommissionRow> Cns, List<MissingCnRow> MissingCns)> GetEmployeeDetailAsync(
            string empNo, int year, int month, string cityCode);

        /// <summary>
        /// Returns processed commission results from hr_commissionprocess,
        /// one row per employee, sorted by GrandTotal DESC, paginated.
        /// </summary>
        Task<(List<ProcessedCommissionRow> Rows, int TotalCount)> GetProcessedCommissionsPagedAsync(
            CommissionVerificationFilter filter, int page, int pageSize);
    }
}
