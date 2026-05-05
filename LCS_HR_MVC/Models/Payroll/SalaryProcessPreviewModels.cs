using System.Collections.Generic;

namespace LCS_HR_MVC.Models.Payroll
{
    public class SalaryProcessPreviewResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string CityCode { get; set; } = string.Empty;
        public int CommissionFilter { get; set; }
        public bool IsExecutive { get; set; }
        public int TotalPersistedHeaderRows { get; set; }
        public int TotalPersistedDetailRows { get; set; }
        public int TotalPersistedCarryForwardPenaltyRows { get; set; }
        public int TotalLoanDeductionRowsInserted { get; set; }
        public int TotalAcknowledgmentRowsInserted { get; set; }
        public int TotalDepartmentStatusRowsAffected { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
        public List<SalaryProcessPreviewDepartment> Departments { get; set; } = new();
    }

    public class SalaryProcessPreviewDepartment
    {
        public string DepartmentId { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public int CandidateRowCount { get; set; }
        public int DistinctEmployeeCount { get; set; }
        public int ExistingHeaderCount { get; set; }
        public int ExistingDetailCount { get; set; }
        public int ExistingLoanDeductionCount { get; set; }
        public int ExistingDepartmentStatusCount { get; set; }
        public int DeletedHeaderRows { get; set; }
        public int DeletedDetailRows { get; set; }
        public int DeletedLoanDeductionRows { get; set; }
        public int PreparedHeaderRows { get; set; }
        public int PreparedDetailRows { get; set; }
        public int PreparedCarryForwardPenaltyRows { get; set; }
        public int PreparedTaxableCashHeaderRows { get; set; }
        public int PreparedTaxableNonCashHeaderRows { get; set; }
        public int MonthWideCashHeaderRowsImpacted { get; set; }
        public int ExpectedTaxRowsAffected { get; set; }
        public int PersistedHeaderRows { get; set; }
        public int PersistedDetailRows { get; set; }
        public int PersistedCarryForwardPenaltyRows { get; set; }
        public int LoanDeductionRowsInserted { get; set; }
        public int TaxRowsAffected { get; set; }
        public int AcknowledgmentRowsInserted { get; set; }
        public int DepartmentStatusRowsAffected { get; set; }
    }
}
