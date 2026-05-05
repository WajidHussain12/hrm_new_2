using System;
using System.Collections.Generic;
using System.Linq;
using LCS_HR_MVC.Models.Payroll;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        private static AuditDiffSummary BuildAuditDiffSummary<T>(
            IReadOnlyCollection<T> historicalRows,
            IReadOnlyCollection<T> generatedRows,
            Func<T, string> keySelector,
            Func<T, string> valueSelector,
            Func<T, string> sampleSelector,
            int sampleLimit = 10)
        {
            var historicalMap = historicalRows.ToDictionary(keySelector, static row => row, StringComparer.OrdinalIgnoreCase);
            var generatedMap = generatedRows.ToDictionary(keySelector, static row => row, StringComparer.OrdinalIgnoreCase);

            var summary = new AuditDiffSummary();

            foreach (string key in historicalMap.Keys.Except(generatedMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
            {
                summary.HistoricalOnlyCount++;
                if (summary.HistoricalOnlySamples.Count < sampleLimit)
                {
                    summary.HistoricalOnlySamples.Add(sampleSelector(historicalMap[key]));
                }
            }

            foreach (string key in generatedMap.Keys.Except(historicalMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
            {
                summary.GeneratedOnlyCount++;
                if (summary.GeneratedOnlySamples.Count < sampleLimit)
                {
                    summary.GeneratedOnlySamples.Add(sampleSelector(generatedMap[key]));
                }
            }

            foreach (string key in historicalMap.Keys.Intersect(generatedMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
            {
                string historicalValue = valueSelector(historicalMap[key]);
                string generatedValue = valueSelector(generatedMap[key]);
                if (string.Equals(historicalValue, generatedValue, StringComparison.Ordinal))
                {
                    continue;
                }

                summary.ValueMismatchCount++;
                if (summary.ValueMismatchSamples.Count < sampleLimit)
                {
                    summary.ValueMismatchSamples.Add($"Key={key} | H={sampleSelector(historicalMap[key])} | G={sampleSelector(generatedMap[key])}");
                }
            }

            return summary;
        }

        private static string NullSafe(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
