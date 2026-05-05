using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class SalaryBankModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Location is required")]
        public string CityId { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bank Account is required")]
        public string BankGlCode { get; set; } = string.Empty;
        public string BankDesc { get; set; } = string.Empty;

        [Required(ErrorMessage = "Name is required")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Address is required")]
        [StringLength(100)]
        public string Address { get; set; } = string.Empty;
    }
}
