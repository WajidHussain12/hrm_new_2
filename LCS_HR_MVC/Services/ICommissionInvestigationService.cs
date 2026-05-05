using LCS_HR_MVC.Models;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// Commission Investigation Center — reads commission data across all 3 servers
    /// and provides full drill-down analysis for any employee.
    ///
    /// Server mapping:
    ///   Server 6  = LHR_Billing connection  (172.16.0.6) — raw billing CNs
    ///   Server 7  = MIS connection           (172.16.0.7) — location + OLE
    ///   Server 10 = DefaultConnection        (172.16.0.10)— HR master + final commission
    ///
    /// ALL queries are SELECT-only — no inserts/updates to commission tables.
    /// The only write operations are to hr_commission_investigation_notes (CRUD log).
    /// </summary>
    public interface ICommissionInvestigationService
    {
        // ── Dropdowns ─────────────────────────────────────────────────────────

        Task<List<int>>                        GetAvailableYearsAsync();
        Task<List<(string Code, string Name)>> GetCitiesAsync();

        // ── Search ────────────────────────────────────────────────────────────

        /// <summary>
        /// Searches employees by EmpNo / Name / RouteCode / City for the given month.
        /// Returns lightweight rows with commission status for the list view.
        /// </summary>
        Task<List<EmpInvestigationSearchRow>> SearchEmployeesAsync(CommissionInvestigationFilter filter);

        // ── Full investigation ────────────────────────────────────────────────

        /// <summary>
        /// Loads complete commission investigation data for a single employee:
        ///  — Server 10: employee master, route codes, eligibility, attendance,
        ///               cash/COD CNs, return COD, final commission, rate policies
        ///  — Server 6:  raw billing CNs (graceful degradation if unavailable)
        ///  — Server 7:  location details, OLE records (graceful degradation)
        /// Auto-detects issues by comparing data across servers.
        /// </summary>
        Task<EmployeeCommissionInvestigationVm> GetInvestigationAsync(
            string empNo, int year, int month, string cityCode);

        // ── Route Code Suggestions (fix modal hint system) ───────────────────

        /// <summary>
        /// Queries all 3 servers + commission history + similar colleagues to produce
        /// a suggestion for what Route Code and Code Type to assign to the employee.
        /// Used to pre-fill the "Fix Route Code" modal.
        /// </summary>
        Task<RouteCodeSuggestionVm> GetRouteCodeSuggestionsAsync(string empNo, string cityCode);

        // ── CRUD — Investigation Notes ────────────────────────────────────────

        Task<List<InvestigationNote>> GetNotesAsync(string empNo, int year, int month);
        Task<int>  CreateNoteAsync(CreateNoteRequest request, string createdBy);
        Task<bool> UpdateNoteStatusAsync(int noteId, string newStatus, string updatedBy);
        Task<bool> SoftDeleteNoteAsync(int noteId, string deletedBy);
    }
}
