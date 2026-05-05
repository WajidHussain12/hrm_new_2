namespace LCS_HR_MVC.Models
{
    /// <summary>
    /// A single structured log line broadcast from the automation service to connected browsers.
    /// Replaces the anonymous-object payload that was sent via SendAsync("logEntry", ...).
    /// </summary>
    public class CommissionLogEntry
    {
        /// <summary>Timestamp of the event, formatted "HH:mm:ss".</summary>
        public string Ts { get; set; } = string.Empty;

        /// <summary>Severity level: INFO / SUCCESS / WARN / ERROR.</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Commission type being processed (e.g. "CashCommission"), or empty for job-level messages.</summary>
        public string Comm { get; set; } = string.Empty;

        /// <summary>City display string (e.g. "Karachi (001)"), or empty for job-level messages.</summary>
        public string City { get; set; } = string.Empty;

        /// <summary>Human-readable message describing the event.</summary>
        public string Msg { get; set; } = string.Empty;
    }
}
