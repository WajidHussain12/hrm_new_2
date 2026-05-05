using System.ComponentModel.DataAnnotations;

namespace LCS_HR_MVC.Models
{
    public class AttendanceAdjustmentModel
    {
        [Required(ErrorMessage = "Employee No is required")]
        public string EmpNo { get; set; } = string.Empty;

        public string EmpName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Adjustment Date is required")]
        public DateTime? AdjustmentDate { get; set; }

        // Original date stored as hidden field for update (composite PK may change on date edit)
        public string OriginalDate { get; set; } = string.Empty;

        [Required(ErrorMessage = "Adjustment Type is required")]
        public string AdjustmentType { get; set; } = "A";

        public string Reason { get; set; } = string.Empty;

        // Derived — not stored separately
        public int Year => AdjustmentDate?.Year ?? 0;
        public int Month => AdjustmentDate?.Month ?? 0;
    }

    public class BulkPresentMarkModel
    {
        public int Year { get; set; } = DateTime.Now.Year;
        public int Month { get; set; } = DateTime.Now.Month;
        public bool IsDateWise { get; set; } = false;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public IFormFile? File { get; set; }
    }

    public class BulkAbsentMarkModel
    {
        public int Year { get; set; } = DateTime.Now.Year;
        public int Month { get; set; } = DateTime.Now.Month;
        public IFormFile? File { get; set; }
    }

    public class BulkMarkResultItem
    {
        public string EmpNo { get; set; } = string.Empty;
        public string AdjDate { get; set; } = string.Empty;
    }
}
