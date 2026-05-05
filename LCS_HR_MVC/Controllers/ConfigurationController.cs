using Dapper;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class ConfigurationController : Controller
    {
        private readonly string _connectionString;
        private readonly IDataSyncService _syncService;

        public ConfigurationController(
            IConfiguration configuration,
            IDataSyncService syncService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is not configured.");
            _syncService = syncService;
        }

        private MySqlConnection OpenConnection()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        // ── GET /Configuration ────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index(string? success = null, string? error = null)
        {
            var vm = new ConfigurationIndexViewModel
            {
                SuccessMessage = success,
                ErrorMessage   = error
            };

            using var conn = OpenConnection();

            vm.CommissionScalars = (await conn.QueryAsync<ConfigScalarRow>(
                "SELECT ConfigKey, ConfigValue FROM lcs_hr.hr_commission_config ORDER BY ConfigKey"
            )).ToList();

            vm.SalaryScalars = (await conn.QueryAsync<ConfigScalarRow>(
                "SELECT ConfigKey, ConfigValue FROM lcs_hr.hr_salary_config ORDER BY ConfigKey"
            )).ToList();

            // Confirmed columns only — no Id, no ClientName
            vm.CodExcludedClients = (await conn.QueryAsync<CodExcludedClientRow>(
                "SELECT ClientId, IsActive FROM lcs_hr.hr_cod_excluded_clients ORDER BY ClientId"
            )).ToList();

            // Confirmed columns only — no Id, no CityName
            vm.CodThresholdCities = (await conn.QueryAsync<CodThresholdCityRow>(
                "SELECT CityCode, ThresholdPct, IsActive FROM lcs_hr.hr_commission_cod_threshold_cities ORDER BY CityCode"
            )).ToList();

            // Confirmed columns only — no Id, no VasName
            vm.VasExclusions = (await conn.QueryAsync<VasExclusionRow>(
                "SELECT VasId, IsActive FROM lcs_hr.hr_commission_vas_exclusions ORDER BY VasId"
            )).ToList();

            // Confirmed columns only — no Id, no EmpName
            vm.SalaryTaxExclusions = (await conn.QueryAsync<SalaryTaxExclusionRow>(
                "SELECT EmpNo, IsActive FROM lcs_hr.hr_salary_tax_exclusions ORDER BY EmpNo"
            )).ToList();

            // Confirmed columns: ProcessName, ShipmentTypeId
            vm.ShipmentExclusions = (await conn.QueryAsync<ShipmentExclusionRow>(
                "SELECT ProcessName, ShipmentTypeId FROM lcs_hr.hr_commission_excluded_shipments ORDER BY ProcessName, ShipmentTypeId"
            )).ToList();

            // Confirmed columns: ExclusionType, ExclusionValue, RuleType, IsActive
            vm.SystemExclusions = (await conn.QueryAsync<SystemExclusionRow>(
                "SELECT ExclusionType, ExclusionValue, RuleType, IsActive FROM lcs_hr.hr_commission_system_exclusions ORDER BY ExclusionType, ExclusionValue"
            )).ToList();

            // Confirmed columns: CommissionType, RateID, CommissionColumn, IsActive
            vm.TypeMappings = (await conn.QueryAsync<TypeMappingRow>(
                "SELECT CommissionType, RateID, CommissionColumn, IsActive FROM lcs_hr.hr_commission_type_mapping ORDER BY CommissionType, RateID"
            )).ToList();

            // Confirmed columns: ProcessName, RuleType, CodeTypeId
            vm.CodeTypeRules = (await conn.QueryAsync<CodeTypeRuleRow>(
                "SELECT ProcessName, RuleType, CodeTypeId FROM lcs_hr.hr_commission_codetype_rules ORDER BY ProcessName, RuleType, CodeTypeId"
            )).ToList();

            return View(vm);
        }

        // ── POST /Configuration/UpdateScalar ──────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateScalar(UpdateScalarRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ConfigKey) || string.IsNullOrWhiteSpace(req.ConfigValue))
                return RedirectToAction(nameof(Index), new { error = "Key and Value are both required." });

            // Table name from whitelist — never raw user input
            var table = req.Table == "salary" ? "hr_salary_config" : "hr_commission_config";

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                $"INSERT INTO lcs_hr.{table} (ConfigKey, ConfigValue) VALUES (@k, @v) " +
                "ON DUPLICATE KEY UPDATE ConfigValue = @v",
                new { k = req.ConfigKey.Trim(), v = req.ConfigValue.Trim() });

            return RedirectToAction(nameof(Index), new { success = $"'{req.ConfigKey}' updated." });
        }

        // ── POST /Configuration/ToggleActive ──────────────────────────────────
        // Uses natural keys (no Id column assumed) — safe for all table layouts.

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(string table, string key, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(key))
                return RedirectToAction(nameof(Index), new { error = "Invalid key." });

            int flag = isActive ? 1 : 0;
            using var conn = OpenConnection();

            switch (table)
            {
                case "cod_clients":
                    await conn.ExecuteAsync(
                        "UPDATE lcs_hr.hr_cod_excluded_clients SET IsActive = @f WHERE ClientId = @k",
                        new { f = flag, k = key });
                    break;

                case "cod_cities":
                    await conn.ExecuteAsync(
                        "UPDATE lcs_hr.hr_commission_cod_threshold_cities SET IsActive = @f WHERE CityCode = @k",
                        new { f = flag, k = key });
                    break;

                case "vas":
                    if (!int.TryParse(key, out var vasId))
                        return RedirectToAction(nameof(Index), new { error = "Invalid VAS ID." });
                    await conn.ExecuteAsync(
                        "UPDATE lcs_hr.hr_commission_vas_exclusions SET IsActive = @f WHERE VasId = @k",
                        new { f = flag, k = vasId });
                    break;

                case "tax":
                    await conn.ExecuteAsync(
                        "UPDATE lcs_hr.hr_salary_tax_exclusions SET IsActive = @f WHERE EmpNo = @k",
                        new { f = flag, k = key });
                    break;

                default:
                    return RedirectToAction(nameof(Index), new { error = "Invalid table." });
            }

            return RedirectToAction(nameof(Index), new { success = "Status updated." });
        }

        // ── POST /Configuration/AddCodClient ──────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCodClient(AddCodClientRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ClientId))
                return RedirectToAction(nameof(Index), new { error = "Client ID is required." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO lcs_hr.hr_cod_excluded_clients (ClientId, IsActive) VALUES (@c, 1)",
                new { c = req.ClientId.Trim() });

            return RedirectToAction(nameof(Index), new { success = $"Client '{req.ClientId.Trim()}' added." });
        }

        // ── POST /Configuration/AddCodCity ────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCodCity(AddCodThresholdCityRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.CityCode) || req.ThresholdPct <= 0)
                return RedirectToAction(nameof(Index), new { error = "City Code and Threshold % are required." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO lcs_hr.hr_commission_cod_threshold_cities (CityCode, ThresholdPct, IsActive) VALUES (@c, @t, 1)",
                new { c = req.CityCode.Trim().ToUpper(), t = req.ThresholdPct });

            return RedirectToAction(nameof(Index), new { success = $"City '{req.CityCode.Trim().ToUpper()}' added." });
        }

        // ── POST /Configuration/AddVasExclusion ───────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddVasExclusion(AddVasExclusionRequest req)
        {
            if (req.VasId <= 0)
                return RedirectToAction(nameof(Index), new { error = "VAS ID must be a positive integer." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO lcs_hr.hr_commission_vas_exclusions (VasId, IsActive) VALUES (@v, 1)",
                new { v = req.VasId });

            return RedirectToAction(nameof(Index), new { success = $"VAS ID {req.VasId} added." });
        }

        // ── POST /Configuration/AddTaxExclusion ───────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTaxExclusion(AddTaxExclusionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.EmpNo))
                return RedirectToAction(nameof(Index), new { error = "Employee No is required." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO lcs_hr.hr_salary_tax_exclusions (EmpNo, IsActive) VALUES (@e, 1)",
                new { e = req.EmpNo.Trim() });

            return RedirectToAction(nameof(Index), new { success = $"Employee '{req.EmpNo.Trim()}' added to tax exclusions." });
        }

        // ── POST /Configuration/AddShipmentExclusion ──────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddShipmentExclusion(AddShipmentExclusionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ProcessName) || req.ShipmentTypeId <= 0)
                return RedirectToAction(nameof(Index), new { error = "Process Name and Shipment Type ID are required." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO lcs_hr.hr_commission_excluded_shipments (ProcessName, ShipmentTypeId) VALUES (@p, @s)",
                new { p = req.ProcessName.Trim(), s = req.ShipmentTypeId });

            return RedirectToAction(nameof(Index), new { success = $"Shipment Type {req.ShipmentTypeId} added to {req.ProcessName}." });
        }

        // ── POST /Configuration/DeleteShipmentExclusion ───────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteShipmentExclusion(string processName, int shipmentTypeId)
        {
            if (string.IsNullOrWhiteSpace(processName) || shipmentTypeId <= 0)
                return RedirectToAction(nameof(Index), new { error = "Invalid shipment exclusion key." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "DELETE FROM lcs_hr.hr_commission_excluded_shipments WHERE ProcessName = @p AND ShipmentTypeId = @s",
                new { p = processName, s = shipmentTypeId });

            return RedirectToAction(nameof(Index), new { success = $"Shipment Type {shipmentTypeId} removed from {processName}." });
        }

        // ── POST /Configuration/ToggleSysExclusion ────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSysExclusion(string exclusionType, string exclusionValue, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(exclusionType) || string.IsNullOrWhiteSpace(exclusionValue))
                return RedirectToAction(nameof(Index), new { error = "Invalid system exclusion key." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "UPDATE lcs_hr.hr_commission_system_exclusions SET IsActive = @a WHERE ExclusionType = @t AND ExclusionValue = @v",
                new { a = isActive ? 1 : 0, t = exclusionType, v = exclusionValue });

            return RedirectToAction(nameof(Index), new { success = "System exclusion status updated." });
        }

        // ── POST /Configuration/AddSysExclusion ───────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSysExclusion(AddSysExclusionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.ExclusionType) || string.IsNullOrWhiteSpace(req.ExclusionValue))
                return RedirectToAction(nameof(Index), new { error = "Exclusion Type and Value are required." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO lcs_hr.hr_commission_system_exclusions (ExclusionType, ExclusionValue, RuleType, IsActive) VALUES (@t, @v, @r, 1)",
                new { t = req.ExclusionType.Trim(), v = req.ExclusionValue.Trim(), r = req.RuleType.Trim() });

            return RedirectToAction(nameof(Index), new { success = $"System exclusion {req.ExclusionType}/{req.ExclusionValue} added." });
        }

        // ── POST /Configuration/DeleteSysExclusion ────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSysExclusion(string exclusionType, string exclusionValue)
        {
            if (string.IsNullOrWhiteSpace(exclusionType) || string.IsNullOrWhiteSpace(exclusionValue))
                return RedirectToAction(nameof(Index), new { error = "Invalid system exclusion key." });

            using var conn = OpenConnection();
            await conn.ExecuteAsync(
                "DELETE FROM lcs_hr.hr_commission_system_exclusions WHERE ExclusionType = @t AND ExclusionValue = @v",
                new { t = exclusionType, v = exclusionValue });

            return RedirectToAction(nameof(Index), new { success = $"System exclusion {exclusionType}/{exclusionValue} deleted." });
        }

        // ── POST /Configuration/DeleteRow ─────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRow(string table, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return RedirectToAction(nameof(Index), new { error = "Invalid key." });

            using var conn = OpenConnection();

            switch (table)
            {
                case "cod_clients":
                    await conn.ExecuteAsync(
                        "DELETE FROM lcs_hr.hr_cod_excluded_clients WHERE ClientId = @k",
                        new { k = key });
                    break;

                case "cod_cities":
                    await conn.ExecuteAsync(
                        "DELETE FROM lcs_hr.hr_commission_cod_threshold_cities WHERE CityCode = @k",
                        new { k = key });
                    break;

                case "vas":
                    if (!int.TryParse(key, out var vasId))
                        return RedirectToAction(nameof(Index), new { error = "Invalid VAS ID." });
                    await conn.ExecuteAsync(
                        "DELETE FROM lcs_hr.hr_commission_vas_exclusions WHERE VasId = @k",
                        new { k = vasId });
                    break;

                case "tax":
                    await conn.ExecuteAsync(
                        "DELETE FROM lcs_hr.hr_salary_tax_exclusions WHERE EmpNo = @k",
                        new { k = key });
                    break;

                default:
                    return RedirectToAction(nameof(Index), new { error = "Invalid table." });
            }

            return RedirectToAction(nameof(Index), new { success = "Record deleted." });
        }

        // ── POST /Configuration/RunSync ────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RunSync()
        {
            var result = await _syncService.RunSyncAsync();

            string? successMsg = null;
            string? errorMsg   = null;

            if (result.TotalErrors > 0)
                errorMsg = $"{result.TotalErrors} config error(s) found. " +
                           string.Join(" | ", result.Errors.Take(3));

            if (result.TotalChanges > 0)
                successMsg = $"Validation complete: {result.TotalChanges} change(s) applied.";
            else if (result.TotalWarnings > 0)
                successMsg = $"Validation complete: {result.TotalWarnings} warning(s). Check logs.";
            else if (result.TotalErrors == 0)
                successMsg = "All config tables validated successfully.";

            return RedirectToAction(nameof(Index),
                new { success = successMsg, error = errorMsg });
        }
    }
}
