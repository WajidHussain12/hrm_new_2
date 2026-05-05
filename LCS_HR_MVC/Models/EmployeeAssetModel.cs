using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class EmployeeAssetModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Asset Code is required")]
        public string AssetCode { get; set; } = string.Empty;

        public string AssetDescription { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public string Remarks { get; set; } = string.Empty;
    }
}