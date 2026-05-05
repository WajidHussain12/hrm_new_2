using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models.ViewModels
{
    public class ChangePasswordViewModel
    {
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Existing Password is Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Existing Password")]
        public string OldPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New Password is Required")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", ErrorMessage = "Password must be at least 8 characters long, contain 1 uppercase letter, 1 number, and 1 special character.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm New Password is Required")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare("NewPassword", ErrorMessage = "Your passwords do not match!")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public bool IsExpired { get; set; } = false;
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }
    }
}
