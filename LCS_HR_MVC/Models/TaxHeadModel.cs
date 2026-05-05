using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class TaxHeadModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Tax Year is required")]
        [Range(2000, 2100, ErrorMessage = "Please enter a valid 4 digit year.")]
        public int? TaxYear { get; set; }

        [Required]
        [Display(Name = "From Date")]
        public DateTime? DateFrom { get; set; }

        [Required]
        [Display(Name = "To Date")]
        public DateTime? DateTo { get; set; }

        [Required(ErrorMessage = "Comments are required")]
        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        public List<TaxDetailModel> Details { get; set; } = new List<TaxDetailModel>();
    }

    public class TaxDetailModel
    {
        public int Sno { get; set; }
        public decimal LimitFrom { get; set; }
        public decimal LimitTo { get; set; }
        public decimal PctAmount { get; set; }
        public decimal FixAmount { get; set; }
        public string Comments { get; set; } = string.Empty;
    }
}
