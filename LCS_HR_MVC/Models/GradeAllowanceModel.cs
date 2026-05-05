using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    public class GradeAllowanceModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Full Name is required!")]
        [StringLength(45)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(10)]
        public string ShortName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Type is required!")]
        public string Type { get; set; } = string.Empty; // 'A' or 'D'

        [Required(ErrorMessage = "GLCODE is required!")]
        [StringLength(5)]
        [RegularExpression(@"^\d+$", ErrorMessage = "Only Numbers allowed")]
        public string GlCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Percentage is required!")]
        public decimal PctAmount { get; set; } = 0;

        [Required(ErrorMessage = "Fixed Amount is required!")]
        public decimal FixAmount { get; set; } = 0;

        public string ExcludeAbsent { get; set; } = "M"; // Q, M, N

        public string ApplyTo { get; set; } = "NIL"; // All, DepartmentCode, NIL

        public string? DepartmentCode { get; set; }
        public string? DepartmentDescription { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;
    }
}