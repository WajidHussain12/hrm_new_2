using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class AssetStructureModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Description is required!")]
        [StringLength(99)]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Property 1 is required!")]
        [StringLength(50)]
        public string Prop1 { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Prop2 { get; set; }

        [StringLength(50)]
        public string? Prop3 { get; set; }

        [StringLength(50)]
        public string? Prop4 { get; set; }

        [StringLength(50)]
        public string? Prop5 { get; set; }

        [StringLength(50)]
        public string? Prop6 { get; set; }

        [StringLength(50)]
        public string? Prop7 { get; set; }

        [StringLength(50)]
        public string? Prop8 { get; set; }

        [StringLength(50)]
        public string? Prop9 { get; set; }

        [StringLength(50)]
        public string? Prop10 { get; set; }
    }
}