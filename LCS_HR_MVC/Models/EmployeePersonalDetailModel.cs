using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LCS_HR_MVC.Models
{
    public class EmployeePersonalDetailModel
    {
        public string EmpNo { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Name is required")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Father Name is required")]
        public string FatherName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Appointment Date is required")]
        public DateTime? AppointDate { get; set; }

        public DateTime? LeftDate { get; set; }
        public DateTime? BirthDate { get; set; }
        public DateTime? MarriageDate { get; set; }

        [Required(ErrorMessage = "Gender is required")]
        public string Gender { get; set; } = "M";

        [Required(ErrorMessage = "Marital Status is required")]
        public string MaritalStatus { get; set; } = "S";

        public string PermanentAddress { get; set; } = string.Empty;
        public string TemporaryAddress1 { get; set; } = string.Empty;
        public string TemporaryAddress2 { get; set; } = string.Empty;

        public string PCountryCode { get; set; } = "00";
        public string PCityCode { get; set; } = "00";

        public string CellContact1 { get; set; } = string.Empty;
        public string CellContact2 { get; set; } = string.Empty;
        public string EmergencyContact1 { get; set; } = string.Empty;
        public string EmergencyContact2 { get; set; } = string.Empty;

        public string Religion { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string NicNo { get; set; } = string.Empty;
        public string BloodGroup { get; set; } = string.Empty;
        public string NtnNo { get; set; } = string.Empty;
        public string EobiNo { get; set; } = string.Empty;
        public string MotherTongue { get; set; } = string.Empty;
        public string EmailAddress { get; set; } = string.Empty;
        public string MarkOfId { get; set; } = string.Empty;
        public string PassportNo { get; set; } = string.Empty;
        public string AttendanceCode { get; set; } = string.Empty;
        public string WalletNumber { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;

        public string EmployeeType { get; set; } = "00";
        public string JobTypeId { get; set; } = "0";
        public string ThirdPartyId { get; set; } = string.Empty;
        public string GenerateSalary { get; set; } = "Y";

        // Y=Confirmed, N=Probation, T=Temporary
        public string ProbationStatus { get; set; } = "N";
        public bool IsExecutive { get; set; }
        public string DualJobApprove { get; set; } = "NA";
        public string EmpStatus { get; set; } = "A";

        // Org structure
        public string CompanyId { get; set; } = "0";
        public string BusinessUnitId { get; set; } = "0";
        public string DivisionId { get; set; } = "0";
        public string DepartmentCode { get; set; } = "0";
        public string DesignationCode { get; set; } = "0";
        public int LocationId { get; set; }
        public string LocationName { get; set; } = string.Empty;

        // Replacement + ReportTo
        public string ReplacementEmpNo { get; set; } = string.Empty;
        public string ReplacementEmpName { get; set; } = string.Empty;
        public string ReportToEmpNo { get; set; } = string.Empty;
        public string ReportToEmpName { get; set; } = string.Empty;

        // Photo upload
        public IFormFile? PhotoFile { get; set; }
    }

    public class EmployeeListItemModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FatherName { get; set; } = string.Empty;
        public string NicNo { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;
        public string EmpStatus { get; set; } = string.Empty;
        public DateTime? AppointDate { get; set; }
    }
}
