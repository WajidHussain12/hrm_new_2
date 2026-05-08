using System;
using System.Threading.Tasks;
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
                "{TimeoutMessage} Closing DB connection to abort any in-flight command/transaction.",
                timeoutMessage);

            ObserveTimedOutCommissionProcessTask(processTask, cityCode, year, month);

            try
            {
                if (connection.State != System.Data.ConnectionState.Closed)
                {
                    connection.Close();
                }

                MySqlConnection.ClearPool(connection);
            }
            catch (Exception closeEx)
            {
                _logger?.LogWarning(
                    closeEx,
                    "CommissionProcess watchdog failed to close/clear connection for City={CityCode}, Period={Year}/{Month}.",
                    cityCode,
                    year,
                    month);
            }

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

            throw new TimeoutException(timeoutMessage);
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
