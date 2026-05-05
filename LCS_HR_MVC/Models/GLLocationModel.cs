using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class GLLocationModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Description is required")]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;
    }
}
