using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class EmployeeOvertimeModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeDescription { get; set; } = string.Empty;

        [Required(ErrorMessage = "Date is required")]
        public DateTime? Date { get; set; }

        [Required(ErrorMessage = "Duration is required")]
        public decimal Duration { get; set; }

        [Required(ErrorMessage = "Unit is required")]
        public string Unit { get; set; } = string.Empty;

        [Required(ErrorMessage = "Total Amount is required")]
        public decimal TotalAmount { get; set; }

        [StringLength(45)]
        public string Reason { get; set; } = string.Empty;

        [Required(ErrorMessage = "Authorize Person is required")]
        [StringLength(45)]
        public string AppAuthPersonName { get; set; } = string.Empty;
        
        public string DurationUnitDisplay => $"{Duration} / {Unit}";
    }
}
