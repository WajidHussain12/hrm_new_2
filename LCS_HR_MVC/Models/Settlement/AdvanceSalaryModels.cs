using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models.Settlement
{
    public class AdvanceSalaryModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Amount is required")]
        public decimal Amount { get; set; }

        public string Status { get; set; } = "P";
        public string CreatorName { get; set; } = string.Empty;

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class AdvanceSalaryApprovalViewModel
    {
        public List<AdvanceSalaryModel> Requests { get; set; } = new List<AdvanceSalaryModel>();
        public List<string> SelectedCodes { get; set; } = new List<string>();
        public string ApprovalStatus { get; set; } = "P";
        public string SortBy { get; set; } = "Code"; // Code or Status
    }
}
