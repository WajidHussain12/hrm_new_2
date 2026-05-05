using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    public class AllowanceDeductionDetailModel
    {
        public string ID { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Allowance Type is required!")]
        public string TypeID { get; set; } = string.Empty;

        public string TypeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "AD Code is required!")]
        [StringLength(10)]
        public string ADCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full Name is required!")]
        [StringLength(50)]
        public string FullName { get; set; } = string.Empty;

        public bool TaxFlag { get; set; }
        public bool OverTimeFlag { get; set; }
        public bool ExcludeAbsent { get; set; }
        public bool PaySlipVisible { get; set; }
        public bool IsActive { get; set; }

        [Required(ErrorMessage = "Payment Mode is required!")]
        public string PaymentMode { get; set; } = string.Empty;

        public string? RateID { get; set; }

        [StringLength(1000)]
        public string? Comments { get; set; }

        public string CreatedBy { get; set; } = string.Empty;
    }
}