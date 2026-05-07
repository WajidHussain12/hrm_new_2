using System.Runtime.CompilerServices;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public static class CommissionAutomationExternalQueryWatchdog
    {
        private const int DefaultScanSeconds = 30;
        private const int DefaultCashFinanceQueryTimeoutSeconds = 360;
        private const int DefaultHardStepTimeoutSeconds = 720;

        [ModuleInitializer]
        public static void StartExternalQueryWatchdog()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(15));

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
                    "CommissionSettings:ExternalQueryWatchdogEnabled",
                    true);

                if (!enabled)
                {
                    Console.WriteLine("[CommissionExternalWatchdog] Disabled by configuration.");
                    return;
                }

                string? connectionString = configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Console.WriteLine("[CommissionExternalWatchdog] DefaultConnection is empty. Watchdog not started.");
                    return;
                }

                int scanSeconds = configuration.GetValue(
                    "CommissionSettings:ExternalQueryWatchdogScanSeconds",
                    DefaultScanSeconds);

                int cashFinanceTimeoutSeconds = configuration.GetValue(
                    "CommissionSettings:CashFinanceQueryTimeoutSeconds",
                    DefaultCashFinanceQueryTimeoutSeconds);

                int hardStepTimeoutSeconds = configuration.GetValue(
                    "CommissionSettings:HardStepTimeoutSeconds",
                    DefaultHardStepTimeoutSeconds);

                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(scanSeconds));

                    try
                    {
                        await KillStuckExternalQueriesAsync(
                            connectionString,
                            cashFinanceTimeoutSeconds,
                            hardStepTimeoutSeconds);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[CommissionExternalWatchdog] Scan failed: {ex.Message}");
                    }
                }
            });
        }

        private static async Task KillStuckExternalQueriesAsync(
            string connectionString,
            int cashFinanceTimeoutSeconds,
            int hardStepTimeoutSeconds)
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            List<RunningCommissionStep> runningSteps = (await connection.QueryAsync<RunningCommissionStep>(
                @"SELECT id AS Id,
                         job_run_id AS JobRunId,
                         city_code AS CityCode,
                         city_name AS CityName,
                         commission_type AS CommissionType,
                         TIMESTAMPDIFF(SECOND, started_at, NOW()) AS SecondsSinceStart,
                         TIMESTAMPDIFF(SECOND, updated_at, NOW()) AS SecondsSinceHeartbeat
                  FROM lcs_hr.hr_commission_automation_log
                  WHERE status = 'Running'
                    AND started_at IS NOT NULL
                    AND commission_type = 'CashCommission'
                    AND TIMESTAMPDIFF(SECOND, started_at, NOW()) >= @MinSeconds;",
                new { MinSeconds = Math.Min(cashFinanceTimeoutSeconds, hardStepTimeoutSeconds) }))
                .ToList();

            if (!runningSteps.Any())
            {
                return;
            }

            List<ProcessListQuery> killCandidates = (await connection.QueryAsync<ProcessListQuery>(
                @"SELECT ID AS Id,
                         USER AS UserName,
                         HOST AS Host,
                         DB AS DbName,
                         TIME AS SecondsRunning,
                         STATE AS State,
                         LEFT(INFO, 2000) AS Info
                  FROM information_schema.processlist
                  WHERE COMMAND = 'Query'
                    AND DB IN ('lcs_finance')
                    AND TIME >= @CashFinanceTimeoutSeconds
                    AND INFO IS NOT NULL
                    AND INFO NOT LIKE '%information_schema.processlist%'
                    AND (
                         INFO LIKE '%finance_vouchersubsidary%'
                      OR INFO LIKE '%finance_vouchersubsidiarydetails%'
                      OR INFO LIKE '%RecoveryOfficerId%'
                      OR INFO LIKE '%InvoiceMonth%'
                    )
                  ORDER BY TIME DESC;",
                new { CashFinanceTimeoutSeconds = cashFinanceTimeoutSeconds }))
                .ToList();

            if (!killCandidates.Any())
            {
                return;
            }

            foreach (ProcessListQuery query in killCandidates)
            {
                try
                {
                    Console.WriteLine(
                        $"[CommissionExternalWatchdog] Killing stuck lcs_finance query Id={query.Id}, Seconds={query.SecondsRunning}. Active automation step(s): {string.Join("; ", runningSteps.Select(DescribeStep))}");

                    await connection.ExecuteAsync($"KILL QUERY {query.Id};");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommissionExternalWatchdog] Failed to kill query Id={query.Id}: {ex.Message}");
                }
            }
        }

        private static string DescribeStep(RunningCommissionStep step)
        {
            return $"{step.JobRunId}/{step.CityCode}-{step.CityName}/{step.CommissionType}/start={step.SecondsSinceStart}s/heartbeat={step.SecondsSinceHeartbeat}s";
        }

        private sealed class RunningCommissionStep
        {
            public int Id { get; set; }
            public string JobRunId { get; set; } = string.Empty;
            public string CityCode { get; set; } = string.Empty;
            public string CityName { get; set; } = string.Empty;
            public string CommissionType { get; set; } = string.Empty;
            public int SecondsSinceStart { get; set; }
            public int SecondsSinceHeartbeat { get; set; }
        }

        private sealed class ProcessListQuery
        {
            public long Id { get; set; }
            public string UserName { get; set; } = string.Empty;
            public string Host { get; set; } = string.Empty;
            public string DbName { get; set; } = string.Empty;
            public int SecondsRunning { get; set; }
            public string? State { get; set; }
            public string? Info { get; set; }
        }
    }
}
