using System;
using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class BulkAttendanceAdjustmentModel
    {
        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmployeeDescription { get; set; } = string.Empty;

        [Required(ErrorMessage = "Year is required")]
        public int Year { get; set; } = DateTime.Now.Year;

        [Required(ErrorMessage = "Month is required")]
        public int Month { get; set; } = DateTime.Now.Month;

        public bool AutoAdjustment { get; set; }
        
        public string AdjustmentType { get; set; } = "A"; // 'A' or 'RA'
    }

    public class BulkAttendanceAdjustmentGridRow
    {
        public DateTime Date { get; set; }
        public bool IsSelected { get; set; }
        public string AdjustType { get; set; } = "A";
    }
}
