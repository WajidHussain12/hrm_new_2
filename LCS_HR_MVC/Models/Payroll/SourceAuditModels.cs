using System;

namespace LCS_HR_MVC.Models.Payroll
{
    public sealed class CashVasSourceAuditResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int StationCount { get; set; }
        public int RequestedCnCount { get; set; }
        public int HistoricalRows { get; set; }
        public int CurrentRows { get; set; }
        public decimal HistoricalTotalIncentive { get; set; }
        public decimal CurrentTotalIncentive { get; set; }
        public AuditDiffSummary VasRowDiff { get; set; } = new();
    }

    public sealed class OverLandSourceAuditResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int StationCount { get; set; }
        public int HistoricalOleRows { get; set; }
        public int CurrentOleRows { get; set; }
        public decimal HistoricalOleWeightTotal { get; set; }
        public decimal CurrentOleWeightTotal { get; set; }
        public int HistoricalRbiRows { get; set; }
        public int CurrentRbiRows { get; set; }
        public decimal HistoricalRbiFinalIncentiveTotal { get; set; }
        public decimal CurrentRbiFinalIncentiveTotal { get; set; }
        public AuditDiffSummary OleRowDiff { get; set; } = new();
        public AuditDiffSummary RbiRowDiff { get; set; } = new();
    }
}
