using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class LoanRequestModel
    {
        public string Code { get; set; } = "Auto Generated"; // Maps to LR_No

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Employee Description is required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Loan Code is required")]
        public string LoanCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Loan Type is required")]
        public string LoanDescription { get; set; } = string.Empty;

        public string LoanName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Request Date is required")]
        public DateTime? RequestDate { get; set; }

        [Required(ErrorMessage = "Reason is required")]
        [StringLength(45)]
        public string Reason { get; set; } = string.Empty;

        [Required(ErrorMessage = "Amount is required")]
        public decimal RequestAmount { get; set; }

        [Required(ErrorMessage = "Installments is required")]
        public int RequestInstallments { get; set; }

        [Required(ErrorMessage = "Start Date is required")]
        public DateTime? StartDate { get; set; }

        public string Status { get; set; } = "P";
        
        [StringLength(45)]
        public string? AppAuthPersonName { get; set; }

        public IFormFile? BulkUploadFile { get; set; }
    }
}