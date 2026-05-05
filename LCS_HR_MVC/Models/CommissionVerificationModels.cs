using System;
using System.Collections.Generic;

namespace LCS_HR_MVC.Models
{
    // ── Filters ─────────────────────────────────────────────────────────────────

    public class CommissionVerificationFilter
    {
        public int    Year                { get; set; }
        public int    Month               { get; set; }
        public string EmpNoSearch         { get; set; } = "";
        public string EmpNameSearch       { get; set; } = "";
        public string CnSearch            { get; set; } = "";
        public string CityCode            { get; set; } = "";
        public string CommissionTypeFilter { get; set; } = "All"; // All|Cash|COD
        public string ProcessedFilter     { get; set; } = "All"; // All|Processed|Remaining
        public string DeliveryFilter      { get; set; } = "All"; // All|OnTime|Delayed (COD only)
    }

    // ── Flat CN row for default all-records view ─────────────────────────────────

    public class CommissionFlatRow
    {
        public DateTime? CnDate          { get; set; }
        public string    EmpNo           { get; set; } = "";
        public string    EmpName         { get; set; } = "";
        public string    RouteCode       { get; set; } = "";
        public string    CityCode        { get; set; } = "";
        public string    CityName        { get; set; } = "";
        public string    CnNumber        { get; set; } = "";
        public string    CommissionType  { get; set; } = ""; // "Cash" | "COD"
        public int?      ShipmentId      { get; set; }
        public string?   Criteria        { get; set; }
        public DateTime? DeliveryDate    { get; set; }
        public int?      DateDif         { get; set; }
        public bool      IsOnTime        { get; set; } = true;
        public decimal   CommissionAmount { get; set; }
        public string?   Reason          { get; set; }
    }

    // ── Employee-level summary ──────────────────────────────────────────────────

    public class EmployeeCommissionSummary
    {
        public string EmpNo    { get; set; } = "";
        public string EmpName  { get; set; } = "";
        public string RouteCode { get; set; } = "";
        public string CityCode { get; set; } = "";
        public string CityName { get; set; } = "";

        // Cash (from hr_cash_consignments for the commission period)
        public int     CashCnCount         { get; set; }
        public decimal CashCommissionAmount { get; set; }

        // COD (from hr_cod_consignments for the month)
        public int     CodCnCount      { get; set; }
        public int     CodOnTimeCount  { get; set; }
        public int     CodDelayedCount { get; set; }
        public decimal CodCommissionAmount { get; set; }

        // Processed status (from hr_commissionprocess)
        public bool     IsCommissionProcessed  { get; set; }
        public decimal? ProcessedTotalAmount   { get; set; }

        // Missing CNs (commission = 0 in source tables)
        public int MissingCnCount { get; set; }

        // Computed
        public int     TotalCnCount        => CashCnCount + CodCnCount;
        public decimal TotalRawCommission  => CashCommissionAmount + CodCommissionAmount;
        public decimal ProcessedDifference =>
            IsCommissionProcessed ? (ProcessedTotalAmount ?? 0) - TotalRawCommission : 0;
    }

    // ── Missing CN row (commission = 0, eligible for reprocess) ────────────────

    public class MissingCnRow
    {
        public string    CnNumber       { get; set; } = "";
        public string    CommissionType { get; set; } = ""; // "Cash" | "COD"
        public DateTime? CnDate         { get; set; }
        public DateTime? DeliveryDate   { get; set; }
        public decimal   BilledAmount   { get; set; }
        public string?   Reason         { get; set; }
        public string?   ShipmentId     { get; set; }
    }

    // ── CN/parcel-level row (employee detail view) ───────────────────────────────

    public class CnCommissionRow
    {
        public string    CnNumber        { get; set; } = "";
        public string    CommissionType  { get; set; } = ""; // "Cash" | "COD"
        public DateTime? CnDate          { get; set; }
        public DateTime? DeliveryDate    { get; set; }
        public int?      DateDif         { get; set; }
        public bool      IsOnTime        { get; set; } = true;
        public decimal   CommissionAmount { get; set; }
        public int?      ShipmentId      { get; set; }
        public string?   ShipmentLabel   { get; set; }
        public string?   Criteria        { get; set; }
        public string?   Reason          { get; set; }
    }

    // ── Processed commission row (from hr_commissionprocess, one row per employee) ──

    public class ProcessedCommissionRow
    {
        public string  EmpNo        { get; set; } = "";
        public string? EmpName      { get; set; }
        public string  CityCode     { get; set; } = "";
        public string? CityName     { get; set; }
        public int     Year         { get; set; }
        public int     Month        { get; set; }

        // Commission columns (grouped for display)
        public decimal Overnight    { get; set; }
        public decimal Cod          { get; set; }
        public decimal CodBonus     { get; set; }
        public decimal CodDeduction { get; set; }
        public decimal OleDelivery  { get; set; }
        public decimal Overland     { get; set; }
        public decimal Vas          { get; set; }
        public decimal OtherCash    { get; set; } // YB + Flayer + Detain + Prepaid + LoveLine

        // Derived
        public decimal CashTotal => Overnight + OtherCash;
        public decimal CodTotal  => Cod + CodBonus - CodDeduction;
        public decimal OleTotal  => OleDelivery + Overland;
        public decimal GrandTotal => CashTotal + CodTotal + OleTotal + Vas;
    }

    // ── Page ViewModel ───────────────────────────────────────────────────────────

    public class CommissionVerificationViewModel
    {
        public CommissionVerificationFilter     Filter           { get; set; } = new();
        public List<EmployeeCommissionSummary>  MatchedEmployees { get; set; } = new();
        public EmployeeCommissionSummary?       SelectedEmployee { get; set; }
        public List<CnCommissionRow>            CnDetails        { get; set; } = new();
        public List<int>                        AvailableYears   { get; set; } = new();
        public List<(string Code, string Name)> AvailableCities  { get; set; } = new();
        public bool    SearchPerformed { get; set; }
        public string? ErrorMessage   { get; set; }

        // Default all-records view (paginated flat list)
        public List<CommissionFlatRow> AllCommissionRows { get; set; } = new();
        public int  TotalAllCount { get; set; }
        public int  CurrentPage   { get; set; } = 1;
        public int  PageSize      { get; set; } = 50;
        public int  TotalPages    => PageSize > 0 ? (int)Math.Ceiling((double)TotalAllCount / PageSize) : 0;
        public bool IsDefaultView { get; set; } = true;

        // Commission period dates (for display)
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo   { get; set; }

        // Processed commission section (from hr_commissionprocess)
        public List<ProcessedCommissionRow> ProcessedRows      { get; set; } = new();
        public int  TotalProcessedCount { get; set; }
        public int  ProcessedPage       { get; set; } = 1;
        public int  TotalProcessedPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalProcessedCount / PageSize) : 0;

        // Missing CNs (commission = 0 in source tables)
        public List<MissingCnRow> MissingCns { get; set; } = new();

        // Derived helpers for detail view
        public List<CnCommissionRow> CashCns => CnDetails.FindAll(r => r.CommissionType == "Cash");
        public List<CnCommissionRow> CodCns  => CnDetails.FindAll(r => r.CommissionType == "COD");
    }
}
