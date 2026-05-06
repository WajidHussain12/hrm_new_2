using System;
using System.Collections.Generic;
using System.Linq;

namespace LCS_HR_MVC.Models
{
    public class CommissionAutomationLogEntry
    {
        public int Id { get; set; }
        public string JobRunId { get; set; } = string.Empty;
        public string? TriggeredBy { get; set; }
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
        public string CommissionType { get; set; } = "";
        public string Label { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public string DateField { get; set; } = "";
        public bool ConnectionAvailable { get; set; }
        public bool HasData { get; set; }
        public long RowCount { get; set; }
        public string? ConnectionError { get; set; }
    }

    public class CommissionExecutionRecord
    {
        public int Id { get; set; }
        public string ExecutionSource { get; set; } = "Manual";
        public string? JobRunId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = "";
        public string CityName { get; set; } = "";
        public string CommissionType { get; set; } = "";
        public string? TriggeredBy { get; set; }
        public string? TriggeredByUserId { get; set; }
        public string Status { get; set; } = "Completed";
        public int RowsProcessed { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int? DurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CommissionTableEvidence
    {
        public string CityCode { get; set; } = "";
        public string CommissionType { get; set; } = "";
        public long RowCount { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? ProcessedByUserId { get; set; }
    }

    public class CommissionHistoryEntry
    {
        public string CityCode { get; set; } = "";
        public string CityName { get; set; } = "";
        public string CommissionType { get; set; } = "";
        public string DerivedStatus { get; set; } = "NoHistory";
        public string StatusSource { get; set; } = "None";
        public long EvidenceRowCount { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? ProcessedByUserId { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; }
        public CommissionAutomationLogEntry? LogEntry { get; set; }
        public List<CommissionAutomationLogEntry> AllLogEntries { get; set; } = new();
        public List<CommissionExecutionRecord> AllExecutions { get; set; } = new();
        public string? TriggeredByUserName { get; set; }
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
        public bool HasLogData { get; set; }
        public bool HasTableData { get; set; }
        public Dictionary<string, string> EvidenceErrors { get; set; } = new();
    }

    public class CommissionAutomationDashboardViewModel
    {
        private List<CommissionAutomationLogEntry> _entries = new();

        public int Year { get; set; }
        public int Month { get; set; }
        public string? JobRunId { get; set; }

        public List<CommissionAutomationLogEntry> Entries
        {
            get => _entries;
            set => _entries = ReconcileDashboardEntries(value ?? new List<CommissionAutomationLogEntry>());
        }

        public bool IsRunning { get; set; }
        public List<int> AvailableYears { get; set; } = new();
        public Dictionary<string, string> CityZones { get; set; } = new();

        private static List<CommissionAutomationLogEntry> ReconcileDashboardEntries(List<CommissionAutomationLogEntry> entries)
        {
            if (entries.Count == 0)
            {
                return new List<CommissionAutomationLogEntry>();
            }

            static int Priority(string? status) => status switch
            {
                "Completed"        => 6,
                "AlreadyProcessed" => 6,
                "Running"          => 5,
                "Pending"          => 4,
                "NoData"           => 3,
                "Skipped"          => 2,
                "Failed"           => 1,
                _                  => 0
            };

            return entries
                .GroupBy(entry => new { entry.CityCode, entry.CommissionType })
                .Select(group => group
                    .OrderByDescending(entry => Priority(entry.Status))
                    .ThenByDescending(entry => entry.UpdatedAt)
                    .ThenByDescending(entry => entry.Id)
                    .First())
                .OrderBy(entry => entry.CityName)
                .ThenBy(entry => entry.CityCode)
                .ThenBy(entry => entry.CommissionType)
                .ToList();
        }
    }
}
