using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class LoanDeductionModel
    {
        public string Code { get; set; } = "Auto Generated"; // LDed_No

        [Required(ErrorMessage = "Loan Disbursed Code is required")]
        public string LoanDisbursedCode { get; set; } = string.Empty; // LD_No

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Loan Code is required")]
        public string LoanCode { get; set; } = string.Empty;

        public string LoanDescription { get; set; } = string.Empty;
        public string LoanName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Deduction Date is required")]
        public DateTime? DeductionDate { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        public decimal DisbursedAmount { get; set; }

        [Required(ErrorMessage = "Deduction Amount is required")]
        public decimal DeductionAmount { get; set; }

        public decimal Balance { get; set; }

        public IFormFile? BulkUploadFile { get; set; }
    }
}
