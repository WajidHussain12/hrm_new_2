using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class LoanDisbursedModel
    {
        public string Code { get; set; } = "Auto Generated"; // LD_No

        [Required(ErrorMessage = "Loan Request Code is required")]
        public string LoanReqCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Loan Code is required")]
        public string LoanCode { get; set; } = string.Empty;

        public string LoanDescription { get; set; } = string.Empty;
        public string LoanName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Disbursed Date is required")]
        public DateTime? DisbursedDate { get; set; }

        [StringLength(45)]
        public string Reason { get; set; } = string.Empty;

        [Required(ErrorMessage = "Requested Amount is required")]
        public decimal RequestAmount { get; set; }

        [Required(ErrorMessage = "Disbursed Amount is required")]
        public decimal DisbursedAmount { get; set; }

        [Required(ErrorMessage = "Installments is required")]
        public int DeductionInstallments { get; set; }

        public DateTime? DeductionStartDate { get; set; }

        [StringLength(45)]
        public string? AppAuthPersonName { get; set; }

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class LoanRequestApprovalViewModel
    {
        public List<LoanRequestModel> Requests { get; set; } = new List<LoanRequestModel>();
        public List<string> SelectedRequestCodes { get; set; } = new List<string>();
        public string ApprovalStatus { get; set; } = "P";
    }
}
