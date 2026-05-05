using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    public class CityModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Country is required")]
        public string CountryCode { get; set; } = string.Empty;

        public string CountryName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Province is required")]
        public string ProvinceCode { get; set; } = string.Empty;

        public string ProvinceName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Zone is required")]
        public string ZoneCode { get; set; } = string.Empty;

        public string ZoneName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Full Name is required")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Short Name is required")]
        [StringLength(2)]
        public string ShortName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Station ID is required")]
        [StringLength(5)]
        public string StationId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Branch ID is required")]
        [StringLength(5)]
        public string BranchId { get; set; } = string.Empty;

        public int ExtraFixedDays { get; set; } = 0;
    }
}