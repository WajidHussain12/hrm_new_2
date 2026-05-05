using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class EmployeeBankDetailModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Employee Description is required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        [Required(ErrorMessage = "Account No is required")]
        [StringLength(30)]
        public string AccountNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bank Name is required")]
        public string BankName { get; set; } = "0";

        [StringLength(10)]
        public string BranchCode { get; set; } = string.Empty;

        [StringLength(45)]
        public string BankLocation { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        // View-only properties
        public string EmployeeName { get; set; } = string.Empty;

        public IFormFile? BulkUploadFile { get; set; }
    }
}