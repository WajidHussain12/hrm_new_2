namespace LCS_HR_MVC.Models
{
    // ── Scalar config row (hr_commission_config / hr_salary_config) ───────────
    // Confirmed columns: ConfigKey, ConfigValue
    public class ConfigScalarRow
    {
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
    }

    // ── COD excluded client (hr_cod_excluded_clients) ─────────────────────────
    // Confirmed columns: ClientId, IsActive  — HR-maintained manually, no auto-sync
    public class CodExcludedClientRow
    {
        public string ClientId { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    // ── COD threshold city (hr_commission_cod_threshold_cities) ──────────────
    // Confirmed columns: CityCode, ThresholdPct, IsActive
    public class CodThresholdCityRow
    {
        public string CityCode { get; set; } = string.Empty;
        public decimal ThresholdPct { get; set; }
        public bool IsActive { get; set; }
    }

    // ── VAS exclusion (hr_commission_vas_exclusions) ──────────────────────────
    // Confirmed columns: VasId, IsActive
    public class VasExclusionRow
    {
        public int VasId { get; set; }
        public bool IsActive { get; set; }
    }

    // ── Salary tax exclusion (hr_salary_tax_exclusions) ──────────────────────
    // Confirmed columns: EmpNo, IsActive
    public class SalaryTaxExclusionRow
    {
        public string EmpNo { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    // ── Shipment exclusion (hr_commission_excluded_shipments) ─────────────────
    // Confirmed columns: ProcessName, ShipmentTypeId
    public class ShipmentExclusionRow
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ShipmentTypeId { get; set; }
    }

    // ── System exclusion (hr_commission_system_exclusions) ────────────────────
    // Confirmed columns: ExclusionType, ExclusionValue, RuleType, IsActive
    public class SystemExclusionRow
    {
        public string ExclusionType { get; set; } = string.Empty;
        public string ExclusionValue { get; set; } = string.Empty;
        public string RuleType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    // ── OLE type mapping (hr_commission_type_mapping) — developer-maintained ──
    // Confirmed columns: CommissionType, RateID, CommissionColumn, IsActive
    public class TypeMappingRow
    {
        public string CommissionType { get; set; } = string.Empty;
        public int? RateID { get; set; }
        public string CommissionColumn { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    // ── CodeType rule (hr_commission_codetype_rules) — read-only display ──────
    // Confirmed columns: ProcessName, RuleType, CodeTypeId
    public class CodeTypeRuleRow
    {
        public string ProcessName { get; set; } = string.Empty;
        public string RuleType { get; set; } = string.Empty;
        public int CodeTypeId { get; set; }
    }

    // ── Main view model ───────────────────────────────────────────────────────
    public class ConfigurationIndexViewModel
    {
        public List<ConfigScalarRow> CommissionScalars { get; set; } = new();
        public List<ConfigScalarRow> SalaryScalars { get; set; } = new();
        public List<CodExcludedClientRow> CodExcludedClients { get; set; } = new();
        public List<CodThresholdCityRow> CodThresholdCities { get; set; } = new();
        public List<VasExclusionRow> VasExclusions { get; set; } = new();
        public List<SalaryTaxExclusionRow> SalaryTaxExclusions { get; set; } = new();
        public List<ShipmentExclusionRow> ShipmentExclusions { get; set; } = new();
        public List<SystemExclusionRow> SystemExclusions { get; set; } = new();
        public List<TypeMappingRow> TypeMappings { get; set; } = new();
        public List<CodeTypeRuleRow> CodeTypeRules { get; set; } = new();
        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // ── Request models (POST payloads) ────────────────────────────────────────

    public class UpdateScalarRequest
    {
        /// <summary>"commission" → hr_commission_config, "salary" → hr_salary_config.</summary>
        public string Table { get; set; } = string.Empty;
        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
    }

    public class AddCodClientRequest
    {
        public string ClientId { get; set; } = string.Empty;
    }

    public class AddCodThresholdCityRequest
    {
        public string CityCode { get; set; } = string.Empty;
        public decimal ThresholdPct { get; set; }
    }

    public class AddVasExclusionRequest
    {
        public int VasId { get; set; }
    }

    public class AddTaxExclusionRequest
    {
        public string EmpNo { get; set; } = string.Empty;
    }

    public class AddShipmentExclusionRequest
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ShipmentTypeId { get; set; }
    }

    public class AddSysExclusionRequest
    {
        public string ExclusionType { get; set; } = string.Empty;
        public string ExclusionValue { get; set; } = string.Empty;
        public string RuleType { get; set; } = "EXCLUDE";
    }
}
