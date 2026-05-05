using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace LCS_HR_MVC.Models
{
    // ═══════════════════════════════════════════════════════════════════════════
    // COMMISSION INVESTIGATION CENTER — MODELS
    // Commission period: 21st of prev month → 20th of current month
    // Server map:
    //   Server 6  = LHR_Billing   (172.16.0.6)  — Billing source data
    //   Server 7  = MIS            (172.16.0.7)  — MIS / Location / OLE data
    //   Server 10 = Main/Default   (172.16.0.10) — HR master + final commission
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Search / filter ───────────────────────────────────────────────────────

    public class CommissionInvestigationFilter
    {
        public int    Year  { get; set; }
        public int    Month { get; set; }
        public string EmpNo { get; set; } = "";
    }

    // ── Lightweight search result (Index page list) ───────────────────────────

    public class EmpInvestigationSearchRow
    {
        public string        EmpNo            { get; set; } = "";
        public string        Name             { get; set; } = "";
        public string?       Designation      { get; set; }
        public string?       Department       { get; set; }
        public string        EmpStatus        { get; set; } = ""; // A=Active L=Left
        public bool?         IsEligible       { get; set; }
        public string        CityCode         { get; set; } = "";
        public string?       CityName         { get; set; }
        public string?       StationId        { get; set; }
        public List<string>  RouteCodes       { get; set; } = new();
        public decimal?      FinalCommission  { get; set; }
        public bool          HasFinalRecord   { get; set; }
        /// <summary>Processed | Missing | Partial | NotEligible | Unknown</summary>
        public string        CommissionStatus { get; set; } = "Unknown";

        public string StatusBadgeClass => CommissionStatus switch {
            "Processed"   => "green",
            "Missing"     => "red",
            "Partial"     => "yellow",
            "NotEligible" => "slate",
            _             => "blue"
        };
        public string StatusIcon => CommissionStatus switch {
            "Processed"   => "fa-check-circle",
            "Missing"     => "fa-times-circle",
            "Partial"     => "fa-exclamation-circle",
            "NotEligible" => "fa-ban",
            _             => "fa-question-circle"
        };
        public bool IsActive => EmpStatus == "A";
    }

    // ── Route code detail ─────────────────────────────────────────────────────

    public class RouteCodeInfo
    {
        public string    RouteCode     { get; set; } = "";
        public string    CityCode      { get; set; } = "";
        public string?   CityName      { get; set; }
        public string?   StationId     { get; set; }
        public int?      LocationId    { get; set; }
        public DateTime? FromDate      { get; set; }
        public DateTime? ToDate        { get; set; }
        public int?      CodeType      { get; set; }
        /// <summary>Human-readable CodeType name from couriercodetype table</summary>
        public string?   CodeTypeName  { get; set; }
        public bool      RBIExclude    { get; set; }
        public string    FoundOn       { get; set; } = ""; // "Main" | "Billing" | "MIS"
        public bool      IsActive      => ToDate == null || ToDate.Value >= DateTime.Now;
        public string    CodeTypeLabel => CodeType.HasValue
            ? $"{CodeType} — {CodeTypeName ?? "Unknown"}"
            : "—";
    }

    // ── Eligibility breakdown per CommissionId/Category ──────────────────────

    public class EligibilityCategory
    {
        public int     CommissionId   { get; set; }
        /// <summary>Category name from couriercodetype (Ground Leopard / In House / Express Center)</summary>
        public string  CategoryName   { get; set; } = "";
        /// <summary>null = no record, true = eligible, false = not eligible</summary>
        public bool?   IsEligible     { get; set; }
        public string  StatusLabel    => IsEligible == true  ? "Eligible"
                                      : IsEligible == false ? "Not Eligible"
                                      : "No Record";
        public string  StatusClass    => IsEligible == true  ? "tw-text-emerald-600"
                                      : IsEligible == false ? "tw-text-red-600"
                                      : "tw-text-slate-400";
        public string  StatusIcon     => IsEligible == true  ? "fa-check-circle"
                                      : IsEligible == false ? "fa-times-circle"
                                      : "fa-minus-circle";
    }

    // ── Commission category applicable to employee based on CodeType ───────────

    public class CommissionCategoryInfo
    {
        public string  Category    { get; set; } = "";
        public string  Description { get; set; } = "";
        public string  RateDisplay { get; set; } = "";
        public string  Source      { get; set; } = ""; // table/column
        public string  Icon        { get; set; } = "fa-money-bill";
        public bool    IsApplicable { get; set; } = true;
    }

    // ── Attendance summary (from hr_employeeattendanceprocess, Server 10) ─────

    public class AttendanceSummary
    {
        public bool    IsProcessed  { get; set; }
        public int?    TotalDays    { get; set; }
        public int?    WorkedDays   { get; set; }
        public int?    AbsentDays   { get; set; }
        public int?    SundayDays   { get; set; }
        public int?    Adjustments  { get; set; }
        public string? Comments     { get; set; }

        /// <summary>Computed: TotalDays - SundayDays - AbsentDays + Adjustments</summary>
        public int? EffectiveDays =>
            (TotalDays.HasValue && SundayDays.HasValue && AbsentDays.HasValue)
            ? TotalDays.Value - SundayDays.Value - AbsentDays.Value + (Adjustments ?? 0)
            : null;
    }

    // ── Cash billing type breakdown (S10 has CASH+RetailCOD, S6 has CASH only) ─

    public class BillingTypeCount
    {
        public string  BillingType { get; set; } = "";
        public int     Count       { get; set; }
    }

    // ── Cash commission CN (from hr_cash_consignments, Server 10) ─────────────

    public class CashCommissionCnDetail
    {
        public string    CnNumber         { get; set; } = "";
        public DateTime? BillingDate      { get; set; }
        public int?      ShipmentTypeId   { get; set; }
        public string?   ShipmentLabel    { get; set; }
        public string?   BillingCategory  { get; set; }  // "CASH" or "Retail COD" from Billing_Type column
        public decimal   BilledAmount     { get; set; }
        public decimal   CommissionAmount { get; set; }
        public int?      RateId           { get; set; }
        public string?   Criteria         { get; set; }
        public string?   RouteCode        { get; set; }
        public string?   StationId        { get; set; }
    }

    // ── COD commission CN (from hr_cod_consignments, Server 10) ─────────────

    public class CodCommissionCnDetail
    {
        public string    CnNumber         { get; set; } = "";
        public DateTime? CourDate         { get; set; }
        public DateTime? DeliveryDate     { get; set; }
        public int?      DateDif          { get; set; }
        public bool      IsOnTime         { get; set; } = true;
        public decimal   CommissionAmount { get; set; }
        public string?   Reason           { get; set; }
        public string?   RouteCode        { get; set; }
    }

    // ── OLE commission row (from hr_olecommissionprocess, Server 7 MIS) ───────

    public class OleCommissionRow
    {
        public int?    GlLocationId  { get; set; }
        public string? CourierId     { get; set; } // varchar(6) — likely RouteCode
        public int?    RateId        { get; set; }
        public decimal OleCommission { get; set; }
        public string? RateTypeName  { get; set; }
    }

    // ── Return COD summary (from hr_codreturncommissionprocess, Server 10) ────

    public class ReturnCodSummary
    {
        public decimal Amount  { get; set; }
        public int     CnCount { get; set; }
        public int?    RateId  { get; set; }
    }

    // ── Raw billing CN (from lcs_billing.billing_details, Server 6) ──────────

    public class BillingCnInfo
    {
        public string    CnNumber       { get; set; } = "";
        public DateTime? BillingDate    { get; set; }
        public int?      ShipmentTypeId { get; set; }
        public string?   ShipmentLabel  { get; set; } // shipment type name from shipment_codes
        public decimal   BilledAmount   { get; set; }
        public string?   CourId         { get; set; } // route code in billing system
        public string?   StationId      { get; set; }
        /// <summary>True if this CN was found in hr_cash_consignments (commission generated)</summary>
        public bool      IsInCommission { get; set; }
    }

    // ── Location info (from hr_employeelocationdetails, Server 7 MIS) ────────

    public class LocationInfo
    {
        public int?      LocationId { get; set; }
        public string?   CityCode   { get; set; }
        public string?   CityName   { get; set; }
        public DateTime? FromDate   { get; set; }
        public DateTime? ToDate     { get; set; }
        public bool      IsActive   => ToDate == null || ToDate.Value >= DateTime.Now;
    }

    // ── Rate policy (from hr_commissionpolicy, Server 10) ─────────────────────

    public class CommissionRateInfo
    {
        public int     RateId    { get; set; }
        public string? Type      { get; set; }
        public decimal Rate      { get; set; }
        public bool    IsPercent { get; set; }
        public int?    RateType  { get; set; }
        public string? Comments  { get; set; }

        public string RateDisplay => IsPercent
            ? $"{Rate:0.##}%"
            : $"Rs {Rate:N2} flat";
    }

    // ── Old incentive rates (from hr_comm_insentives — legacy system) ────────

    public class OldIncentiveRate
    {
        public int     Id       { get; set; }
        public string? Type     { get; set; }
        public decimal Rate     { get; set; }
        public string? Comments { get; set; }
        public string RateDisplay => $"Rs {Rate:N2}";
    }

    // ── RBI / OLE incentive detail (lcs_hr.hr_rbi_incentive_detail, Server 7) ─
    // Queried by: station_id + Cour_id + year + month
    // Includes: Overland, Delivery, Pickup — all RBI types in one table

    public class RbiIncentiveRow
    {
        public string?   CnNumber       { get; set; }
        public string?   CourId         { get; set; }  // RouteCode
        public string?   StationId      { get; set; }
        public decimal   Commission     { get; set; }
        public string?   Type           { get; set; }  // Overland / Delivery / Pickup etc
        public decimal?  Weight         { get; set; }
        public decimal?  Amount         { get; set; }
        public string?   BasisLabel     { get; set; }  // "Weight" or "Amount" — derived
        public int?      RateId         { get; set; }
        public string?   RateName       { get; set; }  // from hr_commissionpolicy
        public DateTime? DeliveryDate   { get; set; }
        public string?   Status         { get; set; }  // DAS/DR/DW for return
    }

    // ── VAS / General incentive detail (lcs_hr.hr_ole_vas_incentive_detail, S7) ─
    // Queried by: ARVL_DEST(=station_id) + COURIER_ID + delivery date range
    // Covers: General Light/Heavy, VAS, Ecommerce Zero COD, SOA, Utility, CNIC, Passport

    public class VasGeneralRow
    {
        public string?   CnNumber       { get; set; }
        public string?   CourierId      { get; set; }
        public string?   ArvlDest       { get; set; }  // station_id
        public decimal   Commission     { get; set; }
        public string?   Type           { get; set; }  // General / VAS / EcomZeroCOD etc
        public decimal?  Weight         { get; set; }
        public decimal?  Amount         { get; set; }
        public string?   BasisLabel     { get; set; }  // "Weight" or "Amount"
        public int?      RateId         { get; set; }
        public string?   RateName       { get; set; }
        public DateTime? DeliveryDate   { get; set; }
    }

    // ── COD Return CN detail (lcs_hr.hr_codreturn_consignments, Server 7) ──────
    // Queried by: Station_id + COURIER_ID + Year + Month
    // Return statuses: DAS = Delivered After Service, DR = Delivery Return, DW = Damage/Write-off

    public class CodReturnCnRow
    {
        public string?   CnNumber    { get; set; }
        public string?   CourierId   { get; set; }
        public string?   StationId   { get; set; }
        public string?   Status      { get; set; }   // DAS / DR / DW
        public decimal   Commission  { get; set; }
        public decimal?  Amount      { get; set; }
        public int?      RateId      { get; set; }
        public string?   RateName    { get; set; }
        public DateTime? ReturnDate  { get; set; }
        public string?   BasisLabel  { get; set; }
    }

    // ── Adjustment detail (hr_empcommadjdtl, Server 10) ──────────────────────
    // Types: Id=1 RBI Billing, Id=2 Cash Billing (adjusment_policy)

    public class AdjustmentRow
    {
        public int       Id             { get; set; }
        public string?   AdjustmentType { get; set; }  // from adjusment_policy.Name
        public decimal   Amount         { get; set; }
        public string?   Remarks        { get; set; }
        public DateTime? CreatedDate    { get; set; }
    }

    // ── Ecom Overall SR Bonus (hr_incentive_overall_SR, Server 10) ───────────
    // Only Eligible employees get bonus

    public class SrBonusRow
    {
        public string?   IsEligible  { get; set; }  // "Eligible" / "Not Eligible"
        public decimal   BonusAmount { get; set; }
        public int?      TotalCn     { get; set; }
        public int?      EligibleCn  { get; set; }
        public string?   Remarks     { get; set; }
    }

    // ── Salary status (payslip paid/unpaid info) ──────────────────────────────

    public class SalaryStatusInfo
    {
        public bool      IsPaid      { get; set; }
        public DateTime? PaidDate    { get; set; }
        public decimal?  NetSalary   { get; set; }
        public string?   PaySlipNote { get; set; }
    }

    // ── Final commission breakdown (from hr_commissionprocess, Server 10) ─────
    // 90+ columns — key ones mapped, rest in NonZeroColumns dictionary

    public class FinalCommissionBreakdown
    {
        public decimal GrandTotal         { get; set; }
        public decimal Overnight          { get; set; }
        public decimal Cod                { get; set; }
        public decimal CodBonus           { get; set; }
        public decimal CodDeduction       { get; set; }
        public decimal Overland           { get; set; }
        public decimal OleDelivery        { get; set; }
        public decimal OleCreditBooking   { get; set; }
        public decimal Yb1Kg              { get; set; }
        public decimal Yb2Kg              { get; set; }
        public decimal Yb5Kg              { get; set; }
        public decimal Yb10Kg             { get; set; }
        public decimal Yb15Kg             { get; set; }
        public decimal Yb25Kg             { get; set; }
        public decimal Flayer             { get; set; }
        public decimal Detain             { get; set; }
        public decimal Economy            { get; set; }
        public decimal Prepaid            { get; set; }
        public decimal LoveLine           { get; set; }
        public decimal Vas                { get; set; }
        public decimal GeneralLight       { get; set; }
        public decimal GeneralHeavy       { get; set; }
        public decimal CashEconomyBooking { get; set; }
        public decimal RetailDeduction    { get; set; }
        public decimal CodDeduct          { get; set; }
        public decimal EcomSrBonus        { get; set; }
        public decimal MtdDelivery        { get; set; }

        // Summary groups for display
        public decimal CashGroup  => Overnight + Yb1Kg + Yb2Kg + Yb5Kg + Yb10Kg + Yb15Kg +
                                     Yb25Kg + Flayer + Detain + Economy + Prepaid + LoveLine + CashEconomyBooking;
        public decimal CodGroup   => Cod + CodBonus - CodDeduction - CodDeduct;
        public decimal OleGroup   => OleDelivery + OleCreditBooking + Overland;
        public decimal ExtraGroup => Vas + GeneralLight + GeneralHeavy + EcomSrBonus + MtdDelivery;

        /// <summary>Route code (Cour_id) from hr_commissionprocess — which route code this record was processed for</summary>
        public string? CourId { get; set; }

        /// <summary>All non-zero columns from hr_commissionprocess for full transparency display</summary>
        public Dictionary<string, decimal> NonZeroColumns { get; set; } = new();
    }

    // ── Investigation issue ───────────────────────────────────────────────────

    public enum IssueSeverity { Critical, Warning, Info }

    public class InvestigationIssue
    {
        public IssueSeverity Severity        { get; set; }
        public string        Category        { get; set; } = ""; // RouteCode|Location|Eligibility|Commission|Server|Data
        public string        Title           { get; set; } = "";
        public string        Description     { get; set; } = "";
        public string?       Server          { get; set; }
        public string?       SourceTable     { get; set; }
        public string?       SuggestedAction { get; set; }
        /// <summary>Ordered step-by-step fix instructions shown as numbered list on Issues tab.</summary>
        public List<string>  FixSteps        { get; set; } = new();
        /// <summary>Optional direct link to the Setup page that fixes this issue (relative URL).</summary>
        public string?       FixPageUrl      { get; set; }
        /// <summary>Label for the FixPageUrl button.</summary>
        public string?       FixPageLabel    { get; set; }
        /// <summary>If set, a "Fix Now" button opens this modal ID directly on the page.</summary>
        public string?       FixModalId      { get; set; }
        /// <summary>Label for the Fix Now modal button.</summary>
        public string?       FixModalLabel   { get; set; }

        public string SeverityClass => Severity switch {
            IssueSeverity.Critical => "red",
            IssueSeverity.Warning  => "yellow",
            _                      => "blue"
        };
        public string SeverityIcon => Severity switch {
            IssueSeverity.Critical => "fa-times-circle",
            IssueSeverity.Warning  => "fa-exclamation-triangle",
            _                      => "fa-info-circle"
        };
        public string SeverityLabel => Severity switch {
            IssueSeverity.Critical => "CRITICAL",
            IssueSeverity.Warning  => "WARNING",
            _                      => "INFO"
        };
        public string BorderClass => Severity switch {
            IssueSeverity.Critical => "tw-border-l-red-500",
            IssueSeverity.Warning  => "tw-border-l-yellow-500",
            _                      => "tw-border-l-blue-500"
        };
    }

    // ── Server health status ──────────────────────────────────────────────────

    public class ServerHealth
    {
        public string  ServerLabel   { get; set; } = "";
        public string  ConnectionKey { get; set; } = "";
        public string  IpAddress     { get; set; } = "";
        public string  DbVersion     { get; set; } = "";
        public bool    IsConnected   { get; set; }
        public string? ErrorMessage  { get; set; }
        public bool    HasQueryError { get; set; }  // connected but a query failed
        public string StatusClass  => IsConnected ? (HasQueryError ? "amber" : "green") : "red";
        public string StatusIcon   => IsConnected ? (HasQueryError ? "fa-exclamation-circle" : "fa-check-circle") : "fa-times-circle";
        public string StatusText   => IsConnected ? (HasQueryError ? "Connected (Query Error)" : "Connected") : "Unavailable";
    }

    // ── Investigation note (CRUD, stored in hr_commission_investigation_notes) ─

    public class InvestigationNote
    {
        public int       Id           { get; set; }
        public string    EmpNo        { get; set; } = "";
        public int       Year         { get; set; }
        public int       Month        { get; set; }
        /// <summary>Note | Reprocess | Escalate | Resolved | LocationFix | RouteFixRequest</summary>
        public string    ActionType   { get; set; } = "Note";
        public string    Notes        { get; set; } = "";
        public string    CreatedBy    { get; set; } = "";
        public DateTime  CreatedDate  { get; set; }
        /// <summary>Open | Resolved | Pending | Escalated</summary>
        public string    Status       { get; set; } = "Open";
        public string?   ResolvedBy   { get; set; }
        public DateTime? ResolvedDate { get; set; }
        public bool      IsDeleted    { get; set; }

        public string StatusBadgeClass => Status switch {
            "Resolved"  => "green",
            "Escalated" => "red",
            "Pending"   => "yellow",
            _           => "blue"
        };
        public string ActionIcon => ActionType switch {
            "Reprocess"        => "fa-redo",
            "Escalate"         => "fa-flag",
            "Resolved"         => "fa-check-double",
            "LocationFix"      => "fa-map-marker-alt",
            "RouteFixRequest"  => "fa-route",
            _                  => "fa-sticky-note"
        };
    }

    public class CreateNoteRequest
    {
        [Required] public string EmpNo      { get; set; } = "";
        [Required] public int    Year       { get; set; }
        [Required] public int    Month      { get; set; }
        public string            CityCode   { get; set; } = "";
        /// <summary>Note | Reprocess | Escalate | LocationFix | RouteFixRequest</summary>
        public string            ActionType { get; set; } = "Note";
        [Required, StringLength(2000, MinimumLength = 3)]
        public string            Notes      { get; set; } = "";
        public string            Status     { get; set; } = "Open";
    }

    // ── SQL query panel item ──────────────────────────────────────────────────

    public class SqlQueryPanelItem
    {
        public string QueryKey     { get; set; } = "";
        public string Title        { get; set; } = "";
        public string Purpose      { get; set; } = "";
        public string Server       { get; set; } = "";
        public string Database     { get; set; } = "";
        public string SourceTable  { get; set; } = "";
        public string SqlText      { get; set; } = "";
    }

    // ── Main investigation view model ─────────────────────────────────────────

    public class EmployeeCommissionInvestigationVm
    {
        // ── Employee master ───────────────────────────────────────────────────
        public string    EmpNo            { get; set; } = "";
        public string    Name             { get; set; } = "";
        public string?   Designation      { get; set; }  // Job title from hr_jobs
        public string?   JobTypeId        { get; set; }  // hr_jobs.Code
        public string?   Department       { get; set; }
        public string    EmpStatus        { get; set; } = ""; // A=Active S=Suspended I=Inactive/Left
        public string?   EmployeeType     { get; set; }   // Code e.g. '003'
        public string?   EmployeeTypeName { get; set; }   // Full name e.g. 'Permanent Zonal Employee'
        public DateTime? AppointDate      { get; set; }
        public DateTime? LeftDate         { get; set; }
        public string EmpStatusLabel => EmpStatus switch {
            "A" => "Active",
            "S" => "Suspended",
            "I" => "Inactive / Left",
            _   => EmpStatus
        };
        public string EmpStatusClass => EmpStatus switch {
            "A" => "tw-text-emerald-600",
            "S" => "tw-text-amber-600",
            _   => "tw-text-red-600"
        };

        // ── Period ────────────────────────────────────────────────────────────
        public int      Year       { get; set; }
        public int      Month      { get; set; }
        public DateTime PeriodFrom { get; set; }
        public DateTime PeriodTo   { get; set; }

        // ── City / Station ────────────────────────────────────────────────────
        public string  CityCode  { get; set; } = "";
        public string? CityName  { get; set; }
        public string? StationId { get; set; }

        // ── Route codes (from Main server / Server 10) ────────────────────────
        public List<RouteCodeInfo> RouteCodes { get; set; } = new();

        // ── Eligibility + Attendance (Server 10) ──────────────────────────────
        public bool?             IsEligible             { get; set; }
        public string?           EligibilityNote        { get; set; }
        /// <summary>CommissionId=2,3,4 breakdown from hr_empcommissioneligibility</summary>
        public List<EligibilityCategory> EligibilityBreakdown { get; set; } = new();
        /// <summary>Applicable commission categories based on CodeType (couriercodetype)</summary>
        public List<CommissionCategoryInfo> CommissionCategories { get; set; } = new();
        public AttendanceSummary Attendance      { get; set; } = new();

        // ── Commission data from Server 10 (Main HR) ─────────────────────────
        public List<CashCommissionCnDetail> CashCns          { get; set; } = new();
        public decimal                       CashTotal        { get; set; }
        /// <summary>Billing_Type breakdown e.g. CASH=1073, Retail COD=1373. Explains why S6 count differs.</summary>
        public List<BillingTypeCount>        CashBillingTypes { get; set; } = new();
        public int CashOnlyCount   => CashBillingTypes.FirstOrDefault(t => t.BillingType == "CASH")?.Count ?? 0;
        public int RetailCodCount  => CashBillingTypes.Where(t => t.BillingType != "CASH").Sum(t => t.Count);
        /// <summary>S6 billing_details CN count per route code. Used in Cash CNs tab for S6 vs S10 comparison.</summary>
        public Dictionary<string, int>       S6CnCountByRoute  { get; set; } = new();
        public List<CodCommissionCnDetail>   CodCns        { get; set; } = new();
        public decimal                       CodTotal      { get; set; }
        public ReturnCodSummary?             ReturnCod     { get; set; }
        public FinalCommissionBreakdown?     FinalComm     { get; set; }
        public List<CommissionRateInfo>      UsedRates     { get; set; } = new();
        /// <summary>All active rate policies from hr_commissionpolicy — shown even when no CNs loaded</summary>
        public List<CommissionRateInfo>      AllRatePolicies   { get; set; } = new();
        /// <summary>Old incentive rates from hr_comm_insentives</summary>
        public List<OldIncentiveRate>        OldIncentiveRates { get; set; } = new();
        /// <summary>Set when CN data could not be loaded and explains why</summary>
        public string?                       NoCnReason        { get; set; }

        // ── Server 6 data (Billing — LHR_Billing connection) ──────────────────
        public bool              S6Connected      { get; set; }
        public string?           S6Error          { get; set; }
        public int               S6TotalBillingCns{ get; set; }
        public int               S6MatchedCns     { get; set; }
        public int               S6MissingCns     { get; set; }
        public List<BillingCnInfo>   S6SampleCns  { get; set; } = new(); // up to 100 rows
        public List<RouteCodeInfo>   S6RouteCodes { get; set; } = new();
        // True if S6 has NO route codes loaded (no data = no mismatch to report),
        // OR at least one route code on S6 matches Main server.
        // False only when S6 has route codes AND none of them match Main server.
        public bool S6RouteCodesMatch => !S6RouteCodes.Any() ||
            RouteCodes.Any(r => S6RouteCodes.Any(s => s.RouteCode == r.RouteCode));

        // ── RBI / OLE / Overland detail (lcs_hr.hr_rbi_incentive_detail, S7) ───
        public List<RbiIncentiveRow>  RbiRows        { get; set; } = new();
        public decimal                RbiTotal       { get; set; }
        public string?                RbiError       { get; set; }
        /// <summary>Actual DB count (may exceed RbiRows.Count which is limited to 2000)</summary>
        public int                    RbiTotalCount  { get; set; }

        // ── VAS / General detail (lcs_hr.hr_ole_vas_incentive_detail, S7) ─────
        public List<VasGeneralRow>    VasRows        { get; set; } = new();
        public decimal                VasTotal       { get; set; }
        public string?                VasError       { get; set; }
        /// <summary>Actual DB count (may exceed VasRows.Count which is limited to 2000)</summary>
        public int                    VasTotalCount  { get; set; }

        // ── COD Return CN detail (lcs_hr.hr_codreturn_consignments, S7) ───────
        public List<CodReturnCnRow>   CodReturnRows      { get; set; } = new();
        public decimal                CodReturnTotal     { get; set; }
        public string?                CodReturnError     { get; set; }
        /// <summary>Actual DB count (may exceed CodReturnRows.Count which is limited to 1000)</summary>
        public int                    CodReturnTotalCount { get; set; }

        // ── Adjustments (hr_empcommadjdtl, S10) ──────────────────────────────
        public List<AdjustmentRow>    Adjustments    { get; set; } = new();
        public decimal                AdjustmentTotal => Adjustments.Sum(a => a.Amount);

        // ── SR Bonus (hr_incentive_overall_SR, S10) ───────────────────────────
        public SrBonusRow?            SrBonus        { get; set; }

        // ── Salary status ──────────────────────────────────────────────────────
        public SalaryStatusInfo?      SalaryStatus   { get; set; }

        // ── StationId resolved from LocationId chain ───────────────────────────
        // hr_employeelocationdetails.LocationId → lcs_setup.locations.BILLINGCITYID = station_id
        public string?                ResolvedStationId { get; set; }
        public string?                LocationName      { get; set; }

        // ── Server 7 data (MIS — MIS connection) ──────────────────────────────
        public bool                   S7Connected  { get; set; }
        public string?                S7Error      { get; set; }
        public List<LocationInfo>     S7Locations  { get; set; } = new();
        public List<OleCommissionRow> S7OleRecords { get; set; } = new();
        public decimal                S7OleTotal   { get; set; }
        public List<RouteCodeInfo>    S7RouteCodes { get; set; } = new();
        public bool S7LocationPresent  => S7Locations.Any(l => l.IsActive);
        // True if S7 has NO route codes loaded (no data = no mismatch to report),
        // OR at least one route code on S7 matches Main server.
        // False only when S7 has route codes AND none of them match Main server.
        public bool S7RouteCodesMatch  => !S7RouteCodes.Any() ||
            RouteCodes.Any(r => S7RouteCodes.Any(s => s.RouteCode == r.RouteCode));

        // ── Server health ──────────────────────────────────────────────────────
        public ServerHealth S6Health { get; set; } = new() { ServerLabel = "Server 6 — Billing", ConnectionKey = "LHR_Billing", IpAddress = "172.16.0.6" };
        public ServerHealth S7Health { get; set; } = new() { ServerLabel = "Server 7 — MIS",     ConnectionKey = "MIS",         IpAddress = "172.16.0.7" };
        public ServerHealth S10Health{ get; set; } = new() { ServerLabel = "Server 10 — Main HR", ConnectionKey = "Default",     IpAddress = "172.16.0.10" };

        // ── Issues ─────────────────────────────────────────────────────────────
        public List<InvestigationIssue> Issues { get; set; } = new();
        public List<InvestigationIssue> CriticalIssues => Issues.FindAll(i => i.Severity == IssueSeverity.Critical);
        public List<InvestigationIssue> Warnings       => Issues.FindAll(i => i.Severity == IssueSeverity.Warning);
        public List<InvestigationIssue> InfoItems       => Issues.FindAll(i => i.Severity == IssueSeverity.Info);

        // ── Commission status ─────────────────────────────────────────────────
        /// <summary>Processed | Pending | Missing | Partial | NotEligible | ZeroNoRoute | ZeroAmount | Unknown</summary>
        public string CommissionStatus { get; set; } = "Unknown";
        public string CommissionStatusClass => CommissionStatus switch {
            "Processed"    => "green",
            "Missing"      => "red",
            "Partial"      => "yellow",
            "NotEligible"  => "slate",
            "Pending"      => "blue",
            "ZeroNoRoute"  => "red",
            "ZeroAmount"   => "red",
            _              => "blue"
        };

        // ── SQL query panel ────────────────────────────────────────────────────
        public List<SqlQueryPanelItem> SqlQueries { get; set; } = new();

        // ── Investigation notes (CRUD) ─────────────────────────────────────────
        public List<InvestigationNote> Notes   { get; set; } = new();
        public CreateNoteRequest       NewNote { get; set; } = new();

        // ── Computed helpers ───────────────────────────────────────────────────
        /// <summary>How many months in hr_commissionprocess have GrandTotal = 0 for this employee (all-time)</summary>
        public int ZeroCommissionMonths { get; set; }
        /// <summary>List of Year/Month pairs where commission = Rs 0 — for batch reprocess UI</summary>
        public List<(int Year, int Month)> ZeroMonthsList { get; set; } = new();

        /// <summary>Number of hr_commissionprocess rows for this emp/year/month (> 1 = duplicate phantom records)</summary>
        public int  CommissionRecordCount   { get; set; }
        /// <summary>True when multiple commission records exist for same year/month (one is usually Cour_id=NULL phantom)</summary>
        public bool HasDuplicateCommission  => CommissionRecordCount > 1;
        /// <summary>True when station has no billing data (hr_cash_consignments=0 AND S6=0) despite route code being assigned</summary>
        public bool NoBillingDataAtStation  { get; set; }

        public bool IsActive           => EmpStatus == "A";
        public bool HasRouteCodes      => RouteCodes.Any(r => r.IsActive);
        public bool HasFinalCommission => FinalComm != null;
        public int  IssueCount         => CriticalIssues.Count + Warnings.Count;
        public int  TotalCashCns       => CashCns.Count;
        public int  TotalCodCns        => CodCns.Count;

        /// <summary>Month name for display (e.g. "February 2026")</summary>
        public string MonthYearLabel =>
            new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    }

    // ── Index page view model ─────────────────────────────────────────────────

    public class CommissionInvestigationIndexVm
    {
        public CommissionInvestigationFilter   Filter          { get; set; } = new();
        public List<EmpInvestigationSearchRow> SearchResults   { get; set; } = new();
        public bool                            SearchPerformed { get; set; }
        public List<int>                       AvailableYears  { get; set; } = new();
        public string?                         ErrorMessage    { get; set; }
        public int                             TotalFound      => SearchResults.Count;
    }

    // ── Route Code Suggestion (modal hint system) ────────────────────────────

    /// <summary>
    /// Aggregated hints from all 3 servers to help the user know what Route Code
    /// and Code Type to assign when an employee has no route code.
    /// </summary>
    public class RouteCodeSuggestionVm
    {
        // ── Employee job info ──────────────────────────────────────────────
        public string? EmployeeType     { get; set; }
        public string? EmployeeTypeName { get; set; }
        public string? JobTypeId        { get; set; }
        public string? JobTitle         { get; set; }

        // ── Commission process history (Cour_id per month) ─────────────────
        public List<CommissionCourIdRow> CommissionHistory { get; set; } = new();

        // ── Best suggestions derived from data ────────────────────────────
        public string? SuggestedRouteCode    { get; set; }
        public int?    SuggestedCodeType     { get; set; }
        public string? SuggestedCodeTypeName { get; set; }

        // ── External server findings ───────────────────────────────────────
        public string? S6RouteCode   { get; set; }
        public int?    S6CodeType    { get; set; }
        public string? S6CodeTypeName { get; set; }
        public string? S7RouteCode   { get; set; }
        public int?    S7CodeType    { get; set; }
        public string? S7CodeTypeName { get; set; }

        // ── Available route codes in this city (from hr_routecodes_hdr) ─────
        public List<AvailableRouteRow> AvailableRouteCodes { get; set; } = new();

        // ── Similar active colleagues in same city ─────────────────────────
        public List<SimilarRouteRow> SimilarEmployees { get; set; } = new();

        // ── Human-readable notes explaining each suggestion ────────────────
        public List<string> Notes { get; set; } = new();
    }

    public class AvailableRouteRow
    {
        public string  RouteCode   { get; set; } = "";
        public string? Description { get; set; }
    }

    public class CommissionCourIdRow
    {
        public string? CourId { get; set; }
        public int     Year   { get; set; }
        public int     Month  { get; set; }
    }

    public class SimilarRouteRow
    {
        public string  EmpNo         { get; set; } = "";
        public string  Name          { get; set; } = "";
        public string  RouteCode     { get; set; } = "";
        public int?    CodeType      { get; set; }
        public string? CodeTypeName  { get; set; }
        public string? EmployeeType  { get; set; }
        public string? JobTitle      { get; set; }
        public int?    LocationId    { get; set; }
        public string? LocationName  { get; set; }
    }

    // ── Fix modal request DTOs ────────────────────────────────────────────────

    public class FixRouteCodeRequest
    {
        public string    EmpNo        { get; set; } = "";
        public string    EmployeeName { get; set; } = "";
        public string    RouteCode    { get; set; } = "";
        public string    CityCode     { get; set; } = "";
        public int       LocationId   { get; set; }
        public string    CodeType     { get; set; } = "0";
        public DateTime? FromDate     { get; set; }
        public DateTime? ToDate       { get; set; }
        public string?   Comments     { get; set; }
    }

    public class FixEligibilityRequest
    {
        public string EmpNo        { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        /// <summary>CommissionId 2 — Ground Leopard / OLE Dispatch Proper</summary>
        public bool   CommissionId2 { get; set; }
        /// <summary>CommissionId 3 — Express Center In-House / OLE Transit Dispatch</summary>
        public bool   CommissionId3 { get; set; }
        /// <summary>CommissionId 4 — Express Center / OLE Delivery OPS</summary>
        public bool   CommissionId4 { get; set; }
    }

    public class ReprocessRequest
    {
        public string EmpNo { get; set; } = "";
        public int    Year  { get; set; }
        public int    Month { get; set; }
    }
}
