using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class EmployeeSalaryModel
    {
        public string ID { get; set; } = "Auto Generated";

        [Required(ErrorMessage = "Employee is required")]
        public string EmpNo { get; set; } = string.Empty;
        public string EmployeeDescription { get; set; } = string.Empty;

        [Required(ErrorMessage = "Allowance/Deduction Code is required")]
        public string ADCode { get; set; } = string.Empty;
        public string ADName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Amount is required")]
        public decimal Amount { get; set; }

        public string Comments { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public string CreatedBy { get; set; } = string.Empty;
    }

    public class EmployeeSalaryListViewModel
    {
        public IEnumerable<EmployeeSalaryModel> SalaryDetails { get; set; } = new List<EmployeeSalaryModel>();
        public EmployeeSalaryModel NewSalaryDetail { get; set; } = new EmployeeSalaryModel();
    }
}