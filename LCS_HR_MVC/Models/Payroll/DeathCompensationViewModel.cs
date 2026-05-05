using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Payroll
{
    public class DeathCompensationProcessedEmployee
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string DepartmentId { get; set; } = string.Empty;
        public decimal BasicSalary { get; set; }
        public decimal Loan { get; set; }
        public decimal Advance { get; set; }
        public decimal TotalDeduction { get; set; }
        public decimal NetPay { get; set; }
        public string PaymentMode { get; set; } = string.Empty;
        public string VoucherStatus { get; set; } = string.Empty;
    }

    public class DeathCompensationProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int PayslipCount { get; set; }
        public int VoucherStatusInserted { get; set; }
        public int VoucherStatusUpdated { get; set; }
        public List<DeathCompensationProcessedEmployee> ProcessedEmployees { get; set; } = new();
    }

    public class DeathCompensationViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = System.DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = System.DateTime.Now.Month;

        public string ZoneId { get; set; } = "00";
        public string CityCode { get; set; } = "0";

        public List<SelectListItem> Years { get; set; } = new();
        public List<SelectListItem> Months { get; set; } = new();
        public List<SelectListItem> Zones { get; set; } = new();
        public List<SelectListItem> Cities { get; set; } = new();

        public int GeneratedCount { get; set; }
        public int VoucherStatusInserted { get; set; }
        public int VoucherStatusUpdated { get; set; }
        public List<DeathCompensationProcessedEmployee> ProcessedEmployees { get; set; } = new();
    }
}
