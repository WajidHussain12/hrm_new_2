using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Data
{
    /// <summary>
    /// Creates any tables that are required by the application but may not yet exist in the database.
    /// Called once at application startup.
    /// </summary>
    public static class DbInitializer
    {
        public static async Task EnsureTablesExistAsync(string connectionString)
        {
            // Override command timeout to 15 s — prevents startup from hanging
            // if MySQL has a metadata lock (ALTER TABLE can wait indefinitely otherwise).
            var csb = new MySqlConnectionStringBuilder(connectionString)
            {
                DefaultCommandTimeout = 15
            };

            using var connection = new MySqlConnection(csb.ConnectionString);
            await connection.OpenAsync();

            // Release any metadata locks before DDL — prevents infinite waits.
            await ExecuteAsync(connection, "SET SESSION lock_wait_timeout = 10");
            await ExecuteAsync(connection, "SET SESSION innodb_lock_wait_timeout = 10");

            // TakenLeaves — day-by-day leave records per employee
            await ExecuteAsync(connection, @"
                CREATE TABLE IF NOT EXISTS TakenLeaves (
                    Id         INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Year       INT          NOT NULL,
                    Emp_no     VARCHAR(50)  NOT NULL,
                    LeaveDate  DATE         NOT NULL,
                    RequestDate DATETIME    NULL,
                    IsApproved TINYINT      NOT NULL DEFAULT 1,
                    IsDeducted TINYINT      NOT NULL DEFAULT 1,
                    IsTaken    TINYINT      NOT NULL DEFAULT 1,
                    Comments   VARCHAR(500) NULL,
                    CreatedBy  VARCHAR(50)  NULL,
                    UpdatedBy  VARCHAR(50)  NULL,
                    UpdatedDate DATETIME    NULL,
                    INDEX idx_taken_emp_year (Emp_no, Year),
                    INDEX idx_taken_date (LeaveDate)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // TotalLeaves — annual leave quota & balance per employee
            await ExecuteAsync(connection, @"
                CREATE TABLE IF NOT EXISTS TotalLeaves (
                    Id              INT         NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Year            INT         NOT NULL,
                    Emp_No          VARCHAR(50) NOT NULL,
                    RemainingLeaves INT         NOT NULL DEFAULT 0,
                    DeductedLeaves  INT         NOT NULL DEFAULT 0,
                    IsActive        TINYINT     NOT NULL DEFAULT 0,
                    UNIQUE KEY uq_total_emp_year (Emp_No, Year)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // Allowance/Deduction code type lookup (e.g. Allowance, Deduction)
            await ExecuteAsync(connection, @"
                CREATE TABLE IF NOT EXISTS hrms_allownce_code_type (
                    ID       INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Code     VARCHAR(100) NOT NULL,
                    IDeleted BIT          NOT NULL DEFAULT 0
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // Seed default code types if table is empty
            await ExecuteAsync(connection, @"
                INSERT IGNORE INTO hrms_allownce_code_type (ID, Code, IDeleted)
                VALUES (1, 'Allowance', 0), (2, 'Deduction', 0);
            ");

            // Allowance/Deduction code header
            await ExecuteAsync(connection, @"
                CREATE TABLE IF NOT EXISTS hrms_allownces_deduction_code (
                    ID           INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Code_ID      INT          NOT NULL,
                    Code_Type    VARCHAR(100) NULL,
                    Description  VARCHAR(255) NOT NULL,
                    Created_By   VARCHAR(50)  NULL,
                    Created_Date DATETIME     NULL,
                    Updated_By   VARCHAR(50)  NULL,
                    Updated_Date DATETIME     NULL,
                    INDEX idx_adc_code_id (Code_ID)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // Allowance/Deduction detail lines (note legacy typo: dedcution)
            await ExecuteAsync(connection, @"
                CREATE TABLE IF NOT EXISTS hrms_allownces_dedcution_detail (
                    ID              INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    Type_ID         INT          NOT NULL,
                    AD_Code         VARCHAR(50)  NOT NULL,
                    FullName        VARCHAR(255) NOT NULL,
                    Tax_Flag        TINYINT      NOT NULL DEFAULT 0,
                    PaymentMode     VARCHAR(50)  NULL,
                    Over_Time_Flag  TINYINT      NOT NULL DEFAULT 0,
                    Exclude_Absent  TINYINT      NOT NULL DEFAULT 0,
                    PaySlip_Visiable TINYINT     NOT NULL DEFAULT 1,
                    RateID          VARCHAR(50)  NULL,
                    Comments        VARCHAR(500) NULL,
                    Created_By      VARCHAR(50)  NULL,
                    Created_Date    DATETIME     NULL,
                    Is_Active       TINYINT      NOT NULL DEFAULT 1,
                    Updated_By      VARCHAR(50)  NULL,
                    Updated_Date    DATETIME     NULL,
                    INDEX idx_add_type (Type_ID)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // Final Commission Process lock — prevents re-running finalization for same city/month/year
            // Mirrors is_final_commission_process used by old FinalComissionProcess.aspx.cs
            await ExecuteAsync(connection, @"
                CREATE TABLE IF NOT EXISTS is_final_commission_process (
                    id         INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    city       VARCHAR(20)  NOT NULL,
                    month      TINYINT      NOT NULL,
                    year       SMALLINT     NOT NULL,
                    userId     VARCHAR(50)  NOT NULL,
                    created_at DATETIME     NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_fcp_period (city, month, year)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // Old project (Lcs_HR Web Forms) created is_final_commission_process with column name
            // 'CityID' instead of 'city'. If that table pre-exists, rename the column so new queries work.
            using (var checkCmd = new MySqlCommand(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE()
                    AND TABLE_NAME   = 'is_final_commission_process'
                    AND COLUMN_NAME  = 'CityID'", connection))
            {
                var hasCityId = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (hasCityId > 0)
                    await ExecuteAsync(connection,
                        "ALTER TABLE is_final_commission_process CHANGE COLUMN `CityID` `city` VARCHAR(20) NOT NULL");
            }

            // Commission Automation Log — tracks each commission step per city per run
            await ExecuteAsync(connection, @"
                CREATE TABLE IF NOT EXISTS hr_commission_automation_log (
                    id               INT AUTO_INCREMENT PRIMARY KEY,
                    job_run_id       VARCHAR(50)  NOT NULL,
                    year             SMALLINT     NOT NULL,
                    month            TINYINT      NOT NULL,
                    commission_type  VARCHAR(50)  NOT NULL,
                    city_code        VARCHAR(20)  NOT NULL,
                    city_name        VARCHAR(100) NOT NULL,
                    status           VARCHAR(20)  NOT NULL DEFAULT 'Pending',
                    progress_pct     TINYINT      NOT NULL DEFAULT 0,
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

            // Add processed_count column if it was not part of the original schema
            // (MySQL does not support ADD COLUMN IF NOT EXISTS — check INFORMATION_SCHEMA instead)
            using (var checkCmd = new MySqlCommand(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE()
                    AND TABLE_NAME   = 'hr_commission_automation_log'
                    AND COLUMN_NAME  = 'processed_count'", connection))
            {
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (exists == 0)
                    await ExecuteAsync(connection,
                        "ALTER TABLE hr_commission_automation_log ADD COLUMN processed_count INT NOT NULL DEFAULT 0");
            }

            using (var checkCmd = new MySqlCommand(
                @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE()
                    AND TABLE_NAME   = 'hr_commission_automation_log'
                    AND COLUMN_NAME  = 'total_count'", connection))
            {
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (exists == 0)
                    await ExecuteAsync(connection,
                        "ALTER TABLE hr_commission_automation_log ADD COLUMN total_count INT NOT NULL DEFAULT 0");
            }
        }

        private static async Task ExecuteAsync(MySqlConnection connection, string sql)
        {
            using var cmd = new MySqlCommand(sql, connection) { CommandTimeout = 15 };
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
