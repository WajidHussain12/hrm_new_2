using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class RouteModel
    {
        [Required(ErrorMessage = "Route Code is required")]
        [StringLength(5)]
        [RegularExpression(@"^[0-9]+$", ErrorMessage = "Route Code must be numeric")]
        public string RouteCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "City is required")]
        public string CityCode { get; set; } = string.Empty;
        
        public string CityName { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        [Required(ErrorMessage = "Description is required")]
        [StringLength(45)]
        public string Description { get; set; } = string.Empty;

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        public bool IsPorter { get; set; } = false;

        public List<RouteDetailModel> Details { get; set; } = new List<RouteDetailModel>();
    }

    public class RouteDetailModel
    {
        [Required(ErrorMessage = "Area Name is required")]
        [StringLength(45)]
        public string AreaName { get; set; } = string.Empty;

        public string AddressType { get; set; } = "Block"; // Block, Street, Mohalla

        [StringLength(100)]
        public string Description { get; set; } = string.Empty;

        [StringLength(20)]
        public string PostalCode { get; set; } = "0";

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;
    }
}
