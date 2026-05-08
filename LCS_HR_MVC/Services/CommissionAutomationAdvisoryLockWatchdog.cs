using System.Runtime.CompilerServices;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public static class CommissionAutomationAdvisoryLockWatchdog
    {
        private const int DefaultScanSeconds = 45;
        private const int DefaultRunningStepRecoverySeconds = 90;
        private const int DefaultHardStepTimeoutSeconds = 12 * 60;
        private const int DefaultRecentHours = 48;

        [ModuleInitializer]
        public static void StartAdvisoryLockWatchdog()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(25));

                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                    .AddJsonFile(
                        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                        optional: true,
                        reloadOnChange: false)
                    .AddEnvironmentVariables()
                    .Build();

                bool enabled = configuration.GetValue(
                    "CommissionSettings:AdvisoryLockWatchdogEnabled",
                    true);

                if (!enabled)
                {
                    Console.WriteLine("[CommissionAdvisoryLockWatchdog] Disabled by configuration.");
                    return;
                }

                string? connectionString = configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Console.WriteLine("[CommissionAdvisoryLockWatchdog] DefaultConnection is empty. Watchdog not started.");
                    return;
                }

                int scanSeconds = configuration.GetValue(
                    "CommissionSettings:AdvisoryLockWatchdogScanSeconds",
                    DefaultScanSeconds);

                int staleSeconds = configuration.GetValue(
                    "CommissionSettings:RunningStepRecoverySeconds",
                    DefaultRunningStepRecoverySeconds);

                int hardTimeoutSeconds = configuration.GetValue(
                    "CommissionSettings:HardStepTimeoutSeconds",
                    DefaultHardStepTimeoutSeconds);

                int recentHours = configuration.GetValue(
                    "CommissionSettings:AdvisoryLockWatchdogRecentHours",
                    DefaultRecentHours);

                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(scanSeconds));

                    try
                    {
                        await RecoverStaleAdvisoryLocksAsync(
                            connectionString,
                            staleSeconds,
                            hardTimeoutSeconds,
                            recentHours);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CommissionAdvisoryLockWatchdog] Scan failed: {ex.Message}");
                    }
                }
            });
        }

        private static async Task RecoverStaleAdvisoryLocksAsync(
            string connectionString,
            int staleSeconds,
            int hardTimeoutSeconds,
            int recentHours)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            List<AutomationPeriod> periods = (await connection.QueryAsync<AutomationPeriod>(
                @"SELECT DISTINCT year AS Year, month AS Month
                  FROM lcs_hr.hr_commission_automation_log
                  WHERE COALESCE(updated_at, created_at) >= DATE_SUB(NOW(), INTERVAL @RecentHours HOUR)
                    AND status IN ('Running', 'Pending', 'Failed')
                  ORDER BY year DESC, month DESC;",
                new { RecentHours = recentHours }))
                .ToList();

            foreach (AutomationPeriod period in periods)
            {
                string lockName = $"commission_automation_{period.Year}_{period.Month:D2}";
                long? holderConnectionId = await connection.ExecuteScalarAsync<long?>(
                    "SELECT IS_USED_LOCK(@LockName);",
                    new { LockName = lockName });

                if (!holderConnectionId.HasValue || holderConnectionId.Value <= 0)
                {
                    continue;
                }

                LockHealth health = await connection.QuerySingleAsync<LockHealth>(
                    @"SELECT COUNT(*) AS RunningCount,
                             SUM(CASE
                                   WHEN updated_at IS NOT NULL
                                    AND started_at IS NOT NULL
                                    AND TIMESTAMPDIFF(SECOND, updated_at, NOW()) < @StaleSeconds
                                    AND TIMESTAMPDIFF(SECOND, started_at, NOW()) < @HardTimeoutSeconds
                                   THEN 1 ELSE 0 END) AS HealthyRunningCount,
                             MAX(TIMESTAMPDIFF(SECOND, updated_at, NOW())) AS MaxSecondsSinceHeartbeat,
                             MAX(TIMESTAMPDIFF(SECOND, started_at, NOW())) AS MaxSecondsSinceStart
                      FROM lcs_hr.hr_commission_automation_log
                      WHERE year = @Year
                        AND month = @Month
                        AND status = 'Running';",
                    new
                    {
                        period.Year,
                        period.Month,
                        StaleSeconds = staleSeconds,
                        HardTimeoutSeconds = hardTimeoutSeconds
                    });

                if (health.HealthyRunningCount > 0)
                {
                    continue;
                }

                string reason =
                    $"Auto recovery: advisory lock {lockName} was held by stale connection {holderConnectionId.Value}, " +
                    $"but no healthy Running automation row existed. Marked Failed and released lock so automation can resume.";

                int markedFailed = await connection.ExecuteAsync(
                    @"UPDATE lcs_hr.hr_commission_automation_log
                      SET status = 'Failed',
                          progress_pct = 0,
                          error_message = @Reason,
                          completed_at = NOW(),
                          updated_at = NOW()
                      WHERE year = @Year
                        AND month = @Month
                        AND status = 'Running';",
                    new
                    {
                        period.Year,
                        period.Month,
                        Reason = reason
                    });

                try
                {
                    Console.WriteLine(
                        $"[CommissionAdvisoryLockWatchdog] Releasing stale advisory lock {lockName}. HolderConnectionId={holderConnectionId.Value}, RunningRows={health.RunningCount}, MarkedFailed={markedFailed}, MaxHeartbeat={health.MaxSecondsSinceHeartbeat}, MaxStart={health.MaxSecondsSinceStart}");

                    await connection.ExecuteAsync($"KILL CONNECTION {holderConnectionId.Value};");
                }
                catch (Exception killEx)
                {
                    Console.WriteLine(
                        $"[CommissionAdvisoryLockWatchdog] Failed to kill stale advisory lock holder {holderConnectionId.Value} for {lockName}: {killEx.Message}");
                }
            }
        }

        private sealed class AutomationPeriod
        {
            public int Year { get; set; }
            public int Month { get; set; }
        }

        private sealed class LockHealth
        {
            public int RunningCount { get; set; }
            public int HealthyRunningCount { get; set; }
            public int? MaxSecondsSinceHeartbeat { get; set; }
            public int? MaxSecondsSinceStart { get; set; }
        }
    }
}
