using System.Collections.Generic;

namespace LCS_HR_MVC.Models.Payroll
{
    public sealed class AuditDiffSummary
    {
        public int HistoricalOnlyCount { get; set; }
        public int GeneratedOnlyCount { get; set; }
        public int ValueMismatchCount { get; set; }
        public List<string> HistoricalOnlySamples { get; set; } = new();
        public List<string> GeneratedOnlySamples { get; set; } = new();
        public List<string> ValueMismatchSamples { get; set; } = new();
    }
}
