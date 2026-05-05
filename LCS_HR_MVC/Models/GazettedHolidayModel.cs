using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class GazettedHolidayModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        [Required(ErrorMessage = "To Date is required")]
        public DateTime? ToDate { get; set; }

        [Required(ErrorMessage = "Reason is required")]
        [StringLength(100)]
        public string Reason { get; set; } = string.Empty;

        public bool IsAllLocations { get; set; } = true;

        public string LocationID { get; set; } = string.Empty;
        
        public string LocationDescription { get; set; } = string.Empty;

        public int Days { get; set; }
        public string DisplayLocation { get; set; } = string.Empty;
    }
}