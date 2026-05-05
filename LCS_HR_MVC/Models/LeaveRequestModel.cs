using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class LeaveRequestModel
    {
        public string RequestNo { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Employee Description is required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        public string? EmployeeName { get; set; }

        [Required(ErrorMessage = "Leave Category is required")]
        public string LeaveCode { get; set; } = string.Empty;

        public string? LeaveCategoryName { get; set; }

        [Required(ErrorMessage = "Leave Type is required")]
        public string RuleCode { get; set; } = string.Empty;

        public string? LeaveTypeName { get; set; }

        [Required(ErrorMessage = "Request Date is required")]
        public DateTime? RequestDate { get; set; }

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? LeaveFromDate { get; set; }

        [Required(ErrorMessage = "To Date is required")]
        public DateTime? LeaveToDate { get; set; }

        [StringLength(45)]
        public string Reason { get; set; } = string.Empty;

        public string Status { get; set; } = "UP"; // UP = Under Process, A = Approved, R = Rejected

        public string? AppAuthPersonName { get; set; }

        // Bulk upload file
        public IFormFile? BulkUploadFile { get; set; }
    }
}