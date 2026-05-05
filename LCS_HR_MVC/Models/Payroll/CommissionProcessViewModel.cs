using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models.Payroll
{
    public class CommissionProcessViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = System.DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = System.DateTime.Now.Month;

        [Required(ErrorMessage = "Zone is required")]
        public string ZoneId { get; set; } = string.Empty;

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;

        public bool BillingStatusConfirmed { get; set; }
        public bool AttendanceStatusConfirmed { get; set; }
        public bool AllCommissionTypesConfirmed { get; set; }
    }
}
