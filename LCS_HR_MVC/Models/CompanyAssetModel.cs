using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace LCS_HR_MVC.Models
{
    public class CompanyAssetModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Name is required")]
        [StringLength(45)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Type is required")]
        public string Type { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string? Prop1 { get; set; }
        public string? Prop2 { get; set; }
        public string? Prop3 { get; set; }
        public string? Prop4 { get; set; }
        public string? Prop5 { get; set; }
        public string? Prop6 { get; set; }
        public string? Prop7 { get; set; }
        public string? Prop8 { get; set; }
        public string? Prop9 { get; set; }
        public string? Prop10 { get; set; }

        public List<string> DynamicLabels { get; set; } = new List<string>();
    }
}