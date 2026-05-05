using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class EmployeeRouteCodeModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Employee Name is required")]
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Route Code is required")]
        public string RouteCode { get; set; } = string.Empty;

        public string RouteDescription { get; set; } = string.Empty;

        public string CityCode { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Location is required")]
        public int LocationId { get; set; }

        public string LocationName { get; set; } = string.Empty;

        public string CodeType { get; set; } = "0";

        public bool IsRBIExclude { get; set; }

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public string Comments { get; set; } = string.Empty;

        // Grid display only
        public string LeopardType { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
    }
}
