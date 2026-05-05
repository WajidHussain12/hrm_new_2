using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models.Settlement
{
    public class EmployeeTerminationModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Termination Date is required")]
        public DateTime? TerminationDate { get; set; }

        [Required(ErrorMessage = "Leaving Reason is required")]
        public string LeavingReason { get; set; } = string.Empty;

        [Required(ErrorMessage = "Settlement status is required")]
        public string Settlement { get; set; } = "N"; // Y or N

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class FinalSettlementModel
    {
        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;

        public int Month1 { get; set; }
        public int WorkingDays1 { get; set; }

        public int Month2 { get; set; }
        public int WorkingDays2 { get; set; }

        public DateTime? ResignDate { get; set; }
    }
}
