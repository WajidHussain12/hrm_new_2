using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models.Payroll
{
    public class SalariesProcessViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = System.DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = System.DateTime.Now.Month;

        [Required(ErrorMessage = "Zone is required")]
        public string ZoneId { get; set; } = string.Empty;

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;

        public string DivisionId { get; set; } = string.Empty;

        public List<string> SelectedSubDepartments { get; set; } = new List<string>();

        public int CommissionFilter { get; set; } = 0; // 0=All, 1=Commission, 2=Non-Commission

        // Acknowledgements
        public bool BillingStatusConfirmed { get; set; }
        public bool AttendanceStatusConfirmed { get; set; }
        public bool CommissionStatusConfirmed { get; set; }
        public bool OneTimeActivityConfirmed { get; set; }
    }
}
