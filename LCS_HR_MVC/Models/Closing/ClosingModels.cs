using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models.Closing
{
    public class CloseProcessModel
    {
        public string Code { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;
        
        public string CityName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Zone is required")]
        public string ZoneCode { get; set; } = string.Empty;

        public string ZoneName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        public DateTime? UpdatedDate { get; set; }
    }

    public class UnlockSalaryViewModel
    {
        public string ZoneCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;
    }

    public class CommissionUnlockViewModel
    {
        public string ZoneCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        public int CommissionType { get; set; } = 0; // 0=Commission Process, 1=OLE, 2=Return COD
    }
}
