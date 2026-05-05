using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public class CodCommissionProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ConsignmentRowsInserted { get; set; }
        public int ActivityRowsInserted { get; set; }
        public int ReturnShipmentRowsInserted { get; set; }
        public int CommissionRowsInserted { get; set; }
        public int StationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class CodCommissionPreviewResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int StationCount { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int SourceRowsRetrieved { get; set; }
        public int DepositDatesBackfilled { get; set; }
        public int ExistingConsignmentsSkipped { get; set; }
        public int ConsignmentRowsInserted { get; set; }
        public int ActivityRowsInserted { get; set; }
        public int DuplicateStatusRows { get; set; }
        public int RemarksUpdatedRows { get; set; }
        public int ReturnShipmentRowsInserted { get; set; }
        public int CommissionRowsInserted { get; set; }
        public decimal GeneratedCodAmountTotal { get; set; }
        public decimal GeneratedBonusTotal { get; set; }
        public decimal GeneratedDeductionTotal { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
    }

    public class CodCommissionViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        public string ZoneId { get; set; } = "00";
        public string CityCode { get; set; } = "0";

        public List<SelectListItem> Years { get; set; } = new();
        public List<SelectListItem> Months { get; set; } = new();
        public List<SelectListItem> Zones { get; set; } = new();
        public List<SelectListItem> Cities { get; set; } = new();

        public int ConsignmentRowsInserted { get; set; }
        public int ActivityRowsInserted { get; set; }
        public int ReturnShipmentRowsInserted { get; set; }
        public int CommissionRowsInserted { get; set; }
        public int StationCount { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }
}
