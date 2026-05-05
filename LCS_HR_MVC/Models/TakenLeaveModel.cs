using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class TakenLeaveModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Employee Description is required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? LeaveFromDate { get; set; }

        [Required(ErrorMessage = "To Date is required")]
        public DateTime? LeaveToDate { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        // View only properties for the grid
        public DateTime? LeaveDate { get; set; } 
    }
}