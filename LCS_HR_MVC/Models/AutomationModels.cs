using System;
using System.Collections.Generic;

namespace LCS_HR_MVC.Models
{
    public class CommissionAutomationLogEntry
    {
        public int Id { get; set; }
        public string JobRunId { get; set; } = string.Empty;
        /// <summary>Display name of the user who triggered this automation run (e.g. "admin", "Scheduled").</summary>
        public string? TriggeredBy { get; set; }
        /// <summary>User ID of the person who triggered this run (numeric string from lcs_users.userID).</summary>
        public string? TriggeredByUserId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string CommissionType { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public int ProgressPct { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; }
        public int ProcessedCount { get; set; }
        public int TotalCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class AutomationProgressUpdate
    {
        public int LogId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ProgressPct { get; set; }
        public string? ErrorMessage { get; set; }
        public string CityName { get; set; } = string.Empty;
        public string CommissionType { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public int ProcessedCount { get; set; }
        public int TotalCount { get; set; }
    }

    // ─── Pre-flight base-data validation ───────────────────────────────────────

    public class CommissionBaseDataValidationResult
    {
        public bool IsValid { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime RequiredFrom { get; set; }
        public DateTime RequiredTo { get; set; }
        public List<CommissionTableCheckResult> TableChecks { get; set; } = new();
    }

    public class CommissionTableCheckResult
    {
        public string CommissionType { get; set; } = "";   // e.g. "CashCommission"
        public string Label { get; set; } = "";            // human-readable
        public string TableName { get; set; } = "";        // schema.table
        public string ConnectionName { get; set; } = "";   // appsettings key
        public string DateField { get; set; } = "";
        public bool ConnectionAvailable { get; set; }
        public bool HasData { get; set; }
        public long RowCount { get; set; }
        public string? ConnectionError { get; set; }
    }

    // ────────────────────────────────────────────────────────────────────────────

    // ─── Per-execution audit record (hr_commission_execution_history) ─────────────

    /// <summary>
    /// One row per commission execution event — inserted for EVERY run regardless of source
    /// (Automation pipeline OR any manual PayrollController action).
    /// Table: hr_commission_execution_history (auto-created on first use).
    /// </summary>
    public class CommissionExecutionRecord
    {
        public int Id { get; set; }
        public string ExecutionSource { get; set; } = "Manual"; // "Automation" | "Manual"
        public string? JobRunId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = "";
        public string CityName { get; set; } = "";
        public string CommissionType { get; set; } = "";
        public string? TriggeredBy { get; set; }   // username / display name
        public string? TriggeredByUserId { get; set; }   // numeric user ID
        public string Status { get; set; } = "Completed";
        public int RowsProcessed { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int? DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ─── Commission History Reconciliation Models ────────────────────────────────

    /// <summary>Evidence row found directly in an output/lock table (not from automation log).</summary>
    public class CommissionTableEvidence
    {
        public string CityCode { get; set; } = "";
        public string CommissionType { get; set; } = "";
        public long RowCount { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? ProcessedByUserId { get; set; }
    }

    /// <summary>
    /// A single city × commission-type history entry that merges automation log data
    /// with evidence queried directly from the actual commission output tables.
    /// </summary>
    public class CommissionHistoryEntry
    {
        public string CityCode { get; set; } = "";
        public string CityName { get; set; } = "";
        public string CommissionType { get; set; } = "";

        /// <summary>
        /// Derived status after reconciling log + actual table evidence.
        /// Values: Completed | AlreadyProcessed | Processed | Failed | NoData | Running | Pending | NoHistory
        /// </summary>
        public string DerivedStatus { get; set; } = "NoHistory";

        /// <summary>Where the status came from: Log | Table | Both | None</summary>
        public string StatusSource { get; set; } = "None";

        public long EvidenceRowCount { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? ProcessedByUserId { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; }

        /// <summary>Latest automation log entry — null if run outside automation.</summary>
        public CommissionAutomationLogEntry? LogEntry { get; set; }

        /// <summary>ALL automation log entries for this city×commType (newest first). Enables full audit trail.</summary>
        public List<CommissionAutomationLogEntry> AllLogEntries { get; set; } = new();

        /// <summary>
        /// Every execution event from hr_commission_execution_history for this city×commType (newest first).
        /// Covers BOTH automation and manual runs — this is the authoritative per-run audit trail.
        /// </summary>
        public List<CommissionExecutionRecord> AllExecutions { get; set; } = new();

        /// <summary>Resolved user name for the triggered_by_user_id of the latest log entry.</summary>
        public string? TriggeredByUserName { get; set; }

        /// <summary>Resolved user name for the evidence CreatedBy (from output table, manual/old-system runs).</summary>
        public string? EvidenceUserName { get; set; }
    }

    public class CommissionHistoryViewModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string? JobRunId { get; set; }
        public List<CommissionHistoryEntry> Entries { get; set; } = new();
        public List<int> AvailableYears { get; set; } = new();
        public Dictionary<string, string> CityZones { get; set; } = new();

        // Reconciliation metadata
        public bool HasLogData { get; set; }   // log table had entries for this month
        public bool HasTableData { get; set; }   // at least one output table had data
        public Dictionary<string, string> EvidenceErrors { get; set; } = new(); // commissionType → error msg if query failed
    }

    // ────────────────────────────────────────────────────────────────────────────

    public class CommissionAutomationDashboardViewModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string? JobRunId { get; set; }
        public List<CommissionAutomationLogEntry> Entries { get; set; } = new();
        public bool IsRunning { get; set; }
        public List<int> AvailableYears { get; set; } = new();
        // cityCode → zoneName  (populated by GetDashboardAsync)
        public Dictionary<string, string> CityZones { get; set; } = new();
    }
}
