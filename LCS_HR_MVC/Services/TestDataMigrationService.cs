using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Hangfire;
using LCS_HR_MVC.Hubs;
using LCS_HR_MVC.Models;
using Microsoft.AspNetCore.SignalR;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class TestDataMigrationService : ITestDataMigrationService
    {
        // ── In-memory state (static so Hangfire-resolved instances share it) ─────
        private static readonly ConcurrentDictionary<string, MigrationStatusViewModel> _state = new();
        private static readonly object _stateLock = new();

        // ── In-process execution lock (SemaphoreSlim(1,1)) ────────────────────────
        // Guarantees only ONE RunMigrationAsync runs at a time regardless of how many
        // Hangfire jobs are queued or how many app instances exist.
        // WaitAsync(0) = non-blocking: a second job attempting to run will immediately
        // see the lock is held and abort itself without touching state.
        private static readonly SemaphoreSlim _executionLock = new(1, 1);

        private const int BatchSize = 500;
        private const int CnChunkSize = 1000;

        // ── Dependencies ──────────────────────────────────────────────────────────
        private readonly IConfiguration _configuration;
        private readonly ILogger<TestDataMigrationService> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IHubContext<DataMigrationHub> _hub;

        // ── Connection strings resolved at construction time ───────────────────
        private readonly Dictionary<string, string> _liveConnections;
        private readonly string _localConnStr; // root@localhost:3309 — full access

        public TestDataMigrationService(
            IConfiguration configuration,
            ILogger<TestDataMigrationService> logger,
            IBackgroundJobClient backgroundJobClient,
            IHubContext<DataMigrationHub> hub)
        {
            _configuration = configuration;
            _logger = logger;
            _backgroundJobClient = backgroundJobClient;
            _hub = hub;

            // Live connections (read-only, from "LiveConnections" section)
            var liveSection = _configuration.GetSection("LiveConnections");
            _liveConnections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in liveSection.GetChildren())
                _liveConnections[child.Key] = child.Value ?? "";

            // Local connection — use DefaultConnection (root has full access)
            _localConnStr = _configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection not found.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Table specifications
        // ─────────────────────────────────────────────────────────────────────────
        private static List<MigrationTableSpec> GetTableSpecs() => new()
        {
            // ── SOURCE (date-filtered) ────────────────────────────────────────────
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs_billing",          TableName = "billing_details",               DateField = "BILLING_DATE", Category = "Source",     Label = "billing_details",               CommissionGroup = "Cash",     CityColumn = "dest_City_id" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs_billing",          TableName = "billing_details_hist",          DateField = "BILLING_DATE", Category = "Source",     Label = "billing_details_hist",          CommissionGroup = "Cash",     CityColumn = "dest_City_id" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "rms_cod_booking",               DateField = "Book_date",    Category = "Source",     Label = "rms_cod_booking",               CommissionGroup = "Cash",     CityColumn = "origin_id" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs_eshipment",        TableName = "rms_cod_booking_dropship",      DateField = "created_date", Category = "Source",     Label = "rms_cod_booking_dropship",      CommissionGroup = "Cash",     CityColumn = "destination_city_id" },
            new() { LiveConnection = "Central_OPS",    SchemaDb = "lcs_db",               TableName = "arival",                        DateField = "COUR_DATE",    Category = "Source",     Label = "arival",                        CommissionGroup = "COD",      CityColumn = "Arvl_Origin" },
            new() { LiveConnection = "Central_OPS",    SchemaDb = "lcs_db",               TableName = "book_dispatch",                 DateField = "BOOK_DATE",    Category = "Source",     Label = "book_dispatch",                 CommissionGroup = "OverLand", CityColumn = "dest_City_id" },
            new() { LiveConnection = "MIS",            SchemaDb = "lcs_billing_download", TableName = "billing_details",               DateField = "billing_date", Category = "Source",     Label = "billing_details (MIS)",         CommissionGroup = "OverLand", CityColumn = "dest_City_id" },

            // ── LOOKUP (full truncate + copy) ─────────────────────────────────────
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs_billing",          TableName = "shipment_codes",                Category = "Lookup",     Label = "shipment_codes",                CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs_billing",          TableName = "zone_color",                    Category = "Lookup",     Label = "zone_color",                    CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "city",                          Category = "Lookup",     Label = "city (lcs)",                    CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "rms_vas",                       Category = "Lookup",     Label = "rms_vas",                       CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "rms_noncore_vendors",           Category = "Lookup",     Label = "rms_noncore_vendors",           CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "rms_mtd_times",                 Category = "Lookup",     Label = "rms_mtd_times",                 CommissionGroup = "Cash" },
            new() { LiveConnection = "Central_OPS",    SchemaDb = "lcs_db",               TableName = "cod_ranges",                    Category = "Lookup",     Label = "cod_ranges",                    CommissionGroup = "COD" },
            new() { LiveConnection = "Central_OPS",    SchemaDb = "lcs_db",               TableName = "city",                          Category = "Lookup",     Label = "city (lcs_db)",                 CommissionGroup = "COD" },
            new() { LiveConnection = "Central_OPS",    SchemaDb = "lcs_setup",            TableName = "locations",                     Category = "Lookup",     Label = "locations (lcs_setup)",         CommissionGroup = "Shared" },
            new() { LiveConnection = "MIS",            SchemaDb = "lcs_billing_download", TableName = "rbi_clients",                   Category = "Lookup",     Label = "rbi_clients",                   CommissionGroup = "OverLand" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "hr_city",                       Category = "Lookup",     Label = "hr_city",                       CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "hr_locationmapping",            Category = "Lookup",     Label = "hr_locationmapping",            CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "hr_commissionpolicy",           Category = "Lookup",     Label = "hr_commissionpolicy",           CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "hr_employeeroutecode",          Category = "Lookup",     Label = "hr_employeeroutecode",          CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "hr_employeepersonaldetail",     Category = "Lookup",     Label = "hr_employeepersonaldetail",     CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "hr_employeelocationdetails",    Category = "Lookup",     Label = "hr_employeelocationdetails",    CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "couriercodetype",               Category = "Lookup",     Label = "couriercodetype",               CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "hr_routecodes_hdr",             Category = "Lookup",     Label = "hr_routecodes_hdr",             CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",            TableName = "lcs_company",                   Category = "Lookup",     Label = "lcs_company",                   CommissionGroup = "Shared" },

            // ── COMMISSION-CRITICAL LOOKUPS (missing from original list) ─────────
            // Client master — INNER JOINed in CashCommission (lcs_billing) and OverLandCommission (lcs_billing_download)
            new() { LiveConnection = "LHR_Billing",       SchemaDb = "lcs_billing",          TableName = "client",                        Category = "Lookup",     Label = "client (lcs_billing)",              CommissionGroup = "Cash" },
            new() { LiveConnection = "MIS",               SchemaDb = "lcs_billing_download", TableName = "client",                        Category = "Lookup",     Label = "client (lcs_billing_download)",     CommissionGroup = "OverLand" },
            // shipment_codes in lcs_billing_download — INNER JOINed in OverLandCommission candidate query
            new() { LiveConnection = "MIS",               SchemaDb = "lcs_billing_download", TableName = "shipment_codes",                Category = "Lookup",     Label = "shipment_codes (lcs_billing_download)", CommissionGroup = "OverLand" },
            // VAS client table — INNER JOINed in CashCommission VAS processing
            new() { LiveConnection = "Central_OPS",       SchemaDb = "lcs_db",               TableName = "client_vas",                    Category = "Lookup",     Label = "client_vas (lcs_db)",               CommissionGroup = "Cash" },
            // CommissionProcess master-data tables
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",               TableName = "hr_empcommissioneligibility",    Category = "Lookup",     Label = "hr_empcommissioneligibility",       CommissionGroup = "Master" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",               TableName = "hr_employeedepartmentdetails",   Category = "Lookup",     Label = "hr_employeedepartmentdetails",      CommissionGroup = "Master" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",               TableName = "hr_subdepartment",              Category = "Lookup",     Label = "hr_subdepartment",                  CommissionGroup = "Master" },
            // User-city access mapping — queried in CommissionProcess city-permission check
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",               TableName = "lcs_user_location",             Category = "Lookup",     Label = "lcs_user_location",                 CommissionGroup = "Shared" },
            // CN exclusion list — used in OverLandCommission NOT EXISTS filter
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr",               TableName = "temp_cn",                       Category = "Lookup",     Label = "temp_cn (lcs_hr)",                  CommissionGroup = "OverLand" },

            // ── YEAR-MONTH FILTERED (filtered by integer Year + Month columns) ──────
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr", TableName = "hr_closeprocesses",           YearField = "Year",        MonthField = "Month",       Category = "YearMonth", Label = "hr_closeprocesses",           CommissionGroup = "Shared" },
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr", TableName = "hr_salaryprocessed_hdr",      YearField = "SalaryYear",  MonthField = "SalaryMonth", Category = "YearMonth", Label = "hr_salaryprocessed_hdr",      CommissionGroup = "Master" },
            // Attendance data used in CommissionProcess earnings/deduction calculation
            new() { LiveConnection = "DefaultConnection", SchemaDb = "lcs_hr", TableName = "hr_employeeattendanceprocess", YearField = "Year",       MonthField = "Month",       Category = "YearMonth", Label = "hr_employeeattendanceprocess", CommissionGroup = "Master" },

            // ── CN-FILTERED (filtered by CN list collected from source tables) ──
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "booking_info",                  CnColumn = "CN_Number",  Category = "CnFiltered", Label = "booking_info",                  CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "rms_mtd_booking",               CnColumn = "CN_Number",  Category = "CnFiltered", Label = "rms_mtd_booking",               CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "rms_vas_booking_detail",        CnColumn = "CN_Number",  Category = "CnFiltered", Label = "rms_vas_booking_detail",        CommissionGroup = "Cash" },
            new() { LiveConnection = "LHR_Billing",    SchemaDb = "lcs",                  TableName = "rms_service_attest_booking",    CnColumn = "CN_Number",  Category = "CnFiltered", Label = "rms_service_attest_booking",    CommissionGroup = "Cash" },
            new() { LiveConnection = "Central_OPS",    SchemaDb = "lcs_db",               TableName = "cod_download",                  CnColumn = "CN_NUMBER",  Category = "CnFiltered", Label = "cod_download",                  CommissionGroup = "COD" },
            new() { LiveConnection = "Central_OPS",    SchemaDb = "lcs_db",               TableName = "oms_cod_download",              CnColumn = "CN_NUMBER",  Category = "CnFiltered", Label = "oms_cod_download",              CommissionGroup = "COD" },
        };

        // Destination tables to check (all in lcs_hr local)
        private static readonly List<string> DestinationTables = new()
        {
            "hr_cash_consignments",
            "hr_vas_incentive_detail",
            "hr_cod_consignments",
            "hr_all_cod_consignment",
            "cod_returnshipments",
            "hr_codcommission",
            "hr_olecommission",
            "hr_RBI_Incentive_Detail",
            "hr_olecommissionprocess",
            "hr_codreturn_consignments",
            "hr_codreturncommission",
            "hr_codreturncommissionprocess",
            "hr_commissionprocess",
            "hr_empcommadjdtl",
            "hr_acknowledgment",
            "hr_commission_automation_log",
        };

        // ─────────────────────────────────────────────────────────────────────────
        // Public interface
        // ─────────────────────────────────────────────────────────────────────────

        public Task<string> StartMigrationAsync(int year, int month, int rowLimit, List<string>? cities = null)
        {
            var key = StateKey(year, month);

            // ── Guard 1: state says Running for this period ───────────────────────
            if (_state.TryGetValue(key, out var existing) && existing.OverallStatus == "Running")
                throw new InvalidOperationException(
                    $"Migration for {year}/{month:D2} is already running (started {existing.StartedAt}). " +
                    "Wait for it to finish before starting a new one.");

            // ── Guard 2: execution lock is held — a job is ACTIVELY executing ─────
            // This catches the case where the state was reset (e.g., a prior job
            // finished with "Failed") but another concurrent job is still mid-flight.
            if (_executionLock.CurrentCount == 0)
                throw new InvalidOperationException(
                    "A migration job is actively executing right now. " +
                    "Wait for it to complete before starting a new one.");

            var specs = GetTableSpecs();
            var (fromDate, toDate) = GetDateRange(year, month);
            var cityFilter = cities?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();

            var vm = new MigrationStatusViewModel
            {
                Year           = year,
                Month          = month,
                RequiredFrom   = fromDate,
                RequiredTo     = toDate,
                RowLimit       = rowLimit,
                OverallStatus  = "Running",
                StartedAt      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalTables    = specs.Count,
                SelectedCities = cityFilter,
                TableResults   = specs.Select(s => new TableMigrationResult
                {
                    Label           = s.Label,
                    FullTableName   = $"`{s.SchemaDb}`.`{s.TableName}`",
                    Category        = s.Category,
                    LiveConnection  = s.LiveConnection,
                    CommissionGroup = s.CommissionGroup,
                    Status          = "Pending",
                }).ToList(),
                DestinationChecks = DestinationTables.Select(t => new DestinationTableCheck
                {
                    FullTableName = $"lcs_hr.{t}",
                }).ToList(),
            };

            _state[key] = vm;

            var jobId = _backgroundJobClient.Enqueue<ITestDataMigrationService>(
                svc => svc.RunMigrationAsync(year, month, rowLimit, cityFilter));

            return Task.FromResult(jobId);
        }

        public async Task<MigrationStatusViewModel> GetCurrentStatusAsync(int year, int month)
        {
            var cities = await GetAvailableCitiesAsync();

            if (_state.TryGetValue(StateKey(year, month), out var existing))
            {
                existing.AvailableCities = cities;
                return existing;
            }

            var (from, to) = GetDateRange(year, month);
            return new MigrationStatusViewModel
            {
                Year            = year,
                Month           = month,
                RequiredFrom    = from,
                RequiredTo      = to,
                OverallStatus   = "Idle",
                TotalTables     = GetTableSpecs().Count,
                AvailableCities = cities,
                TableResults    = GetTableSpecs().Select(s => new TableMigrationResult
                {
                    Label           = s.Label,
                    FullTableName   = $"`{s.SchemaDb}`.`{s.TableName}`",
                    Category        = s.Category,
                    LiveConnection  = s.LiveConnection,
                    CommissionGroup = s.CommissionGroup,
                    Status          = "Pending",
                }).ToList(),
                DestinationChecks = DestinationTables.Select(t => new DestinationTableCheck
                {
                    FullTableName = $"lcs_hr.{t}",
                }).ToList(),
            };
        }

        public async Task<List<CityOption>> GetAvailableCitiesAsync()
        {
            try
            {
                if (!_liveConnections.TryGetValue("Central_OPS", out var liveConnStr)
                    || string.IsNullOrEmpty(liveConnStr))
                    return new List<CityOption>();

                using var liveConn = OpenLive(liveConnStr);
                var rows = await liveConn.QueryAsync<(string Id, string Name)>(
                    "SELECT CITY_ID, CITY_NAME FROM `lcs_db`.`city` WHERE inactive = 0 ORDER BY CITY_NAME");
                return rows.Select(r => new CityOption { Id = r.Id, Name = r.Name }).ToList();
            }
            catch
            {
                return new List<CityOption>();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Destination table helpers (used by DestinationTables view)
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<List<DestinationTableCheck>> GetDestinationTablesStatusAsync()
        {
            var checks = DestinationTables.Select(t => new DestinationTableCheck
            {
                FullTableName = $"lcs_hr.{t}",
            }).ToList();

            using var localConn = OpenLocal();
            foreach (var check in checks)
            {
                var tableName = check.FullTableName.Split('.').Last();
                try
                {
                    var exists = await localConn.ExecuteScalarAsync<long>(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'lcs_hr' AND TABLE_NAME = @t",
                        new { t = tableName });
                    check.Exists = exists > 0;

                    if (check.Exists)
                    {
                        check.RowCount = await localConn.ExecuteScalarAsync<long>(
                            $"SELECT COUNT(*) FROM `lcs_hr`.`{tableName}`");
                    }
                }
                catch
                {
                    check.Exists   = false;
                    check.RowCount = -1;
                }
            }

            return checks;
        }

        public async Task<(int created, int skipped, int failed, List<string> messages)> CreateDestinationTablesAsync()
        {
            int created = 0, skipped = 0, failed = 0;
            var messages = new List<string>();

            if (!_liveConnections.TryGetValue("DefaultConnection", out var liveConnStr) || string.IsNullOrEmpty(liveConnStr))
            {
                messages.Add("ERROR: LiveConnections:DefaultConnection is not configured.");
                return (0, 0, 1, messages);
            }

            using var localConn = OpenLocal();
            await localConn.ExecuteAsync("CREATE DATABASE IF NOT EXISTS `lcs_hr`");
            await localConn.ExecuteAsync("SET FOREIGN_KEY_CHECKS=0");

            foreach (var tableName in DestinationTables)
            {
                try
                {
                    var exists = await localConn.ExecuteScalarAsync<long>(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'lcs_hr' AND TABLE_NAME = @t",
                        new { t = tableName });

                    if (exists > 0)
                    {
                        skipped++;
                        messages.Add($"[SKIP] {tableName} — already exists.");
                        continue;
                    }

                    string ddl;
                    using (var liveConn = OpenLive(liveConnStr))
                    {
                        ddl = await GetCreateTableDdlAsync(liveConn, "lcs_hr", tableName);
                    }

                    var localDdl = PatchDdl(ddl, "lcs_hr", tableName);
                    await localConn.ExecuteAsync(localDdl);
                    created++;
                    messages.Add($"[CREATED] {tableName}");
                }
                catch (Exception ex)
                {
                    failed++;
                    messages.Add($"[FAILED] {tableName}: {ex.Message}");
                    _logger.LogError(ex, "CreateDestinationTables failed for {Table}", tableName);
                }
            }

            await localConn.ExecuteAsync("SET FOREIGN_KEY_CHECKS=1");
            return (created, skipped, failed, messages);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Main migration worker (called by Hangfire)
        // ─────────────────────────────────────────────────────────────────────────

        // One job at a time (across all year/month) — prevents two Hangfire workers
        // running the same migration concurrently.
        [DisableConcurrentExecution(timeoutInSeconds: 0)]   // Hangfire-level: fail-fast if locked
        [AutomaticRetry(Attempts = 0)]                      // No Hangfire retries on interruption
        public async Task RunMigrationAsync(int year, int month, int rowLimit, List<string>? cities = null)
        {
            // ── In-process lock: abort immediately if another job is already running ──
            // This is the reliable guard. [DisableConcurrentExecution] only covers the
            // Hangfire distributed layer; this SemaphoreSlim covers the in-process layer.
            if (!await _executionLock.WaitAsync(0))
            {
                _logger.LogWarning(
                    "RunMigrationAsync({Year}/{Month}): aborted — another job is already executing.",
                    year, month);
                return;
            }

            var cityFilter = cities?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
            var key = StateKey(year, month);
            var vm  = _state.GetOrAdd(key, _ =>
            {
                var (f, t) = GetDateRange(year, month);
                return new MigrationStatusViewModel
                {
                    Year = year, Month = month,
                    RequiredFrom = f, RequiredTo = t,
                    RowLimit = rowLimit,
                    OverallStatus  = "Running",
                    StartedAt      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    SelectedCities = cityFilter,
                    TotalTables    = GetTableSpecs().Count,
                    TableResults   = GetTableSpecs().Select(s => new TableMigrationResult
                    {
                        Label           = s.Label,
                        FullTableName   = $"`{s.SchemaDb}`.`{s.TableName}`",
                        Category        = s.Category,
                        LiveConnection  = s.LiveConnection,
                        CommissionGroup = s.CommissionGroup,
                        Status          = "Pending",
                    }).ToList(),
                    DestinationChecks = DestinationTables.Select(t2 => new DestinationTableCheck
                    {
                        FullTableName = $"lcs_hr.{t2}",
                    }).ToList(),
                };
            });

            try
            {
                vm.RowLimit       = rowLimit;
                vm.SelectedCities = cityFilter;
                vm.OverallStatus  = "Running";
                vm.StartedAt      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                var (fromDate, toDate) = GetDateRange(year, month);
                var cityLabel = cityFilter.Count > 0 ? $" [Cities: {string.Join(", ", cityFilter)}]" : " [All Cities]";
                Log(vm, $"Migration started — period: {fromDate:yyyy-MM-dd} → {toDate:yyyy-MM-dd}{cityLabel}");

                // ── Phase 1: Check destination tables ─────────────────────────
                Log(vm, "Checking destination tables…");
                await CheckDestinationTablesAsync(vm);
                Log(vm, $"Destination check complete — {vm.DestinationChecks.Count(d => d.IsReady)} / {vm.DestinationChecks.Count} tables ready.");

                // ── Phase 2: Collect CNs needed for CN-filtered tables ─────────
                Log(vm, "Collecting CN list from billing_details and arival…");
                var cnSet = await CollectCnSetAsync(vm, fromDate, toDate, cityFilter);
                Log(vm, $"Collected {cnSet.Count:N0} unique CNs.");

                // ── Phase 3: Migrate each table ───────────────────────────────
                var specs = GetTableSpecs();
                foreach (var spec in specs)
                {
                    var result = vm.TableResults.First(r => r.FullTableName == $"`{spec.SchemaDb}`.`{spec.TableName}`" && r.Label == spec.Label);

                    // Resume: skip tables already completed in a prior attempt
                    if (result.Status == "Done")
                    {
                        Log(vm, $"[{spec.Label}] Already Done — skipping.");
                        continue;
                    }

                    // Reset a previously Failed result for a clean retry
                    if (result.Status == "Failed")
                    {
                        result.Status = "Pending";
                        result.Error  = null;
                    }

                    await MigrateTableAsync(spec, result, vm, fromDate, toDate, cnSet, vm.RowLimit, cityFilter);
                }

                var failCount = vm.TableResults.Count(r => r.Status == "Failed");
                vm.OverallStatus = failCount == 0 ? "Done" : "Failed";
                vm.CompletedAt   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                vm.DoneTables    = vm.TableResults.Count(r => r.Status == "Done");
                vm.FailedTables  = failCount;
                Log(vm, $"Migration {vm.OverallStatus}. Done: {vm.DoneTables}, Failed: {failCount}.");
                await PushOverallStatusAsync(vm);
            }
            catch (Exception ex)
            {
                vm.OverallStatus = "Failed";
                vm.CompletedAt   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Log(vm, $"FATAL: {ex.Message}");
                await PushOverallStatusAsync(vm);
                _logger.LogError(ex, "Migration RunMigrationAsync failed for {Year}/{Month}", year, month);
            }
            finally
            {
                // Always release the lock so the next migration can start
                _executionLock.Release();
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Destination table check
        // ─────────────────────────────────────────────────────────────────────────

        private async Task CheckDestinationTablesAsync(MigrationStatusViewModel vm)
        {
            using var localConn = OpenLocal();

            foreach (var check in vm.DestinationChecks)
            {
                var tableName = check.FullTableName.Split('.').Last();
                try
                {
                    var exists = await localConn.ExecuteScalarAsync<long>(
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'lcs_hr' AND TABLE_NAME = @t",
                        new { t = tableName });
                    check.Exists = exists > 0;

                    if (check.Exists)
                    {
                        if (tableName == "hr_commission_automation_log")
                        {
                            // Check only for the migration year/month
                            check.RowCount = await localConn.ExecuteScalarAsync<long>(
                                "SELECT COUNT(*) FROM `lcs_hr`.`hr_commission_automation_log` WHERE YEAR(created_at) = @y AND MONTH(created_at) = @m",
                                new { y = vm.Year, m = vm.Month });
                        }
                        else
                        {
                            check.RowCount = await localConn.ExecuteScalarAsync<long>(
                                $"SELECT COUNT(*) FROM `lcs_hr`.`{tableName}`");
                        }
                    }
                }
                catch (Exception ex)
                {
                    check.Exists   = false;
                    check.RowCount = -1;
                    Log(vm, $"  WARN: destination check for {tableName}: {ex.Message}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Collect CN set from source tables
        // ─────────────────────────────────────────────────────────────────────────

        private async Task<HashSet<string>> CollectCnSetAsync(
            MigrationStatusViewModel vm, DateTime fromDate, DateTime toDate,
            List<string> cityFilter)
        {
            var cnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cityWhereBilling = cityFilter.Count > 0 ? " AND `dest_City_id` IN @Cities" : "";
            var cityWhereArival  = cityFilter.Count > 0 ? " AND `Arvl_Origin` IN @Cities"  : "";

            // From lcs_billing.billing_details (LHR_Billing)
            try
            {
                if (_liveConnections.TryGetValue("LHR_Billing", out var lhrConn) && !string.IsNullOrEmpty(lhrConn))
                {
                    using var conn = OpenLive(lhrConn);
                    var cns = await conn.QueryAsync<string>(
                        $"SELECT DISTINCT `CN_Number` FROM `lcs_billing`.`billing_details` WHERE `BILLING_DATE` BETWEEN @From AND @To{cityWhereBilling}",
                        new { From = fromDate, To = toDate, Cities = cityFilter });
                    foreach (var cn in cns)
                        if (!string.IsNullOrWhiteSpace(cn)) cnSet.Add(cn);
                    Log(vm, $"  CNs from billing_details: {cns.Count():N0}");
                }
            }
            catch (Exception ex)
            {
                Log(vm, $"  WARN: CN collection from billing_details failed: {ex.Message}");
            }

            // From lcs_db.arival (Central_OPS)
            try
            {
                if (_liveConnections.TryGetValue("Central_OPS", out var opsConn) && !string.IsNullOrEmpty(opsConn))
                {
                    using var conn = OpenLive(opsConn);
                    var cns = await conn.QueryAsync<string>(
                        $"SELECT DISTINCT `CN_NUMBER` FROM `lcs_db`.`arival` WHERE `COUR_DATE` BETWEEN @From AND @To{cityWhereArival}",
                        new { From = fromDate, To = toDate, Cities = cityFilter });
                    foreach (var cn in cns)
                        if (!string.IsNullOrWhiteSpace(cn)) cnSet.Add(cn);
                    Log(vm, $"  CNs from arival: {cns.Count():N0}");
                }
            }
            catch (Exception ex)
            {
                Log(vm, $"  WARN: CN collection from arival failed: {ex.Message}");
            }

            return cnSet;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Per-table migration
        // ─────────────────────────────────────────────────────────────────────────

        private async Task MigrateTableAsync(
            MigrationTableSpec spec,
            TableMigrationResult result,
            MigrationStatusViewModel vm,
            DateTime fromDate,
            DateTime toDate,
            HashSet<string> cnSet,
            int rowLimit = 0,
            List<string>? cityFilter = null)
        {
            result.Status    = "Running";
            result.StartedAt = DateTime.Now;
            Log(vm, $"[{spec.Label}] Starting ({spec.Category})…");
            await PushTableProgressAsync(vm, result);

            try
            {
                // Resolve live connection string
                if (!_liveConnections.TryGetValue(spec.LiveConnection, out var liveConnStr)
                    || string.IsNullOrEmpty(liveConnStr))
                    throw new InvalidOperationException($"Live connection '{spec.LiveConnection}' not configured.");

                // Build city filter fragment for Source tables
                var cities       = cityFilter ?? new List<string>();
                var cityFragment = cities.Count > 0 && spec.CityColumn != null
                    ? $" AND `{spec.CityColumn}` IN @Cities" : "";

                // ── Count live rows ──────────────────────────────────────────
                long liveCount = 0;
                using (var liveConn = OpenLive(liveConnStr))
                {
                    liveCount = spec.Category switch
                    {
                        "Source"     => await liveConn.ExecuteScalarAsync<long>(
                            $"SELECT COUNT(*) FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.DateField}` BETWEEN @From AND @To{cityFragment}",
                            new { From = fromDate, To = toDate, Cities = cities }),
                        "Lookup"     => await liveConn.ExecuteScalarAsync<long>(
                            $"SELECT COUNT(*) FROM `{spec.SchemaDb}`.`{spec.TableName}`"),
                        "CnFiltered" => cnSet.Count == 0 ? 0 : await CountCnFilteredAsync(liveConn, spec, cnSet),
                        "YearMonth"  => await liveConn.ExecuteScalarAsync<long>(
                            $"SELECT COUNT(*) FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.YearField}` = @Year AND `{spec.MonthField}` = @Month",
                            new { vm.Year, vm.Month }),
                        _            => 0
                    };
                }
                result.LiveRowCount = liveCount;
                vm.TotalLiveRows  += liveCount;   // accumulate for overall progress %
                Log(vm, $"  [{spec.Label}] Live rows: {liveCount:N0}");
                await PushTableProgressAsync(vm, result);

                // ── Get DDL from live and create table locally ───────────────
                string ddl;
                using (var liveConn = OpenLive(liveConnStr))
                {
                    ddl = await GetCreateTableDdlAsync(liveConn, spec.SchemaDb, spec.TableName);
                }

                using var localConn = OpenLocal();

                // Create schema and table on local
                await localConn.ExecuteAsync($"CREATE DATABASE IF NOT EXISTS `{spec.SchemaDb}`");
                await localConn.ExecuteAsync("SET FOREIGN_KEY_CHECKS=0");

                var localDdl = PatchDdl(ddl, spec.SchemaDb, spec.TableName);
                await localConn.ExecuteAsync(localDdl);

                // ── Clear existing local data ────────────────────────────────
                if (spec.Category == "Source")
                {
                    await localConn.ExecuteAsync(
                        $"DELETE FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.DateField}` BETWEEN @From AND @To{cityFragment}",
                        new { From = fromDate, To = toDate, Cities = cities });
                }
                else if (spec.Category is "Lookup" or "CnFiltered")
                {
                    await localConn.ExecuteAsync(
                        $"TRUNCATE TABLE `{spec.SchemaDb}`.`{spec.TableName}`");
                }
                else if (spec.Category == "YearMonth")
                {
                    await localConn.ExecuteAsync(
                        $"DELETE FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.YearField}` = @Year AND `{spec.MonthField}` = @Month",
                        new { vm.Year, vm.Month });
                }

                // ── Paginated copy from live → local ─────────────────────────
                // Progress callback: called after EVERY row insert.
                // Throttled to 100 ms so SignalR isn't flooded on large tables,
                // but the UI always shows the latest count within ≤100 ms.
                DateTime lastPush    = DateTime.MinValue;
                long     prevCount   = 0;          // track increment for TotalLocalRows
                Func<long, Task> onRowInserted = (count) =>
                {
                    var delta             = count - prevCount;
                    prevCount             = count;
                    result.LocalRowCount  = count;
                    vm.TotalLocalRows    += delta;  // keep overall total in sync

                    var now = DateTime.UtcNow;
                    if ((now - lastPush).TotalMilliseconds < 100) return Task.CompletedTask;
                    lastPush = now;
                    return PushProgressAsync(vm, result);
                };

                if (liveCount > 0)
                {
                    using var liveConn = OpenLive(liveConnStr);

                    if (spec.Category == "CnFiltered")
                    {
                        long copied = await CopyCnFilteredAsync(liveConn, localConn, spec, cnSet, vm, onRowInserted, rowLimit);
                        result.LocalRowCount = copied;
                    }
                    else
                    {
                        long copied = await CopyPaginatedAsync(liveConn, localConn, spec, fromDate, toDate, vm, onRowInserted, rowLimit, vm.Year, vm.Month, cities);
                        result.LocalRowCount = copied;
                    }
                }
                else
                {
                    result.LocalRowCount = 0;
                }

                await localConn.ExecuteAsync("SET FOREIGN_KEY_CHECKS=1");

                result.Status      = "Done";
                result.CompletedAt = DateTime.Now;
                vm.DoneTables      = vm.TableResults.Count(r => r.Status == "Done");
                Log(vm, $"  [{spec.Label}] Done — local: {result.LocalRowCount:N0} rows.");
                await PushTableProgressAsync(vm, result);
                await PushOverallStatusAsync(vm);
            }
            catch (Exception ex)
            {
                result.Status      = "Failed";
                result.Error       = ex.Message;
                result.CompletedAt = DateTime.Now;
                vm.FailedTables    = vm.TableResults.Count(r => r.Status == "Failed");
                Log(vm, $"  [{spec.Label}] FAILED: {ex.Message}");
                await PushTableProgressAsync(vm, result);
                _logger.LogError(ex, "Migration failed for {Table}", spec.FullTableName);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Paginated copy helpers — per-row INSERT inside transaction
        // ─────────────────────────────────────────────────────────────────────────

        private async Task<long> CopyPaginatedAsync(
            MySqlConnection liveConn,
            MySqlConnection localConn,
            MigrationTableSpec spec,
            DateTime fromDate,
            DateTime toDate,
            MigrationStatusViewModel vm,
            Func<long, Task>? onRowInserted = null,
            int rowLimit = 0,
            int year = 0,
            int month = 0,
            List<string>? cityFilter = null)
        {
            long totalCopied = 0;
            int offset = 0;
            var cities       = cityFilter ?? new List<string>();
            var cityFragment = cities.Count > 0 && spec.CityColumn != null
                ? $" AND `{spec.CityColumn}` IN @Cities" : "";

            while (true)
            {
                // Respect per-table row limit
                var remaining  = rowLimit > 0 ? rowLimit - (int)totalCopied : BatchSize;
                if (remaining <= 0) break;
                var pageSize   = Math.Min(BatchSize, remaining);

                var selectSql = spec.Category switch
                {
                    "Source"    => $"SELECT * FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.DateField}` BETWEEN @From AND @To{cityFragment} LIMIT {pageSize} OFFSET {offset}",
                    "YearMonth" => $"SELECT * FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.YearField}` = @Year AND `{spec.MonthField}` = @Month LIMIT {pageSize} OFFSET {offset}",
                    _           => $"SELECT * FROM `{spec.SchemaDb}`.`{spec.TableName}` LIMIT {pageSize} OFFSET {offset}",
                };

                object? sqlParams = spec.Category switch
                {
                    "Source"    => new { From = fromDate, To = toDate, Cities = cities },
                    "YearMonth" => new { Year = year, Month = month },
                    _           => null,
                };

                var rows = (await liveConn.QueryAsync(selectSql, sqlParams)).AsList();

                if (rows.Count == 0) break;

                totalCopied += await InsertRowsAsync(localConn, spec.SchemaDb, spec.TableName, rows, totalCopied, onRowInserted);
                offset += rows.Count;

                if (rows.Count < pageSize) break;
            }

            return totalCopied;
        }

        private async Task<long> CountCnFilteredAsync(
            MySqlConnection liveConn,
            MigrationTableSpec spec,
            HashSet<string> cnSet)
        {
            long total = 0;
            foreach (var chunk in Chunk(cnSet.ToList(), CnChunkSize))
            {
                var dp = new DynamicParameters();
                dp.Add("cns", chunk);
                total += await liveConn.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.CnColumn}` IN @cns", dp);
            }
            return total;
        }

        private async Task<long> CopyCnFilteredAsync(
            MySqlConnection liveConn,
            MySqlConnection localConn,
            MigrationTableSpec spec,
            HashSet<string> cnSet,
            MigrationStatusViewModel vm,
            Func<long, Task>? onRowInserted = null,
            int rowLimit = 0)
        {
            long totalCopied = 0;

            foreach (var chunk in Chunk(cnSet.ToList(), CnChunkSize))
            {
                if (rowLimit > 0 && totalCopied >= rowLimit) break;

                int offset = 0;
                while (true)
                {
                    var remaining = rowLimit > 0 ? rowLimit - (int)totalCopied : BatchSize;
                    if (remaining <= 0) break;
                    var pageSize  = Math.Min(BatchSize, remaining);

                    var dp = new DynamicParameters();
                    dp.Add("cns", chunk);
                    var rows = (await liveConn.QueryAsync(
                        $"SELECT * FROM `{spec.SchemaDb}`.`{spec.TableName}` WHERE `{spec.CnColumn}` IN @cns LIMIT {pageSize} OFFSET {offset}",
                        dp)).AsList();

                    if (rows.Count == 0) break;

                    totalCopied += await InsertRowsAsync(localConn, spec.SchemaDb, spec.TableName, rows, totalCopied, onRowInserted);
                    offset += rows.Count;

                    if (rows.Count < pageSize) break;
                }
            }

            return totalCopied;
        }

        /// <summary>
        /// Inserts rows one-by-one inside a single transaction (fast locally),
        /// calling <paramref name="onRowInserted"/> after each row so SignalR
        /// can push the running count in near-real-time.
        /// </summary>
        private static async Task<long> InsertRowsAsync(
            MySqlConnection localConn,
            string schemaDb,
            string tableName,
            List<dynamic> rows,
            long startCount,
            Func<long, Task>? onRowInserted)
        {
            if (rows.Count == 0) return 0;

            // Build INSERT template from the first row's columns
            var firstRow  = (IDictionary<string, object>)rows[0];
            var cols      = firstRow.Keys.ToList();
            var colsStr   = string.Join(", ", cols.Select(c => $"`{c}`"));
            var paramStr  = string.Join(", ", cols.Select((_, i) => $"@p{i}"));
            var insertSql = $"INSERT IGNORE INTO `{schemaDb}`.`{tableName}` ({colsStr}) VALUES ({paramStr})";

            long inserted = 0;

            // Wrap entire page in one transaction for performance
            using var tx = await localConn.BeginTransactionAsync();
            try
            {
                foreach (var dynRow in rows)
                {
                    var row = (IDictionary<string, object>)dynRow;
                    using var cmd = new MySqlCommand(insertSql, localConn, tx);
                    for (int i = 0; i < cols.Count; i++)
                        cmd.Parameters.AddWithValue($"@p{i}", row.TryGetValue(cols[i], out var v) ? v : null);

                    inserted += await cmd.ExecuteNonQueryAsync();

                    // Notify caller with the running total (throttled inside the callback)
                    if (onRowInserted != null)
                        await onRowInserted(startCount + inserted);
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            return inserted;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DDL helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static async Task<string> GetCreateTableDdlAsync(
            MySqlConnection conn, string schemaDb, string tableName)
        {
            var row = await conn.QueryFirstAsync(
                $"SHOW CREATE TABLE `{schemaDb}`.`{tableName}`");
            var dict = (IDictionary<string, object>)row;
            return dict["Create Table"]?.ToString() ?? "";
        }

        private static string PatchDdl(string ddl, string localSchema, string tableName)
        {
            // Add IF NOT EXISTS and schema prefix
            ddl = Regex.Replace(ddl,
                @"CREATE TABLE `([^`]+)`",
                $"CREATE TABLE IF NOT EXISTS `{localSchema}`.`{tableName}`");

            // Strip AUTO_INCREMENT value from ENGINE clause
            ddl = Regex.Replace(ddl, @"\s*AUTO_INCREMENT=\d+", "");

            // Fix zero-date defaults that MySQL strict mode rejects locally.
            // NOT NULL DEFAULT '0000-00-00 00:00:00' / '0000-00-00'
            //   → drop NOT NULL constraint and use DEFAULT NULL
            ddl = Regex.Replace(ddl,
                @"NOT NULL DEFAULT '0000-00-00(?: 00:00:00)?'",
                "DEFAULT NULL");

            // Any remaining zero-date default on a nullable column
            ddl = Regex.Replace(ddl,
                @"DEFAULT '0000-00-00(?: 00:00:00)?'",
                "DEFAULT NULL");

            return ddl;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Connection factories
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Opens a connection to a LIVE server.
        /// SAFETY: Live connections are read-only — never execute INSERT/UPDATE/DELETE/ALTER/DROP on these.
        /// </summary>
        private static MySqlConnection OpenLive(string connStr)
        {
            var builder = new MySqlConnectionStringBuilder(connStr);

            // Large tables (arival 22M, book_dispatch 36M) need more than the default 30s command timeout
            if (builder.DefaultCommandTimeout < 600)
                builder.DefaultCommandTimeout = 600; // 10 minutes

            // Some tables (e.g. hr_employeepersonaldetail) have MySQL zero-dates (0000-00-00)
            // that cannot map to System.DateTime — convert them to DateTime.MinValue instead
            builder.ConvertZeroDateTime = true;

            var conn = new MySqlConnection(builder.ConnectionString);
            conn.Open();
            return conn;
        }

        private MySqlConnection OpenLocal()
        {
            var conn = new MySqlConnection(_localConnStr);
            conn.Open();
            return conn;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Utility helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static (DateTime from, DateTime to) GetDateRange(int year, int month)
        {
            // e.g. month=1 2026 → Dec 21, 2025 → Jan 20, 2026
            var from = new DateTime(year, month, 1).AddMonths(-1).AddDays(20); // 21st of prev month
            var to   = new DateTime(year, month, 20);
            return (from, to);
        }

        private static string StateKey(int year, int month) => $"{year}-{month:D2}";

        // Single push — table detail + overall progress in one message
        private Task PushProgressAsync(MigrationStatusViewModel vm, TableMigrationResult? result) =>
            _hub.Clients.Group(MigrationGroupKey(vm.Year, vm.Month)).SendAsync("progress", new
            {
                // Table-level
                label         = result?.Label,
                fullTableName = result?.FullTableName,
                category      = result?.Category,
                status        = result?.Status,
                liveRowCount  = result?.LiveRowCount ?? 0,
                localRowCount = result?.LocalRowCount ?? 0,
                error         = result?.Error,
                // Overall
                overallStatus   = vm.OverallStatus,
                doneTables      = vm.DoneTables,
                failedTables    = vm.FailedTables,
                totalTables     = vm.TotalTables,
                totalLiveRows   = vm.TotalLiveRows,
                totalLocalRows  = vm.TotalLocalRows,
                progressPercent = vm.ProgressPercent,
            });

        // Keep old names as aliases so existing call-sites compile
        private Task PushTableProgressAsync(MigrationStatusViewModel vm, TableMigrationResult result) =>
            PushProgressAsync(vm, result);

        private Task PushOverallStatusAsync(MigrationStatusViewModel vm) =>
            PushProgressAsync(vm, null);

        private void Log(MigrationStatusViewModel vm, string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            lock (vm.Logs)
                vm.Logs.Add(entry);

            // Push log entry to all clients watching this migration
            var groupKey = MigrationGroupKey(vm.Year, vm.Month);
            _ = _hub.Clients.Group(groupKey).SendAsync("logEntry", entry);
        }

        private static string MigrationGroupKey(int year, int month) =>
            $"migration-{year}-{month:D2}";

        private static List<List<T>> Chunk<T>(List<T> source, int chunkSize)
        {
            var result = new List<List<T>>();
            for (int i = 0; i < source.Count; i += chunkSize)
                result.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
            return result;
        }
    }
}
