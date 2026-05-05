using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class CommissionEligibilityModel
    {
        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Employee Description is required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        public bool OLE_Dispatch_Proper { get; set; }
        public bool OLE_Transit_Dispatch { get; set; }
        public bool OLE_Delivery_OPS { get; set; }
    }

    public class CommissionEligibilityListModel
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmpName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string IsEligible { get; set; } = string.Empty;
    }
}