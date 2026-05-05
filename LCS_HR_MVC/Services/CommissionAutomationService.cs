using System;
using System.Threading;
using Dapper;
using Hangfire;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Hubs;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Models.Payroll;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class CommissionAutomationService : ICommissionAutomationService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IPayrollService _payrollService;
        private readonly IHubContext<CommissionProgressHub, ICommissionProgressClient> _hubContext;
        private readonly ILogger<CommissionAutomationService> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IConfiguration _configuration;
        private readonly ICommissionExecutionHistoryService _executionHistory;

        private static readonly string[] CommissionTypes =
        {
            "CashCommission",
            "CodCommission",
            "OverLandCommission",
            "ReturnCodCommission",
            "CommissionProcess",
            "FinalCommission"
        };

        // Fallback user ID for the "Automation" system account (userID = 210 in lcs_users)
        private const string AutomationUserId = "210";
        private const int ActiveAutomationPendingWindowHours = 12;
        private const int RunningStepStaleMinutes = 20;
        private const int HangfireServerStaleMinutes = 5;
        private const int StepHeartbeatIntervalSeconds = 30;

        // ── In-process concurrency guard ────────────────────────────────────────
        // Hangfire [DisableConcurrentExecution] keys on method+args.  Different
        // jobRunId values produce different keys, so two jobs for the same period
        // CAN run simultaneously through Hangfire alone.  This semaphore is the
        // definitive single-process gate ensuring only one automation job executes
        // at a time regardless of jobRunId or trigger source.
        private static readonly SemaphoreSlim _automationJobGate = new(1, 1);
        private const int JobGateTimeoutSeconds = 10;

        // Tracks the jobRunId currently executing under the semaphore gate.
        // When Hangfire fires the SAME jobRunId twice, the duplicate invocation
        // must silently exit WITHOUT calling MarkJobRunAsDuplicateAsync — otherwise
        // it destroys the first invocation's pending work queue.
        private static volatile string? _currentRunningJobRunId;

        // Seconds between advisory-lock keepalive pings.  Prevents MySQL from
        // killing the idle lock-holding connection via wait_timeout.
        private const int AdvisoryLockKeepaliveIntervalSeconds = 60;

        // Per-instance keepalive state (managed by ExecuteAutomationJobCoreAsync).
        private CancellationTokenSource? _lockKeepaliveCts;

        private static string QualifyHrTable(string tableName) => $"lcs_hr.{tableName}";

        private static string CashConsignmentsTable => QualifyHrTable(AcTestTableNames.T_CashConsignments);
        private static string CodCommissionTable => QualifyHrTable(AcTestTableNames.T_CodCommission);
        private static string OleCommissionProcessTable => QualifyHrTable(AcTestTableNames.T_OleCommissionProcess);
        private static string CodReturnCommissionProcessTable => QualifyHrTable(AcTestTableNames.T_CodReturnCommissionProc);
        private static string CommissionProcessTable => QualifyHrTable(AcTestTableNames.T_CommissionProcess);

        private bool SkipFinalCommissionInTestMode =>
            AcTestTableNames.IsTestMode
            && _configuration.GetValue<bool>("CommissionSettings:TestMode_SkipFinalCommission");

        public CommissionAutomationService(
            IDbConnectionFactory connectionFactory,
            IPayrollService payrollService,
            IHubContext<CommissionProgressHub, ICommissionProgressClient> hubContext,
            ILogger<CommissionAutomationService> logger,
            IBackgroundJobClient backgroundJobClient,
            IConfiguration configuration,
            ICommissionExecutionHistoryService executionHistory)
        {
            _connectionFactory  = connectionFactory;
            _payrollService     = payrollService;
            _hubContext         = hubContext;
            _logger             = logger;
            _backgroundJobClient = backgroundJobClient;
            _configuration      = configuration;
            _executionHistory   = executionHistory;
        }

        // Called by the recurring Hangfire job so DateTime.Now is evaluated at fire time
        public Task TriggerScheduledAsync()
        {
            var now = DateTime.Now;
            return StartAutomationAsync(now.Year, now.Month, "Scheduled", AutomationUserId);
        }

        // ── Schema auto-migration ─────────────────────────────────────────────────
        // Adds triggered_by / triggered_by_user_id columns to hr_commission_automation_log
        // if they do not already exist. Safe to call on every request — INFORMATION_SCHEMA
        // check is fast and the ALTER only runs once in the lifetime of the table.
        private async Task EnsureLogTableSchemaAsync(MySqlConnection connection)
        {
            var colCount = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE()
                    AND TABLE_NAME   = 'hr_commission_automation_log'
                    AND COLUMN_NAME  = 'triggered_by'");

            if (colCount == 0)
            {
                await connection.ExecuteAsync(
                    @"ALTER TABLE hr_commission_automation_log
                        ADD COLUMN triggered_by         VARCHAR(100) NULL AFTER job_run_id,
                        ADD COLUMN triggered_by_user_id VARCHAR(20)  NULL AFTER triggered_by");

                _logger.LogInformation(
                    "DB schema upgraded: added triggered_by + triggered_by_user_id to hr_commission_automation_log");
            }
        }

        public async Task<string> StartAutomationAsync(int year, int month, string triggeredBy, string userId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create database connection.");
            await connection.OpenAsync();

            // ── Serialize concurrent StartAutomationAsync calls for the same period ──
            // Prevents race where two simultaneous triggers both see no active entries
            // and both create duplicate entry sets + enqueue duplicate Hangfire jobs.
            // Lock is auto-released when this connection is disposed (method return).
            var startLockName = $"commission_auto_start_{year}_{month:D2}";
            var startLockResult = await connection.ExecuteScalarAsync<int?>(
                "SELECT GET_LOCK(@LockName, 10);",
                new { LockName = startLockName });
            if (startLockResult != 1)
            {
                _logger.LogWarning(
                    "Could not acquire start-serialization lock {LockName} for {Year}/{Month}. " +
                    "Another StartAutomationAsync call is likely in progress.",
                    startLockName, year, month);
                return "BLOCKED";
            }

            await EnsureLogTableSchemaAsync(connection);
            await RecoverAbandonedAutomationRunsAsync(connection, year, month);

            var existingEntries = (await connection.QueryAsync<CommissionAutomationLogEntry>(
                @"SELECT id AS Id, job_run_id AS JobRunId,
                         triggered_by AS TriggeredBy, triggered_by_user_id AS TriggeredByUserId,
                         year AS Year, month AS Month,
                         commission_type AS CommissionType, city_code AS CityCode, city_name AS CityName,
                         status AS Status, progress_pct AS ProgressPct, started_at AS StartedAt,
                         completed_at AS CompletedAt, error_message AS ErrorMessage,
                         retry_count AS RetryCount, processed_count AS ProcessedCount,
                         total_count AS TotalCount, created_at AS CreatedAt, updated_at AS UpdatedAt
                  FROM hr_commission_automation_log
                  WHERE year = @Year AND month = @Month
                  ORDER BY id ASC",
                new { Year = year, Month = month })).ToList();

            string jobRunId;
            var pendingFreshThreshold = DateTime.Now.AddHours(-ActiveAutomationPendingWindowHours);
            var activeEntries = existingEntries
                .Where(e => e.Status == "Running"
                    || (e.Status == "Pending" && e.UpdatedAt >= pendingFreshThreshold))
                .ToList();

            if (activeEntries.Any())
            {
                string activeJobRunId = activeEntries
                    .OrderByDescending(e => e.UpdatedAt)
                    .Select(e => e.JobRunId)
                    .First();

                _logger.LogWarning(
                    "Commission automation for {Year}/{Month} already has an active or freshly queued run ({JobRunId}) — skipping duplicate enqueue.",
                    year, month, activeJobRunId);
                return activeJobRunId;
            }

            IReadOnlyDictionary<string, AutomationCityValidation> cityValidationLookup =
                (await LoadAutomationCityValidationsAsync(connection))
                .ToDictionary(static city => city.Code, StringComparer.OrdinalIgnoreCase);

            if (existingEntries.Any())
            {
                // "Incomplete" = anything that is not a terminal done/skip state.
                // AlreadyProcessed / Skipped are treated as "done" — re-running them is pointless.
                var incompleteEntries = existingEntries
                    .Where(e => e.Status is not ("Completed" or "AlreadyProcessed" or "Skipped"))
                    .ToList();

                if (incompleteEntries.Any())
                {
                    // ── Preserve history: do NOT overwrite existing rows ───────────────
                    // Old Failed / Pending rows remain as permanent historical records.
                    // New Pending rows are inserted for each incomplete entry so that
                    // AllLogEntries accumulates both old (failure) and new (fresh attempt).
                    jobRunId = GenerateJobRunId();

                    var retryableEntries = incompleteEntries
                        .Where(e => e.RetryCount < 3)
                        .Select(e => (e.CityCode, e.CityName, e.CommissionType))
                        .Distinct()
                        .ToList();

                    var entriesToRetry = retryableEntries
                        .Where(entry =>
                            cityValidationLookup.TryGetValue(entry.CityCode, out var cityValidation)
                            && cityValidation.IsValid)
                        .ToList();

                    foreach (var skippedEntry in retryableEntries.Except(entriesToRetry))
                    {
                        if (cityValidationLookup.TryGetValue(skippedEntry.CityCode, out var cityValidation))
                        {
                            _logger.LogWarning(
                                "Skipping automation retry enqueue for {CityCode} {CityName} because city configuration is incomplete: {Reason}",
                                skippedEntry.CityCode,
                                skippedEntry.CityName,
                                cityValidation.SkipReason);
                        }
                    }

                    foreach (var (cityCode, cityName, commType) in entriesToRetry)
                    {
                        await connection.ExecuteAsync(
                            @"INSERT INTO hr_commission_automation_log
                              (job_run_id, triggered_by, triggered_by_user_id,
                               year, month, commission_type, city_code, city_name, status, progress_pct, retry_count)
                              VALUES (@JobRunId, @TriggeredBy, @TriggeredByUserId,
                                      @Year, @Month, @CommissionType, @CityCode, @CityName, 'Pending', 0, 0)",
                            new
                            {
                                JobRunId          = jobRunId,
                                TriggeredBy       = triggeredBy,
                                TriggeredByUserId = userId,
                                Year              = year,
                                Month             = month,
                                CommissionType    = commType,
                                CityCode          = cityCode,
                                CityName          = cityName
                            });
                    }

                    if (!entriesToRetry.Any())
                    {
                        // Nothing new to insert — remaining work is either max-retry
                        // or blocked by incomplete city configuration.
                        _logger.LogWarning(
                            "No retryable automation entries were enqueued for {Year}/{Month}; remaining work is either max-retry or has incomplete city configuration.",
                            year, month);
                        return existingEntries.First().JobRunId; // return last known job id
                    }
                }
                else
                {
                    // All entries completed/already-processed — fresh re-run for the month.
                    jobRunId = GenerateJobRunId();
                    await InsertAllPendingEntriesAsync(connection, jobRunId, year, month, triggeredBy, userId);
                }
            }
            else
            {
                // Brand new run — no prior log entries for this month.
                jobRunId = GenerateJobRunId();
                await InsertAllPendingEntriesAsync(connection, jobRunId, year, month, triggeredBy, userId);
            }

            _backgroundJobClient.Enqueue<ICommissionAutomationService>(
                svc => svc.ExecuteAutomationJobAsync(jobRunId, year, month, userId));

            _logger.LogInformation(
                "Commission automation started by {User}: jobRunId={JobRunId}, {Year}/{Month}",
                triggeredBy, jobRunId, year, month);

            return jobRunId;
        }

        private static string GenerateJobRunId() => Guid.NewGuid().ToString("N")[..20];

        private async Task InsertAllPendingEntriesAsync(
            MySqlConnection connection, string jobRunId, int year, int month,
            string triggeredBy, string triggeredByUserId)
        {
            List<AutomationCityValidation> cityValidations = await LoadAutomationCityValidationsAsync(connection);
            List<AutomationCityValidation> validCities = cityValidations
                .Where(static city => city.IsValid)
                .ToList();

            foreach (AutomationCityValidation invalidCity in cityValidations.Where(static city => !city.IsValid))
            {
                _logger.LogWarning(
                    "Skipping automation enqueue for {CityCode} {CityName} because city configuration is incomplete: {Reason}",
                    invalidCity.Code,
                    invalidCity.FullName,
                    invalidCity.SkipReason);
            }

            if (!validCities.Any())
                throw new InvalidOperationException("No commission-eligible cities found in hr_city table.");

            foreach (AutomationCityValidation city in validCities)
            {
                foreach (var commType in CommissionTypes)
                {
                    await connection.ExecuteAsync(
                        @"INSERT INTO hr_commission_automation_log
                          (job_run_id, triggered_by, triggered_by_user_id,
                           year, month, commission_type, city_code, city_name, status, progress_pct, retry_count)
                          VALUES (@JobRunId, @TriggeredBy, @TriggeredByUserId,
                                  @Year, @Month, @CommissionType, @CityCode, @CityName, 'Pending', 0, 0)",
                        new
                        {
                            JobRunId           = jobRunId,
                            TriggeredBy        = triggeredBy,
                            TriggeredByUserId  = triggeredByUserId,
                            Year               = year,
                            Month              = month,
                            CommissionType     = commType,
                            CityCode           = city.Code,
                            CityName           = city.FullName
                        });
                }
            }
        }

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task ExecuteAutomationJobAsync(string jobRunId, int year, int month, string userId)
        {
            _logger.LogInformation(
                "Commission automation job received by Hangfire: jobRunId={JobRunId}, {Year}/{Month}",
                jobRunId, year, month);

            // ── In-process concurrency gate ─────────────────────────────────────
            // Ensures only one automation job executes at a time within this process,
            // regardless of Hangfire lock key differences across jobRunIds.
            bool semaphoreAcquired = await _automationJobGate.WaitAsync(
                TimeSpan.FromSeconds(JobGateTimeoutSeconds));
            if (!semaphoreAcquired)
            {
                // ── CRITICAL: Check if this is the SAME jobRunId being fired again ──
                // Hangfire can re-fire the same jobRunId (IIS recycle, timeout retry,
                // or duplicate trigger).  If the currently running job has the same ID,
                // calling MarkJobRunAsDuplicateAsync would bulk-UPDATE all its Pending
                // entries to Failed — destroying the running job's work queue.
                // In that case: log and return silently. Do NOT touch the DB.
                string? runningId = _currentRunningJobRunId;
                if (string.Equals(runningId, jobRunId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Automation job {JobRunId} is a duplicate Hangfire invocation of the already-running job. " +
                        "Exiting silently — the original invocation is still processing.",
                        jobRunId);
                    return;
                }

                // Different jobRunId — this is a genuinely separate job that must wait.
                // Mark the NEW job's entries as duplicate (safe — doesn't affect the running job).
                _logger.LogWarning(
                    "Automation job {JobRunId} blocked by in-process concurrency gate. " +
                    "Another job ({RunningJobRunId}) is already executing. Marking as duplicate.",
                    jobRunId, runningId ?? "unknown");
                using var dupConn = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot create database connection.");
                await dupConn.OpenAsync();
                await MarkJobRunAsDuplicateAsync(dupConn, jobRunId,
                    $"Blocked by in-process concurrency gate — job {runningId ?? "unknown"} is already executing.");
                await BroadcastLogAsync(jobRunId, "WARN", "", "", "",
                    $"Blocked — job {runningId ?? "unknown"} is already running in this process.");
                return;
            }

            try
            {
                _currentRunningJobRunId = jobRunId;
                await ExecuteAutomationJobCoreAsync(jobRunId, year, month, userId);
            }
            finally
            {
                _currentRunningJobRunId = null;
                _automationJobGate.Release();
            }
        }

        /// <summary>Core automation job execution — runs under the in-process semaphore gate.
        /// Acquires advisory lock, processes all cities sequentially, releases lock.</summary>
        private async Task ExecuteAutomationJobCoreAsync(string jobRunId, int year, int month, string userId)
        {
            var jobStartTime = DateTime.Now;
            _logger.LogInformation(
                "Commission automation job executing: jobRunId={JobRunId}, {Year}/{Month}",
                jobRunId, year, month);

            string advisoryLockName = BuildAutomationAdvisoryLockName(year, month);
            await using var advisoryLockConnection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create database connection.");
            await advisoryLockConnection.OpenAsync();

            // ── Advisory Lock Guard: session safety + stale-lock self-healing ─────
            await EnsureAdvisoryLockSessionSettingsAsync(advisoryLockConnection);
            await RecoverAbandonedAutomationRunsAsync(advisoryLockConnection, year, month);

            bool advisoryLockAcquired = false;
            try
            {
                advisoryLockAcquired = await TryAcquireAdvisoryLockAsync(
                    advisoryLockConnection, advisoryLockName, jobRunId, year, month);

                if (!advisoryLockAcquired)
                {
                    string? competingJobRunId = await FindCompetingAutomationRunIdAsync(
                        advisoryLockConnection,
                        jobRunId,
                        year,
                        month);

                    string duplicateMessage =
                        !string.IsNullOrWhiteSpace(competingJobRunId)
                            ? $"Skipped duplicate automation run because job {competingJobRunId} is already active for {year}/{month:D2}."
                            : $"Skipped duplicate automation run because advisory lock {advisoryLockName} is already held for {year}/{month:D2}.";

                    await MarkJobRunAsDuplicateAsync(advisoryLockConnection, jobRunId, duplicateMessage);

                    _logger.LogWarning(
                        "Skipping duplicate automation job {JobRunId} for {Year}/{Month}; advisory lock {LockName} is already held. Competing run: {CompetingJobRunId}",
                        jobRunId,
                        year,
                        month,
                        advisoryLockName,
                        competingJobRunId);

                    await BroadcastLogAsync(jobRunId, "WARN", "", "", "", duplicateMessage);
                    return;
                }

                // ── Start keepalive to prevent MySQL from killing the idle advisory lock connection ──
                // The lock connection sits idle while commission processing happens on other
                // connections.  Without keepalive, wait_timeout/interactive_timeout would
                // close it, releasing the advisory lock mid-job.
                StartAdvisoryLockKeepalive(advisoryLockConnection);

                // ── Load entry list once with a short-lived connection, then release it. ──
                // The long-running per-city loop below opens a fresh connection per attempt
                // so no single connection is held open for the duration of the entire job.
                List<CommissionAutomationLogEntry> entries;
                using (var loadConn = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot create database connection."))
                {
                    await loadConn.OpenAsync();
                    await EnsureLogTableSchemaAsync(loadConn);
                    await RecoverAbandonedAutomationRunsAsync(loadConn, year, month);

                    entries = (await loadConn.QueryAsync<CommissionAutomationLogEntry>(
                        @"SELECT id AS Id, job_run_id AS JobRunId,
                                 triggered_by AS TriggeredBy, triggered_by_user_id AS TriggeredByUserId,
                                 year AS Year, month AS Month,
                                 commission_type AS CommissionType, city_code AS CityCode, city_name AS CityName,
                                 status AS Status, progress_pct AS ProgressPct, started_at AS StartedAt,
                                 completed_at AS CompletedAt, error_message AS ErrorMessage,
                                 retry_count AS RetryCount, processed_count AS ProcessedCount,
                                 total_count AS TotalCount, created_at AS CreatedAt, updated_at AS UpdatedAt
                          FROM hr_commission_automation_log
                          WHERE job_run_id = @JobRunId
                          ORDER BY id ASC",
                        new { JobRunId = jobRunId })).ToList();

                    IReadOnlyDictionary<string, AutomationCityValidation> cityValidations =
                        (await LoadAutomationCityValidationsAsync(
                            loadConn,
                            entries.Select(static entry => entry.CityCode)
                                .Where(static cityCode => !string.IsNullOrWhiteSpace(cityCode))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList()))
                        .ToDictionary(static city => city.Code, StringComparer.OrdinalIgnoreCase);

                    await MarkInvalidCityEntriesAsSkippedAsync(loadConn, entries, cityValidations, jobRunId);
                }

                if (!entries.Any())
                {
                    _logger.LogWarning("No entries found for jobRunId={JobRunId}. Nothing to process.", jobRunId);
                    return;
                }

                var cityGroups = entries
                    .Select(e => new { e.CityCode, e.CityName })
                    .Distinct()
                    .OrderBy(c => c.CityName)
                    .ToList();

                await BroadcastLogAsync(jobRunId, "INFO", "", "", "",
                    $"Job started — {cityGroups.Count} cities × {CommissionTypes.Length} commission types ({entries.Count} tasks total). Period: {year}/{month:D2}");

                int cityIndex = 0;
                foreach (var city in cityGroups)
                {
                    cityIndex++;
                    var cityStartTime = DateTime.Now;

                    await BroadcastLogAsync(jobRunId, "INFO", "", city.CityCode, city.CityName,
                        $"Starting city {cityIndex}/{cityGroups.Count}");

                    // Track per-city step results for prerequisite validation.
                    // Key = CommissionType, Value = terminal status string.
                    var cityStepResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    int stepIndex = 0;
                    foreach (var commType in CommissionTypes)
                    {
                        stepIndex++;
                        var entry = entries.FirstOrDefault(
                            e => e.CityCode == city.CityCode && e.CommissionType == commType);

                        // ── No log entry for this step ──────────────────────────────
                        if (entry == null)
                        {
                            _logger.LogWarning(
                                "[{JobRunId}] City={CityCode} Step {StepIndex}/6 ({CommType}): No log entry — skipping.",
                                jobRunId, city.CityCode, stepIndex, commType);
                            await BroadcastLogAsync(jobRunId, "WARN", commType, city.CityCode, city.CityName,
                                "No log entry found for this step — skipping.");
                            cityStepResults[commType] = "NoEntry";
                            continue;
                        }

                        // ── Re-check status from DB before executing ────────────────
                        // Catches updates from manual execution, other job runs, or
                        // retry attempts that completed between job start and now.
                        using (var preCheckConn = _connectionFactory.CreateConnection() as MySqlConnection
                            ?? throw new InvalidOperationException("Cannot create database connection."))
                        {
                            await preCheckConn.OpenAsync();
                            var freshEntry = await preCheckConn.QueryFirstOrDefaultAsync<CommissionAutomationLogEntry>(
                                @"SELECT id AS Id, status AS Status, retry_count AS RetryCount,
                                         error_message AS ErrorMessage
                                  FROM hr_commission_automation_log WHERE id = @Id",
                                new { entry.Id });
                            if (freshEntry != null)
                            {
                                entry.Status = freshEntry.Status;
                                entry.RetryCount = freshEntry.RetryCount;
                            }
                        }

                        // ── Already terminal — skip ─────────────────────────────────
                        if (entry.Status is "Completed" or "AlreadyProcessed" or "Skipped" or "NoData")
                        {
                            _logger.LogInformation(
                                "[{JobRunId}] City={CityCode} Step {StepIndex}/6 ({CommType}): Already {Status} — skipping.",
                                jobRunId, city.CityCode, stepIndex, commType, entry.Status);
                            await BroadcastLogAsync(jobRunId, "INFO", commType, city.CityCode, city.CityName,
                                $"Already {entry.Status} — skipping.");
                            cityStepResults[commType] = entry.Status;
                            continue;
                        }

                        if (entry.Status == "Failed" && entry.RetryCount >= 3)
                        {
                            _logger.LogInformation(
                                "[{JobRunId}] City={CityCode} Step {StepIndex}/6 ({CommType}): Max retries exhausted (3/3) — skipping.",
                                jobRunId, city.CityCode, stepIndex, commType);
                            await BroadcastLogAsync(jobRunId, "WARN", commType, city.CityCode, city.CityName,
                                "Max retries exhausted — permanently failed.");
                            cityStepResults[commType] = "Failed";
                            continue;
                        }

                        // ── Prerequisite check: CommissionProcess requires steps 1-4 ──
                        if (string.Equals(commType, "CommissionProcess", StringComparison.OrdinalIgnoreCase))
                        {
                            var prerequisiteTypes = new[]
                            {
        "CashCommission",
        "CodCommission",
        "OverLandCommission",
        "ReturnCodCommission"
    };

                            var blockingPrerequisites = prerequisiteTypes
                                .Where(pt =>
                                    !cityStepResults.TryGetValue(pt, out var status)
                                    || status is not ("Completed" or "AlreadyProcessed" or "NoData"))
                                .ToList();

                            if (blockingPrerequisites.Any())
                            {
                                string reason =
                                    $"Skipped: prerequisite steps not successfully completed: {string.Join(", ", blockingPrerequisites)}.";

                                _logger.LogWarning(
                                    "[{JobRunId}] City={CityCode} Step {StepIndex}/6 ({CommType}): {Reason}",
                                    jobRunId, city.CityCode, stepIndex, commType, reason);

                                using (var skipConn = _connectionFactory.CreateConnection() as MySqlConnection
                                    ?? throw new InvalidOperationException("Cannot create database connection."))
                                {
                                    await skipConn.OpenAsync();
                                    await skipConn.ExecuteAsync(
                                        @"UPDATE hr_commission_automation_log
                  SET status = 'Skipped',
                      progress_pct = 0,
                      error_message = @Reason,
                      completed_at = NOW(),
                      updated_at = NOW()
                  WHERE id = @Id AND status IN ('Pending', 'Failed')",
                                        new { Reason = reason, entry.Id });
                                }

                                entry.Status = "Skipped";

                                await BroadcastAsync(
                                    jobRunId,
                                    entry.Id,
                                    "Skipped",
                                    0,
                                    reason,
                                    entry.CityName,
                                    entry.CommissionType,
                                    entry.RetryCount);

                                await BroadcastLogAsync(
                                    jobRunId,
                                    "WARN",
                                    commType,
                                    city.CityCode,
                                    city.CityName,
                                    reason);

                                cityStepResults[commType] = "Skipped";
                                continue;
                            }
                        }
                        // ── Prerequisite check: FinalCommission requires CommissionProcess ──
                        if (string.Equals(commType, "FinalCommission", StringComparison.OrdinalIgnoreCase))
                        {
                            bool cpDone = cityStepResults.TryGetValue("CommissionProcess", out var cpStatus)
                                && cpStatus is "Completed" or "AlreadyProcessed";

                            if (!cpDone && !SkipFinalCommissionInTestMode)
                            {
                                string reason = $"Skipped: CommissionProcess status is '{cpStatus ?? "Unknown"}'. " +
                                                "FinalCommission requires CommissionProcess to be completed first.";
                                _logger.LogWarning(
                                    "[{JobRunId}] City={CityCode} Step {StepIndex}/6 ({CommType}): {Reason}",
                                    jobRunId, city.CityCode, stepIndex, commType, reason);

                                using (var skipConn = _connectionFactory.CreateConnection() as MySqlConnection
                                    ?? throw new InvalidOperationException("Cannot create database connection."))
                                {
                                    await skipConn.OpenAsync();
                                    await skipConn.ExecuteAsync(
                                        @"UPDATE hr_commission_automation_log
                                          SET status = 'Skipped', progress_pct = 0,
                                              error_message = @Reason, completed_at = NOW(), updated_at = NOW()
                                          WHERE id = @Id AND status IN ('Pending', 'Failed')",
                                        new { Reason = reason, entry.Id });
                                }
                                entry.Status = "Skipped";

                                await BroadcastAsync(jobRunId, entry.Id, "Skipped", 0, reason,
                                    entry.CityName, entry.CommissionType, entry.RetryCount);
                                await BroadcastLogAsync(jobRunId, "WARN", commType, city.CityCode, city.CityName, reason);
                                cityStepResults[commType] = "Skipped";
                                continue;
                            }
                        }

                        // ── Test-mode FinalCommission skip ──────────────────────────
                        if (SkipFinalCommissionInTestMode
                            && string.Equals(commType, "FinalCommission", StringComparison.OrdinalIgnoreCase))
                        {
                            await SkipFinalCommissionEntryAsync(entry, year, month, jobRunId, userId);
                            cityStepResults[commType] = "Skipped";
                            continue;
                        }

                        // ── Execute the commission step with retry ──────────────────────
                        // Protection: bounded DB commandTimeouts guarantee every query throws
                        // within its configured limit. No unsafe outer timeout that could leave
                        // a background DB operation running while starting the next step.
                        var stepStartTime = DateTime.Now;
                        _logger.LogInformation(
                            "[{JobRunId}] City={CityCode} Step {StepIndex}/6 ({CommType}): Executing...",
                            jobRunId, city.CityCode, stepIndex, commType);

                        await ExecuteSingleCommissionWithRetryAsync(entry, year, month, jobRunId, userId);

                        var stepDuration = DateTime.Now - stepStartTime;

                        // Stale-running watchdog: warn if step took longer than expected threshold.
                        if (stepDuration.TotalMinutes > 15)
                        {
                            _logger.LogWarning(
                                "[{JobRunId}] City={CityCode} Step ({CommType}): completed but took {DurationMinutes:F1} minutes — exceeds 15-min warning threshold. " +
                                "Investigate DB performance for this city/step.",
                                jobRunId, city.CityCode, commType, stepDuration.TotalMinutes);
                        }

                        // Refresh entry status using a fresh short-lived connection.
                        using (var refreshConn = _connectionFactory.CreateConnection() as MySqlConnection
                            ?? throw new InvalidOperationException("Cannot create database connection."))
                        {
                            await refreshConn.OpenAsync();
                            var refreshed = await refreshConn.QueryFirstOrDefaultAsync<CommissionAutomationLogEntry>(
                                @"SELECT id AS Id, status AS Status, retry_count AS RetryCount,
                                         error_message AS ErrorMessage, progress_pct AS ProgressPct
                                  FROM hr_commission_automation_log WHERE id = @Id",
                                new { entry.Id });

                            if (refreshed != null)
                            {
                                entry.Status = refreshed.Status;
                                entry.RetryCount = refreshed.RetryCount;
                            }
                        }

                        cityStepResults[commType] = entry.Status ?? "Unknown";

                        _logger.LogInformation(
                            "[{JobRunId}] City={CityCode} Step {StepIndex}/6 ({CommType}): Finished — status={Status}, duration={Duration}",
                            jobRunId, city.CityCode, stepIndex, commType, entry.Status, stepDuration);
                    }

                    var cityDuration = DateTime.Now - cityStartTime;
                    var cityResultSummary = string.Join(", ", cityStepResults.Select(kv => $"{kv.Key}={kv.Value}"));

                    _logger.LogInformation(
                        "[{JobRunId}] City {CityIndex}/{CityTotal} {CityName} ({CityCode}) completed in {Duration}. Results: {Results}",
                        jobRunId, cityIndex, cityGroups.Count, city.CityName, city.CityCode, cityDuration, cityResultSummary);

                    await BroadcastLogAsync(jobRunId, "INFO", "", city.CityCode, city.CityName,
                        $"City {cityIndex}/{cityGroups.Count} finished in {cityDuration:mm\\:ss}. [{cityResultSummary}]");
                }

                var doneCount             = entries.Count(e => e.Status == "Completed");
                var alreadyProcessedCount = entries.Count(e => e.Status == "AlreadyProcessed");
                var skippedCount          = entries.Count(e => e.Status == "Skipped");
                var noDataCount           = entries.Count(e => e.Status == "NoData");
                var failedCount           = entries.Count(e => e.Status == "Failed");
                var summaryLevel = failedCount > 0 ? "WARN" : (noDataCount > 0 || skippedCount > 0) ? "WARN" : "SUCCESS";
                await BroadcastLogAsync(jobRunId, summaryLevel, "", "", "",
                    $"Job finished — {doneCount} completed, {alreadyProcessedCount} already processed, {skippedCount} skipped, {noDataCount} no-MIS-data, {failedCount} failed out of {entries.Count} total tasks.");

                _logger.LogInformation("Commission automation job finished: jobRunId={JobRunId}", jobRunId);
            }
            finally
            {
                // Stop keepalive BEFORE touching the advisory lock connection —
                // prevents concurrent access from the keepalive background task.
                StopAdvisoryLockKeepalive();

                if (advisoryLockAcquired)
                {
                    await ReleaseAdvisoryLockAsync(advisoryLockConnection, advisoryLockName);
                }

                var jobDuration = DateTime.Now - jobStartTime;
                _logger.LogInformation(
                    "Commission automation job {JobRunId} ended. Total duration: {Duration}",
                    jobRunId, jobDuration);
            }
        }

        private async Task SkipFinalCommissionEntryAsync(
            CommissionAutomationLogEntry entry,
            int year,
            int month,
            string jobRunId,
            string userId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create database connection.");
            await connection.OpenAsync();

            var completedAt = DateTime.Now;
            var triggeredByName = entry.TriggeredBy;
            if (string.IsNullOrWhiteSpace(triggeredByName) && !string.IsNullOrWhiteSpace(userId))
            {
                triggeredByName = await connection.ExecuteScalarAsync<string>(
                    "SELECT UserName FROM lcs_users WHERE userID = @UserId",
                    new { UserId = userId }) ?? userId;
            }

            const string skipMessage =
                "Skipped FinalCommission because test mode is enabled and CommissionSettings:TestMode_SkipFinalCommission=true.";

            await connection.ExecuteAsync(
                @"UPDATE hr_commission_automation_log
                  SET status = 'Completed', progress_pct = 100,
                      processed_count = 0, total_count = 0,
                      started_at = COALESCE(started_at, NOW()),
                      completed_at = NOW(),
                      error_message = @ErrorMessage,
                      updated_at = NOW()
                  WHERE id = @Id",
                new { ErrorMessage = skipMessage, entry.Id });

            await _executionHistory.RecordAsync(new CommissionExecutionRecord
            {
                ExecutionSource   = "Automation",
                JobRunId          = jobRunId,
                Year              = year,
                Month             = month,
                CityCode          = entry.CityCode,
                CityName          = entry.CityName,
                CommissionType    = entry.CommissionType,
                TriggeredBy       = triggeredByName ?? entry.TriggeredBy,
                TriggeredByUserId = userId,
                Status            = "Completed",
                RowsProcessed     = 0,
                StartedAt         = completedAt,
                CompletedAt       = completedAt,
                DurationMs        = 0,
                ErrorMessage      = skipMessage
            });

            entry.Status = "Completed";
            entry.ProgressPct = 100;
            entry.ProcessedCount = 0;
            entry.TotalCount = 0;
            entry.ErrorMessage = skipMessage;

            await BroadcastAsync(jobRunId, entry.Id, "Completed", 100, skipMessage,
                entry.CityName, entry.CommissionType, entry.RetryCount, 0, 0);

            await BroadcastLogAsync(jobRunId, "WARN", entry.CommissionType, entry.CityCode, entry.CityName, skipMessage);

            _logger.LogWarning(
                "[{CommType}] City={City} skipped because test mode final-commission skipping is enabled.",
                entry.CommissionType, entry.CityCode);
        }

        private async Task ExecuteSingleCommissionWithRetryAsync(
            CommissionAutomationLogEntry entry,
            int year,
            int month,
            string jobRunId,
            string userId)
        {
            const int MaxRetries = 3;
            if (!await TryClaimCommissionEntryAsync(entry.Id))
            {
                _logger.LogInformation(
                    "[{CommType}] City={City} skipped because another worker already claimed log entry {LogId}.",
                    entry.CommissionType, entry.CityCode, entry.Id);
                return;
            }

            int attempt = entry.RetryCount;

            while (attempt < MaxRetries)
            {
                // Fresh connection per attempt — released before the next attempt or on return.
                using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot create database connection.");
                await connection.OpenAsync();

                var attemptStartedAt = DateTime.Now;

                // Resolve user name — done once per attempt inside the scoped connection.
                var triggeredByName = entry.TriggeredBy;
                if (string.IsNullOrWhiteSpace(triggeredByName) && !string.IsNullOrWhiteSpace(userId))
                {
                    triggeredByName = await connection.ExecuteScalarAsync<string>(
                        "SELECT UserName FROM lcs_users WHERE userID = @UserId",
                        new { UserId = userId }) ?? userId;
                }

                await BroadcastAsync(jobRunId, entry.Id, "Running", 10, null,
                    entry.CityName, entry.CommissionType, attempt);

                await BroadcastLogAsync(jobRunId, "INFO", entry.CommissionType, entry.CityCode, entry.CityName,
                    attempt == 0
                        ? "Starting..."
                        : $"Retrying (attempt {attempt + 1}/{MaxRetries})...");

                try
                {
                    // Build live-progress callback: fires on each milestone inside the payroll service.
                    // Fire-and-forget: broadcasts SignalR update and — on first call (processed=0) —
                    // persists the total_count to the log table so page-refreshes still show the total.
                    int capturedLogId   = entry.Id;
                    string capturedJob  = jobRunId;
                    string capturedCity = entry.CityName;
                    string capturedComm = entry.CommissionType;
                    int capturedRetry   = attempt;

                    Func<int, int, Task> onProgress = async (processed, total) =>
                    {
                        try
                        {
                            // Persist total_count once (when first known)
                            if (processed == 0 && total > 0)
                            {
                                using var updConn = _connectionFactory.CreateConnection() as MySql.Data.MySqlClient.MySqlConnection;
                                await updConn!.OpenAsync();
                                await updConn.ExecuteAsync(
                                    "UPDATE hr_commission_automation_log SET total_count=@T WHERE id=@Id",
                                    new { T = total, Id = capturedLogId });
                            }

                            int pct = total > 0
                                ? Math.Clamp((int)(10 + (double)processed / total * 80), 10, 90)
                                : 10;

                            await BroadcastAsync(capturedJob, capturedLogId, "Running", pct, null,
                                capturedCity, capturedComm, capturedRetry, processed, total);
                        }
                        catch { /* never let a progress error abort the job */ }
                    };

                    using var heartbeatCts = new CancellationTokenSource();
                    var heartbeatTask = RunRunningEntryHeartbeatAsync(
                        entry.Id,
                        jobRunId,
                        entry.CommissionType,
                        entry.CityCode,
                        heartbeatCts.Token);

                    (int processedCount, int totalCount) resultCounts;
                    try
                    {
                        resultCounts = await ExecuteCommissionTypeAsync(entry.CommissionType, entry.CityCode, year, month, userId, onProgress);
                    }
                    finally
                    {
                        heartbeatCts.Cancel();
                        try
                        {
                            await heartbeatTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when the commission step finishes before the next heartbeat.
                        }
                    }

                    var (processedCount, totalCount) = resultCounts;

                    var completedAt  = DateTime.Now;
                    var completedMsg = $"Processed on {completedAt:dd-MMM-yyyy}. \u2014 By: {userId} ({triggeredByName ?? userId})";

                    await connection.ExecuteAsync(
                        @"UPDATE hr_commission_automation_log
                          SET status = 'Completed', progress_pct = 100,
                              processed_count = @ProcessedCount, total_count = @TotalCount,
                              completed_at = NOW(), error_message = @Msg, updated_at = NOW()
                          WHERE id = @Id",
                        new { ProcessedCount = processedCount, TotalCount = totalCount, Msg = completedMsg, entry.Id });

                    // ── Record in execution history ───────────────────────────────
                    await _executionHistory.RecordAsync(new CommissionExecutionRecord
                    {
                        ExecutionSource   = "Automation",
                        JobRunId          = jobRunId,
                        Year              = year,
                        Month             = month,
                        CityCode          = entry.CityCode,
                        CityName          = entry.CityName,
                        CommissionType    = entry.CommissionType,
                        TriggeredBy       = triggeredByName ?? entry.TriggeredBy,
                        TriggeredByUserId = userId,
                        Status            = "Completed",
                        RowsProcessed     = processedCount,
                        StartedAt         = attemptStartedAt,
                        CompletedAt       = completedAt,
                        DurationMs        = (int)(completedAt - attemptStartedAt).TotalMilliseconds
                    });

                    await BroadcastAsync(jobRunId, entry.Id, "Completed", 100, completedMsg,
                        entry.CityName, entry.CommissionType, attempt, processedCount, totalCount);

                    await BroadcastLogAsync(jobRunId, "SUCCESS", entry.CommissionType, entry.CityCode, entry.CityName,
                        processedCount > 0
                            ? $"Completed successfully — {processedCount:N0} rows processed."
                            : "Completed successfully.");

                    _logger.LogInformation(
                        "[{CommType}] City={City} completed successfully.",
                        entry.CommissionType, entry.CityCode);
                    return;
                }
                catch (Exception ex)
                {
                    bool isCancellation = ex is OperationCanceledException;
                    var errorMsg = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

                    if (errorMsg.StartsWith("Already Processed on ", StringComparison.OrdinalIgnoreCase))
                    {
                        var apCompletedAt = DateTime.Now;
                        await connection.ExecuteAsync(
                            @"UPDATE hr_commission_automation_log
                              SET status = 'AlreadyProcessed', progress_pct = 100,
                                  error_message = @ErrorMessage, completed_at = NOW(), updated_at = NOW()
                              WHERE id = @Id",
                            new { ErrorMessage = errorMsg, entry.Id });

                        await _executionHistory.RecordAsync(new CommissionExecutionRecord
                        {
                            ExecutionSource   = "Automation",
                            JobRunId          = jobRunId,
                            Year              = year,
                            Month             = month,
                            CityCode          = entry.CityCode,
                            CityName          = entry.CityName,
                            CommissionType    = entry.CommissionType,
                            TriggeredBy       = triggeredByName ?? entry.TriggeredBy,
                            TriggeredByUserId = userId,
                            Status            = "AlreadyProcessed",
                            RowsProcessed     = 0,
                            StartedAt         = attemptStartedAt,
                            CompletedAt       = apCompletedAt,
                            DurationMs        = (int)(apCompletedAt - attemptStartedAt).TotalMilliseconds,
                            ErrorMessage      = errorMsg
                        });

                        await BroadcastAsync(jobRunId, entry.Id, "AlreadyProcessed", 100, errorMsg,
                            entry.CityName, entry.CommissionType, attempt);

                        await BroadcastLogAsync(jobRunId, "WARN", entry.CommissionType, entry.CityCode, entry.CityName,
                            errorMsg);

                        _logger.LogInformation(
                            "[{CommType}] City={City} skipped — {Msg}",
                            entry.CommissionType, entry.CityCode, errorMsg);
                        return;
                    }

                    // FinalCommission returned 0 rows — MIS source tables have no data.
                    if (errorMsg.StartsWith("NoData:", StringComparison.OrdinalIgnoreCase))
                    {
                        var noDataMsg = errorMsg.Length > 7 ? errorMsg[7..].Trim() : "No MIS incentive data for this city/period.";
                        var ndCompletedAt = DateTime.Now;
                        await connection.ExecuteAsync(
                            @"UPDATE hr_commission_automation_log
                              SET status = 'NoData', progress_pct = 0, retry_count = 0,
                                  error_message = @ErrorMessage, completed_at = NOW(), updated_at = NOW()
                              WHERE id = @Id",
                            new { ErrorMessage = noDataMsg, entry.Id });

                        await _executionHistory.RecordAsync(new CommissionExecutionRecord
                        {
                            ExecutionSource   = "Automation",
                            JobRunId          = jobRunId,
                            Year              = year,
                            Month             = month,
                            CityCode          = entry.CityCode,
                            CityName          = entry.CityName,
                            CommissionType    = entry.CommissionType,
                            TriggeredBy       = triggeredByName ?? entry.TriggeredBy,
                            TriggeredByUserId = userId,
                            Status            = "NoData",
                            RowsProcessed     = 0,
                            StartedAt         = attemptStartedAt,
                            CompletedAt       = ndCompletedAt,
                            DurationMs        = (int)(ndCompletedAt - attemptStartedAt).TotalMilliseconds,
                            ErrorMessage      = noDataMsg
                        });

                        await BroadcastAsync(jobRunId, entry.Id, "NoData", 0, noDataMsg,
                            entry.CityName, entry.CommissionType, attempt);

                        await BroadcastLogAsync(jobRunId, "WARN", entry.CommissionType, entry.CityCode, entry.CityName,
                            $"No MIS data — {noDataMsg} Re-run automation after MIS source data is uploaded.");

                        _logger.LogInformation(
                            "[{CommType}] City={City} — no MIS data, marked NoData. Will retry on next automation run.",
                            entry.CommissionType, entry.CityCode);
                        return;
                    }

                    attempt++;

                    if (isCancellation)
                    {
                        _logger.LogError(ex,
                            "[{CommType}] City={City} cancelled or timed out (attempt {Attempt}/{Max}): {Error}",
                            entry.CommissionType, entry.CityCode, attempt, MaxRetries, errorMsg);
                    }
                    else
                    {
                        _logger.LogWarning(ex,
                            "[{CommType}] City={City} failed (attempt {Attempt}/{Max}): {Error}",
                            entry.CommissionType, entry.CityCode, attempt, MaxRetries, errorMsg);
                    }

                    bool shouldRetry = attempt < MaxRetries && IsRetryableCommissionFailure(ex);

                    if (shouldRetry)
                    {
                        var delaySec = attempt == 1 ? 5 : 15;
                        await connection.ExecuteAsync(
                            @"UPDATE hr_commission_automation_log
                              SET status = 'Running', retry_count = @RetryCount,
                                  error_message = @ErrorMessage, updated_at = NOW()
                              WHERE id = @Id",
                            new
                            {
                                RetryCount = attempt,
                                ErrorMessage = $"Attempt {attempt}/{MaxRetries}: {errorMsg}",
                                entry.Id
                            });

                        // Record each failed attempt so audit trail shows the failure
                        await _executionHistory.RecordAsync(new CommissionExecutionRecord
                        {
                            ExecutionSource   = "Automation",
                            JobRunId          = jobRunId,
                            Year              = year,
                            Month             = month,
                            CityCode          = entry.CityCode,
                            CityName          = entry.CityName,
                            CommissionType    = entry.CommissionType,
                            TriggeredBy       = triggeredByName ?? entry.TriggeredBy,
                            TriggeredByUserId = userId,
                            Status            = "Failed",
                            RowsProcessed     = 0,
                            StartedAt         = attemptStartedAt,
                            CompletedAt       = DateTime.Now,
                            DurationMs        = (int)(DateTime.Now - attemptStartedAt).TotalMilliseconds,
                            ErrorMessage      = $"Attempt {attempt}/{MaxRetries}: {errorMsg}"
                        });

                        await BroadcastLogAsync(jobRunId, "WARN", entry.CommissionType, entry.CityCode, entry.CityName,
                            $"Attempt {attempt}/{MaxRetries} failed — retrying in {delaySec}s. Error: {errorMsg}");

                        // Back-off for transient lock-contention failures: 5 s, 15 s
                        await Task.Delay(TimeSpan.FromSeconds(delaySec));
                    }
                    else
                    {
                        var failedAt = DateTime.Now;
                        await connection.ExecuteAsync(
                            @"UPDATE hr_commission_automation_log
                              SET status = 'Failed', retry_count = @RetryCount,
                                  error_message = @ErrorMessage, completed_at = NOW(), updated_at = NOW()
                              WHERE id = @Id",
                            new { RetryCount = attempt, ErrorMessage = errorMsg, entry.Id });

                        await _executionHistory.RecordAsync(new CommissionExecutionRecord
                        {
                            ExecutionSource   = "Automation",
                            JobRunId          = jobRunId,
                            Year              = year,
                            Month             = month,
                            CityCode          = entry.CityCode,
                            CityName          = entry.CityName,
                            CommissionType    = entry.CommissionType,
                            TriggeredBy       = triggeredByName ?? entry.TriggeredBy,
                            TriggeredByUserId = userId,
                            Status            = "Failed",
                            RowsProcessed     = 0,
                            StartedAt         = attemptStartedAt,
                            CompletedAt       = failedAt,
                            DurationMs        = (int)(failedAt - attemptStartedAt).TotalMilliseconds,
                            ErrorMessage      = $"Permanently failed after {attempt} attempt(s): {errorMsg}"
                        });

                        await BroadcastAsync(jobRunId, entry.Id, "Failed", 0, errorMsg,
                            entry.CityName, entry.CommissionType, attempt);

                        await BroadcastLogAsync(jobRunId, "ERROR", entry.CommissionType, entry.CityCode, entry.CityName,
                            $"PERMANENTLY FAILED after {attempt} attempt(s) — {errorMsg}");

                        _logger.LogError(
                            "[{CommType}] City={City} permanently failed after {Attempts} attempt(s).",
                            entry.CommissionType, entry.CityCode, attempt);
                        return; // Continue pipeline — never abort
                    }
                }
            }
        }

        private async Task<bool> TryClaimCommissionEntryAsync(int logId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create database connection.");
            await connection.OpenAsync();

            // Clear completed_at and error_message when reclaiming a Failed entry
            // to prevent dirty state (Running + completed_at set + stale error message).
            int affectedRows = await connection.ExecuteAsync(
                @"UPDATE hr_commission_automation_log
                  SET status = 'Running', progress_pct = 10,
                      started_at = COALESCE(started_at, NOW()), updated_at = NOW(),
                      completed_at = NULL, error_message = NULL
                  WHERE id = @Id
                    AND status IN ('Pending', 'Failed')",
                new { Id = logId });

            return affectedRows > 0;
        }

        private async Task RunRunningEntryHeartbeatAsync(
            int logId,
            string jobRunId,
            string commissionType,
            string cityCode,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(StepHeartbeatIntervalSeconds), cancellationToken);

                    using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                        ?? throw new InvalidOperationException("Cannot create database connection.");
                    await connection.OpenAsync(cancellationToken);

                    await connection.ExecuteAsync(new CommandDefinition(
                        @"UPDATE hr_commission_automation_log
                          SET updated_at = NOW()
                          WHERE id = @Id
                            AND job_run_id = @JobRunId
                            AND status = 'Running';",
                        new { Id = logId, JobRunId = jobRunId },
                        commandTimeout: 15,
                        cancellationToken: cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Automation heartbeat failed for jobRunId={JobRunId}, logId={LogId}, commissionType={CommissionType}, cityCode={CityCode}.",
                        jobRunId,
                        logId,
                        commissionType,
                        cityCode);
                }
            }
        }

        private async Task RecoverAbandonedAutomationRunsAsync(
            MySqlConnection connection,
            int year,
            int month)
        {
            string advisoryLockName = BuildAutomationAdvisoryLockName(year, month);
            long? lockHolder = await GetLockHolderConnectionIdAsync(connection, advisoryLockName);
            if (lockHolder.HasValue)
            {
                _logger.LogDebug(
                    "Skipping abandoned automation recovery for {Year}/{Month}; advisory lock {LockName} is held by connection {ConnectionId}.",
                    year,
                    month,
                    advisoryLockName,
                    lockHolder.Value);
                return;
            }

            List<string> abandonedJobRunIds;
            try
            {
                abandonedJobRunIds = (await connection.QueryAsync<string>(
                    $@"SELECT DISTINCT l.job_run_id
                       FROM hr_commission_automation_log l
                       WHERE l.year = @Year
                         AND l.month = @Month
                         AND l.status = 'Running'
                         AND l.updated_at < DATE_SUB(NOW(), INTERVAL {RunningStepStaleMinutes} MINUTE)
                         AND NOT EXISTS (
                             SELECT 1
                             FROM Hangfire_Job hj
                             JOIN Hangfire_State ps
                               ON ps.JobId = hj.Id
                              AND ps.Id = (
                                  SELECT MAX(ps2.Id)
                                  FROM Hangfire_State ps2
                                  WHERE ps2.JobId = hj.Id
                                    AND ps2.Name = 'Processing'
                              )
                             JOIN Hangfire_Server hs
                               ON hs.Id = JSON_UNQUOTE(JSON_EXTRACT(ps.Data, '$.ServerId'))
                             WHERE hj.Arguments LIKE CONCAT('%', l.job_run_id, '%')
                               AND hj.StateName = 'Processing'
                               AND hs.LastHeartbeat >= DATE_SUB(UTC_TIMESTAMP(), INTERVAL {HangfireServerStaleMinutes} MINUTE)
                         );",
                    new { Year = year, Month = month },
                    commandTimeout: 30)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not inspect Hangfire state while recovering abandoned commission automation runs for {Year}/{Month}. Falling back to advisory-lock-only stale detection.",
                    year,
                    month);

                abandonedJobRunIds = (await connection.QueryAsync<string>(
                    $@"SELECT DISTINCT job_run_id
                       FROM hr_commission_automation_log
                       WHERE year = @Year
                         AND month = @Month
                         AND status = 'Running'
                         AND updated_at < DATE_SUB(NOW(), INTERVAL {RunningStepStaleMinutes} MINUTE);",
                    new { Year = year, Month = month },
                    commandTimeout: 30)).ToList();
            }

            if (!abandonedJobRunIds.Any())
            {
                return;
            }

            string runningMessage =
                $"Recovered abandoned automation run: the step was Running for more than {RunningStepStaleMinutes} minutes, " +
                "but no live Hangfire worker/advisory lock was found. Marked Failed so it can be retried safely.";
            string pendingMessage =
                "Recovered abandoned automation run: the Hangfire worker stopped before this queued step started. " +
                "Marked Failed so it can be retried safely.";

            int affectedRows = await connection.ExecuteAsync(
                @"UPDATE hr_commission_automation_log
                  SET retry_count = CASE
                          WHEN status = 'Running' THEN LEAST(retry_count + 1, 3)
                          ELSE retry_count
                      END,
                      status = 'Failed',
                      progress_pct = 0,
                      error_message = CASE
                          WHEN status = 'Running' THEN @RunningMessage
                          ELSE @PendingMessage
                      END,
                      completed_at = NOW(),
                      updated_at = NOW()
                  WHERE year = @Year
                    AND month = @Month
                    AND job_run_id IN @JobRunIds
                    AND status IN ('Pending', 'Running');",
                new
                {
                    Year = year,
                    Month = month,
                    JobRunIds = abandonedJobRunIds,
                    RunningMessage = runningMessage,
                    PendingMessage = pendingMessage
                },
                commandTimeout: 60);

            _logger.LogWarning(
                "Recovered abandoned commission automation run(s) for {Year}/{Month}: {JobRunIds}. Marked {AffectedRows} pending/running log row(s) as Failed.",
                year,
                month,
                string.Join(", ", abandonedJobRunIds),
                affectedRows);
        }

        private static async Task<string?> FindCompetingAutomationRunIdAsync(
            MySqlConnection connection,
            string jobRunId,
            int year,
            int month)
        {
            return await connection.QueryFirstOrDefaultAsync<string>(
                $@"SELECT job_run_id
                   FROM hr_commission_automation_log
                   WHERE year = @Year
                     AND month = @Month
                     AND job_run_id <> @JobRunId
                     AND (
                         status = 'Running'
                         OR (
                             status = 'Pending'
                             AND updated_at >= DATE_SUB(NOW(), INTERVAL {ActiveAutomationPendingWindowHours} HOUR)
                         )
                     )
                   ORDER BY updated_at DESC
                   LIMIT 1;",
                new
                {
                    Year = year,
                    Month = month,
                    JobRunId = jobRunId
                });
        }

        private static string BuildAutomationAdvisoryLockName(int year, int month)
        {
            return $"commission_automation_{year}_{month:D2}";
        }

        // ── Advisory Lock Guard ──────────────────────────────────────────────────
        // Self-healing stale-lock detection, safe KILL, permission-safe handling,
        // and guaranteed release via try/finally.

        /// <summary>Stale lock threshold — a sleeping connection holding the lock
        /// for at least this many seconds is considered abandoned.</summary>
        private const int StaleLockThresholdSeconds = 300;

        /// <summary>Advisory-lock session timeout (8 hours).  Must be long enough for the
        /// entire automation run across all 175+ cities.  The stale-lock detection threshold
        /// (<see cref="StaleLockThresholdSeconds"/> = 300s) is for detecting ABANDONED
        /// connections from crashed processes — it must NOT be used as the session timeout,
        /// or it will kill the CURRENT advisory-lock connection mid-job (the root cause of
        /// the overlapping-execution bug).</summary>
        private const int AdvisoryLockSessionTimeoutSeconds = 28800;

        /// <summary>Configures the advisory-lock connection to survive for the full
        /// duration of the automation job.  The keepalive loop provides an additional
        /// safety layer on top of this timeout.</summary>
        private async Task EnsureAdvisoryLockSessionSettingsAsync(MySqlConnection connection)
        {
            try
            {
                await connection.ExecuteAsync(
                    $"SET SESSION wait_timeout = {AdvisoryLockSessionTimeoutSeconds};");
                _logger.LogInformation(
                    "Advisory lock session wait_timeout set to {Seconds}s ({Hours}h) for full-job duration.",
                    AdvisoryLockSessionTimeoutSeconds, AdvisoryLockSessionTimeoutSeconds / 3600);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not set session wait_timeout for advisory lock connection. Continuing without it.");
            }
        }

        /// <summary>Returns the MySQL CONNECTION_ID() for the given connection.</summary>
        private static async Task<long> GetCurrentConnectionIdAsync(MySqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<long>("SELECT CONNECTION_ID();");
        }

        /// <summary>Returns the connection ID currently holding the named advisory lock,
        /// or <c>null</c> if no connection holds it.</summary>
        private static async Task<long?> GetLockHolderConnectionIdAsync(
            MySqlConnection connection, string lockName)
        {
            return await connection.ExecuteScalarAsync<long?>(
                "SELECT IS_USED_LOCK(@LockName);",
                new { LockName = lockName });
        }

        /// <summary>Model for a row from information_schema.processlist.</summary>
        private sealed class ProcessListEntry
        {
            public long ID { get; set; }
            public string? COMMAND { get; set; }
            public long TIME { get; set; }
            public string? STATE { get; set; }
            public string? INFO { get; set; }
            public string? DB { get; set; }
        }

        /// <summary>Inspects the processlist to determine whether the lock-holding connection
        /// is stale (sleeping for at least <see cref="StaleLockThresholdSeconds"/>).</summary>
        /// <returns><c>true</c> if the connection is confirmed stale; <c>false</c> otherwise
        /// (active, unknown, or insufficient privileges).</returns>
        private async Task<bool> IsStaleLockConnectionAsync(
            MySqlConnection connection, long holderConnectionId, long currentConnectionId, string lockName)
        {
            if (holderConnectionId == currentConnectionId)
            {
                _logger.LogInformation(
                    "Advisory lock {LockName} is held by the current connection {ConnectionId}. Not stale.",
                    lockName, holderConnectionId);
                return false;
            }

            ProcessListEntry? proc;
            try
            {
                proc = await connection.QueryFirstOrDefaultAsync<ProcessListEntry>(
                    @"SELECT ID, COMMAND, TIME, STATE, INFO, DB
                      FROM information_schema.processlist
                      WHERE ID = @ConnectionId;",
                    new { ConnectionId = holderConnectionId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Unable to inspect processlist for advisory lock {LockName}. " +
                    "Database user may be missing PROCESS privilege. Error: {ErrorMessage}",
                    lockName, ex.Message);
                return false;
            }

            if (proc == null)
            {
                // Connection already gone — the lock should be released automatically.
                _logger.LogInformation(
                    "Lock holder connection {ConnectionId} for {LockName} no longer exists in processlist. " +
                    "It may have already been cleaned up.",
                    holderConnectionId, lockName);
                return false;
            }

            bool isSleeping = string.Equals(proc.COMMAND, "Sleep", StringComparison.OrdinalIgnoreCase);
            bool isOldEnough = proc.TIME >= StaleLockThresholdSeconds;
            bool isIdle = string.IsNullOrWhiteSpace(proc.INFO);

            _logger.LogInformation(
                "Lock holder inspection for {LockName}: ConnectionId={ConnectionId}, " +
                "Command={Command}, Time={Time}s, State={State}, DB={DB}, Info={Info}",
                lockName, proc.ID, proc.COMMAND, proc.TIME, proc.STATE, proc.DB,
                string.IsNullOrWhiteSpace(proc.INFO) ? "(idle)" : proc.INFO);

            if (isSleeping && isOldEnough && isIdle)
            {
                return true;
            }

            _logger.LogInformation(
                "Lock {LockName} is currently held by Connection ID {ConnectionId}, but it is not stale. " +
                "Skipping this automation run.",
                lockName, holderConnectionId);
            return false;
        }

        /// <summary>Attempts to KILL a confirmed-stale connection. Returns <c>true</c> if
        /// the KILL succeeded or the connection already disappeared.</summary>
        private async Task<bool> TryKillStaleConnectionAsync(
            MySqlConnection connection, long staleConnectionId, string lockName)
        {
            // Safety: validate connection ID is a positive integer (defense against injection).
            if (staleConnectionId <= 0)
            {
                _logger.LogWarning(
                    "Refusing to KILL invalid connection ID {ConnectionId} for lock {LockName}.",
                    staleConnectionId, lockName);
                return false;
            }

            try
            {
                _logger.LogWarning(
                    "Stale lock detected for {LockName} held by Connection ID {ConnectionId}. " +
                    "Attempting to self-heal...",
                    lockName, staleConnectionId);

                // MySQL KILL does not support parameterized connection ID in all providers.
                // The value is validated as a positive long — safe from injection.
                await connection.ExecuteAsync($"KILL {staleConnectionId};");

                _logger.LogWarning(
                    "Stale connection {ConnectionId} terminated. Proceeding with new job.",
                    staleConnectionId);

                // Brief pause to let MySQL fully release the advisory lock.
                await Task.Delay(TimeSpan.FromSeconds(1));
                return true;
            }
            catch (MySqlException ex) when (
                ex.Message.Contains("Unknown thread id", StringComparison.OrdinalIgnoreCase))
            {
                // Connection already gone — this is fine.
                _logger.LogInformation(
                    "Stale connection {ConnectionId} for {LockName} was already terminated.",
                    staleConnectionId, lockName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Unable to terminate stale connection {ConnectionId} for advisory lock {LockName}. " +
                    "Database user may be missing CONNECTION_ADMIN or SUPER privilege. Error: {ErrorMessage}",
                    staleConnectionId, lockName, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Full advisory lock acquisition flow with stale-lock self-healing.
        /// Returns <c>true</c> if the lock was successfully acquired.
        /// </summary>
        private async Task<bool> TryAcquireAdvisoryLockAsync(
            MySqlConnection connection, string lockName, string jobRunId, int year, int month)
        {
            _logger.LogInformation("Checking advisory lock status for {LockName}.", lockName);

            // ── Step 1: Check if lock is already held ────────────────────────────
            long currentConnectionId = await GetCurrentConnectionIdAsync(connection);
            long? holderConnectionId = await GetLockHolderConnectionIdAsync(connection, lockName);

            if (holderConnectionId.HasValue)
            {
                bool isStale = await IsStaleLockConnectionAsync(
                    connection, holderConnectionId.Value, currentConnectionId, lockName);

                if (isStale)
                {
                    bool killed = await TryKillStaleConnectionAsync(
                        connection, holderConnectionId.Value, lockName);

                    if (!killed)
                    {
                        _logger.LogWarning(
                            "Could not self-heal stale lock {LockName}. Aborting automation run {JobRunId}.",
                            lockName, jobRunId);
                        return false;
                    }
                }
                else if (holderConnectionId.Value != currentConnectionId)
                {
                    // Lock is held by an active (non-stale) process — do not proceed.
                    return false;
                }
            }

            // ── Step 2: Acquire the lock ─────────────────────────────────────────
            int? getLockResult = await connection.ExecuteScalarAsync<int?>(
                "SELECT GET_LOCK(@LockName, 5);",
                new { LockName = lockName });

            if (getLockResult == 1)
            {
                _logger.LogInformation("Advisory lock {LockName} acquired successfully.", lockName);
                return true;
            }

            if (getLockResult == 0)
            {
                _logger.LogWarning(
                    "Could not acquire advisory lock {LockName}. Another process may already be running.",
                    lockName);
            }
            else
            {
                _logger.LogWarning(
                    "GET_LOCK returned unexpected value {Result} for {LockName}. Lock was not acquired.",
                    getLockResult, lockName);
            }

            return false;
        }

        /// <summary>Releases the advisory lock and logs the result. Safe to call even
        /// if the connection is in a faulted state.</summary>
        private async Task ReleaseAdvisoryLockAsync(
            MySqlConnection connection, string lockName)
        {
            try
            {
                int? releaseResult = await connection.ExecuteScalarAsync<int?>(
                    "SELECT RELEASE_LOCK(@LockName);",
                    new { LockName = lockName });

                if (releaseResult == 1)
                {
                    _logger.LogInformation("Advisory lock {LockName} released successfully.", lockName);
                }
                else if (releaseResult == 0)
                {
                    _logger.LogWarning(
                        "RELEASE_LOCK returned 0 for {LockName} — lock was not established by this session.",
                        lockName);
                }
                else
                {
                    _logger.LogWarning(
                        "RELEASE_LOCK returned {Result} for {LockName} — lock may not exist or an error occurred.",
                        releaseResult, lockName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to release advisory lock {LockName}. Connection may already be closed.",
                    lockName);
            }
        }

        // ── Advisory Lock Keepalive ──────────────────────────────────────────────
        // Sends periodic SELECT 1 on the advisory lock connection to prevent MySQL
        // from closing the idle connection via wait_timeout or interactive_timeout.
        // This is the second layer of defence (first layer = high wait_timeout).

        /// <summary>Starts a background loop that pings the advisory lock connection
        /// every <see cref="AdvisoryLockKeepaliveIntervalSeconds"/> seconds.
        /// Must be stopped via <see cref="StopAdvisoryLockKeepalive"/> BEFORE
        /// accessing the connection from the main thread (e.g. RELEASE_LOCK).</summary>
        private void StartAdvisoryLockKeepalive(MySqlConnection connection)
        {
            StopAdvisoryLockKeepalive(); // idempotent cleanup of any prior instance
            _lockKeepaliveCts = new CancellationTokenSource();
            var token = _lockKeepaliveCts.Token;

            _ = Task.Run(async () =>
            {
                int pingCount = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(AdvisoryLockKeepaliveIntervalSeconds), token);
                        if (token.IsCancellationRequested) break;

                        await connection.ExecuteAsync("SELECT 1;", commandTimeout: 10);
                        pingCount++;

                        // Log every ~10 minutes to avoid flooding
                        if (pingCount % 10 == 0)
                        {
                            _logger.LogDebug(
                                "Advisory lock keepalive: {PingCount} pings sent ({Minutes} min). Connection alive.",
                                pingCount, pingCount * AdvisoryLockKeepaliveIntervalSeconds / 60);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Advisory lock keepalive ping failed after {PingCount} successful pings. " +
                            "Lock connection may have been terminated by MySQL.",
                            pingCount);
                        break;
                    }
                }
            }, CancellationToken.None);
        }

        /// <summary>Stops the keepalive loop. Safe to call even if never started.</summary>
        private void StopAdvisoryLockKeepalive()
        {
            try
            {
                _lockKeepaliveCts?.Cancel();
                _lockKeepaliveCts?.Dispose();
            }
            catch { /* defensive — Dispose may throw if already disposed */ }
            _lockKeepaliveCts = null;
        }

        private async Task<List<AutomationCityValidation>> LoadAutomationCityValidationsAsync(
            MySqlConnection connection,
            IReadOnlyCollection<string>? cityCodes = null)
        {
            bool filterByCodes = cityCodes != null && cityCodes.Count > 0;
            string sql = filterByCodes
                ? @"SELECT
                        c.Code,
                        c.FullName,
                        c.station_id AS StationId,
                        CASE WHEN c.station_id IS NOT NULL AND c.station_id <> '' THEN 1 ELSE 0 END AS HasStationId,
                        CASE WHEN EXISTS (
                            SELECT 1
                            FROM lcs_setup.locations l
                            WHERE l.BILLINGCITYID = c.station_id
                        ) THEN 1 ELSE 0 END AS HasLocationDefinition,
                        CASE WHEN EXISTS (
                            SELECT 1
                            FROM lcs_setup.locations l
                            INNER JOIN lcs_hr.hr_locationmapping lm
                                ON lm.GlLocationId = l.LocationID
                               AND lm.BStationId IS NOT NULL
                            WHERE l.BILLINGCITYID = c.station_id
                        ) THEN 1 ELSE 0 END AS HasStationMapping
                    FROM hr_city c
                    WHERE c.Code IN @Codes
                    ORDER BY c.FullName ASC;"
                : @"SELECT
                        c.Code,
                        c.FullName,
                        c.station_id AS StationId,
                        CASE WHEN c.station_id IS NOT NULL AND c.station_id <> '' THEN 1 ELSE 0 END AS HasStationId,
                        CASE WHEN EXISTS (
                            SELECT 1
                            FROM lcs_setup.locations l
                            WHERE l.BILLINGCITYID = c.station_id
                        ) THEN 1 ELSE 0 END AS HasLocationDefinition,
                        CASE WHEN EXISTS (
                            SELECT 1
                            FROM lcs_setup.locations l
                            INNER JOIN lcs_hr.hr_locationmapping lm
                                ON lm.GlLocationId = l.LocationID
                               AND lm.BStationId IS NOT NULL
                            WHERE l.BILLINGCITYID = c.station_id
                        ) THEN 1 ELSE 0 END AS HasStationMapping
                    FROM hr_city c
                    ORDER BY c.FullName ASC;";

            List<AutomationCityValidation> rows = filterByCodes
                ? (await connection.QueryAsync<AutomationCityValidation>(
                    sql,
                    new { Codes = cityCodes!.ToArray() })).ToList()
                : (await connection.QueryAsync<AutomationCityValidation>(sql)).ToList();

            foreach (AutomationCityValidation city in rows)
            {
                city.SkipReason = BuildAutomationCitySkipReason(city);
            }

            if (filterByCodes)
            {
                foreach (string missingCode in cityCodes!.Except(rows.Select(static city => city.Code), StringComparer.OrdinalIgnoreCase))
                {
                    rows.Add(new AutomationCityValidation
                    {
                        Code = missingCode,
                        FullName = missingCode,
                        SkipReason = "Skipped because hr_city has no matching configuration row for this city code."
                    });
                }
            }

            return rows;
        }

        private async Task MarkInvalidCityEntriesAsSkippedAsync(
            MySqlConnection connection,
            IReadOnlyCollection<CommissionAutomationLogEntry> entries,
            IReadOnlyDictionary<string, AutomationCityValidation> cityValidations,
            string jobRunId)
        {
            foreach (var city in entries
                .Select(static entry => new { entry.CityCode, entry.CityName })
                .Distinct())
            {
                if (string.IsNullOrWhiteSpace(city.CityCode))
                {
                    continue;
                }

                if (cityValidations.TryGetValue(city.CityCode, out AutomationCityValidation? cityValidation)
                    && cityValidation.IsValid)
                {
                    continue;
                }

                string skipReason = cityValidations.TryGetValue(city.CityCode, out cityValidation)
                    ? cityValidation.SkipReason
                    : "Skipped because city configuration could not be validated.";

                int updatedRows = await connection.ExecuteAsync(
                    @"UPDATE hr_commission_automation_log
                      SET status = 'Skipped',
                          progress_pct = 0,
                          retry_count = 0,
                          error_message = @Message,
                          completed_at = NOW(),
                          updated_at = NOW()
                      WHERE job_run_id = @JobRunId
                        AND city_code = @CityCode
                        AND status IN ('Pending', 'Failed', 'Running');",
                    new
                    {
                        JobRunId = jobRunId,
                        CityCode = city.CityCode,
                        Message = skipReason
                    });

                if (updatedRows == 0)
                {
                    continue;
                }

                foreach (CommissionAutomationLogEntry entry in entries.Where(entry =>
                    string.Equals(entry.CityCode, city.CityCode, StringComparison.OrdinalIgnoreCase)
                    && entry.Status is "Pending" or "Failed" or "Running"))
                {
                    entry.Status = "Skipped";
                    entry.RetryCount = 0;
                    entry.ErrorMessage = skipReason;
                }

                _logger.LogWarning(
                    "Skipping automation city {CityCode} {CityName}: {Reason}",
                    city.CityCode,
                    city.CityName,
                    skipReason);

                await BroadcastLogAsync(
                    jobRunId,
                    "WARN",
                    "",
                    city.CityCode,
                    city.CityName,
                    skipReason);
            }
        }

        private static string BuildAutomationCitySkipReason(AutomationCityValidation city)
        {
            if (!city.HasStationId)
            {
                return "Skipped because hr_city.station_id is not configured for this city.";
            }

            if (!city.HasLocationDefinition)
            {
                return $"Skipped because lcs_setup.locations has no BILLINGCITYID mapping for station_id {city.StationId}.";
            }

            if (!city.HasStationMapping)
            {
                return $"Skipped because hr_locationmapping has no BStationId mapping for station_id {city.StationId}.";
            }

            return string.Empty;
        }

        private static async Task MarkJobRunAsDuplicateAsync(
            MySqlConnection connection,
            string jobRunId,
            string message)
        {
            await connection.ExecuteAsync(
                @"UPDATE hr_commission_automation_log
                  SET status = 'Failed',
                      progress_pct = 0,
                      error_message = @Message,
                      completed_at = NOW(),
                      updated_at = NOW()
                  WHERE job_run_id = @JobRunId
                    AND status IN ('Pending', 'Running');",
                new
                {
                    JobRunId = jobRunId,
                    Message = message
                });
        }

        private static bool IsRetryableCommissionFailure(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                string message = current.Message ?? string.Empty;
                if (message.Contains("Lock wait timeout", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Deadlock found", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("Connection must be valid and open to rollback transaction", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<(int processedCount, int totalCount)> ExecuteCommissionTypeAsync(
            string commissionType, string cityCode, int year, int month, string userId,
            Func<int, int, Task>? onProgress = null)
        {
            switch (commissionType)
            {
                case "CashCommission":
                {
                    var model = new CashCommissionViewModel
                    {
                        Year = year, Month = month, CityCode = cityCode, BillingStatus = true
                    };
                    var result = await _payrollService.ProcessCashCommissionAsync(model, userId, onProgress);
                    if (!result.Success)
                        throw new InvalidOperationException(result.Message);
                    return (result.CashRowsInserted + result.VasRowsInserted,
                            result.CashSourceRowsRetrieved + result.VasSourceRowsRetrieved);
                }
                case "CodCommission":
                {
                    var model = new CodCommissionViewModel
                    {
                        Year = year, Month = month, CityCode = cityCode
                    };
                    var result = await _payrollService.ProcessCodCommissionAsync(model, userId, onProgress);
                    if (!result.Success)
                    {
                        // "No record found for cod commission" is a valid NoData scenario, not a retryable failure.
                        if (result.Message != null && result.Message.Contains("No record found", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"NoData: {result.Message}");
                        throw new InvalidOperationException(result.Message);
                    }
                    int processed = result.ConsignmentRowsInserted + result.ActivityRowsInserted
                                  + result.ReturnShipmentRowsInserted + result.CommissionRowsInserted;
                    return (processed, processed); // total = processed for single-transaction types
                }
                case "OverLandCommission":
                {
                    var model = new OverLandCommissionViewModel
                    {
                        Year = year, Month = month, CityCode = cityCode,
                        BillingStatus = true, AttendanceStatus = true
                    };
                    var result = await _payrollService.ProcessOverLandCommissionAsync(model, userId, onProgress);
                    if (!result.Success)
                        throw new InvalidOperationException(result.Message);
                    return (result.OleRowsInserted + result.RbiRowsInserted + result.ProcessRowsInserted,
                            result.OleRowsInserted + result.RbiRowsInserted + result.ProcessRowsInserted);
                }
                case "ReturnCodCommission":
                {
                    var model = new ReturnCodCommissionViewModel
                    {
                        Year = year, Month = month, CityCode = cityCode, BillingStatus = true
                    };
                    var result = await _payrollService.ProcessReturnCodCommissionAsync(model, userId, onProgress);
                    if (!result.Success)
                    {
                        if (result.Message != null && result.Message.Contains("No record found", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"NoData: {result.Message}");
                        throw new InvalidOperationException(result.Message);
                    }
                    int processed = result.ConsignmentRowsInserted + result.CommissionRowsInserted
                                  + result.ProcessRowsInserted;
                    return (processed, result.SourceRowsRetrieved);
                }
                case "CommissionProcess":
                {
                    var model = new CommissionProcessViewModel
                    {
                        Year = year, Month = month, CityCode = cityCode,
                        BillingStatusConfirmed = true,
                        AttendanceStatusConfirmed = true,
                        AllCommissionTypesConfirmed = true
                    };
                    var (success, message) = await _payrollService.ProcessCommissionAsync(model, userId);
                    if (!success)
                        throw new InvalidOperationException(message);
                    return (0, 0);
                }
                case "FinalCommission":
                {
                    var model = new FinalCommissionProcessViewModel
                    {
                        Year = year, Month = month, CityCode = cityCode
                    };
                    var result = await _payrollService.ProcessFinalCommissionAsync(model, userId, onProgress);
                    if (!result.Success)
                        throw new InvalidOperationException(result.Message);
                    // 0 rows = MIS source tables have no data for this city/period.
                    // Not an error — mirrors old project's empty else {} (lines 186-188).
                    // Signal "NoData:" so the automation layer can set a distinct status
                    // instead of "Completed", keeping city out of the done-count.
                    if (result.ProcessedCount == 0)
                        throw new InvalidOperationException("NoData: " + result.Message);
                    return (result.ProcessedCount, result.ProcessedCount);
                }
                default:
                    throw new ArgumentException($"Unknown commission type: {commissionType}");
            }
        }

        private async Task BroadcastAsync(
            string jobRunId, int logId, string status, int progressPct,
            string? errorMessage, string cityName, string commissionType, int retryCount,
            int processedCount = 0, int totalCount = 0)
        {
            try
            {
                var update = new AutomationProgressUpdate
                {
                    LogId = logId,
                    Status = status,
                    ProgressPct = progressPct,
                    ErrorMessage = errorMessage,
                    CityName = cityName,
                    CommissionType = commissionType,
                    RetryCount = retryCount,
                    ProcessedCount = processedCount,
                    TotalCount = totalCount
                };
                await _hubContext.Clients.Group(jobRunId).ProgressUpdate(update);
            }
            catch (Exception ex)
            {
                // SignalR failure must never fail the commission processing
                _logger.LogWarning(ex, "SignalR broadcast failed for logId={LogId}. Continuing.", logId);
            }
        }

        private async Task BroadcastLogAsync(
            string jobRunId, string level, string commissionType, string cityCode,
            string cityName, string message)
        {
            try
            {
                await _hubContext.Clients.Group(jobRunId).LogEntry(new CommissionLogEntry
                {
                    Ts    = DateTime.Now.ToString("HH:mm:ss"),
                    Level = level,
                    Comm  = commissionType,
                    City  = string.IsNullOrEmpty(cityName) ? "" : $"{cityName} ({cityCode})",
                    Msg   = message
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "SignalR commission log broadcast failed for jobRunId={JobRunId}, commissionType={CommissionType}, cityCode={CityCode}.",
                    jobRunId,
                    commissionType,
                    cityCode);
            }
        }

        // ─── Pre-flight base-data validation ─────────────────────────────────────────
        // Date range: 21st of previous month → 20th of current month  (matches commission period)

        public async Task<CommissionBaseDataValidationResult> ValidateBaseDataAsync(int year, int month)
        {
            var requiredFrom = new DateTime(year, month, 21).AddMonths(-1);
            var requiredTo   = new DateTime(year, month, 20);

            // Each entry represents one source table that must have data in the commission window.
            // COD and ReturnCOD both read lcs_db.arival via Central_OPS — listed separately so
            // the user can see per-commission status clearly.
            var checks = new List<CommissionTableCheckResult>
            {
                new() { CommissionType = "CashCommission",       Label = "Cash Commission — Billing Details",          TableName = "lcs_billing.billing_details",          ConnectionName = "LHR_Billing",  DateField = "BILLING_DATE" },
                new() { CommissionType = "CashCommission",       Label = "Cash Commission — Retail COD Bookings",      TableName = "lcs.rms_cod_booking",                  ConnectionName = "LHR_Billing",  DateField = "Book_date"    },
                new() { CommissionType = "CodCommission",        Label = "COD Commission — Arrivals",                  TableName = "lcs_db.arival",                        ConnectionName = "Central_OPS",  DateField = "COUR_DATE"    },
                new() { CommissionType = "OverLandCommission",   Label = "OverLand Commission — Billing Details",      TableName = "lcs_billing_download.billing_details", ConnectionName = "MIS",          DateField = "billing_date" },
                new() { CommissionType = "ReturnCodCommission",  Label = "Return COD Commission — Arrivals",           TableName = "lcs_db.arival",                        ConnectionName = "Central_OPS",  DateField = "COUR_DATE"    },
            };

            // Run all checks in parallel for speed
            await Task.WhenAll(checks.Select(c => CheckSourceTableAsync(c, requiredFrom, requiredTo)));

            return new CommissionBaseDataValidationResult
            {
                IsValid      = checks.All(c => c.ConnectionAvailable && c.HasData),
                Year         = year,
                Month        = month,
                RequiredFrom = requiredFrom,
                RequiredTo   = requiredTo,
                TableChecks  = checks
            };
        }

        private async Task CheckSourceTableAsync(
            CommissionTableCheckResult check, DateTime from, DateTime to)
        {
            var connStr = _configuration.GetConnectionString(check.ConnectionName);
            if (string.IsNullOrWhiteSpace(connStr))
            {
                check.ConnectionAvailable = false;
                check.ConnectionError     = $"Connection string '{check.ConnectionName}' is not configured in appsettings.";
                return;
            }

            try
            {
                using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                check.ConnectionAvailable = true;

                var count = await conn.QueryFirstAsync<long>(
                    $"SELECT COUNT(*) FROM (SELECT 1 FROM {check.TableName} WHERE {check.DateField} BETWEEN @From AND @To LIMIT 1) AS _chk",
                    new { From = from, To = to },
                    commandTimeout: 120);

                check.RowCount = count;
                check.HasData  = count > 0;
            }
            catch (Exception ex)
            {
                check.ConnectionAvailable = false;
                check.ConnectionError     = ex.Message.Length > 250 ? ex.Message[..250] : ex.Message;
                _logger.LogWarning("Pre-flight check failed for {Table} on {Conn}: {Error}",
                    check.TableName, check.ConnectionName, check.ConnectionError);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────

        public async Task<CommissionAutomationDashboardViewModel> GetDashboardAsync(int year, int month)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create database connection.");
            await connection.OpenAsync();

            var entries = (await connection.QueryAsync<CommissionAutomationLogEntry>(
                @"SELECT id AS Id, job_run_id AS JobRunId,
                         triggered_by AS TriggeredBy, triggered_by_user_id AS TriggeredByUserId,
                         year AS Year, month AS Month,
                         commission_type AS CommissionType, city_code AS CityCode, city_name AS CityName,
                         status AS Status, progress_pct AS ProgressPct, started_at AS StartedAt,
                         completed_at AS CompletedAt, error_message AS ErrorMessage,
                         retry_count AS RetryCount, processed_count AS ProcessedCount,
                         total_count AS TotalCount, created_at AS CreatedAt, updated_at AS UpdatedAt
                  FROM hr_commission_automation_log
                  WHERE year = @Year AND month = @Month
                  ORDER BY city_name ASC, id ASC",
                new { Year = year, Month = month })).ToList();

            // Fetch zone name for each city that appears in the log entries
            var cityCodes = entries.Select(e => e.CityCode).Distinct().ToList();
            var cityZones = new Dictionary<string, string>();
            if (cityCodes.Any())
            {
                var zoneRows = await connection.QueryAsync<(string CityCode, string ZoneName)>(
                    @"SELECT c.Code AS CityCode, COALESCE(z.FullName, 'N/A') AS ZoneName
                      FROM hr_city c
                      LEFT JOIN hr_regionalzones z ON z.Code = c.RZoneCode
                      WHERE c.Code IN @Codes",
                    new { Codes = cityCodes });

                foreach (var row in zoneRows)
                    cityZones[row.CityCode] = row.ZoneName;
            }

            var currentYear = DateTime.Now.Year;

            return new CommissionAutomationDashboardViewModel
            {
                Year = year,
                Month = month,
                JobRunId = entries.FirstOrDefault()?.JobRunId,
                Entries = entries,
                IsRunning = entries.Any(e => e.Status == "Running"),
                AvailableYears = Enumerable.Range(currentYear - 2, 4).ToList(),
                CityZones = cityZones
            };
        }

        // ─── Commission History Reconciliation ────────────────────────────────────────
        // Merges hr_commission_automation_log (ALL historical entries) with evidence from
        // 6 actual output tables. Each CommissionHistoryEntry carries:
        //   • DerivedStatus   — final reconciled state (latest log + table evidence)
        //   • LogEntry        — the most-recent log row for this city×commType
        //   • AllLogEntries   — every log row ever written (full audit trail, newest first)
        //   • TriggeredByUserName / EvidenceUserName — resolved from lcs_users
        public async Task<CommissionHistoryViewModel> GetReconciledHistoryAsync(int year, int month)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create database connection.");
            await connection.OpenAsync();

            await EnsureLogTableSchemaAsync(connection);

            // ── 1. ALL automation log entries for the period (not deduplicated) ───────
            var logEntries = (await connection.QueryAsync<CommissionAutomationLogEntry>(
                @"SELECT id AS Id, job_run_id AS JobRunId,
                         triggered_by AS TriggeredBy, triggered_by_user_id AS TriggeredByUserId,
                         year AS Year, month AS Month,
                         commission_type AS CommissionType, city_code AS CityCode, city_name AS CityName,
                         status AS Status, progress_pct AS ProgressPct, started_at AS StartedAt,
                         completed_at AS CompletedAt, error_message AS ErrorMessage,
                         retry_count AS RetryCount, processed_count AS ProcessedCount,
                         total_count AS TotalCount, created_at AS CreatedAt, updated_at AS UpdatedAt
                  FROM hr_commission_automation_log
                  WHERE year = @Year AND month = @Month
                  ORDER BY id ASC",
                new { Year = year, Month = month })).ToList();

            // ── 1b. Execution history (all sources: Automation + Manual) ─────────────
            var executionRecords = await _executionHistory.GetByMonthAsync(year, month);
            var executionsByKey = executionRecords
                .GroupBy(e => (e.CityCode, e.CommissionType))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(e => e.Id).ToList());

            // ── 2. Commission period date range ───────────────────────────────────────
            var fromDate = new DateTime(year, month, 21).AddMonths(-1);
            var toDate   = new DateTime(year, month, 20);

            // ── 3. Bulk evidence queries against actual output tables ─────────────────
            var evidence       = new List<CommissionTableEvidence>();
            var evidenceErrors = new Dictionary<string, string>();

            async Task RunEvidenceQueryAsync(string commType, string sql, object param, int timeout = 60)
            {
                try
                {
                    var rows = await connection.QueryAsync<CommissionTableEvidence>(sql, param, commandTimeout: timeout);
                    foreach (var r in rows) r.CommissionType = commType;
                    evidence.AddRange(rows);
                }
                catch (Exception ex)
                {
                    var msg = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
                    evidenceErrors[commType] = msg;
                    _logger.LogWarning("History evidence query failed for {CommType} ({Year}/{Month}): {Err}",
                        commType, year, month, msg);
                }
            }

            await RunEvidenceQueryAsync("CodCommission",
                $@"SELECT City AS CityCode, '' AS CommissionType,
                         COUNT(*) AS RowCount, MAX(CreatedDate) AS ProcessedAt,
                         MAX(CAST(CreatedBy AS CHAR)) AS ProcessedByUserId
                  FROM {CodCommissionTable}
                  WHERE Year = @Year AND Month = @Month
                  GROUP BY City",
                new { Year = year, Month = month });

            await RunEvidenceQueryAsync("OverLandCommission",
                $@"SELECT hc.Code AS CityCode, '' AS CommissionType,
                         COUNT(*) AS RowCount, MAX(p.CreatedDate) AS ProcessedAt,
                         MAX(CAST(p.CreatedBy AS CHAR)) AS ProcessedByUserId
                  FROM {OleCommissionProcessTable} p
                  INNER JOIN hr_locationmapping lm ON lm.GlLocationId = p.GlLocationId
                  INNER JOIN hr_city hc ON hc.station_id = lm.BStationId
                  WHERE p.Year = @Year AND p.Month = @Month
                    AND hc.Code IS NOT NULL AND hc.Code <> ''
                  GROUP BY hc.Code",
                new { Year = year, Month = month });

            await RunEvidenceQueryAsync("ReturnCodCommission",
                $@"SELECT hc.Code AS CityCode, '' AS CommissionType,
                         COUNT(*) AS RowCount, MAX(p.CreatedDate) AS ProcessedAt,
                         MAX(CAST(p.CreatedBy AS CHAR)) AS ProcessedByUserId
                  FROM {CodReturnCommissionProcessTable} p
                  INNER JOIN hr_locationmapping lm ON lm.GlLocationId = p.GlLocationId
                  INNER JOIN hr_city hc ON hc.station_id = lm.BStationId
                  WHERE p.Year = @Year AND p.Month = @Month
                    AND hc.Code IS NOT NULL AND hc.Code <> ''
                  GROUP BY hc.Code",
                new { Year = year, Month = month });

            await RunEvidenceQueryAsync("CommissionProcess",
                $@"SELECT citycode AS CityCode, '' AS CommissionType,
                         COUNT(*) AS RowCount, MAX(CreatedDate) AS ProcessedAt,
                         MAX(CAST(CreatedBy AS CHAR)) AS ProcessedByUserId
                  FROM {CommissionProcessTable}
                  WHERE year = @Year AND month = @Month
                  GROUP BY citycode",
                new { Year = year, Month = month });

            await RunEvidenceQueryAsync("FinalCommission",
                @"SELECT city AS CityCode, '' AS CommissionType,
                         COUNT(*) AS RowCount, MAX(created_at) AS ProcessedAt,
                         MAX(userId) AS ProcessedByUserId
                  FROM is_final_commission_process
                  WHERE year = @Year AND month = @Month
                  GROUP BY city",
                new { Year = year, Month = month });

            await RunEvidenceQueryAsync("CashCommission",
                $@"SELECT hc.Code AS CityCode, '' AS CommissionType,
                         COUNT(*) AS RowCount, MAX(c.CreatedDate) AS ProcessedAt,
                         NULL AS ProcessedByUserId
                  FROM {CashConsignmentsTable} c
                  INNER JOIN hr_locationmapping lm ON lm.BStationId = c.Station_id
                  INNER JOIN hr_city hc ON hc.station_id = lm.BStationId
                  WHERE c.billing_date BETWEEN @FromDate AND @ToDate
                    AND hc.Code IS NOT NULL AND hc.Code <> ''
                  GROUP BY hc.Code",
                new { FromDate = fromDate, ToDate = toDate },
                timeout: 120);

            // ── 4. Batch resolve user names from lcs_users ────────────────────────────
            var allUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var le in logEntries)
            {
                if (!string.IsNullOrWhiteSpace(le.TriggeredByUserId)) allUserIds.Add(le.TriggeredByUserId);
            }
            foreach (var ev in evidence)
            {
                if (!string.IsNullOrWhiteSpace(ev.ProcessedByUserId)) allUserIds.Add(ev.ProcessedByUserId);
            }
            foreach (var er in executionRecords)
            {
                if (!string.IsNullOrWhiteSpace(er.TriggeredByUserId)) allUserIds.Add(er.TriggeredByUserId);
            }

            var userNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (allUserIds.Any())
            {
                var validIds = allUserIds
                    .Where(id => int.TryParse(id, out _))
                    .Select(id => int.Parse(id))
                    .ToList();

                if (validIds.Any())
                {
                    var userRows = await connection.QueryAsync<(int UserId, string UserName)>(
                        "SELECT userID AS UserId, UserName FROM lcs_users WHERE userID IN @Ids",
                        new { Ids = validIds });
                    foreach (var row in userRows)
                        userNameMap[row.UserId.ToString()] = row.UserName;
                }
            }

            // ── 5. Build per-city×commType lookup structures ──────────────────────────
            var logsByKey = logEntries
                .GroupBy(e => (e.CityCode, e.CommissionType))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(e => e.Id).ToList());

            var evidenceLookup = evidence
                .GroupBy(e => (e.CityCode, e.CommissionType))
                .ToDictionary(g => g.Key, g => g.First());

            // ── 6. Collect all unique city codes ──────────────────────────────────────
            var allCityCodes = logEntries.Select(e => e.CityCode)
                .Union(evidence.Select(e => e.CityCode))
                .Distinct()
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            // ── 7. City name lookup ───────────────────────────────────────────────────
            var cityNameLookup = logEntries
                .Select(e => new { e.CityCode, e.CityName })
                .GroupBy(e => e.CityCode)
                .ToDictionary(g => g.Key, g => g.First().CityName);

            if (allCityCodes.Any())
            {
                var missingCodes = allCityCodes.Where(c => !cityNameLookup.ContainsKey(c)).ToList();
                if (missingCodes.Any())
                {
                    var cityRows = await connection.QueryAsync<(string Code, string FullName)>(
                        "SELECT Code, FullName FROM hr_city WHERE Code IN @Codes",
                        new { Codes = missingCodes });
                    foreach (var row in cityRows)
                        cityNameLookup[row.Code] = row.FullName;
                }
            }

            // ── 8. Merge log + evidence → CommissionHistoryEntry per city×commType ────
            var commissionTypes = new[]
            {
                "CashCommission", "CodCommission", "OverLandCommission",
                "ReturnCodCommission", "CommissionProcess", "FinalCommission"
            };

            var resultEntries = new List<CommissionHistoryEntry>();

            foreach (var cityCode in allCityCodes.OrderBy(c => cityNameLookup.GetValueOrDefault(c, c)))
            {
                var cityName = cityNameLookup.GetValueOrDefault(cityCode, cityCode);

                foreach (var commType in commissionTypes)
                {
                    var key = (cityCode, commType);
                    logsByKey.TryGetValue(key, out var allLogsForKey);
                    var latestLog   = allLogsForKey?.FirstOrDefault();
                    evidenceLookup.TryGetValue(key, out var tableEvidence);

                    bool hasLog   = latestLog != null;
                    bool hasTable = tableEvidence != null && tableEvidence.RowCount > 0;
                    string logStatus = latestLog?.Status ?? "";

                    string derivedStatus;
                    string statusSource;

                    if (hasLog && logStatus is "Completed" or "AlreadyProcessed" or "Skipped")
                    {
                        derivedStatus = logStatus;
                        statusSource  = hasTable ? "Both" : "Log";
                    }
                    else if (hasLog && logStatus == "NoData")
                    {
                        derivedStatus = hasTable ? "Processed" : "NoData";
                        statusSource  = hasTable ? "Table" : "Log";
                    }
                    else if (hasTable)
                    {
                        derivedStatus = "Processed";
                        statusSource  = hasLog ? "Both" : "Table";
                    }
                    else if (hasLog)
                    {
                        derivedStatus = logStatus is "Failed" or "Pending" or "Running"
                            ? logStatus : "Pending";
                        statusSource = "Log";
                    }
                    else
                    {
                        derivedStatus = "NoHistory";
                        statusSource  = "None";
                    }

                    var triggeredByUserId = latestLog?.TriggeredByUserId ?? "";
                    var triggeredByUserName = !string.IsNullOrWhiteSpace(triggeredByUserId)
                        ? userNameMap.GetValueOrDefault(triggeredByUserId)
                        : null;

                    var evidenceUserId   = tableEvidence?.ProcessedByUserId ?? "";
                    var evidenceUserName = !string.IsNullOrWhiteSpace(evidenceUserId)
                        ? userNameMap.GetValueOrDefault(evidenceUserId)
                        : null;

                    resultEntries.Add(new CommissionHistoryEntry
                    {
                        CityCode              = cityCode,
                        CityName              = cityName,
                        CommissionType        = commType,
                        DerivedStatus         = derivedStatus,
                        StatusSource          = statusSource,
                        EvidenceRowCount      = tableEvidence?.RowCount ?? latestLog?.ProcessedCount ?? 0,
                        ProcessedAt           = tableEvidence?.ProcessedAt ?? latestLog?.CompletedAt,
                        ProcessedByUserId     = tableEvidence?.ProcessedByUserId,
                        ErrorMessage          = latestLog?.ErrorMessage,
                        RetryCount            = latestLog?.RetryCount ?? 0,
                        LogEntry              = latestLog,
                        AllLogEntries         = allLogsForKey ?? new List<CommissionAutomationLogEntry>(),
                        AllExecutions         = executionsByKey.TryGetValue(key, out var execsForKey)
                                                    ? execsForKey
                                                    : new List<CommissionExecutionRecord>(),
                        TriggeredByUserName   = triggeredByUserName,
                        EvidenceUserName      = evidenceUserName
                    });
                }
            }

            // Drop cities where every step is NoHistory
            resultEntries = resultEntries
                .GroupBy(e => e.CityCode)
                .Where(g => g.Any(e => e.DerivedStatus != "NoHistory"))
                .SelectMany(g => g)
                .ToList();

            // ── 9. Zone lookup ────────────────────────────────────────────────────────
            var finalCityCodes = resultEntries.Select(e => e.CityCode).Distinct().ToList();
            var cityZones = new Dictionary<string, string>();
            if (finalCityCodes.Any())
            {
                var zoneRows = await connection.QueryAsync<(string CityCode, string ZoneName)>(
                    @"SELECT c.Code AS CityCode, COALESCE(z.FullName, 'N/A') AS ZoneName
                      FROM hr_city c
                      LEFT JOIN hr_regionalzones z ON z.Code = c.RZoneCode
                      WHERE c.Code IN @Codes",
                    new { Codes = finalCityCodes });
                foreach (var row in zoneRows)
                    cityZones[row.CityCode] = row.ZoneName;
            }

            return new CommissionHistoryViewModel
            {
                Year           = year,
                Month          = month,
                JobRunId       = logEntries.Select(e => e.JobRunId).Distinct().LastOrDefault(),
                Entries        = resultEntries,
                AvailableYears = Enumerable.Range(DateTime.Now.Year - 2, 4).ToList(),
                CityZones      = cityZones,
                HasLogData     = logEntries.Any(),
                HasTableData   = evidence.Any(),
                EvidenceErrors = evidenceErrors
            };
        }

        private sealed class AutomationCityValidation
        {
            public string Code { get; init; } = string.Empty;
            public string FullName { get; init; } = string.Empty;
            public string? StationId { get; init; }
            public bool HasStationId { get; init; }
            public bool HasLocationDefinition { get; init; }
            public bool HasStationMapping { get; init; }
            public string SkipReason { get; set; } = string.Empty;

            public bool IsValid => HasStationId && HasLocationDefinition && HasStationMapping;
        }

    }
}
