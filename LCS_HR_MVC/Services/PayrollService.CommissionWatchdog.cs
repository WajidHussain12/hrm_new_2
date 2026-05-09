using System;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        private static readonly TimeSpan CommissionProcessHardTimeout = TimeSpan.FromMinutes(20);

        private async Task<CommissionProcessPreviewResult> ProcessCommissionInternalWithWatchdogAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            bool billingStatusConfirmed = true,
            bool attendanceStatusConfirmed = true,
            bool allCommissionTypesConfirmed = true)
        {
            long? serverThreadId = TryGetServerThreadId(connection);
            string connectionString = connection.ConnectionString;

            Task<CommissionProcessPreviewResult> processTask = ProcessCommissionInternalAsync(
                connection,
                transaction,
                year,
                month,
                cityCode,
                currentUserId,
                billingStatusConfirmed,
                attendanceStatusConfirmed,
                allCommissionTypesConfirmed);

            Task timeoutTask = Task.Delay(CommissionProcessHardTimeout);
            Task completedTask = await Task.WhenAny(processTask, timeoutTask);

            if (ReferenceEquals(completedTask, processTask))
            {
                return await processTask;
            }

            string timeoutMessage =
                $"CommissionProcess timeout after {CommissionProcessHardTimeout.TotalMinutes:N0} minutes. " +
                $"City={cityCode}, Period={year}/{month:D2}. " +
                "The step was aborted safely so automation can mark it Failed and continue/resume instead of staying Running forever.";

            _logger?.LogError(
                "{TimeoutMessage} Aborting the timed-out MySQL session without calling Close() on the broken reader connection.",
                timeoutMessage);

            ObserveTimedOutCommissionProcessTask(processTask, cityCode, year, month);

            await AbortTimedOutCommissionConnectionAsync(
                connectionString,
                serverThreadId,
                cityCode,
                year,
                month);

            TryClearBrokenConnectionPool(connection, cityCode, year, month);
            TryDisposeTimedOutTransaction(transaction, cityCode, year, month);

            throw new TimeoutException(timeoutMessage);
        }

        private static long? TryGetServerThreadId(MySqlConnection connection)
        {
            try
            {
                var property = typeof(MySqlConnection).GetProperty("ServerThread");
                object? value = property?.GetValue(connection);
                return value == null ? null : Convert.ToInt64(value);
            }
            catch
            {
                return null;
            }
        }

        private async Task AbortTimedOutCommissionConnectionAsync(
            string connectionString,
            long? serverThreadId,
            string cityCode,
            int year,
            int month)
        {
            if (!serverThreadId.HasValue || serverThreadId.Value <= 0)
            {
                _logger?.LogWarning(
                    "CommissionProcess watchdog could not determine MySQL ServerThread for City={CityCode}, Period={Year}/{Month}. Timeout will still be reported cleanly.",
                    cityCode,
                    year,
                    month);
                return;
            }

            try
            {
                await using var killerConnection = new MySqlConnection(connectionString);
                await killerConnection.OpenAsync();
                await killerConnection.ExecuteAsync($"KILL CONNECTION {serverThreadId.Value};");

                _logger?.LogWarning(
                    "CommissionProcess watchdog killed timed-out MySQL connection {ServerThreadId} for City={CityCode}, Period={Year}/{Month}.",
                    serverThreadId.Value,
                    cityCode,
                    year,
                    month);
            }
            catch (Exception killEx)
            {
                _logger?.LogWarning(
                    killEx,
                    "CommissionProcess watchdog could not kill timed-out MySQL connection {ServerThreadId} for City={CityCode}, Period={Year}/{Month}. Timeout will still be reported cleanly.",
                    serverThreadId.Value,
                    cityCode,
                    year,
                    month);
            }
        }

        private void TryClearBrokenConnectionPool(
            MySqlConnection connection,
            string cityCode,
            int year,
            int month)
        {
            try
            {
                // Do not call connection.Close() here. MySql.Data can throw NullReferenceException
                // while closing a timed-out connection that still owns a broken/open DataReader.
                MySqlConnection.ClearPool(connection);
            }
            catch (Exception clearEx)
            {
                _logger?.LogWarning(
                    clearEx,
                    "CommissionProcess watchdog failed to clear broken connection pool for City={CityCode}, Period={Year}/{Month}.",
                    cityCode,
                    year,
                    month);
            }
        }

        private void TryDisposeTimedOutTransaction(
            MySqlTransaction transaction,
            string cityCode,
            int year,
            int month)
        {
            try
            {
                transaction.Dispose();
            }
            catch (Exception disposeEx)
            {
                _logger?.LogWarning(
                    disposeEx,
                    "CommissionProcess watchdog transaction dispose failed for City={CityCode}, Period={Year}/{Month}.",
                    cityCode,
                    year,
                    month);
            }
        }

        private void ObserveTimedOutCommissionProcessTask(
            Task<CommissionProcessPreviewResult> processTask,
            string cityCode,
            int year,
            int month)
        {
            _ = processTask.ContinueWith(
                task =>
                {
                    if (task.Exception != null)
                    {
                        _logger?.LogWarning(
                            task.Exception.GetBaseException(),
                            "CommissionProcess timed-out background task ended after connection abort. City={CityCode}, Period={Year}/{Month}.",
                            cityCode,
                            year,
                            month);
                    }
                },
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
