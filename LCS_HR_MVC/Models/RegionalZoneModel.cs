using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class RegionalZoneModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Full Name is required")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Short Name is required")]
        public string ShortName { get; set; } = string.Empty;
    }
}