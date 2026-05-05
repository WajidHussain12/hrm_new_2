using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class EmployeeTrainingModel
    {
        public string Code { get; set; } = "Auto Generated";

        public string Mode { get; set; } = "E"; // E = Employee Wise, D = Department Wise
        
        public string? DepartmentId { get; set; }

        public string? EmpNo { get; set; }
        public string? EmployeeDescription { get; set; }
        public string? EmployeeName { get; set; }

        [Required(ErrorMessage = "Training Name is required")]
        public string TrainingName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Reason is required")]
        public string Reason { get; set; } = string.Empty;

        [Required(ErrorMessage = "Institution Name is required")]
        public string InstitutionName { get; set; } = string.Empty;

        public string CountryCode { get; set; } = "00";
        public string CityCode { get; set; } = "00";

        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public decimal? Amount { get; set; }
        public string Comments { get; set; } = string.Empty;
    }

    public class EmployeeShowCauseModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        public string Type { get; set; } = "00";

        [Required(ErrorMessage = "Reason is required")]
        public string Reason { get; set; } = string.Empty;

        public DateTime? IssueDate { get; set; }
        
        public string Reply { get; set; } = string.Empty;
        
        public DateTime? ReplyDate { get; set; }

        public string Comments { get; set; } = string.Empty;

        public IFormFile? DocumentFile { get; set; }
        public bool HasFile { get; set; }
    }

    public class EmployeePromotionAwardModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        public string PAType { get; set; } = "00";

        [Required(ErrorMessage = "Reason is required")]
        public string ReasonForRecognition { get; set; } = string.Empty;

        public DateTime? AnnouncementDate { get; set; }
        public DateTime? FromDate { get; set; }

        public decimal? Amount { get; set; }
        public string Comments { get; set; } = string.Empty;

        public IFormFile? DocumentFile { get; set; }
        public bool HasFile { get; set; }
    }

    public class EmployeePartTimeModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public decimal? Amount { get; set; }
        public string Comments { get; set; } = string.Empty;
    }

    public class MultipleJobsApproveModel
    {
        public string Code { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Date is required")]
        public DateTime? Date { get; set; }

        public string Comments { get; set; } = string.Empty;
    }

    public class IncrementModel
    {
        public string Code { get; set; } = "Auto Generated";

        public string Mode { get; set; } = "E"; // E = Employee, D = Department

        public string? DepartmentId { get; set; }
        public string? SubDepartmentId { get; set; }

        public string? EmpNo { get; set; }
        public string? EmployeeDescription { get; set; }
        public string? EmployeeName { get; set; }

        public string Type { get; set; } = "I"; // I = Increment, D = Decrement

        [Required(ErrorMessage = "Date is required")]
        public DateTime? FromDate { get; set; }

        public decimal CurrentSalary { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        public decimal Amount { get; set; }

        public string Comments { get; set; } = string.Empty;

        public IFormFile? BulkUploadFile { get; set; }
    }

    public class IncrementApprovalModel
    {
        public string Code { get; set; } = string.Empty;
        public string EmpNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime? FromDate { get; set; }
        public decimal Amount { get; set; }
        public string Comments { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;

        // Form binding
        public int SelectedStatusId { get; set; } = 1;
    }

    public class IncrementApprovalViewModel
    {
        public List<IncrementApprovalModel> Increments { get; set; } = new List<IncrementApprovalModel>();
        public string CityCode { get; set; } = string.Empty;
        public string DepartmentId { get; set; } = string.Empty;
        public int GlobalStatusId { get; set; } = 1;
    }

    public class IncrementApprovalPreviewModel
    {
        public string Code { get; set; } = string.Empty;
        public string EmpNo { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int SelectedStatusId { get; set; }
        public DateTime? FromDate { get; set; }
        public decimal IncrementAmount { get; set; }
        public string SalaryCode { get; set; } = string.Empty;
        public decimal FixGrossBefore { get; set; }
        public decimal FixGrossAfter { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal HouseRent { get; set; }
        public decimal Medical { get; set; }
        public decimal Utility { get; set; }
        public decimal SalaryAmountToPersist { get; set; }
        public int ClosedFixedAllowanceRows { get; set; }
        public int InsertedFixedAllowanceRows { get; set; }
        public int CreatedAllowanceMasterRows { get; set; }
        public string AllowanceCodes { get; set; } = string.Empty;
    }
}
