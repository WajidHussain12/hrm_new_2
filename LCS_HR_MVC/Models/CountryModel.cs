using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class CountryModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Full Name is required")]
        [StringLength(99)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Short Name is required")]
        [StringLength(10)]
        public string ShortName { get; set; } = string.Empty;
    }
}