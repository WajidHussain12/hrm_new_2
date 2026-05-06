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
    /// marks orphaned/stale Running rows as Failed automatically so Start/Resume
    /// Automation can retry from the incomplete step without manual SQL.
    ///
    /// The hosted-service path is preferred when registered in DI. The module
    /// initializer below is an additional safety net for the current local project:
    /// it runs once when the app assembly loads and performs startup recovery even
    /// if Program.cs registration is missed.
    /// </summary>
    public sealed class CommissionAutomationRecoveryHostedService : BackgroundService
    {
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
                60);

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
                    90);

                int affected = await RecoverRunningRowsCoreAsync(
                    connection,
                    forceAllRunningRows,
                    staleSeconds,
                    cancellationToken);

                if (affected > 0)
                {
                    _logger.LogWarning(
                        "[CommissionRecovery] Auto-marked {AffectedRows} orphaned/stale Running automation row(s) as Failed. ForceAll={ForceAllRunningRows}, StaleSeconds={StaleSeconds}",
                        affected,
                        forceAllRunningRows,
                        staleSeconds);
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
            CancellationToken cancellationToken)
        {
            string reason = forceAllRunningRows
                ? "Auto recovery: application started while commission automation had orphaned Running row(s). Marked Failed so automation can resume/retry automatically."
                : $"Auto recovery: commission automation Running row had no heartbeat for {staleSeconds}+ seconds. Marked Failed so automation can resume/retry automatically.";

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
  AND TIMESTAMPDIFF(SECOND, updated_at, NOW()) >= @StaleSeconds;";

            return await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        Reason = reason,
                        StaleSeconds = staleSeconds
                    },
                    cancellationToken: cancellationToken));
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

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                int affected = await RecoverRunningRowsCoreAsync(
                    connection,
                    forceAllRunningRows: true,
                    staleSeconds: configuration.GetValue("CommissionSettings:RunningStepRecoverySeconds", 90),
                    cancellationToken: CancellationToken.None);

                if (affected > 0)
                {
                    Console.WriteLine($"[CommissionRecovery] Startup auto-marked {affected} orphaned Running automation row(s) as Failed.");
                }
            });
        }
    }
}
