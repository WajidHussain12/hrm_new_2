using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        /// <summary>
        /// Executes the final commission finalization step for a given city/month/year.
        /// Mirrors old project FinalComissionProcess.aspx.cs behaviour exactly:
        ///   1. Validates month is not closed (EnsureProcessesOpenAsync)
        ///   2. Validates this city/month/year has not already been finalized (is_final_commission_process)
        ///   3. Validates commission period has ended (toDate &lt;= today)
        ///   4. Resolves station IDs via hr_locationmapping
        ///   5. Calls lcs_incentive.ProcessFinalCommission(...) via MIS connection
        ///   6. On success inserts lock record into is_final_commission_process
        /// </summary>
        public async Task<FinalCommissionProcessResult> ProcessFinalCommissionAsync(
            FinalCommissionProcessViewModel model, string userId, Func<int, int, Task>? onProgress = null)
        {
            int year     = model.Year;
            int month    = model.Month;
            string city  = model.CityCode;

            // ── MAIN HR DB connection ────────────────────────────────────────────────
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new InvalidOperationException("Cannot create main database connection.");
            await connection.OpenAsync();
            var fcConnId = await LogConnectionIdAsync(connection, "FinalCommission", city, "ProcessFinalCommissionAsync_Start");
            var fcOverallStart = System.Diagnostics.Stopwatch.StartNew();

            // 1. Reject if the month is already closed (hr_closeprocesses)
            await EnsureProcessesOpenAsync(connection, year, month, city);

            // 2. Reject if final commission was already processed for this city/month/year
            // In test mode: skip this check — is_final_commission_process is never written
            // in test mode (the INSERT is guarded by !IsTestMode), so reading it would
            // always show "production already finalized" and block every test run.
            if (!AcTestTableNames.IsTestMode)
            {
                var existingLock = await connection.QueryFirstOrDefaultAsync<FinalCommissionLockRow>(
                    @"SELECT city    AS City,
                             month   AS Month,
                             year    AS Year,
                             userId  AS UserId,
                             created_at AS CreatedAt
                      FROM is_final_commission_process
                      WHERE city = @City AND month = @Month AND year = @Year
                      LIMIT 1",
                    new { City = city, Month = month, Year = year },
                    commandTimeout: 30);

                if (existingLock != null)
                {
                    var userName = await connection.ExecuteScalarAsync<string>(
                        "SELECT UserName FROM lcs_users WHERE userID = @UserId",
                        new { UserId = existingLock.UserId },
                        commandTimeout: 30);

                    throw new ArgumentException(
                        $"Already Processed on {existingLock.CreatedAt:dd-MMM-yyyy}."
                        + BuildProcessedByInfo(existingLock.UserId, userName));
                }
            }

            // 3. Commission period must have ended (toDate <= today)
            var commCfg  = await CommissionConfig.LoadAsync(connection);
            var toDate   = new DateTime(year, month, commCfg.CommissionEndDay);
            var fromDate = new DateTime(year, month, commCfg.CommissionStartDay).AddMonths(-1);

            if (toDate > DateTime.Now)
                throw new ArgumentException("Process Cannot run on current working Month");

            // 4. Resolve station IDs for the city from hr_locationmapping
            var stationIds = (await connection.QueryAsync<string>(
                @"SELECT DISTINCT BStationId
                  FROM lcs_hr.hr_locationmapping lm
                  WHERE lm.GlLocationId IN (
                      SELECT l.LocationID
                      FROM   lcs_setup.locations l
                      WHERE  l.BILLINGCITYID = (
                          SELECT c.station_id
                          FROM   lcs_hr.hr_city c
                          WHERE  c.Code = @CityCode
                      )
                  )
                  AND BStationId IS NOT NULL",
                new { CityCode = city },
                commandTimeout: 120)).ToList();

            if (!stationIds.Any())
                throw new ArgumentException("Station ID is not defined for the selected city");

            string stationsCsv = string.Join(",", stationIds);

            // 5. Call stored procedure via MIS connection (same as old project)
            string? misConnStr = ResolveOptionalConnectionString("MIS");
            if (string.IsNullOrWhiteSpace(misConnStr))
                throw new ArgumentException("MIS connection string is not configured.");

            int rowsAffected;

            using (var misConn = new MySqlConnection(misConnStr))
            {
                await misConn.OpenAsync();
                var fcMisConnId = await LogConnectionIdAsync(misConn, "FinalCommission", city, "MIS_StoredProc_Connection");
                using var misTx = await misConn.BeginTransactionAsync();
                try
                {
                    // Exact same call as old FinalComissionProcess.aspx.cs line 255
                    string spQuery =
                        $"CALL lcs_incentive.ProcessFinalCommission(" +
                        $"'{city}'," +
                        $"'{stationsCsv}'," +
                        $"'{fromDate:yyyy-MM-dd}'," +
                        $"'{toDate:yyyy-MM-dd}'," +
                        $"{userId});";

                    var spStart = System.Diagnostics.Stopwatch.StartNew();
                    rowsAffected = await misConn.ExecuteAsync(
                        spQuery,
                        commandTimeout: 900,
                        transaction: misTx);
                    spStart.Stop();
                    LogOperationComplete("FinalCommission", city, "SP_ProcessFinalCommission", fcMisConnId, spStart.Elapsed, rowsAffected);

                    if (rowsAffected > 0)
                    {
                        await misTx.CommitAsync();
                    }
                    else
                    {
                        // 0 rows is a valid no-data scenario — mirrors old project FinalComissionProcess.aspx.cs
                        // behaviour exactly: the else block was empty (lines 186-188), meaning 0 rows =
                        // silent pass with no error, no lock insert, and no retry.
                        // Root cause: MIS incentive source tables (delivery_commission, rbi_cn_detail, etc.)
                        // have no data for this city/period. The SP has nothing to INSERT, so it returns 0.
                        // Correct response: rollback (nothing to commit), return Success=true with 0 rows.
                        // No lock is inserted — city remains re-runnable if MIS data is loaded later.
                        await misTx.RollbackAsync();
                        return new FinalCommissionProcessResult
                        {
                            Success        = true,
                            Message        = "No incentive records found for this city/period in the MIS system (0 rows). " +
                                             "Verify that MIS incentive source data has been uploaded for the selected month.",
                            ProcessedCount = 0
                        };
                    }
                }
                catch
                {
                    await misTx.RollbackAsync();
                    throw;
                }
            }

            // 6. Insert lock record — prevents re-processing (mirrors is_final_commission_process INSERT)
            //    SKIPPED in test mode — is_final_commission_process is on S7 MIS.
            if (!AcTestTableNames.IsTestMode)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO is_final_commission_process (city, month, year, userId, created_at)
                      VALUES (@City, @Month, @Year, @UserId, NOW())",
                    new { City = city, Month = month, Year = year, UserId = userId },
                    commandTimeout: 30);
            }

            onProgress?.Invoke(rowsAffected, rowsAffected);

            fcOverallStart.Stop();
            LogOperationComplete("FinalCommission", city, "ProcessFinalCommission_TOTAL", fcConnId, fcOverallStart.Elapsed, rowsAffected);

            return new FinalCommissionProcessResult
            {
                Success       = true,
                Message       = $"{rowsAffected} Records Processed Successfully!",
                ProcessedCount = rowsAffected
            };
        }

        // ── Private helper to deserialise the lock row ───────────────────────────
        private sealed class FinalCommissionLockRow
        {
            public string   City      { get; init; } = string.Empty;
            public int      Month     { get; init; }
            public int      Year      { get; init; }
            public string   UserId    { get; init; } = string.Empty;
            public DateTime CreatedAt { get; init; }
        }
    }
}
