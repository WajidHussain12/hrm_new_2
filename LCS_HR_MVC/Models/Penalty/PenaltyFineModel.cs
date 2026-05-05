using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models.Penalty
{
    public class PenaltyFineModel
    {
        public string ID { get; set; } = "Auto Generated";

        public string Mode { get; set; } = "E"; // E = Employee Wise, D = Department Wise

        public string? DivisionId { get; set; }
        public string? DepartmentId { get; set; }
        public string? SubDepartmentId { get; set; }

        public string? EmpNo { get; set; }
        public string? EmployeeDescription { get; set; }
        public string? EmployeeName { get; set; }

        [Required(ErrorMessage = "Penalty Type is required")]
        public string PenaltyType { get; set; } = string.Empty;

        public string PenaltyTypeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Fine Date is required")]
        public DateTime? FineDate { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Reason is required")]
        [StringLength(45)]
        public string Reason { get; set; } = string.Empty;

        public string? CreatorName { get; set; }

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class BulkPenaltyDeleteModel
    {
        public string CreatedDate { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public int RecordsCount { get; set; }
    }
}
