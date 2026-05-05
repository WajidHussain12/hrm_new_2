using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    public class DepartmentModel
    {
        public string SDID { get; set; } = "Auto Generated";
        public string PDID { get; set; } = string.Empty;

        public string PdeptName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full Name is required")]
        [StringLength(99)]
        public string SdeptName { get; set; } = string.Empty;

        public string CourierDept { get; set; } = "N";

        [Required(ErrorMessage = "Short Name is required")]
        [StringLength(10)]
        public string ShortSDname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Company is required")]
        public string CID { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;

        [Required(ErrorMessage = "Division is required")]
        public string BUID { get; set; } = string.Empty;
        public string Bunit { get; set; } = string.Empty;

        public bool IsParent { get; set; } = false;
        public bool IsCourierDept { get; set; } = false;
    }
}