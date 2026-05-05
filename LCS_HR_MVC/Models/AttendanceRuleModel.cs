using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class AttendanceRuleModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Leave Name is required")]
        [StringLength(45)]
        [RegularExpression(@"^[a-zA-Z\s',.]+$", ErrorMessage = "Only letters, spaces, and punctuation are allowed.")]
        public string LeaveName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Min Unit is required")]
        public decimal MinUnit { get; set; }

        [Required(ErrorMessage = "Unit is required")]
        public decimal Unit { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;
    }
}