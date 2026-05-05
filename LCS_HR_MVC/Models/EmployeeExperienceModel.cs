using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class EmployeeExperienceModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmpName { get; set; } = string.Empty;
        public int Sno { get; set; }

        [Required(ErrorMessage = "Organization Name is required")]
        public string OrganizationName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Designation is required")]
        public string Designation { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        [Required(ErrorMessage = "To Date is required")]
        public DateTime? ToDate { get; set; }

        public string ReasonForLeaving { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
    }

    public class EmployeeEducationModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmpName { get; set; } = string.Empty;
        public int Sno { get; set; }

        [Required(ErrorMessage = "Degree Title is required")]
        public string DegreeTitle { get; set; } = string.Empty;

        public string Area { get; set; } = string.Empty;

        [Required(ErrorMessage = "Institution Name is required")]
        public string InstitutionName { get; set; } = string.Empty;

        public string InstitutionType { get; set; } = string.Empty;
        public string CountryCode { get; set; } = "00";
        public string CityCode { get; set; } = "00";

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        [Required(ErrorMessage = "To Date is required")]
        public DateTime? ToDate { get; set; }

        public string Grade { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
    }

    public class EmployeeMedicalHistoryModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmpName { get; set; } = string.Empty;
        public int Sno { get; set; }

        [Required(ErrorMessage = "Disease Name is required")]
        public string DiseaseName { get; set; } = string.Empty;

        public string DiagnosticDetail { get; set; } = string.Empty;

        [Required(ErrorMessage = "From Date is required")]
        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }
        public string HospitalName { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string TreatmentDetail { get; set; } = string.Empty;
        public string CurrentSituation { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
    }

    public class MedicalSurveyFamilyMember
    {
        public string Name { get; set; } = string.Empty;
        public string Relation { get; set; } = string.Empty;
        public string Nic { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? Dob { get; set; }
    }

    public class EmployeeMedicalSurveyModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmpName { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public string NicNo { get; set; } = string.Empty;
        public string AppointDate { get; set; } = string.Empty;
        public string Cell { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string MaritalStatus { get; set; } = string.Empty;
        public string RequiredPolicyFor { get; set; } = string.Empty;
        public List<MedicalSurveyFamilyMember> FamilyMembers { get; set; } = new List<MedicalSurveyFamilyMember>();
    }
}
