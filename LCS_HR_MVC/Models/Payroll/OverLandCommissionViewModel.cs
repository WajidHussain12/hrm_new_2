using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public sealed class OverLandCommissionProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int OleRowsInserted { get; set; }
        public int RbiRowsInserted { get; set; }
        public int ProcessRowsInserted { get; set; }
        public int StationCount { get; set; }
        public int LocationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public sealed class OverLandCommissionPreviewResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int StationCount { get; set; }
        public int LocationCount { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int OleSourceRowsRetrieved { get; set; }
        public int RbiSourceRowsRetrieved { get; set; }
        public int OleRowsInserted { get; set; }
        public int RbiRowsInserted { get; set; }
        public int ProcessRowsInserted { get; set; }
        public decimal GeneratedProcessAmountTotal { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
    }

    public sealed class OverLandCommissionAuditResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int StationCount { get; set; }
        public int LocationCount { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int HistoricalOleRows { get; set; }
        public int GeneratedOleRows { get; set; }
        public decimal HistoricalOleWeightTotal { get; set; }
        public decimal GeneratedOleWeightTotal { get; set; }
        public int HistoricalOleDuplicateGroups { get; set; }
        public int GeneratedOleDuplicateGroups { get; set; }
        public int HistoricalRbiRows { get; set; }
        public int GeneratedRbiRows { get; set; }
        public decimal HistoricalRbiFinalIncentiveTotal { get; set; }
        public decimal GeneratedRbiFinalIncentiveTotal { get; set; }
        public int HistoricalProcessRows { get; set; }
        public int GeneratedProcessRows { get; set; }
        public decimal HistoricalProcessAmountTotal { get; set; }
        public decimal GeneratedProcessAmountTotal { get; set; }
        public int HistoricalProcessDuplicateGroups { get; set; }
        public int GeneratedProcessDuplicateGroups { get; set; }
        public AuditDiffSummary OleRowDiff { get; set; } = new();
        public AuditDiffSummary RbiRowDiff { get; set; } = new();
        public AuditDiffSummary ProcessRowDiff { get; set; } = new();
        public bool HistoricalParityMatch { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
    }

    public sealed class OverLandCommissionViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        public string ZoneId { get; set; } = "00";
        public string CityCode { get; set; } = "0";
        public bool BillingStatus { get; set; }
        public bool AttendanceStatus { get; set; }

        public List<SelectListItem> Years { get; set; } = new();
        public List<SelectListItem> Months { get; set; } = new();
        public List<SelectListItem> Zones { get; set; } = new();
        public List<SelectListItem> Cities { get; set; } = new();

        public int OleRowsInserted { get; set; }
        public int RbiRowsInserted { get; set; }
        public int ProcessRowsInserted { get; set; }
        public int StationCount { get; set; }
        public int LocationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }
}
