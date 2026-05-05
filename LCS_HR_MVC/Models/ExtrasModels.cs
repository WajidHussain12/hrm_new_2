using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class EmployeeExtraModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Extra Type is required")]
        public int ExtraType { get; set; }

        public string ExtraTypeName { get; set; } = string.Empty;

        public int? ExtraSubType { get; set; }

        public string ExtraSubTypeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Value is required")]
        public decimal Value { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        public int Status { get; set; } = 1; // 1=Pending

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class EmployeeExtraFixedModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public int? ExtraType { get; set; } // Wait, old system uses 4 for Fixed Extra and sub type ID is Extra_TypeID
        
        [Required(ErrorMessage = "Extra Sub Type is required")]
        public int ExtraSubType { get; set; }

        public string ExtraSubTypeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Amount is required")]
        public decimal Amount { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class EmpADDetailsModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "AD Code is required")]
        public string ADCode { get; set; } = string.Empty;

        public string ADDescription { get; set; } = string.Empty;
        public string ADName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Effective From is required")]
        public DateTime? EffectiveFrom { get; set; }

        public DateTime? EffectiveTo { get; set; }

        public int ADYear { get; set; }

        [StringLength(45)]
        public string Comments { get; set; } = string.Empty;

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class ExtraHoursApprovalViewModel
    {
        public string? CityCode { get; set; }
        public string? DeptCode { get; set; }
        
        public List<EmployeeExtraModel> Extras { get; set; } = new List<EmployeeExtraModel>();
        public List<string> SelectedCodes { get; set; } = new List<string>();
        public int ApprovalStatus { get; set; } = 1;
    }
}
