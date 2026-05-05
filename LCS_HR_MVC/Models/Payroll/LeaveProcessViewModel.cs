using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public class LeaveProcessViewModel
    {
        [Required(ErrorMessage = "Year is required.")]
        public int Year { get; set; } = DateTime.Now.Year;

        public string ZoneId { get; set; } = "00";

        public string CityCode { get; set; } = "00";

        public string Mode { get; set; } = "Department";

        public string DivisionId { get; set; } = "0";

        public string DepartmentId { get; set; } = "0";

        public string SubDepartmentId { get; set; } = "0";

        public string EmployeeDescription { get; set; } = string.Empty;

        public string EmployeeCode { get; set; } = string.Empty;

        public List<SelectListItem> Years { get; set; } = new();

        public List<SelectListItem> Zones { get; set; } = new();

        public List<SelectListItem> Cities { get; set; } = new();

        public List<SelectListItem> Divisions { get; set; } = new();

        public List<SelectListItem> Departments { get; set; } = new();

        public List<SelectListItem> SubDepartments { get; set; } = new();
    }
}
