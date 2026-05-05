using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class JobModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Full Name is required")]
        [StringLength(45)]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Short Name is required")]
        [StringLength(10)]
        public string ShortName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Grade is required")]
        [StringLength(3)]
        [RegularExpression(@"^[0-9]+$", ErrorMessage = "Only numbers allowed.")]
        public string Level { get; set; } = string.Empty;

        [Required(ErrorMessage = "Parent Department is required")]
        public string ParentDeptId { get; set; } = string.Empty;
        public string ParentDeptName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Sub Department is required")]
        public string SubDeptId { get; set; } = string.Empty;
        public string SubDeptName { get; set; } = string.Empty;

        public bool IsEligible { get; set; } = false;
    }
}
