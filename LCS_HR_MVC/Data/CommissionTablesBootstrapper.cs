using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Data
{
    /// <summary>
    /// Ensures all commission and salary config tables exist and contain their baseline seed rows.
    /// Runs exactly once per process lifetime (singleton guard via SemaphoreSlim + volatile flag).
    /// Every method uses INSERT IGNORE — existing HR-modified values are NEVER overwritten.
    /// Every private method has its own try/catch so one failure never blocks the others.
    /// </summary>
    public static class CommissionTablesBootstrapper
    {
        private static volatile bool _completed = false;
        private static readonly SemaphoreSlim _gate = new(1, 1);

        public static async Task EnsureAllTablesAsync(string connectionString, ILogger logger)
        {
            // Fast path — already ran this process lifetime
            if (_completed) return;

            await _gate.WaitAsync();
            try
            {
                if (_completed) return;   // double-checked locking

                logger.LogInformation("[Bootstrap] Starting commission/salary table bootstrap.");

                var csb = new MySqlConnectionStringBuilder(connectionString)
                {
                    DefaultCommandTimeout = 30
                };
                var cs = csb.ConnectionString;

                await EnsureVasExclusionsAsync(cs, logger);
                await EnsureCodThresholdCitiesAsync(cs, logger);
                await EnsureSalaryTaxExclusionsAsync(cs, logger);
                await EnsureSalaryConfigAsync(cs, logger);
                await EnsureCommissionConfigKeysAsync(cs, logger);
                await EnsureExecutionHistoryTableAsync(cs, logger);
                await EnsureAutomationLogSchemaAsync(cs, logger);

                _completed = true;
                logger.LogInformation("[Bootstrap] Commission/salary table bootstrap complete.");
            }
            finally
            {
                _gate.Release();
            }
        }

        // ── 1. hr_commission_vas_exclusions ───────────────────────────────────

        private static async Task EnsureVasExclusionsAsync(string cs, ILogger logger)
        {
            try
            {
                using var conn = new MySqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteAsync(conn, @"
                    CREATE TABLE IF NOT EXISTS lcs_hr.hr_commission_vas_exclusions (
                        VasId    INT          NOT NULL,
                        Reason   VARCHAR(255) NULL,
                        IsActive TINYINT      NOT NULL DEFAULT 1,
                        PRIMARY KEY (VasId)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");

                // Seed — live data as of 2026-04-12
                // INSERT IGNORE: existing rows are untouched; only truly missing rows are added.
                await ExecuteAsync(conn, @"
                    INSERT IGNORE INTO lcs_hr.hr_commission_vas_exclusions (VasId, Reason, IsActive) VALUES
                        (5,  'System/internal VAS excluded from cash commission', 1),
                        (6,  'System/internal VAS excluded from cash commission', 1),
                        (12, 'System/internal VAS excluded from cash commission', 1),
                        (13, 'System/internal VAS excluded from cash commission', 1),
                        (15, 'System/internal VAS excluded from cash commission', 1),
                        (16, 'System/internal VAS excluded from cash commission', 1),
                        (31, 'System/internal VAS excluded from cash commission', 1),
                        (32, 'System/internal VAS excluded from cash commission', 1),
                        (48, 'System/internal VAS excluded from cash commission', 1);
                ");

                logger.LogInformation("[Bootstrap] hr_commission_vas_exclusions: OK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Bootstrap] hr_commission_vas_exclusions: FAILED (non-fatal)");
            }
        }

        // ── 2. hr_commission_cod_threshold_cities ─────────────────────────────

        private static async Task EnsureCodThresholdCitiesAsync(string cs, ILogger logger)
        {
            try
            {
                using var conn = new MySqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteAsync(conn, @"
                    CREATE TABLE IF NOT EXISTS lcs_hr.hr_commission_cod_threshold_cities (
                        CityCode     VARCHAR(20)    NOT NULL,
                        ThresholdPct DECIMAL(5,2)   NOT NULL DEFAULT 85.00,
                        Notes        VARCHAR(255)   NULL,
                        IsActive     TINYINT        NOT NULL DEFAULT 1,
                        PRIMARY KEY (CityCode)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");

                // Seed — live data as of 2026-04-12
                await ExecuteAsync(conn, @"
                    INSERT IGNORE INTO lcs_hr.hr_commission_cod_threshold_cities (CityCode, ThresholdPct, Notes, IsActive) VALUES
                        ('001', 85.00, 'Karachi — higher COD delivery threshold',   1),
                        ('002', 85.00, 'Lahore — higher COD delivery threshold',    1),
                        ('003', 85.00, 'Islamabad — higher COD delivery threshold', 1),
                        ('080', 85.00, 'ISB Hub — higher COD delivery threshold',   1);
                ");

                logger.LogInformation("[Bootstrap] hr_commission_cod_threshold_cities: OK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Bootstrap] hr_commission_cod_threshold_cities: FAILED (non-fatal)");
            }
        }

        // ── 3. hr_salary_tax_exclusions ───────────────────────────────────────

        private static async Task EnsureSalaryTaxExclusionsAsync(string cs, ILogger logger)
        {
            try
            {
                using var conn = new MySqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteAsync(conn, @"
                    CREATE TABLE IF NOT EXISTS lcs_hr.hr_salary_tax_exclusions (
                        EmpNo    VARCHAR(50)  NOT NULL,
                        Reason   VARCHAR(255) NULL,
                        IsActive TINYINT      NOT NULL DEFAULT 1,
                        PRIMARY KEY (EmpNo)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");

                // Seed — live data as of 2026-04-12
                await ExecuteAsync(conn, @"
                    INSERT IGNORE INTO lcs_hr.hr_salary_tax_exclusions (EmpNo, Reason, IsActive) VALUES
                        ('00000000013203', 'Special tax arrangement', 1),
                        ('00000000013548', 'Special tax arrangement', 1),
                        ('00000000032787', 'Special tax arrangement', 1),
                        ('00000000032837', 'Special tax arrangement', 1),
                        ('00000000032838', 'Special tax arrangement', 1),
                        ('00000000032857', 'Special tax arrangement', 1),
                        ('00000000032861', 'Special tax arrangement', 1),
                        ('00000000032864', 'Special tax arrangement', 1),
                        ('00000000032865', 'Special tax arrangement', 1);
                ");

                logger.LogInformation("[Bootstrap] hr_salary_tax_exclusions: OK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Bootstrap] hr_salary_tax_exclusions: FAILED (non-fatal)");
            }
        }

        // ── 4. hr_salary_config ───────────────────────────────────────────────

        private static async Task EnsureSalaryConfigAsync(string cs, ILogger logger)
        {
            try
            {
                using var conn = new MySqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteAsync(conn, @"
                    CREATE TABLE IF NOT EXISTS lcs_hr.hr_salary_config (
                        ConfigKey   VARCHAR(100) NOT NULL,
                        ConfigValue VARCHAR(500) NOT NULL,
                        DataType    VARCHAR(20)  NOT NULL DEFAULT 'string',
                        Description VARCHAR(500) NULL,
                        PRIMARY KEY (ConfigKey)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");

                // Seed — live data as of 2026-04-12
                await ExecuteAsync(conn, @"
                    INSERT IGNORE INTO lcs_hr.hr_salary_config (ConfigKey, ConfigValue, DataType, Description) VALUES
                        ('LEAVE_ENCASHMENT_GROSS_THRESHOLD', '50000', 'decimal', 'Gross salary threshold for leave cashability rules'),
                        ('LEAVE_MONTHS_MIN_FOR_CASHOUT',     '6',     'int',     'Minimum service months required for leave encashment'),
                        ('LEAVE_YEAR_START_DAY',             '1',     'int',     'Day when leave year starts'),
                        ('LEAVE_YEAR_START_MONTH',           '7',     'int',     'Month when leave year starts (July=7)'),
                        ('SALARY_DAYS_DIVISOR',              '30',    'int',     'Standard days divisor for per-day salary calculation'),
                        ('TAX_SALARY_GLCODE',                '167',   'int',     'GL code for salary deductions in tax correction');
                ");

                logger.LogInformation("[Bootstrap] hr_salary_config: OK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Bootstrap] hr_salary_config: FAILED (non-fatal)");
            }
        }

        // ── 5. hr_commission_config ───────────────────────────────────────────

        private static async Task EnsureCommissionConfigKeysAsync(string cs, ILogger logger)
        {
            try
            {
                using var conn = new MySqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteAsync(conn, @"
                    CREATE TABLE IF NOT EXISTS lcs_hr.hr_commission_config (
                        ConfigKey   VARCHAR(100) NOT NULL,
                        ConfigValue VARCHAR(500) NOT NULL,
                        DataType    VARCHAR(20)  NOT NULL DEFAULT 'string',
                        Description VARCHAR(500) NULL,
                        PRIMARY KEY (ConfigKey)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");

                // Seed — live data as of 2026-04-12
                await ExecuteAsync(conn, @"
                    INSERT IGNORE INTO lcs_hr.hr_commission_config (ConfigKey, ConfigValue, DataType, Description) VALUES
                        ('ADJUSTMENT_PRESERVE_TYPE_IDS',  '1,2',                                        'csv',     'Adjusment_Type_id values NOT deleted during commission reprocess'),
                        ('AUTOMATION_MAX_RETRIES',         '3',                                          'int',     'Maximum retry attempts per commission type per city'),
                        ('AUTOMATION_USER_ID',             '210',                                        'int',     'System user ID for scheduled/automated commission runs'),
                        ('CEB_SPLIT_DATE',                 '2023-07-01',                                 'date',    'Accounts opened on/before = Existing, after = New Acquisition'),
                        ('COD_BONUS_SLAB1_MAX_CN',         '500',                                        'int',     'COD bonus slab 1: up to this many CNs'),
                        ('COD_BONUS_SLAB1_RATE',           '10',                                         'decimal', 'COD bonus slab 1: Rs per CN'),
                        ('COD_BONUS_SLAB2_MAX_CN',         '750',                                        'int',     'COD bonus slab 2: up to this many CNs'),
                        ('COD_BONUS_SLAB2_RATE',           '15',                                         'decimal', 'COD bonus slab 2: Rs per CN'),
                        ('COD_BONUS_SLAB3_RATE',           '20',                                         'decimal', 'COD bonus slab 3 (>slab2): Rs per CN'),
                        ('COMMISSION_END_DAY',             '20',                                         'int',     'Commission period end day of month'),
                        ('COMMISSION_START_DAY',           '21',                                         'int',     'Commission period start day of month'),
                        ('DUMMY_CLIENT_ID',                '999998',                                     'string',  'Internal/test client_id excluded from OLE credit queries'),
                        ('DUMMY_COURIER_ID',               '00000',                                      'string',  'Dummy courier code excluded from OLE credit queries'),
                        ('MIN_GUARANTEE_AMOUNT',           '1500',                                       'decimal', 'Minimum monthly commission guarantee (Rs)'),
                        ('MIN_GUARANTEE_WORKING_DAYS',     '25',                                         'int',     'Standard working days divisor for pro-rata guarantee'),
                        ('OVERLAND_PROCESS_RATE_IDS',      '1,2,3,4,5,8,11,12,84,85,86,87,88,89,90,91,92,93,94,95', 'csv', 'RateIDs queried from hr_olecommissionprocess in OverLand process'),
                        ('RBI_CAP',                        '100000',                                     'decimal', 'Maximum credit booking commission per employee per month (Rs)'),
                        ('RETURNCOD_POLICY_RATE_IDS',      '100,101,102,103',                            'csv',     'RateIDs from hr_commissionpolicy used in ReturnCOD commission queries');
                ");

                logger.LogInformation("[Bootstrap] hr_commission_config: OK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Bootstrap] hr_commission_config: FAILED (non-fatal)");
            }
        }

        // ── 6. is_final_commission_process (execution history) ────────────────
        // DbInitializer also creates this table; this method is idempotent/redundant
        // by design — CREATE IF NOT EXISTS ensures it is always a no-op when already present.

        private static async Task EnsureExecutionHistoryTableAsync(string cs, ILogger logger)
        {
            try
            {
                using var conn = new MySqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteAsync(conn, @"
                    CREATE TABLE IF NOT EXISTS lcs_hr.is_final_commission_process (
                        id         INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        city       VARCHAR(20)  NOT NULL,
                        month      TINYINT      NOT NULL,
                        year       SMALLINT     NOT NULL,
                        userId     VARCHAR(50)  NOT NULL,
                        created_at DATETIME     NULL DEFAULT CURRENT_TIMESTAMP,
                        INDEX idx_fcp_period (city, month, year)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");

                logger.LogInformation("[Bootstrap] is_final_commission_process: OK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Bootstrap] is_final_commission_process: FAILED (non-fatal)");
            }
        }

        // ── 7. hr_commission_automation_log schema guard ──────────────────────
        // Ensures processed_count and total_count columns exist.
        // DbInitializer.cs also does this via INFORMATION_SCHEMA checks;
        // this method is idempotent backup in case bootstrapper runs before DbInitializer.

        private static async Task EnsureAutomationLogSchemaAsync(string cs, ILogger logger)
        {
            try
            {
                using var conn = new MySqlConnection(cs);
                await conn.OpenAsync();

                await ExecuteAsync(conn, @"
                    CREATE TABLE IF NOT EXISTS lcs_hr.hr_commission_automation_log (
                        id               INT AUTO_INCREMENT PRIMARY KEY,
                        job_run_id       VARCHAR(50)  NOT NULL,
                        year             SMALLINT     NOT NULL,
                        month            TINYINT      NOT NULL,
                        commission_type  VARCHAR(50)  NOT NULL,
                        city_code        VARCHAR(20)  NOT NULL,
                        city_name        VARCHAR(100) NOT NULL,
                        status           VARCHAR(20)  NOT NULL DEFAULT 'Pending',
                        progress_pct     TINYINT      NOT NULL DEFAULT 0,
                        processed_count  INT          NOT NULL DEFAULT 0,
                        total_count      INT          NOT NULL DEFAULT 0,
                        started_at       DATETIME     NULL,
                        completed_at     DATETIME     NULL,
                        error_message    TEXT         NULL,
                        retry_count      TINYINT      NOT NULL DEFAULT 0,
                        created_at       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        updated_at       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                        INDEX idx_run (job_run_id),
                        INDEX idx_period (year, month)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                ");

                // Add processed_count if table pre-existed without it
                using (var checkCmd = new MySqlCommand(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = 'lcs_hr'
                        AND TABLE_NAME   = 'hr_commission_automation_log'
                        AND COLUMN_NAME  = 'processed_count'", conn))
                {
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists == 0)
                        await ExecuteAsync(conn,
                            "ALTER TABLE lcs_hr.hr_commission_automation_log ADD COLUMN processed_count INT NOT NULL DEFAULT 0");
                }

                // Add total_count if table pre-existed without it
                using (var checkCmd = new MySqlCommand(
                    @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                      WHERE TABLE_SCHEMA = 'lcs_hr'
                        AND TABLE_NAME   = 'hr_commission_automation_log'
                        AND COLUMN_NAME  = 'total_count'", conn))
                {
                    var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists == 0)
                        await ExecuteAsync(conn,
                            "ALTER TABLE lcs_hr.hr_commission_automation_log ADD COLUMN total_count INT NOT NULL DEFAULT 0");
                }

                logger.LogInformation("[Bootstrap] hr_commission_automation_log schema: OK");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Bootstrap] hr_commission_automation_log schema: FAILED (non-fatal)");
            }
        }

        // ── Shared helper ─────────────────────────────────────────────────────

        private static async Task ExecuteAsync(MySqlConnection conn, string sql)
        {
            using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 30 };
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
