using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class LoanTypeModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Full Name is required")]
        [StringLength(45)]
        [RegularExpression(@"^[a-zA-Z\s',.]+$", ErrorMessage = "Only letters, spaces, and punctuation are allowed.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Short Name is required")]
        [StringLength(10)]
        [RegularExpression(@"^[a-zA-Z\s',.]+$", ErrorMessage = "Only letters, spaces, and punctuation are allowed.")]
        public string ShortName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Comments are required")]
        [StringLength(45)]
        [RegularExpression(@"^[a-zA-Z\s',.]+$", ErrorMessage = "Only letters, spaces, and punctuation are allowed.")]
        public string Comments { get; set; } = string.Empty;
    }
}