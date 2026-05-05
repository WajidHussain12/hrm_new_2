using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class AttendanceProcessViewModel
    {
        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;
    }
}
