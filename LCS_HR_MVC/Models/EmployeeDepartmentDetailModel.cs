using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    public class EmployeeDepartmentDetailModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Employee Description is required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = "00";

        [Required(ErrorMessage = "Division is required")]
        public string BUID { get; set; } = "00";

        [Required(ErrorMessage = "Department is required")]
        public string ParentDeptId { get; set; } = "00";

        [Required(ErrorMessage = "Sub-Department is required")]
        public string SubDeptId { get; set; } = "00";

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        // View-only properties
        public string EmployeeName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
    }
}