using System.Collections.Generic;

namespace LCS_HR_MVC.Models.Payroll
{
    public class CommissionProcessPreviewResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int EmployeeRouteCount { get; set; }
        public int CashRowsGenerated { get; set; }
        public int CommissionRowsInserted { get; set; }
        public int AdjustmentRowsInserted { get; set; }
        public int AcknowledgmentRowsInserted { get; set; }
        public decimal TotalCommissionAmount { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
        public List<CommissionProcessPreviewEmployee> Employees { get; set; } = new();
    }

    public class CommissionProcessPreviewEmployee
    {
        public string EmpNo { get; set; } = string.Empty;
        public string? RouteCode { get; set; }
        public int WorkingDays { get; set; }
        public bool IsEligible { get; set; }
        public decimal TotalCommission { get; set; }
        public decimal AdjustmentAmount { get; set; }
    }
}
