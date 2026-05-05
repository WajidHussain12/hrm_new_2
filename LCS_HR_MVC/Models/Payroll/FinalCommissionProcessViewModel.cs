using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models.Payroll
{
    public class FinalCommissionProcessViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;
    }

    public class FinalCommissionProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ProcessedCount { get; set; }
    }
}
