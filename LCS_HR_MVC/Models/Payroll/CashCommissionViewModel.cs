using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public sealed class CashCommissionProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int CashSourceRowsRetrieved { get; set; }
        public int VasSourceRowsRetrieved { get; set; }
        public int CashRowsInserted { get; set; }
        public int VasRowsInserted { get; set; }
        public int StationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public sealed class CashCommissionPreviewResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int StationCount { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int CashSourceRowsRetrieved { get; set; }
        public int VasSourceRowsRetrieved { get; set; }
        public int CashRowsInserted { get; set; }
        public int VasRowsInserted { get; set; }
        public int AcknowledgmentRowsInserted { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
    }

    public sealed class CashCommissionAuditResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int StationCount { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int HistoricalCashRows { get; set; }
        public int GeneratedCashRows { get; set; }
        public int HistoricalCashDistinctCn { get; set; }
        public int GeneratedCashDistinctCn { get; set; }
        public int HistoricalCashDuplicateGroups { get; set; }
        public int GeneratedCashDuplicateGroups { get; set; }
        public decimal HistoricalCashTotalCommission { get; set; }
        public decimal GeneratedCashTotalCommission { get; set; }
        public int HistoricalVasRows { get; set; }
        public int GeneratedVasRows { get; set; }
        public int HistoricalVasDistinctCn { get; set; }
        public int GeneratedVasDistinctCn { get; set; }
        public int HistoricalVasDuplicateGroups { get; set; }
        public int GeneratedVasDuplicateGroups { get; set; }
        public decimal HistoricalVasTotalIncentive { get; set; }
        public decimal GeneratedVasTotalIncentive { get; set; }
        public AuditDiffSummary CashRowDiff { get; set; } = new();
        public AuditDiffSummary VasRowDiff { get; set; } = new();
        public bool HistoricalParityMatch { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
    }

    public sealed class CashCommissionViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        public string ZoneId { get; set; } = "00";
        public string CityCode { get; set; } = "0";
        public bool BillingStatus { get; set; }

        public List<SelectListItem> Years { get; set; } = new();
        public List<SelectListItem> Months { get; set; } = new();
        public List<SelectListItem> Zones { get; set; } = new();
        public List<SelectListItem> Cities { get; set; } = new();

        public int CashSourceRowsRetrieved { get; set; }
        public int VasSourceRowsRetrieved { get; set; }
        public int CashRowsInserted { get; set; }
        public int VasRowsInserted { get; set; }
        public int StationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }
}
