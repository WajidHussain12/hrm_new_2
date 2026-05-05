using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models.Payroll
{
    public class SingleEmployeeCommissionViewModel
    {
        [Required(ErrorMessage = "Employee number is required")]
        public string EmpNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;
    }

    public class SingleEmployeeCommissionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string EmpNo { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public int RowsInserted { get; set; }
        public int AdjustmentsInserted { get; set; }
    }
}
