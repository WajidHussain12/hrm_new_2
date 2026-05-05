using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Globalization;

namespace LCS_HR_MVC.Services
{
    public class DataSyncService : IDataSyncService
    {
        private readonly string _connectionString;
        private readonly ILogger<DataSyncService> _logger;

        public DataSyncService(
            IConfiguration configuration,
            ILogger<DataSyncService> logger)
        {
            _connectionString = configuration
                .GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "DefaultConnection not configured.");
            _logger = logger;
        }

        public async Task<DataSyncResult> RunSyncAsync(
            CancellationToken ct = default)
        {
            var result = new DataSyncResult();

            // ── RULE 0: Bootstrap Check ──────────────────────────────────────────
            // Ensures all commission tables exist with seed data.
            // If a table was accidentally dropped or data deleted,
            // this restores it before validation runs.
            try
            {
                await CommissionTablesBootstrapper
                    .EnsureAllTablesAsync(_connectionString, _logger);

                result.Log.Add(new SyncLogEntry
                {
                    Level   = "INFO",
                    Table   = "ALL",
                    Message = "Bootstrap check passed — all commission tables verified."
                });
                _logger.LogInformation("[Sync] Rule 0: Bootstrap check passed.");
            }
            catch (Exception ex)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "ERROR",
                    Table   = "ALL",
                    Message = $"Bootstrap failed: {ex.Message}"
                });
                _logger.LogError(ex, "[Sync] Rule 0: Bootstrap failed.");
            }

            // ── RULE 1: Holiday Validation ───────────────────────────────────────
            try
            {
                await ValidateHolidaysAsync(result, ct);
            }
            catch (Exception ex)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "ERROR",
                    Table   = "hr_setup_holidays",
                    Message = $"Holiday validation exception: {ex.Message}"
                });
                _logger.LogError(ex, "[Sync] Rule 1: Holiday check failed.");
            }

            // ── RULE 2: Commission Config Validation ─────────────────────────────
            try
            {
                await ValidateCommissionConfigAsync(result, ct);
            }
            catch (Exception ex)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "ERROR",
                    Table   = "hr_commission_config",
                    Message = $"Commission config exception: {ex.Message}"
                });
                _logger.LogError(ex, "[Sync] Rule 2: Commission config check failed.");
            }

            // ── RULE 3: Config Tables Row Count ──────────────────────────────────
            try
            {
                await ValidateConfigTableCountsAsync(result, ct);
            }
            catch (Exception ex)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "ERROR",
                    Table   = "MULTIPLE",
                    Message = $"Table count check exception: {ex.Message}"
                });
                _logger.LogError(ex, "[Sync] Rule 3: Table count check failed.");
            }

            // ── RULE 4: COD Excluded Clients Consistency ─────────────────────────
            try
            {
                await ValidateCodExcludedClientsAsync(result, ct);
            }
            catch (Exception ex)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "ERROR",
                    Table   = "hr_cod_excluded_clients",
                    Message = $"COD clients check exception: {ex.Message}"
                });
                _logger.LogError(ex, "[Sync] Rule 4: COD clients check failed.");
            }

            // ── RULE 5: Salary Config Validation ────────────────────────────────
            try
            {
                await ValidateSalaryConfigAsync(result, ct);
            }
            catch (Exception ex)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "ERROR",
                    Table   = "hr_salary_config",
                    Message = $"Salary config exception: {ex.Message}"
                });
                _logger.LogError(ex, "[Sync] Rule 5: Salary config check failed.");
            }

            // ── FINAL STATUS ──────────────────────────────────────────────────────
            result.Success = result.TotalErrors == 0;

            _logger.LogInformation(
                "[Sync] Complete — Changes:{c} Warnings:{w} Errors:{e}",
                result.TotalChanges,
                result.TotalWarnings,
                result.TotalErrors);

            return result;
        }

        // ════════════════════════════════════════════════════════════════════════
        // RULE 1 — HOLIDAY VALIDATION
        // ════════════════════════════════════════════════════════════════════════
        private async Task ValidateHolidaysAsync(
            DataSyncResult result,
            CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var now       = DateTime.Now;
            var thisYear  = now.Year;
            var thisMon   = now.Month;
            var nextDate  = now.AddMonths(1);
            var nextYear  = nextDate.Year;
            var nextMon   = nextDate.Month;

            const string sql =
                "SELECT COUNT(*) FROM lcs_hr.hr_setup_holidays " +
                "WHERE YEAR(HolidayDate) = @Y " +
                "  AND MONTH(HolidayDate) = @M";

            var countThis = await conn.ExecuteScalarAsync<int>(
                sql, new { Y = thisYear, M = thisMon });
            var countNext = await conn.ExecuteScalarAsync<int>(
                sql, new { Y = nextYear, M = nextMon });

            var thisMonthName = now.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            var nextMonthName = nextDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

            if (countThis == 0)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "WARN",
                    Table   = "hr_setup_holidays",
                    Message = $"No holidays configured for {thisMonthName}. " +
                              "Commission DateDif calculations may be incorrect. " +
                              "Add via: INSERT INTO hr_setup_holidays " +
                              "(HolidayDate) VALUES ('YYYY-MM-DD');"
                });
                _logger.LogWarning("[Sync] No holidays for {Month}.", thisMonthName);
            }

            if (countNext == 0)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "WARN",
                    Table   = "hr_setup_holidays",
                    Message = $"No holidays configured for next month ({nextMonthName}). " +
                              "Please verify before next commission cycle."
                });
            }

            if (countThis > 0 && countNext > 0)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "INFO",
                    Table   = "hr_setup_holidays",
                    Message = $"Holiday data OK — {thisMonthName}: {countThis} day(s), " +
                              $"{nextMonthName}: {countNext} day(s)."
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // RULE 2 — COMMISSION CONFIG VALIDATION
        // ════════════════════════════════════════════════════════════════════════
        private async Task ValidateCommissionConfigAsync(
            DataSyncResult result,
            CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var rows = (await conn.QueryAsync<(string Key, string Value)>(
                "SELECT ConfigKey, ConfigValue FROM lcs_hr.hr_commission_config")).ToList();

            var dict = rows.ToDictionary(
                r => r.Key,
                r => r.Value,
                StringComparer.OrdinalIgnoreCase);

            // Check required keys exist
            var requiredKeys = new[]
            {
                "COMMISSION_START_DAY", "COMMISSION_END_DAY",
                "RBI_CAP", "MIN_GUARANTEE_AMOUNT",
                "MIN_GUARANTEE_WORKING_DAYS",
                "COD_BONUS_SLAB1_MAX_CN", "COD_BONUS_SLAB2_MAX_CN",
                "COD_BONUS_SLAB1_RATE", "COD_BONUS_SLAB2_RATE",
                "COD_BONUS_SLAB3_RATE", "CEB_SPLIT_DATE",
                "AUTOMATION_MAX_RETRIES", "AUTOMATION_USER_ID"
            };

            foreach (var key in requiredKeys)
            {
                if (!dict.ContainsKey(key))
                {
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "WARN",
                        Table   = "hr_commission_config",
                        Message = $"Config key '{key}' not found. Default value will be used."
                    });
                }
            }

            // COMMISSION_START_DAY: 1-28
            if (dict.TryGetValue("COMMISSION_START_DAY", out var sdVal))
            {
                if (!int.TryParse(sdVal, out var sd) || sd < 1 || sd > 28)
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = "hr_commission_config",
                        Message = $"COMMISSION_START_DAY value '{sdVal}' is outside valid range 1-28."
                    });
            }

            // COMMISSION_END_DAY: 1-28
            if (dict.TryGetValue("COMMISSION_END_DAY", out var edVal))
            {
                if (!int.TryParse(edVal, out var ed) || ed < 1 || ed > 28)
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = "hr_commission_config",
                        Message = $"COMMISSION_END_DAY value '{edVal}' is outside valid range 1-28."
                    });
            }

            // RBI_CAP: decimal > 0
            if (dict.TryGetValue("RBI_CAP", out var rbiVal))
            {
                if (!decimal.TryParse(rbiVal, out var rbi) || rbi <= 0)
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = "hr_commission_config",
                        Message = $"RBI_CAP value '{rbiVal}' must be > 0."
                    });
            }

            // MIN_GUARANTEE_AMOUNT: decimal > 0
            if (dict.TryGetValue("MIN_GUARANTEE_AMOUNT", out var mgaVal))
            {
                if (!decimal.TryParse(mgaVal, out var mga) || mga <= 0)
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = "hr_commission_config",
                        Message = $"MIN_GUARANTEE_AMOUNT value '{mgaVal}' must be > 0."
                    });
            }

            // MIN_GUARANTEE_WORKING_DAYS: int 1-31
            if (dict.TryGetValue("MIN_GUARANTEE_WORKING_DAYS", out var wdVal))
            {
                if (!int.TryParse(wdVal, out var wd) || wd < 1 || wd > 31)
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = "hr_commission_config",
                        Message = $"MIN_GUARANTEE_WORKING_DAYS value '{wdVal}' must be between 1 and 31."
                    });
            }

            // COD Slab logical order: slab1 < slab2
            if (dict.TryGetValue("COD_BONUS_SLAB1_MAX_CN", out var s1v) &&
                dict.TryGetValue("COD_BONUS_SLAB2_MAX_CN", out var s2v) &&
                int.TryParse(s1v, out var slab1Max) &&
                int.TryParse(s2v, out var slab2Max) &&
                slab1Max > 0 && slab2Max > 0 && slab1Max >= slab2Max)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "WARN",
                    Table   = "hr_commission_config",
                    Message = $"COD_BONUS_SLAB1_MAX_CN ({slab1Max}) should be " +
                              $"less than COD_BONUS_SLAB2_MAX_CN ({slab2Max})."
                });
            }

            // AUTOMATION_MAX_RETRIES: int 1-10
            if (dict.TryGetValue("AUTOMATION_MAX_RETRIES", out var arVal))
            {
                if (!int.TryParse(arVal, out var ar) || ar < 1 || ar > 10)
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "WARN",
                        Table   = "hr_commission_config",
                        Message = $"AUTOMATION_MAX_RETRIES value '{arVal}' recommended range is 1-10."
                    });
            }

            // CEB_SPLIT_DATE: valid yyyy-MM-dd
            if (dict.TryGetValue("CEB_SPLIT_DATE", out var cebVal))
            {
                if (!DateTime.TryParseExact(cebVal, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = "hr_commission_config",
                        Message = $"CEB_SPLIT_DATE value '{cebVal}' is not a valid date (expected yyyy-MM-dd)."
                    });
            }

            // All-OK message (only if no errors or warnings for this table)
            if (!result.Log.Any(e =>
                e.Table == "hr_commission_config" &&
                (e.Level == "ERROR" || e.Level == "WARN")))
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "INFO",
                    Table   = "hr_commission_config",
                    Message = $"Commission config OK — {rows.Count} keys validated."
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // RULE 3 — CONFIG TABLES ROW COUNT VALIDATION
        // ════════════════════════════════════════════════════════════════════════
        private async Task ValidateConfigTableCountsAsync(
            DataSyncResult result,
            CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var checks = new[]
            {
                (
                    Table   : "hr_commission_excluded_shipments",
                    Sql     : "SELECT COUNT(*) FROM lcs_hr.hr_commission_excluded_shipments",
                    IsError : true,
                    EmptyMsg: "hr_commission_excluded_shipments is empty! " +
                              "Commission will include ALL shipment types. Insert rules immediately."
                ),
                (
                    Table   : "hr_commission_system_exclusions",
                    Sql     : "SELECT COUNT(*) FROM lcs_hr.hr_commission_system_exclusions",
                    IsError : true,
                    EmptyMsg: "hr_commission_system_exclusions is empty! " +
                              "System exclusions (express IDs, location types) not configured."
                ),
                (
                    Table   : "hr_commission_type_mapping",
                    Sql     : "SELECT COUNT(*) FROM lcs_hr.hr_commission_type_mapping",
                    IsError : true,
                    EmptyMsg: "hr_commission_type_mapping is empty! " +
                              "OLE RateID to column mapping missing. OverLand commission will fail."
                ),
                (
                    Table   : "hr_commission_codetype_rules",
                    Sql     : "SELECT COUNT(*) FROM lcs_hr.hr_commission_codetype_rules",
                    IsError : true,
                    EmptyMsg: "hr_commission_codetype_rules is empty! CodeType filters not configured."
                ),
                (
                    Table   : "hr_commission_cod_threshold_cities",
                    Sql     : "SELECT COUNT(*) FROM lcs_hr.hr_commission_cod_threshold_cities",
                    IsError : false,
                    EmptyMsg: "No COD threshold cities configured. " +
                              "All cities will use default 80% threshold."
                ),
            };

            foreach (var chk in checks)
            {
                try
                {
                    var count = await conn.ExecuteScalarAsync<int>(chk.Sql);

                    if (count == 0)
                    {
                        result.Log.Add(new SyncLogEntry
                        {
                            Level   = chk.IsError ? "ERROR" : "WARN",
                            Table   = chk.Table,
                            Message = chk.EmptyMsg
                        });
                    }
                    else
                    {
                        result.Log.Add(new SyncLogEntry
                        {
                            Level   = "INFO",
                            Table   = chk.Table,
                            Message = $"{chk.Table}: {count} row(s) — OK."
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = chk.Table,
                        Message = $"Count check failed for {chk.Table}: {ex.Message}"
                    });
                }
            }

            // hr_commission_type_mapping extra check: expected ~89 rows
            try
            {
                var mapCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM lcs_hr.hr_commission_type_mapping");
                if (mapCount > 0 && mapCount < 80)
                {
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "WARN",
                        Table   = "hr_commission_type_mapping",
                        Message = $"hr_commission_type_mapping has only {mapCount} rows. " +
                                  "Expected ~89. Some RateIDs may be unmapped."
                    });
                }
            }
            catch { /* already covered in the main checks above */ }

            // hr_commission_vas_exclusions: active-only count
            try
            {
                var vasActive = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM lcs_hr.hr_commission_vas_exclusions WHERE IsActive = 1");
                if (vasActive == 0)
                {
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "WARN",
                        Table   = "hr_commission_vas_exclusions",
                        Message = "No active VAS exclusions. " +
                                  "All VAS types will be included in cash commission calculation."
                    });
                }
                else
                {
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "INFO",
                        Table   = "hr_commission_vas_exclusions",
                        Message = $"{vasActive} active VAS exclusion(s) — OK."
                    });
                }
            }
            catch (Exception ex)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "WARN",
                    Table   = "hr_commission_vas_exclusions",
                    Message = $"VAS exclusion check failed: {ex.Message}"
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // RULE 4 — COD EXCLUDED CLIENTS CONSISTENCY CHECK
        // ════════════════════════════════════════════════════════════════════════
        private async Task ValidateCodExcludedClientsAsync(
            DataSyncResult result,
            CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // a) Active count
            var activeCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM lcs_hr.hr_cod_excluded_clients WHERE IsActive = 1");

            result.Log.Add(new SyncLogEntry
            {
                Level   = "INFO",
                Table   = "hr_cod_excluded_clients",
                Message = $"{activeCount} active COD excluded client(s) configured."
            });

            // b) Duplicate check
            var dupes = (await conn.QueryAsync<string>(
                "SELECT ClientId FROM lcs_hr.hr_cod_excluded_clients " +
                "GROUP BY ClientId HAVING COUNT(*) > 1")).ToList();

            if (dupes.Any())
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "WARN",
                    Table   = "hr_cod_excluded_clients",
                    Message = "Duplicate ClientId(s) found: " +
                              string.Join(", ", dupes) +
                              ". Review and remove duplicates."
                });
            }

            // c) Auto-sync note
            result.Log.Add(new SyncLogEntry
            {
                Level   = "INFO",
                Table   = "hr_cod_excluded_clients",
                Message = "Auto-sync from lcs_billing.client skipped. " +
                          "Business rule not yet defined. " +
                          "HR manages this list manually via Admin Config."
            });
        }

        // ════════════════════════════════════════════════════════════════════════
        // RULE 5 — SALARY CONFIG VALIDATION
        // ════════════════════════════════════════════════════════════════════════
        private async Task ValidateSalaryConfigAsync(
            DataSyncResult result,
            CancellationToken ct)
        {
            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var rows = (await conn.QueryAsync<(string Key, string Value)>(
                "SELECT ConfigKey, ConfigValue FROM lcs_hr.hr_salary_config")).ToList();

            var dict = rows.ToDictionary(
                r => r.Key,
                r => r.Value,
                StringComparer.OrdinalIgnoreCase);

            // Int range checks
            var intRangeChecks = new[]
            {
                (Key:"LEAVE_YEAR_START_MONTH",      Min:1,  Max:12,    Desc:"Month 1-12"),
                (Key:"LEAVE_YEAR_START_DAY",        Min:1,  Max:28,    Desc:"Day 1-28"),
                (Key:"SALARY_DAYS_DIVISOR",         Min:1,  Max:31,    Desc:"Must be > 0"),
                (Key:"TAX_SALARY_GLCODE",           Min:1,  Max:99999, Desc:"Must be > 0"),
                (Key:"LEAVE_MONTHS_MIN_FOR_CASHOUT",Min:1,  Max:24,    Desc:"Range 1-24"),
            };

            foreach (var chk in intRangeChecks)
            {
                if (!dict.TryGetValue(chk.Key, out var val))
                {
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "WARN",
                        Table   = "hr_salary_config",
                        Message = $"Salary config key '{chk.Key}' not found. Default value will be used."
                    });
                    continue;
                }
                if (!int.TryParse(val, out var iv) || iv < chk.Min || iv > chk.Max)
                {
                    result.Log.Add(new SyncLogEntry
                    {
                        Level   = "ERROR",
                        Table   = "hr_salary_config",
                        Message = $"Salary config '{chk.Key}' value '{val}' is invalid ({chk.Desc})."
                    });
                }
            }

            // LEAVE_ENCASHMENT_GROSS_THRESHOLD: decimal > 0
            if (!dict.TryGetValue("LEAVE_ENCASHMENT_GROSS_THRESHOLD", out var thVal))
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "WARN",
                    Table   = "hr_salary_config",
                    Message = "Salary config key 'LEAVE_ENCASHMENT_GROSS_THRESHOLD' not found. " +
                              "Default value will be used."
                });
            }
            else if (!decimal.TryParse(thVal, out var th) || th <= 0)
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "ERROR",
                    Table   = "hr_salary_config",
                    Message = $"LEAVE_ENCASHMENT_GROSS_THRESHOLD value '{thVal}' must be > 0."
                });
            }

            // All-OK message (only if no errors or warnings for this table)
            if (!result.Log.Any(e =>
                e.Table == "hr_salary_config" &&
                (e.Level == "ERROR" || e.Level == "WARN")))
            {
                result.Log.Add(new SyncLogEntry
                {
                    Level   = "INFO",
                    Table   = "hr_salary_config",
                    Message = $"Salary config OK — {rows.Count} keys validated."
                });
            }
        }
    }
}
