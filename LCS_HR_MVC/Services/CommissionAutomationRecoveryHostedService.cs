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
    /// <summary>
    /// Self-heals orphaned commission automation rows.
    ///
    /// If the local app is stopped/rebuilt/crashes while a commission step is Running,
    /// the DB row remains Running because the worker process is gone. This service
    /// marks orphaned/stale/over-time Running rows as Failed automatically so
    /// Start/Resume Automation can retry from the incomplete step without manual SQL.
    ///
    /// It also cleans dashboard-confusing duplicate rows:
    /// - Pending/Running rows superseded by a later Completed/AlreadyProcessed row
    /// - Failed rows created only because a duplicate job was blocked by the in-process gate
    ///
    /// This keeps the Automation dashboard from showing old duplicate entries as if they
    /// are still active, while preserving the historical row as Skipped with explanation.
    /// </summary>
    public sealed class CommissionAutomationRecoveryHostedService : BackgroundService
    {
        private const int DefaultHeartbeatStaleSeconds = 90;
        private const int DefaultHardStepTimeoutSeconds = 12 * 60;
        private const int DefaultScanSeconds = 60;

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
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
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
