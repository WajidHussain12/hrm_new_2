using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class LeaveStructureModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Full Name is required!")]
        [StringLength(45)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Short Name is required!")]
        [StringLength(10)]
        public string ShortName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Total Leaves is required!")]
        [Range(0, 999, ErrorMessage = "Invalid value")]
        public int TotalLeaves { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;
    }
}