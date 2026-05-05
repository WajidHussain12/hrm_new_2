using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    public class UserAdminModel
    {
        public string UserID { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Location is Required")]
        public string LocationID { get; set; } = string.Empty;

        [Required(ErrorMessage = "Location Description is Required")]
        public string LocationDescription { get; set; } = string.Empty;

        [Required(ErrorMessage = "User Role is Required")]
        public string UserRole { get; set; } = string.Empty;

        [Required(ErrorMessage = "Expiry Date is Required")]
        [Display(Name = "Expiry Date")]
        public DateTime ExpiryDate { get; set; } = DateTime.Now.AddYears(1);

        [Required(ErrorMessage = "User Name is Required")]
        [StringLength(20)]
        [Display(Name = "User Name")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full Name is Required")]
        [StringLength(50)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Job Description is Required")]
        [StringLength(30)]
        [Display(Name = "Job Description")]
        public string JobDescription { get; set; } = string.Empty;

        public bool Active { get; set; } = true;

        public string? RoleDescription { get; set; }

        [StringLength(10)]
        public string? Password { get; set; } 
        
        [Required(ErrorMessage = "Amount Limit is Required")]
        [Display(Name = "Amount Limit")]
        public decimal AmtLimit { get; set; } = 0;

        public IFormFile? SignatureFile { get; set; }
        
        public string? SignatureBase64 { get; set; }

        public IEnumerable<SelectListItem> RolesList { get; set; } = new List<SelectListItem>();
    }
}
