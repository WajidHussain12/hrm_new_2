using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class EmployeePayStructureModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Employee Description is required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        public bool SalaryFlag { get; set; }
        public bool CommissionFlag { get; set; }
        public bool FuelFlag { get; set; }

        [Required(ErrorMessage = "Date is required")]
        public DateTime? PayStrucDate { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        // View-only properties
        public string EmployeeName { get; set; } = string.Empty;
    }
}