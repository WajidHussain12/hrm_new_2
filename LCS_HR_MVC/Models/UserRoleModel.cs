using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class UserRoleModel
    {
        public string RoleID { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Role Description is Required")]
        [RegularExpression(@"^[a-zA-Z\s'.]+$", ErrorMessage = "Only letters, spaces, quotes, and dots are allowed.")]
        public string Description { get; set; } = string.Empty;
        
        [MaxLength(300, ErrorMessage = "Remarks cannot exceed 300 characters.")]
        public string? Remarks { get; set; }
    }
}
