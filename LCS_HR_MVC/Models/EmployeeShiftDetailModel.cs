using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class EmployeeShiftDetailModel
    {
        public string Id { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Employee Name is required")]
        public string EmpName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Shift is required")]
        public string ShiftCode { get; set; } = "0";

        public string ShiftName { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public string Comments { get; set; } = string.Empty;

        public string? OffDay { get; set; }

        public IFormFile? BulkUploadFile { get; set; }
    }
}
