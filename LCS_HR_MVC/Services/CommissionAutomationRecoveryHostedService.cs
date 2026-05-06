using System.Data;
using Dapper;
using LCS_HR_MVC.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// Self-heals orphaned commission automation rows.
    ///
    /// If the local app is stopped/rebuilt/crashes while a commission step is Running,
    /// the DB row remains Running because the worker process is gone. This service
    /// marks orphaned/stale Running rows as Failed automatically so Start/Resume
    /// Automation can retry from the incomplete step without manual SQL.
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

                int affected = await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            Reason = reason,
                            StaleSeconds = staleSeconds
                        },
                        cancellationToken: cancellationToken));

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
    }
}
