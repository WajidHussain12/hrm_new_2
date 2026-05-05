using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class CommissionRateModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "City is required")]
        public string Citycode { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Required")]
        public decimal DOM_Cash { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal DOM_Credit { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal LCL_Cash { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal LCL_Credit { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal LCL_DLD { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal PMCL { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal INTL { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal Porter { get; set; }

        [Required(ErrorMessage = "Required")]
        public decimal COD { get; set; }
    }
}