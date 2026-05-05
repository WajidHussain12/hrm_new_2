using System;
using System.Collections.Generic;

namespace LCS_HR_MVC.Models.Settlement
{
    public class FinalSettlementPreviewResult
    {
        public string EmpNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public DateTime ResignDate { get; set; }
        public bool RollbackIntegrityPreserved { get; set; }
        public List<FinalSettlementMonthPreview> Months { get; set; } = new();
    }

    public class FinalSettlementMonthPreview
    {
        public int SalaryYear { get; set; }
        public int SalaryMonth { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public string AttendanceSource { get; set; } = string.Empty;
        public int InputWorkingDays { get; set; }
        public int WorkedDays { get; set; }
        public int AbsentDays { get; set; }
        public int RuleAbsentDays { get; set; }
        public int FuelDays { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }
        public decimal Loan { get; set; }
        public decimal Advance { get; set; }
        public decimal Tax { get; set; }
        public decimal AbsentAmount { get; set; }
        public decimal FuelAmount { get; set; }
        public decimal AmountBank { get; set; }
        public decimal AmountCash { get; set; }
        public string PaymentMode { get; set; } = string.Empty;
        public decimal GrossPay { get; set; }
        public decimal TotalDeduction { get; set; }
        public decimal NetPay { get; set; }
        public int DetailRowsGenerated { get; set; }
        public int LoanDeductionRowsInserted { get; set; }
        public bool ExistingSalaryRowDetected { get; set; }
    }
}
