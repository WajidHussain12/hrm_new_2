using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public sealed class ReturnCodCommissionProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ConsignmentRowsInserted { get; set; }
        public int CommissionRowsInserted { get; set; }
        public int ProcessRowsInserted { get; set; }
        public int StationCount { get; set; }
        public int LocationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int SourceRowsRetrieved { get; set; }
        public int GroupedRowsGenerated { get; set; }
        public decimal GeneratedProcessAmountTotal { get; set; }
    }

    public sealed class ReturnCodCommissionPreviewResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int StationCount { get; set; }
        public int LocationCount { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int SourceRowsRetrieved { get; set; }
        public int GroupedRowsGenerated { get; set; }
        public int ConsignmentRowsInserted { get; set; }
        public int CommissionRowsInserted { get; set; }
        public int ProcessRowsInserted { get; set; }
        public decimal GeneratedProcessAmountTotal { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
    }

    public sealed class ReturnCodCommissionViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        public string ZoneId { get; set; } = "00";
        public string CityCode { get; set; } = "00";
        public bool BillingStatus { get; set; }

        public List<SelectListItem> Years { get; set; } = new();
        public List<SelectListItem> Months { get; set; } = new();
        public List<SelectListItem> Zones { get; set; } = new();
        public List<SelectListItem> Cities { get; set; } = new();

        public int ConsignmentRowsInserted { get; set; }
        public int CommissionRowsInserted { get; set; }
        public int ProcessRowsInserted { get; set; }
        public int StationCount { get; set; }
        public int LocationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }
}
