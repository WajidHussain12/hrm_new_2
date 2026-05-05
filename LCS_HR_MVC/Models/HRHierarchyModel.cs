using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class HRHierarchyModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CellNo { get; set; } = string.Empty;
        public string ReportTo { get; set; } = string.Empty;
        public string DeleteHirerchyEmpIDS { get; set; } = string.Empty;
    }

    public class HRHierarchyViewModel
    {
        [Required(ErrorMessage = "Division is required")]
        public string BUID { get; set; } = "00";

        [Required(ErrorMessage = "Department is required")]
        public string DepartmentID { get; set; } = "00";

        [Required(ErrorMessage = "Sub-Department is required")]
        public string SubDepartmentID { get; set; } = "00";

        [Required(ErrorMessage = "Employee Manager is required")]
        public string HODEmployeeCode { get; set; } = string.Empty;
        public string HODEmployeeDescription { get; set; } = string.Empty;

        public List<HRHierarchyModel> Employees { get; set; } = new List<HRHierarchyModel>();
    }
}
