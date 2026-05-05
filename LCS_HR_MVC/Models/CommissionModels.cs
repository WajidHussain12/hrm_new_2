using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class EmployeeCommissionModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Route Code is required")]
        public string RouteCode { get; set; } = string.Empty;

        public string RouteDescription { get; set; } = string.Empty;

        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Commission Type is required")]
        public string CommType { get; set; } = string.Empty;

        [Required(ErrorMessage = "Quantity is required")]
        public decimal Quantity { get; set; }

        [Required(ErrorMessage = "Rate is required")]
        public decimal Rate { get; set; }

        public decimal Amount { get; set; }

        [StringLength(45)]
        public string Reason { get; set; } = string.Empty;
    }

    public class TagCommissionViewModel
    {
        [Required(ErrorMessage = "From City is required")]
        public string CityFrom { get; set; } = string.Empty;

        [Required(ErrorMessage = "To City is required")]
        public string CityTo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Route Codes are required")]
        public string RouteCodes { get; set; } = string.Empty; // Comma-separated route codes
    }
}
