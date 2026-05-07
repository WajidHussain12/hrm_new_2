using System.Data;
using System.Runtime.CompilerServices;
using Dapper;
using LCS_HR_MVC.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public sealed class CommissionAutomationRecoveryHostedService : BackgroundService
    {
        private const int DefaultHeartbeatStaleSeconds = 90;
        private const int DefaultHardStepTimeoutSeconds = 12 * 60;
        private const int DefaultScanSeconds = 60;
        private const int DefaultAutoResumeRecentHours = 24;
        private const int DefaultAutoResumeCooldownSeconds = 180;
        private const string AutoRecoveryUserId = "210";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CommissionAutomationRecoveryHostedService> _logger;
        private readonly IConfiguration _configuration;

        public CommissionAutomationRecoveryHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<CommissionAutomationRecoveryHostedService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            bool autoRecoverOnStartup = _configuration.GetValue(
                "CommissionSettings:AutoRecoverRunningOnStartup",
                true);

            if (autoRecoverOnStartup)
            {
                await RecoverRunningRowsAsync(forceAllRunningRows: true, stoppingToken);
            }

            int scanSeconds = _configuration.GetValue(
                "CommissionSettings:RecoveryScanSeconds",
                DefaultScanSeconds);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scanSeconds));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RecoverRunningRowsAsync(forceAllRunningRows: false, stoppingToken);
            }
        }

        private async Task RecoverRunningRowsAsync(bool forceAllRunningRows, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var connectionFactory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();

                using IDbConnection connection = connectionFactory.CreateConnection();
                connection.Open();

                int staleSeconds = _configuration.GetValue(
                    "CommissionSettings:RunningStepRecoverySeconds",
                    DefaultHeartbeatStaleSeconds);

                int hardTimeoutSeconds = _configuration.GetValue(
                    "CommissionSettings:HardStepTimeoutSeconds",
                    DefaultHardStepTimeoutSeconds);

                int affected = await RecoverRunningRowsCoreAsync(
                    connection,
                    forceAllRunningRows,
                    staleSeconds,
                    hardTimeoutSeconds,
                    cancellationToken);

                int cleaned = await CleanupSupersededDashboardRowsAsync(connection, cancellationToken);

                if (affected > 0)
                {
                    _logger.LogWarning(
                        "[CommissionRecovery] Auto-marked {AffectedRows} Running automation row(s) as Failed. ForceAll={ForceAllRunningRows}, StaleSeconds={StaleSeconds}, HardTimeoutSeconds={HardTimeoutSeconds}",
                        affected,
                        forceAllRunningRows,
                        staleSeconds,
                        hardTimeoutSeconds);
                }

                if (cleaned > 0)
                {
                    _logger.LogWarning(
                        "[CommissionRecovery] Cleaned {CleanedRows} superseded/duplicate automation dashboard row(s) as Skipped.",
                        cleaned);
                }

                bool autoResumeEnabled = _configuration.GetValue(
                    "CommissionSettings:AutoResumeAfterRecoveryFailure",
                    true);

                if (!forceAllRunningRows && affected > 0 && autoResumeEnabled)
                {
                    int recentHours = _configuration.GetValue(
                        "CommissionSettings:AutoResumeRecentHours",
                        DefaultAutoResumeRecentHours);

                    int cooldownSeconds = _configuration.GetValue(
                        "CommissionSettings:AutoResumeCooldownSeconds",
                        DefaultAutoResumeCooldownSeconds);

                    AutoResumeCandidate? candidate = await FindAutoResumeCandidateAsync(
                        connection,
                        recentHours,
                        cooldownSeconds,
                        cancellationToken);

                    if (candidate != null)
                    {
                        var automationService = scope.ServiceProvider.GetRequiredService<ICommissionAutomationService>();
                        string jobRunId = await automationService.StartAutomationAsync(
                            candidate.Year,
                            candidate.Month,
                            "AutoRecovery",
                            AutoRecoveryUserId);

                        _logger.LogWarning(
                            "[CommissionRecovery] Auto-resumed commission automation after recovered stale row(s). NewJobRunId={JobRunId}, Period={Year}/{Month}",
                            jobRunId,
                            candidate.Year,
                            candidate.Month);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CommissionRecovery] Failed to recover stale commission automation rows.");
            }
        }

        private static async Task<int> RecoverRunningRowsCoreAsync(
            IDbConnection connection,
            bool forceAllRunningRows,
            int staleSeconds,
            int hardTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            string reason = forceAllRunningRows
                ? "Auto recovery: application started while commission automation had orphaned Running row(s). Marked Failed so automation can resume/retry automatically."
                : $"Auto recovery: commission automation Running row was stale or exceeded hard timeout ({hardTimeoutSeconds} seconds). Marked Failed so automation can resume/retry automatically.";

            string sql = forceAllRunningRows
                ? @"
UPDATE lcs_hr.hr_commission_automation_log
SET status = 'Failed',
    progress_pct = 0,
    error_message = @Reason,
    completed_at = NOW(),
    updated_at = NOW()
WHERE status = 'Running';"
                : @"
UPDATE lcs_hr.hr_commission_automation_log
SET status = 'Failed',
    progress_pct = 0,
    error_message = @Reason,
    completed_at = NOW(),
    updated_at = NOW()
WHERE status = 'Running'
  AND (
        TIMESTAMPDIFF(SECOND, updated_at, NOW()) >= @StaleSeconds
        OR TIMESTAMPDIFF(SECOND, started_at, NOW()) >= @HardTimeoutSeconds
      );";

            return await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Reason = reason,
                        StaleSeconds = staleSeconds,
                        HardTimeoutSeconds = hardTimeoutSeconds
                    },
                    cancellationToken: cancellationToken));
        }

        private static async Task<int> CleanupSupersededDashboardRowsAsync(
            IDbConnection connection,
            CancellationToken cancellationToken)
        {
            const string supersededSql = @"
UPDATE lcs_hr.hr_commission_automation_log stale
JOIN lcs_hr.hr_commission_automation_log done
  ON done.year = stale.year
 AND done.month = stale.month
 AND done.city_code = stale.city_code
 AND done.commission_type = stale.commission_type
 AND done.status IN ('Completed', 'AlreadyProcessed')
 AND done.updated_at IS NOT NULL
 AND stale.updated_at IS NOT NULL
 AND done.updated_at > stale.updated_at
SET stale.status = 'Skipped',
    stale.progress_pct = 0,
    stale.error_message = 'Auto cleanup: superseded by a later Completed/AlreadyProcessed entry for the same city and commission type.',
    stale.completed_at = COALESCE(stale.completed_at, NOW()),
    stale.updated_at = NOW()
WHERE stale.status IN ('Pending', 'Running');";

            const string duplicateGateSql = @"
UPDATE lcs_hr.hr_commission_automation_log
SET status = 'Skipped',
    progress_pct = 0,
    error_message = 'Auto cleanup: duplicate job row skipped because another automation job was already executing.',
    completed_at = COALESCE(completed_at, NOW()),
    updated_at = NOW()
WHERE status = 'Failed'
  AND error_message LIKE 'Blocked by in-process concurrency gate%';";

            int affected = 0;
            affected += await connection.ExecuteAsync(
                new CommandDefinition(supersededSql, cancellationToken: cancellationToken));
            affected += await connection.ExecuteAsync(
                new CommandDefinition(duplicateGateSql, cancellationToken: cancellationToken));
            return affected;
        }

        private static async Task<AutoResumeCandidate?> FindAutoResumeCandidateAsync(
            IDbConnection connection,
            int recentHours,
            int cooldownSeconds,
            CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT year AS Year,
       month AS Month
FROM lcs_hr.hr_commission_automation_log candidate
WHERE candidate.status = 'Failed'
  AND candidate.retry_count < 3
  AND candidate.error_message LIKE 'Auto recovery:%'
  AND candidate.updated_at >= DATE_SUB(NOW(), INTERVAL @RecentHours HOUR)
  AND NOT EXISTS (
      SELECT 1
      FROM lcs_hr.hr_commission_automation_log running
      WHERE running.year = candidate.year
        AND running.month = candidate.month
        AND running.status = 'Running'
  )
  AND NOT EXISTS (
      SELECT 1
      FROM lcs_hr.hr_commission_automation_log pending
      WHERE pending.year = candidate.year
        AND pending.month = candidate.month
        AND pending.status = 'Pending'
        AND COALESCE(pending.updated_at, pending.created_at) >= DATE_SUB(NOW(), INTERVAL @CooldownSeconds SECOND)
  )
GROUP BY year, month
ORDER BY MAX(candidate.updated_at) DESC
LIMIT 1;";

            return await connection.QueryFirstOrDefaultAsync<AutoResumeCandidate>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        RecentHours = recentHours,
                        CooldownSeconds = cooldownSeconds
                    },
                    cancellationToken: cancellationToken));
        }

        private sealed class AutoResumeCandidate
        {
            public int Year { get; set; }
            public int Month { get; set; }
        }

        [ModuleInitializer]
        public static void StartModuleRecoveryLoop()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(8));

                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile(
                        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                        optional: true,
                        reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .Build();

                bool autoRecoverOnStartup = configuration.GetValue(
                    "CommissionSettings:AutoRecoverRunningOnStartup",
                    true);

                if (!autoRecoverOnStartup)
                {
                    return;
                }

                string? connectionString = configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return;
                }

                int staleSeconds = configuration.GetValue(
                    "CommissionSettings:RunningStepRecoverySeconds",
                    DefaultHeartbeatStaleSeconds);

                int hardTimeoutSeconds = configuration.GetValue(
                    "CommissionSettings:HardStepTimeoutSeconds",
                    DefaultHardStepTimeoutSeconds);

                int scanSeconds = configuration.GetValue(
                    "CommissionSettings:RecoveryScanSeconds",
                    DefaultScanSeconds);

                while (true)
                {
                    try
                    {
                        using var connection = new MySqlConnection(connectionString);
                        await connection.OpenAsync();

                        int affected = await RecoverRunningRowsCoreAsync(
                            connection,
                            forceAllRunningRows: true,
                            staleSeconds,
                            hardTimeoutSeconds,
                            cancellationToken: CancellationToken.None);

                        int cleaned = await CleanupSupersededDashboardRowsAsync(
                            connection,
                            cancellationToken: CancellationToken.None);

                        if (affected > 0)
                        {
                            Console.WriteLine($"[CommissionRecovery] Startup auto-marked {affected} orphaned Running automation row(s) as Failed.");
                        }

                        if (cleaned > 0)
                        {
                            Console.WriteLine($"[CommissionRecovery] Startup cleaned {cleaned} superseded/duplicate automation dashboard row(s) as Skipped.");
                        }

                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CommissionRecovery] Startup recovery failed: {ex.Message}");
                        break;
                    }
                }

                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(scanSeconds));

                    try
                    {
                        using var connection = new MySqlConnection(connectionString);
                        await connection.OpenAsync();

                        int affected = await RecoverRunningRowsCoreAsync(
                            connection,
                            forceAllRunningRows: false,
                            staleSeconds,
                            hardTimeoutSeconds,
                            cancellationToken: CancellationToken.None);

                        int cleaned = await CleanupSupersededDashboardRowsAsync(
                            connection,
                            cancellationToken: CancellationToken.None);

                        if (affected > 0)
                        {
                            Console.WriteLine($"[CommissionRecovery] Periodic auto-marked {affected} stale/over-time Running automation row(s) as Failed.");
                        }

                        if (cleaned > 0)
                        {
                            Console.WriteLine($"[CommissionRecovery] Periodic cleaned {cleaned} superseded/duplicate automation dashboard row(s) as Skipped.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CommissionRecovery] Periodic recovery failed: {ex.Message}");
                    }
                }
            });
        }
    }
}
