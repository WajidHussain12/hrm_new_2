using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class ProvinceModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Country is required")]
        public string CountryCode { get; set; } = string.Empty;

        public string CountryName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full Name is required")]
        [StringLength(99)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Short Name is required")]
        [StringLength(45)]
        public string ShortName { get; set; } = string.Empty;
    }
}