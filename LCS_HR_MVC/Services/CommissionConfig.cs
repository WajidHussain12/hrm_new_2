using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// All commission process configuration loaded from DB tables.
    /// Replaces every hardcoded magic number / ID list in PayrollService commission files.
    /// Load once per commission run via <see cref="LoadAsync"/>.
    /// </summary>
    public class CommissionConfig
    {
        // ── Scalar values (hr_commission_config) ──────────────────────────────

        /// <summary>Commission period start day (default 21).</summary>
        public int CommissionStartDay { get; private set; }

        /// <summary>Commission period end day of next month (default 20).</summary>
        public int CommissionEndDay { get; private set; }

        /// <summary>Maximum credit-booking commission per employee per month (default Rs 1,00,000).</summary>
        public decimal RbiCap { get; private set; }

        /// <summary>Minimum monthly commission guarantee amount (default Rs 1,500).</summary>
        public decimal MinGuaranteeAmount { get; private set; }

        /// <summary>Standard working-days divisor for pro-rata guarantee (default 25).</summary>
        public int MinGuaranteeWorkingDays { get; private set; }

        /// <summary>Cut-off date for CEB Existing vs New Acquisition split (2023-07-01).</summary>
        public DateTime CebSplitDate { get; private set; }

        /// <summary>Dummy/internal client_id excluded from OLE credit billing queries.</summary>
        public string DummyClientId { get; private set; } = "999998";

        /// <summary>Dummy courier code excluded from OLE credit billing queries.</summary>
        public string DummyCourierId { get; private set; } = "00000";

        /// <summary>COD Bonus slab 1: up to this many CNs (default 500).</summary>
        public int CodBonusSlab1MaxCn { get; private set; }

        /// <summary>COD Bonus slab 1 rate: Rs per CN (default 10).</summary>
        public decimal CodBonusSlab1Rate { get; private set; }

        /// <summary>COD Bonus slab 2: up to this many CNs (default 750).</summary>
        public int CodBonusSlab2MaxCn { get; private set; }

        /// <summary>COD Bonus slab 2 rate: Rs per CN (default 15).</summary>
        public decimal CodBonusSlab2Rate { get; private set; }

        /// <summary>COD Bonus slab 3 rate (above slab 2): Rs per CN (default 20).</summary>
        public decimal CodBonusSlab3Rate { get; private set; }

        /// <summary>Adjustment type IDs NOT deleted during commission reprocess.</summary>
        public int[] AdjustmentPreserveTypeIds { get; private set; } = Array.Empty<int>();

        /// <summary>All RateIDs fetched from hr_olecommissionprocess in OverLand process.</summary>
        public int[] OverlandProcessRateIds { get; private set; } = Array.Empty<int>();

        /// <summary>System user ID used for scheduled/automated commission runs (default 210).</summary>
        public string AutomationUserId { get; private set; } = "210";

        /// <summary>Maximum retry attempts per commission type per city in automation (default 3).</summary>
        public int AutomationMaxRetries { get; private set; } = 3;

        /// <summary>RateIDs from hr_commissionpolicy used in ReturnCOD commission queries (default 100,101,102,103).</summary>
        public int[] ReturnCodPolicyRateIds { get; private set; } = new[] { 100, 101, 102, 103 };

        // ── CodeType rule sets (hr_commission_codetype_rules) ─────────────────

        /// <summary>CodeTypes EXCLUDED from employee route query (NOT IN clause).</summary>
        public int[] RouteExcludeCodeTypes { get; private set; } = Array.Empty<int>();

        /// <summary>CommissionId values INCLUDED for hr_empcommissioneligibility (IN clause).</summary>
        public int[] EligibilityCommissionIds { get; private set; } = Array.Empty<int>();

        /// <summary>CodeTypes INCLUDED for OLE Credit Booking (RateId=1).</summary>
        public int[] OleCreditBookingIncludeCodeTypes { get; private set; } = Array.Empty<int>();

        /// <summary>CodeTypes EXCLUDED from OLE Delivery (RateId=5) query.</summary>
        public int[] OleDeliveryExcludeCodeTypes { get; private set; } = Array.Empty<int>();

        /// <summary>CodeTypes EXCLUDED from Cash VAS route filter.</summary>
        public int[] CashVasRouteExcludeCodeTypes { get; private set; } = Array.Empty<int>();

        /// <summary>CodeTypes EXCLUDED from ReturnCOD route filter.</summary>
        public int[] ReturnCodRouteExcludeCodeTypes { get; private set; } = Array.Empty<int>();

        /// <summary>CodeTypes that receive fixed RateID 103 (InHouse/Recovery flat rate) in ReturnCOD.</summary>
        public int[] ReturnCodInHouseCodeTypes { get; private set; } = Array.Empty<int>();

        // ── Excluded client IDs (hr_cod_excluded_clients) ─────────────────────

        /// <summary>Client IDs excluded from COD download queries (NOT IN clause).</summary>
        public string[] CodExcludedClientIds { get; private set; } = Array.Empty<string>();

        // ── Excluded shipment type IDs (hr_commission_excluded_shipments) ─────

        /// <summary>Shipment type IDs excluded from billing general queries.</summary>
        public int[] BillingGeneralExcludedShipmentTypes { get; private set; } = Array.Empty<int>();

        /// <summary>Shipment type IDs excluded from OverLand domestic/local credit queries.</summary>
        public int[] OverlandCreditExcludeShipmentTypes { get; private set; } = Array.Empty<int>();

        /// <summary>Shipment type IDs used in OverLand express/dispatch IN clause.</summary>
        public int[] OverlandExpressIncludeShipmentTypes { get; private set; } = Array.Empty<int>();

        /// <summary>Shipment type IDs used in OverLand delivery OPS IN clause.</summary>
        public int[] OverlandDeliveryOpsShipmentTypes { get; private set; } = Array.Empty<int>();

        // ── System exclusions (hr_commission_system_exclusions) ───────────────

        /// <summary>Express branch IDs excluded from Retail COD queries.</summary>
        public int[] ExcludedExpressIds { get; private set; } = Array.Empty<int>();

        /// <summary>LocationTypeIDs INCLUDED in Cash VAS station context lookup.</summary>
        public int[] CashLocationTypeIds { get; private set; } = Array.Empty<int>();

        /// <summary>LocationTypeIDs EXCLUDED in ReturnCOD location filter.</summary>
        public int[] ReturnCodExcludeLocationTypeIds { get; private set; } = Array.Empty<int>();

        // ── RateID → CommissionColumn mapping (hr_commission_type_mapping) ────

        /// <summary>
        /// Maps RateID → CommissionColumn for OLECommission rows.
        /// Used in AppendCorporateRbiRows to replace: item.RateID == 84 ? value : 0
        /// </summary>
        public Dictionary<int, string> OleRateIdToColumn { get; private set; } = new();

        // ── VAS exclusions (hr_commission_vas_exclusions) ─────────────────────

        /// <summary>VasIds excluded from Cash commission VAS booking queries (NOT IN clause).</summary>
        public int[] VasExclusionIds { get; private set; } = new[] { 5, 6, 12, 13, 15, 16, 31, 32, 48 };

        // ── COD threshold cities (hr_commission_cod_threshold_cities) ─────────

        /// <summary>
        /// Per-city COD delivery rate threshold percentage.
        /// Cities not in the dictionary use the default 80% threshold.
        /// Key: CityCode (case-insensitive). Value: required delivery rate (e.g. 85.00).
        /// </summary>
        public Dictionary<string, decimal> CodThresholdByCityCode { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["001"] = 85m,
                ["002"] = 85m,
                ["003"] = 85m,
                ["080"] = 85m,
            };

        /// <summary>
        /// Returns the COD delivery rate threshold for the given city code.
        /// Falls back to 80% for cities not in the table.
        /// </summary>
        public decimal GetCodThresholdForCity(string cityCode) =>
            CodThresholdByCityCode.TryGetValue(cityCode, out var t) ? t : 80m;

        // ── Salary config scalars (hr_salary_config) ──────────────────────────

        /// <summary>Month number when the leave year starts (default 7 = July).</summary>
        public int LeaveYearStartMonth { get; private set; } = 7;

        /// <summary>Day of month when the leave year starts (default 1).</summary>
        public int LeaveYearStartDay { get; private set; } = 1;

        /// <summary>Gross salary threshold for leave encashment cashability rules (default Rs 50,000).</summary>
        public decimal LeaveEncashmentGrossThreshold { get; private set; } = 50000m;

        /// <summary>Standard days divisor for per-day salary calculation (default 30).</summary>
        public int SalaryDaysDivisor { get; private set; } = 30;

        /// <summary>GL code used for salary deductions in monthly tax correction (default 167).</summary>
        public int TaxSalaryGlCode { get; private set; } = 167;

        /// <summary>Minimum months of service required for leave encashment eligibility (default 6).</summary>
        public int LeaveMonthsMinForCashout { get; private set; } = 6;

        // ── SQL helpers (computed from loaded data) ────────────────────────────

        /// <summary>Comma-separated excluded client IDs for inline SQL NOT IN clause.</summary>
        public string CodExcludedClientIdsCsv =>
            CodExcludedClientIds.Length == 0 ? "'-1'" : string.Join(",", CodExcludedClientIds.Select(c => $"'{c}'"));

        /// <summary>Comma-separated billing excluded shipment IDs for SQL NOT IN clause.</summary>
        public string BillingExcludedShipmentTypesCsv =>
            BillingGeneralExcludedShipmentTypes.Length == 0 ? "0" : string.Join(",", BillingGeneralExcludedShipmentTypes);

        /// <summary>Comma-separated overland credit-exclude shipment IDs for SQL NOT IN clause.</summary>
        public string OverlandCreditExcludeShipmentTypesCsv =>
            OverlandCreditExcludeShipmentTypes.Length == 0 ? "0" : string.Join(",", OverlandCreditExcludeShipmentTypes);

        /// <summary>Comma-separated overland express shipment IDs for SQL IN clause.</summary>
        public string OverlandExpressIncludeShipmentTypesCsv =>
            OverlandExpressIncludeShipmentTypes.Length == 0 ? "0" : string.Join(",", OverlandExpressIncludeShipmentTypes);

        /// <summary>Comma-separated overland delivery OPS shipment IDs for SQL IN clause.</summary>
        public string OverlandDeliveryOpsShipmentTypesCsv =>
            OverlandDeliveryOpsShipmentTypes.Length == 0 ? "0" : string.Join(",", OverlandDeliveryOpsShipmentTypes);

        /// <summary>Comma-separated route-exclude CodeType IDs for SQL NOT IN clause.</summary>
        public string RouteExcludeCodeTypesCsv =>
            RouteExcludeCodeTypes.Length == 0 ? "0" : string.Join(",", RouteExcludeCodeTypes);

        /// <summary>Comma-separated eligibility CommissionId values for SQL IN clause.</summary>
        public string EligibilityCommissionIdsCsv =>
            EligibilityCommissionIds.Length == 0 ? "0" : string.Join(",", EligibilityCommissionIds);

        /// <summary>Comma-separated OLE Credit Booking include CodeTypes for SQL IN clause.</summary>
        public string OleCreditBookingIncludeCodeTypesCsv =>
            OleCreditBookingIncludeCodeTypes.Length == 0 ? "0" : string.Join(",", OleCreditBookingIncludeCodeTypes);

        /// <summary>Comma-separated OLE Delivery exclude CodeTypes for SQL NOT IN clause.</summary>
        public string OleDeliveryExcludeCodeTypesCsv =>
            OleDeliveryExcludeCodeTypes.Length == 0 ? "0" : string.Join(",", OleDeliveryExcludeCodeTypes);

        /// <summary>Comma-separated Cash VAS route exclude CodeTypes for SQL NOT IN clause.</summary>
        public string CashVasRouteExcludeCodeTypesCsv =>
            CashVasRouteExcludeCodeTypes.Length == 0 ? "0" : string.Join(",", CashVasRouteExcludeCodeTypes);

        /// <summary>Comma-separated ReturnCOD route exclude CodeTypes for SQL NOT IN clause.</summary>
        public string ReturnCodRouteExcludeCodeTypesCsv =>
            ReturnCodRouteExcludeCodeTypes.Length == 0 ? "0" : string.Join(",", ReturnCodRouteExcludeCodeTypes);

        /// <summary>Comma-separated ReturnCOD InHouse CodeTypes for SQL IN clause.</summary>
        public string ReturnCodInHouseCodeTypesCsv =>
            ReturnCodInHouseCodeTypes.Length == 0 ? "0" : string.Join(",", ReturnCodInHouseCodeTypes);

        /// <summary>Comma-separated excluded express IDs for SQL NOT IN clause.</summary>
        public string ExcludedExpressIdsCsv =>
            ExcludedExpressIds.Length == 0 ? "0" : string.Join(",", ExcludedExpressIds);

        /// <summary>Comma-separated Cash location type IDs for SQL IN clause.</summary>
        public string CashLocationTypeIdsCsv =>
            CashLocationTypeIds.Length == 0 ? "0" : string.Join(",", CashLocationTypeIds);

        /// <summary>Comma-separated ReturnCOD exclude location type IDs for SQL NOT IN clause.</summary>
        public string ReturnCodExcludeLocationTypeIdsCsv =>
            ReturnCodExcludeLocationTypeIds.Length == 0 ? "0" : string.Join(",", ReturnCodExcludeLocationTypeIds);

        /// <summary>Comma-separated adjustment type IDs to preserve for SQL NOT IN clause.</summary>
        public string AdjustmentPreserveTypeIdsCsv =>
            AdjustmentPreserveTypeIds.Length == 0 ? "0" : string.Join(",", AdjustmentPreserveTypeIds);

        /// <summary>Comma-separated OverLand process RateIDs for SQL IN clause.</summary>
        public string OverlandProcessRateIdsCsv =>
            OverlandProcessRateIds.Length == 0 ? "0" : string.Join(",", OverlandProcessRateIds);

        /// <summary>Comma-separated ReturnCOD policy RateIDs (from DB, default 100,101,102,103) for SQL IN clause.</summary>
        public string ReturnCodPolicyRateIdsCsv =>
            ReturnCodPolicyRateIds.Length == 0 ? "0" : string.Join(",", ReturnCodPolicyRateIds);

        /// <summary>Comma-separated VAS exclusion IDs for SQL NOT IN clause.</summary>
        public string VasExclusionIdsCsv =>
            VasExclusionIds.Length == 0 ? "0" : string.Join(",", VasExclusionIds);

        /// <summary>Parameterless constructor — all values are at safe defaults. Use <see cref="LoadAsync"/> for production.</summary>
        public CommissionConfig() { }

        // ── Loader ────────────────────────────────────────────────────────────

        /// <summary>
        /// Load all commission configuration from S10 database tables.
        /// Call once at the start of each commission process run.
        /// </summary>
        public static async Task<CommissionConfig> LoadAsync(MySqlConnection connection, MySqlTransaction? transaction = null)
        {
            var cfg = new CommissionConfig();

            // ── 1. Scalar config (hr_commission_config) ───────────────────────
            var scalars = (await connection.QueryAsync<(string Key, string Value)>(
                "SELECT ConfigKey, ConfigValue FROM lcs_hr.hr_commission_config",
                transaction: transaction)).ToDictionary(r => r.Key, r => r.Value);

            cfg.CommissionStartDay       = GetInt(scalars,    "COMMISSION_START_DAY",         21);
            cfg.CommissionEndDay         = GetInt(scalars,    "COMMISSION_END_DAY",            20);
            cfg.RbiCap                   = GetDecimal(scalars,"RBI_CAP",                  100000m);
            cfg.MinGuaranteeAmount       = GetDecimal(scalars,"MIN_GUARANTEE_AMOUNT",        1500m);
            cfg.MinGuaranteeWorkingDays  = GetInt(scalars,    "MIN_GUARANTEE_WORKING_DAYS",    25);
            cfg.CebSplitDate             = GetDate(scalars,   "CEB_SPLIT_DATE",       new DateTime(2023, 7, 1));
            cfg.DummyClientId            = GetString(scalars, "DUMMY_CLIENT_ID",         "999998");
            cfg.DummyCourierId           = GetString(scalars, "DUMMY_COURIER_ID",         "00000");
            cfg.CodBonusSlab1MaxCn       = GetInt(scalars,    "COD_BONUS_SLAB1_MAX_CN",       500);
            cfg.CodBonusSlab1Rate        = GetDecimal(scalars,"COD_BONUS_SLAB1_RATE",          10m);
            cfg.CodBonusSlab2MaxCn       = GetInt(scalars,    "COD_BONUS_SLAB2_MAX_CN",       750);
            cfg.CodBonusSlab2Rate        = GetDecimal(scalars,"COD_BONUS_SLAB2_RATE",          15m);
            cfg.CodBonusSlab3Rate        = GetDecimal(scalars,"COD_BONUS_SLAB3_RATE",          20m);
            cfg.AdjustmentPreserveTypeIds = GetCsvInts(scalars,"ADJUSTMENT_PRESERVE_TYPE_IDS", new[] { 1, 2 });
            cfg.OverlandProcessRateIds    = GetCsvInts(scalars,"OVERLAND_PROCESS_RATE_IDS",
                new[] { 1,2,3,4,5,8,11,12,84,85,86,87,88,89,90,91,92,93,94,95 });
            cfg.AutomationUserId         = GetString(scalars, "AUTOMATION_USER_ID",          "210");
            cfg.AutomationMaxRetries     = GetInt(scalars,    "AUTOMATION_MAX_RETRIES",          3);
            cfg.ReturnCodPolicyRateIds   = GetCsvInts(scalars,"RETURNCOD_POLICY_RATE_IDS",
                new[] { 100, 101, 102, 103 });

            // ── 2. CodeType rules (hr_commission_codetype_rules) ──────────────
            var codeTypeRules = (await connection.QueryAsync<(string Process, string RuleType, int CodeTypeId)>(
                "SELECT ProcessName, RuleType, CodeTypeId FROM lcs_hr.hr_commission_codetype_rules",
                transaction: transaction)).ToList();

            cfg.RouteExcludeCodeTypes            = FilterCodeTypes(codeTypeRules, "EmployeeRoute",       "EXCLUDE");
            cfg.EligibilityCommissionIds          = FilterCodeTypes(codeTypeRules, "EligibilityCommission","INCLUDE");
            cfg.OleCreditBookingIncludeCodeTypes  = FilterCodeTypes(codeTypeRules, "OLECreditBooking",    "INCLUDE");
            cfg.OleDeliveryExcludeCodeTypes       = FilterCodeTypes(codeTypeRules, "OLEDelivery",         "EXCLUDE");
            cfg.CashVasRouteExcludeCodeTypes      = FilterCodeTypes(codeTypeRules, "CashVASRoute",        "EXCLUDE");
            cfg.ReturnCodRouteExcludeCodeTypes    = FilterCodeTypes(codeTypeRules, "ReturnCODRoute",      "EXCLUDE");
            cfg.ReturnCodInHouseCodeTypes         = FilterCodeTypes(codeTypeRules, "ReturnCODInHouse",    "INCLUDE");

            // ── 3. COD excluded clients (hr_cod_excluded_clients) ─────────────
            cfg.CodExcludedClientIds = (await connection.QueryAsync<string>(
                "SELECT ClientId FROM lcs_hr.hr_cod_excluded_clients WHERE IsActive = 1",
                transaction: transaction)).ToArray();

            // ── 4. Excluded shipment types (hr_commission_excluded_shipments) ──
            var shipmentExclusions = (await connection.QueryAsync<(string Process, int ShipmentTypeId)>(
                "SELECT ProcessName, ShipmentTypeId FROM lcs_hr.hr_commission_excluded_shipments",
                transaction: transaction)).ToList();

            cfg.BillingGeneralExcludedShipmentTypes  = FilterShipments(shipmentExclusions, "BillingGeneral");
            cfg.OverlandCreditExcludeShipmentTypes   = FilterShipments(shipmentExclusions, "OverLandCreditExclude");
            cfg.OverlandExpressIncludeShipmentTypes  = FilterShipments(shipmentExclusions, "OverLandExpressInclude");
            cfg.OverlandDeliveryOpsShipmentTypes     = FilterShipments(shipmentExclusions, "OverLandDeliveryOPS");

            // ── 5. System exclusions (hr_commission_system_exclusions) ─────────
            var sysExclusions = (await connection.QueryAsync<(string Type, string Value, string RuleType)>(
                "SELECT ExclusionType, ExclusionValue, RuleType FROM lcs_hr.hr_commission_system_exclusions WHERE IsActive = 1",
                transaction: transaction)).ToList();

            cfg.ExcludedExpressIds               = FilterSysExclusions(sysExclusions, "EXCLUDED_EXPRESS_ID");
            cfg.CashLocationTypeIds              = FilterSysExclusions(sysExclusions, "CASH_LOCATION_TYPE");
            cfg.ReturnCodExcludeLocationTypeIds  = FilterSysExclusions(sysExclusions, "RETURNCOD_EXCLUDE_LOCATION_TYPE");

            // ── 6. OLE RateID → CommissionColumn mapping (hr_commission_type_mapping)
            var rateMap = await connection.QueryAsync<(int RateID, string CommissionColumn)>(
                @"SELECT RateID, CommissionColumn
                  FROM lcs_hr.hr_commission_type_mapping
                  WHERE CommissionType = 'OLECommission'
                    AND RateID IS NOT NULL
                    AND IsActive = 1",
                transaction: transaction);

            cfg.OleRateIdToColumn = rateMap.ToDictionary(r => r.RateID, r => r.CommissionColumn);

            // ── 7. VAS exclusions (hr_commission_vas_exclusions) ──────────────
            var vasExclusions = (await connection.QueryAsync<int>(
                "SELECT VasId FROM lcs_hr.hr_commission_vas_exclusions WHERE IsActive = 1",
                transaction: transaction)).ToArray();

            if (vasExclusions.Length > 0)
                cfg.VasExclusionIds = vasExclusions;

            // ── 8. COD threshold cities (hr_commission_cod_threshold_cities) ───
            var codThresholds = (await connection.QueryAsync<(string CityCode, decimal ThresholdPct)>(
                "SELECT CityCode, ThresholdPct FROM lcs_hr.hr_commission_cod_threshold_cities WHERE IsActive = 1",
                transaction: transaction)).ToList();

            if (codThresholds.Count > 0)
            {
                cfg.CodThresholdByCityCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                foreach (var (cityCode, thresholdPct) in codThresholds)
                    cfg.CodThresholdByCityCode[cityCode] = thresholdPct;
            }

            // ── 9. Salary config scalars (hr_salary_config) ───────────────────
            var salaryScalars = (await connection.QueryAsync<(string Key, string Value)>(
                "SELECT ConfigKey, ConfigValue FROM lcs_hr.hr_salary_config",
                transaction: transaction)).ToDictionary(r => r.Key, r => r.Value);

            cfg.LeaveYearStartMonth             = GetInt(salaryScalars,    "LEAVE_YEAR_START_MONTH",              7);
            cfg.LeaveYearStartDay               = GetInt(salaryScalars,    "LEAVE_YEAR_START_DAY",                1);
            cfg.LeaveEncashmentGrossThreshold   = GetDecimal(salaryScalars,"LEAVE_ENCASHMENT_GROSS_THRESHOLD", 50000m);
            cfg.SalaryDaysDivisor               = GetInt(salaryScalars,    "SALARY_DAYS_DIVISOR",                30);
            cfg.TaxSalaryGlCode                 = GetInt(salaryScalars,    "TAX_SALARY_GLCODE",                 167);
            cfg.LeaveMonthsMinForCashout        = GetInt(salaryScalars,    "LEAVE_MONTHS_MIN_FOR_CASHOUT",        6);

            return cfg;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static int GetInt(Dictionary<string, string> d, string key, int fallback) =>
            d.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : fallback;

        private static decimal GetDecimal(Dictionary<string, string> d, string key, decimal fallback) =>
            d.TryGetValue(key, out var v) && decimal.TryParse(v, out var r) ? r : fallback;

        private static DateTime GetDate(Dictionary<string, string> d, string key, DateTime fallback) =>
            d.TryGetValue(key, out var v) && DateTime.TryParse(v, out var r) ? r : fallback;

        private static string GetString(Dictionary<string, string> d, string key, string fallback) =>
            d.TryGetValue(key, out var v) ? v : fallback;

        private static int[] GetCsvInts(Dictionary<string, string> d, string key, int[] fallback)
        {
            if (!d.TryGetValue(key, out var v)) return fallback;
            var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<int>();
            foreach (var p in parts)
                if (int.TryParse(p.Trim(), out var n)) result.Add(n);
            return result.Count > 0 ? result.ToArray() : fallback;
        }

        private static int[] FilterCodeTypes(
            List<(string Process, string RuleType, int CodeTypeId)> rules,
            string process, string ruleType) =>
            rules.Where(r => r.Process == process && r.RuleType == ruleType)
                 .Select(r => r.CodeTypeId).ToArray();

        private static int[] FilterShipments(
            List<(string Process, int ShipmentTypeId)> list, string process) =>
            list.Where(r => r.Process == process).Select(r => r.ShipmentTypeId).ToArray();

        private static int[] FilterSysExclusions(
            List<(string Type, string Value, string RuleType)> list, string type) =>
            list.Where(r => r.Type == type)
                .Select(r => int.TryParse(r.Value, out var n) ? n : -1)
                .Where(n => n >= 0).ToArray();
    }
}
