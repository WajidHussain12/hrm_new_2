using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// Writes and reads hr_commission_execution_history.
    /// One row per execution event from any source (Automation or Manual).
    /// The table is created on first use — no migration script required.
    /// </summary>
    public class CommissionExecutionHistoryService : ICommissionExecutionHistoryService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly ILogger<CommissionExecutionHistoryService> _logger;

        // Process-lifetime flag so we only run the CREATE TABLE check once per app restart.
        private static volatile bool _tableEnsured = false;
        private static readonly SemaphoreSlim _schemaSemaphore = new(1, 1);

        private const string TableName = "hr_commission_execution_history";

        public CommissionExecutionHistoryService(
            IDbConnectionFactory connectionFactory,
            ILogger<CommissionExecutionHistoryService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
        }

        // ── Schema bootstrap ──────────────────────────────────────────────────────
        private async Task EnsureTableAsync(MySqlConnection conn)
        {
            if (_tableEnsured) return;

            await _schemaSemaphore.WaitAsync();
            try
            {
                if (_tableEnsured) return;

                await conn.ExecuteAsync($@"
                    CREATE TABLE IF NOT EXISTS {TableName} (
                        id                    INT          AUTO_INCREMENT PRIMARY KEY,
                        execution_source      VARCHAR(20)  NOT NULL DEFAULT 'Manual',
                        job_run_id            VARCHAR(30)  NULL,
                        year                  INT          NOT NULL,
                        month                 INT          NOT NULL,
                        city_code             VARCHAR(20)  NOT NULL DEFAULT '',
                        city_name             VARCHAR(100) NOT NULL DEFAULT '',
                        commission_type       VARCHAR(50)  NOT NULL DEFAULT '',
                        triggered_by          VARCHAR(100) NULL,
                        triggered_by_user_id  VARCHAR(20)  NULL,
                        status                VARCHAR(30)  NOT NULL DEFAULT 'Completed',
                        rows_processed        INT          NOT NULL DEFAULT 0,
                        started_at            DATETIME     NULL,
                        completed_at          DATETIME     NULL,
                        duration_ms           INT          NULL,
                        error_message         TEXT         NULL,
                        created_at            DATETIME     NOT NULL DEFAULT NOW(),
                        INDEX idx_ym          (year, month),
                        INDEX idx_city_type   (city_code, commission_type),
                        INDEX idx_source      (execution_source),
                        INDEX idx_created     (created_at)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");

                _tableEnsured = true;
                _logger.LogInformation("{Table} created/verified.", TableName);
            }
            finally
            {
                _schemaSemaphore.Release();
            }
        }

        // ── Write ─────────────────────────────────────────────────────────────────
        public async Task RecordAsync(CommissionExecutionRecord record)
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot open DB connection.");
                await conn.OpenAsync();
                await EnsureTableAsync(conn);

                await conn.ExecuteAsync($@"
                    INSERT INTO {TableName}
                        (execution_source, job_run_id, year, month, city_code, city_name, commission_type,
                         triggered_by, triggered_by_user_id, status, rows_processed,
                         started_at, completed_at, duration_ms, error_message)
                    VALUES
                        (@ExecutionSource, @JobRunId, @Year, @Month, @CityCode, @CityName, @CommissionType,
                         @TriggeredBy, @TriggeredByUserId, @Status, @RowsProcessed,
                         @StartedAt, @CompletedAt, @DurationMs, @ErrorMessage)",
                    record);
            }
            catch (Exception ex)
            {
                // Recording failures must NEVER interrupt commission processing.
                _logger.LogWarning(ex,
                    "Failed to record execution history for {CommType}/{City} {Year}/{Month}. Execution continues.",
                    record.CommissionType, record.CityCode, record.Year, record.Month);
            }
        }

        // ── Read ──────────────────────────────────────────────────────────────────
        public async Task<List<CommissionExecutionRecord>> GetByMonthAsync(int year, int month)
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new InvalidOperationException("Cannot open DB connection.");
                await conn.OpenAsync();
                await EnsureTableAsync(conn);

                var rows = await conn.QueryAsync<CommissionExecutionRecord>($@"
                    SELECT
                        id             AS Id,
                        execution_source      AS ExecutionSource,
                        job_run_id            AS JobRunId,
                        year                  AS Year,
                        month                 AS Month,
                        city_code             AS CityCode,
                        city_name             AS CityName,
                        commission_type       AS CommissionType,
                        triggered_by          AS TriggeredBy,
                        triggered_by_user_id  AS TriggeredByUserId,
                        status                AS Status,
                        rows_processed        AS RowsProcessed,
                        started_at            AS StartedAt,
                        completed_at          AS CompletedAt,
                        duration_ms           AS DurationMs,
                        error_message         AS ErrorMessage,
                        created_at            AS CreatedAt
                    FROM {TableName}
                    WHERE year = @Year AND month = @Month
                    ORDER BY id DESC",
                    new { Year = year, Month = month });

                return rows.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read execution history for {Year}/{Month}.", year, month);
                return new List<CommissionExecutionRecord>();
            }
        }
    }
}
