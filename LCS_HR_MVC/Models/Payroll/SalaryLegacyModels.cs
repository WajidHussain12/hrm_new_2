namespace LCS_HR_MVC.Models.Payroll
{
    public class PayrollEmployeeType
    {
        public string EmpType { get; set; } = string.Empty;
        public bool IsFiler { get; set; }
    }

    public class TaxSlabList
    {
        public int sno { get; set; }
        public decimal LimitFrom { get; set; }
        public decimal LimitTo { get; set; }
        public decimal Pct_Amount { get; set; }
        public decimal Fix_Amount { get; set; }
    }

    public class SalaryProcessDetail
    {
        public int SalaryYear { get; set; }
        public int SalaryMonth { get; set; }
        public string Emp_No { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Deduction_Type { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string Allow_Code { get; set; } = string.Empty;
        public string glcode { get; set; } = string.Empty;
    }

    public class PenaltyFineList
    {
        public string FineDate { get; set; } = string.Empty;
        public string city_id { get; set; } = string.Empty;
        public string Emp_No { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class SalaryProcessHeader
    {
        public int SalaryYear { get; set; }
        public int SalaryMonth { get; set; }
        public string Emp_No { get; set; } = string.Empty;
        public string Dept { get; set; } = string.Empty;
        public decimal BasicSalary { get; set; }
        public int WorkedDays { get; set; }
        public decimal currentsalary { get; set; }
        public int AbsentDays { get; set; }
        public int RAbsentDays { get; set; }
        public decimal Absent_amt { get; set; }
        public decimal OT_Amount { get; set; }
        public decimal PT_Amount { get; set; }
        public decimal extra_hours { get; set; }
        public decimal extra_hours_amt { get; set; }
        public decimal extra_days { get; set; }
        public decimal extra_days_amt { get; set; }
        public decimal extra_fuel { get; set; }
        public decimal extra_fuel_amt { get; set; }
        public decimal Extra_amount { get; set; }
        public decimal Extra_Amount_Taxable { get; set; }
        public decimal Fuel_pday { get; set; }
        public int Fuel_days { get; set; }
        public decimal Fuel_Amount { get; set; }
        public decimal Fuel_Card_Usage { get; set; }
        public decimal FuelCard_Qty_Usage { get; set; }
        public decimal CommAmount { get; set; }
        public decimal CODKPIBonus { get; set; }
        public decimal CODKPIDeduction { get; set; }
        public decimal Allowances { get; set; }
        public decimal deductions { get; set; }
        public decimal Loan { get; set; }
        public decimal loan_balance { get; set; }
        public decimal Advance { get; set; }
        public decimal Tax { get; set; }
        public decimal GrossPay { get; set; }
        public decimal Total_Deduction { get; set; }
        public decimal NetPay { get; set; }
        public decimal CashPayment { get; set; }
        public decimal amount_bank { get; set; }
        public decimal amount_cash { get; set; }
        public string Payment_Mode { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
        public int createdStation { get; set; }
        public int CreatedLocation { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal SalaryAdjustmentExtraFixed { get; set; }
        public decimal TotalFixedGross { get; set; }
        public decimal NewBasic { get; set; }
        public decimal FixGross { get; set; }
    }
}
