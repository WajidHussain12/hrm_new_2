using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public class SalaryProcessEmployeeOption
    {
        public string EmpNo { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class SalaryReprocessViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = System.DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = System.DateTime.Now.Month;

        public string ZoneId { get; set; } = "00";
        public string CityCode { get; set; } = "0";
        public string SubDepartmentId { get; set; } = "0";
        public List<string> SelectedEmployeeIds { get; set; } = new();

        public bool BillingStatusConfirmed { get; set; }
        public bool AttendanceStatusConfirmed { get; set; }
        public bool CommissionStatusConfirmed { get; set; }
        public bool OneTimeActivityConfirmed { get; set; }

        public List<SelectListItem> Years { get; set; } = new();
        public List<SelectListItem> Zones { get; set; } = new();
        public List<SelectListItem> Cities { get; set; } = new();
        public List<SelectListItem> SubDepartments { get; set; } = new();
        public List<SalaryProcessEmployeeOption> EmployeeOptions { get; set; } = new();
    }

    public class SalaryVouchersViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = System.DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = System.DateTime.Now.Month;

        public string ZoneId { get; set; } = "00";
        public string CityCode { get; set; } = "0";
        public string SubDepartmentId { get; set; } = "0";
        public List<string> SelectedEmployeeIds { get; set; } = new();

        public List<SelectListItem> Years { get; set; } = new();
        public List<SelectListItem> Zones { get; set; } = new();
        public List<SelectListItem> Cities { get; set; } = new();
        public List<SelectListItem> SubDepartments { get; set; } = new();
        public List<SalaryProcessEmployeeOption> EmployeeOptions { get; set; } = new();
    }
}
