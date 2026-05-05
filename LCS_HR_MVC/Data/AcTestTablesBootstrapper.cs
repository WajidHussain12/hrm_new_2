using Dapper;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Data
{
    public static class AcTestTablesBootstrapper
    {
        private static volatile bool _completed = false;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public static async Task EnsureTestTablesAsync(
            string connectionString,
            ILogger? logger = null)
        {
            // Double-checked locking — runs once per app lifetime
            if (_completed) return;
            await _lock.WaitAsync();
            try
            {
                if (_completed) return;

                // Only run if test mode is active
                if (!AcTestTableNames.IsTestMode)
                {
                    logger?.LogInformation(
                        "[ACTest] Production mode — " +
                        "test table check skipped.");
                    _completed = true;
                    return;
                }

                using var conn = new MySqlConnection(connectionString);
                await conn.OpenAsync();

                int created = 0;
                int existed = 0;
                int failed  = 0;

                foreach (var realTable in AcTestTableNames.AllOutputTables)
                {
                    var testTable = realTable + AcTestTableNames.TestSuffix;
                    try
                    {
                        // STEP A: Check if test table already exists
                        var existCount = await conn.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                            "WHERE TABLE_SCHEMA = DATABASE() " +
                            "AND TABLE_NAME = @t",
                            new { t = testTable });

                        if (existCount > 0)
                        {
                            existed++;
                            logger?.LogDebug(
                                "[ACTest] {T}: already exists — skipped.",
                                testTable);
                            continue;
                        }

                        // STEP B: Check if real table exists
                        var realExists = await conn.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                            "WHERE TABLE_SCHEMA = DATABASE() " +
                            "AND TABLE_NAME = @t",
                            new { t = realTable });

                        if (realExists == 0)
                        {
                            failed++;
                            logger?.LogWarning(
                                "[ACTest] {T}: SKIPPED — real table {R} " +
                                "does not exist.",
                                testTable, realTable);
                            continue;
                        }

                        // STEP C: Create test table using LIKE
                        // This guarantees exact DDL match with real table.
                        // If real table DDL changes → recreate test tables
                        // via POST /ac-test/recreate
                        await conn.ExecuteAsync(
                            $"CREATE TABLE IF NOT EXISTS " +
                            $"`lcs_hr`.`{testTable}` " +
                            $"LIKE `lcs_hr`.`{realTable}`");

                        // STEP D: Verify creation
                        var verified = await conn.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES " +
                            "WHERE TABLE_SCHEMA = DATABASE() " +
                            "AND TABLE_NAME = @t",
                            new { t = testTable });

                        if (verified > 0)
                        {
                            created++;
                            logger?.LogInformation(
                                "[ACTest] {T}: created (LIKE {R}).",
                                testTable, realTable);
                        }
                        else
                        {
                            failed++;
                            logger?.LogError(
                                "[ACTest] {T}: CREATE ran but " +
                                "table not found after creation.",
                                testTable);
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        logger?.LogError(ex,
                            "[ACTest] {T}: exception during create.",
                            testTable);
                    }
                }

                _completed = true;

                // Summary log
                if (failed == 0)
                {
                    logger?.LogInformation(
                        "[ACTest] All test tables ready — " +
                        "Created:{C} AlreadyExisted:{E}",
                        created, existed);
                }
                else
                {
                    logger?.LogWarning(
                        "[ACTest] Test table setup incomplete — " +
                        "Created:{C} Existed:{E} Failed:{F}",
                        created, existed, failed);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "[ACTest] CRITICAL: Bootstrap failed.");
                // Do NOT rethrow — app must start even if this fails
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Resets the singleton so bootstrap runs again on next call.
        /// Useful for testing or after a table drop/recreate cycle.
        /// </summary>
        public static void Reset() => _completed = false;

        /// <summary>
        /// Returns row counts for all test tables vs real tables.
        /// Use this to compare after a test run.
        /// </summary>
        public static async Task<List<AcTestTableComparison>> GetComparisonAsync(
            string connectionString)
        {
            var result = new List<AcTestTableComparison>();
            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            foreach (var realTable in AcTestTableNames.AllOutputTables)
            {
                var testTable = realTable + AcTestTableNames.TestSuffix;
                var row = new AcTestTableComparison
                {
                    RealTable = realTable,
                    TestTable = testTable
                };

                try
                {
                    row.RealTableRows = await conn.ExecuteScalarAsync<long>(
                        $"SELECT COUNT(*) FROM `lcs_hr`.`{realTable}`");
                }
                catch { row.RealTableRows = -1; }

                try
                {
                    row.TestTableRows = await conn.ExecuteScalarAsync<long>(
                        $"SELECT COUNT(*) FROM `lcs_hr`.`{testTable}`");
                }
                catch { row.TestTableRows = -1; }

                result.Add(row);
            }
            return result;
        }

        /// <summary>
        /// Truncates all _AC_Test tables for a clean test run.
        /// NEVER touches real tables.
        /// </summary>
        public static async Task TruncateAllTestTablesAsync(
            string connectionString,
            ILogger? logger = null)
        {
            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            foreach (var realTable in AcTestTableNames.AllOutputTables)
            {
                var testTable = realTable + AcTestTableNames.TestSuffix;
                try
                {
                    await conn.ExecuteAsync($"TRUNCATE TABLE `lcs_hr`.`{testTable}`");
                    logger?.LogInformation("[ACTest] Truncated {T}.", testTable);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "[ACTest] Could not truncate {T}.", testTable);
                }
            }
        }
    }

    public class AcTestTableComparison
    {
        public string RealTable     { get; set; } = "";
        public string TestTable     { get; set; } = "";
        public long   RealTableRows { get; set; }
        public long   TestTableRows { get; set; }
        public bool   Match         => RealTableRows == TestTableRows;
    }
}
