using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class ShiftModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Name is required")]
        [StringLength(99)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Start Time is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "Start Time")]
        public string StartTime { get; set; } = string.Empty;

        [Required(ErrorMessage = "End Time is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "End Time")]
        public string EndTime { get; set; } = string.Empty;

        [Required(ErrorMessage = "Grace Time In is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "Grace Time In")]
        public string GraceTimeIn { get; set; } = string.Empty;

        [Required(ErrorMessage = "Grace Time Out is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "Grace Time Out")]
        public string GraceTimeOut { get; set; } = string.Empty;

        [Required(ErrorMessage = "Begin In is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "Begin In")]
        public string BeginIn { get; set; } = string.Empty;

        [Required(ErrorMessage = "End In is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "End In")]
        public string EndIn { get; set; } = string.Empty;

        [Required(ErrorMessage = "Begin Out is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "Begin Out")]
        public string BeginOut { get; set; } = string.Empty;

        [Required(ErrorMessage = "End Out is required")]
        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        [Display(Name = "End Out")]
        public string EndOut { get; set; } = string.Empty;

        [RegularExpression(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Invalid format (HH:mm)")]
        public string OverTime { get; set; } = "00:00";

        public bool NightShift { get; set; } = false;
    }
}