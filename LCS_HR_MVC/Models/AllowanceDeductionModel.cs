using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    public class AllowanceDeductionModel
    {
        public string ID { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Code Type is required")]
        public string CodeID { get; set; } = string.Empty;

        public string CodeType { get; set; } = string.Empty;

        [Required(ErrorMessage = "Description is required")]
        [StringLength(100)]
        public string Description { get; set; } = string.Empty;

        public string CreatedBy { get; set; } = string.Empty;
    }
}