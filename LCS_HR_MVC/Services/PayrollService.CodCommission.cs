using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using LCS_HR_MVC.Data;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        /// <summary>Timeout for large external source SELECTs from Central_OPS (seconds).</summary>
        private const int CodSourceSelectTimeoutSeconds = 900;

        /// <summary>Timeout for DELETE/UPDATE operations within COD commission processing (seconds).</summary>
        private const int CodWriteOperationTimeoutSeconds = 300;

        /// <summary>Timeout for local SELECT/aggregation queries in COD commission processing (seconds).</summary>
        private const int CodLocalQueryTimeoutSeconds = 600;

        public async Task<CodCommissionViewModel> GetCodCommissionPageAsync(
            DateTime workingDate,
            string currentUserId,
            CodCommissionViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var model = existingModel ?? new CodCommissionViewModel();
            if (model.Year <= 0)
            {
                model.Year = workingDate.Year;
            }

            if (model.Month <= 0)
            {
                model.Month = Math.Min(workingDate.Month, 12);
            }

            model.Years = BuildYearSelectList(workingDate);
            model.Months = BuildMonthSelectList(workingDate, model.Year);
            model.Zones = await BuildUserZoneSelectItemsAsync(connection, currentUserId, "Please Select", "00");
            model.Cities = await BuildUserCitySelectItemsAsync(connection, currentUserId, model.ZoneId, "Please Select", "0", includeAllCity: false);

            return model;
        }

        public async Task<CodCommissionProcessResult> ProcessCodCommissionAsync(CodCommissionViewModel model, string currentUserId, Func<int, int, Task>? onProgress = null)
        {
            var result = new CodCommissionProcessResult();

            try
            {
                using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new ArgumentException("Database error");

                await connection.OpenAsync();
                var codConnId = await LogConnectionIdAsync(connection, "CodCommission", model.CityCode, "ProcessCodCommissionAsync_Start");
                var codOverallStart = System.Diagnostics.Stopwatch.StartNew();

                await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

                var prepareStart = System.Diagnostics.Stopwatch.StartNew();
                CodCommissionExecutionContext context = await PrepareCodCommissionExecutionContextAsync(
                    connection,
                    model.Year,
                    model.Month,
                    model.CityCode,
                    currentUserId);
                prepareStart.Stop();
                LogOperationComplete("CodCommission", model.CityCode, "PrepareCodContext", codConnId, prepareStart.Elapsed, context.SourceLoad.Rows.Count);

                if (onProgress != null) await onProgress(0, context.SourceLoad.Rows.Count); // source rows loaded — total known

                using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                try
                {
                    var execStart = System.Diagnostics.Stopwatch.StartNew();
                    CodCommissionExecutionResult execution = await ExecuteCodCommissionAsync(connection, transaction as MySqlTransaction, context, currentUserId, onProgress, context.SourceLoad.Rows.Count);
                    await transaction.CommitAsync();
                    execStart.Stop();
                    LogOperationComplete("CodCommission", model.CityCode, "ExecuteCodCommission+Commit", codConnId, execStart.Elapsed, execution.TotalStageRowsAffected);
                    codOverallStart.Stop();
                    _logger?.LogInformation(
                        "[DbDiag] CodCommission City={CityCode} TOTAL duration={TotalSec:F2}s ConnId={ConnId}",
                        model.CityCode, codOverallStart.Elapsed.TotalSeconds, codConnId);

                    result.Success = execution.TotalStageRowsAffected > 0;
                    result.Message = execution.TotalStageRowsAffected > 0
                        ? $"{execution.CommissionRowsInserted} Record(s) inserted."
                        : "No record found for cod commission";
                    result.ConsignmentRowsInserted = execution.ConsignmentRowsInserted;
                    result.ActivityRowsInserted = execution.ActivityRowsInserted;
                    result.ReturnShipmentRowsInserted = execution.ReturnShipmentRowsInserted;
                    result.CommissionRowsInserted = execution.CommissionRowsInserted;
                    result.StationCount = context.StationIds.Count;
                    result.FromDate = context.FromDate;
                    result.ToDate = context.ToDate;

                    // Signal final inserted count so view shows "X / Y rows" before Completed fires
                    int codInserted = execution.ConsignmentRowsInserted + execution.ActivityRowsInserted
                                    + execution.ReturnShipmentRowsInserted + execution.CommissionRowsInserted;
                    if (onProgress != null) await onProgress(codInserted, context.SourceLoad.Rows.Count);
                }
                catch
                {
                    try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                    throw;
                }
            }
            catch (ArgumentException ex)
            {
                result.Message = ex.Message;
            }

            return result;
        }

        public async Task<CodCommissionPreviewResult> PreviewCodCommissionAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();
            await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

            CodCommissionExecutionContext context = await PrepareCodCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId,
                rejectExistingRows: false);

            CodCommissionPreviewBaseline baseline = await CaptureCodCommissionPreviewBaselineAsync(
                connection,
                context.Year,
                context.Month,
                context.CityCode,
                context.StationIds);

            using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                CodCommissionExecutionResult execution = await ExecuteCodCommissionAsync(connection, transaction as MySqlTransaction, context, currentUserId);
                await transaction.RollbackAsync();

                return new CodCommissionPreviewResult
                {
                    Year = context.Year,
                    Month = context.Month,
                    CityCode = context.CityCode,
                    StationCount = context.StationIds.Count,
                    FromDate = context.FromDate,
                    ToDate = context.ToDate,
                    SourceRowsRetrieved = context.SourceLoad.Rows.Count,
                    DepositDatesBackfilled = context.SourceLoad.BackfilledDepositDates,
                    ExistingConsignmentsSkipped = execution.ExistingConsignmentsSkipped,
                    ConsignmentRowsInserted = execution.ConsignmentRowsInserted,
                    ActivityRowsInserted = execution.ActivityRowsInserted,
                    DuplicateStatusRows = execution.DuplicateStatusRows,
                    RemarksUpdatedRows = execution.RemarksUpdatedRows,
                    ReturnShipmentRowsInserted = execution.ReturnShipmentRowsInserted,
                    CommissionRowsInserted = execution.CommissionRowsInserted,
                    GeneratedCodAmountTotal = execution.GeneratedCodAmountTotal,
                    GeneratedBonusTotal = execution.GeneratedBonusTotal,
                    GeneratedDeductionTotal = execution.GeneratedDeductionTotal,
                    RollbackIntegrityPreserved = await VerifyCodCommissionPreviewBaselineAsync(
                        connection,
                        context.Year,
                        context.Month,
                        context.CityCode,
                        context.StationIds,
                        baseline)
                };
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                throw;
            }
        }

        private async Task<CodCommissionExecutionContext> PrepareCodCommissionExecutionContextAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            bool rejectExistingRows = true)
        {
            if (year <= 0 || month <= 0)
            {
                throw new ArgumentException("Year and month are required.");
            }

            if (string.IsNullOrWhiteSpace(cityCode) || cityCode == "0")
            {
                throw new ArgumentException("City is required.");
            }

            var cfg = await CommissionConfig.LoadAsync(connection);
            DateTime toDate = new DateTime(year, month, cfg.CommissionEndDay);
            DateTime fromDate = new DateTime(year, month, cfg.CommissionStartDay).AddMonths(-1);
            if (toDate > DateTime.Now)
            {
                throw new ArgumentException("Process Can not run on current working Month");
            }

            await EnsureCodCommissionCityAllowedAsync(connection, currentUserId, cityCode);

            List<string> stationIds = (await connection.QueryAsync<string>(
                @"SELECT DISTINCT lm.BStationId
                  FROM lcs_hr.hr_locationmapping lm
                  WHERE lm.GlLocationId IN (
                      SELECT l.LocationID
                      FROM lcs_setup.locations l
                      WHERE l.BILLINGCITYID = (
                          SELECT c.station_id
                          FROM lcs_hr.hr_city c
                          WHERE c.Code = @CityCode
                      )
                  )
                  AND lm.BStationId IS NOT NULL;",
                new { CityCode = cityCode.Trim() })).ToList();

            if (stationIds.Count == 0)
            {
                throw new ArgumentException("Station ID is not define for the selected city");
            }

            if (rejectExistingRows)
            {
                var info = await connection.QueryFirstOrDefaultAsync(
                    $@"SELECT t.CreatedDate AS ProcessedDate, t.CreatedBy, u.UserName
                      FROM {CodConsignmentsTable} t
                      LEFT JOIN lcs_users u ON u.userID = t.CreatedBy
                      WHERE t.Cyear = @Year AND t.CMonth = @Month AND t.Arivl_Dest IN @Stations
                      ORDER BY t.CreatedDate DESC LIMIT 1",
                    new { Year = year, Month = month, Stations = stationIds },
                    commandTimeout: 30);
                if (info?.ProcessedDate != null)
                {
                    throw new ArgumentException($"Already Processed on {((DateTime)info.ProcessedDate):dd-MMM-yyyy}.{BuildProcessedByInfo(info.CreatedBy?.ToString(), info.UserName?.ToString())}");
                }
            }

            CodCommissionSourceLoadResult sourceLoad = await LoadCodCommissionSourceRowsAsync(
                stationIds,
                fromDate,
                toDate);

            List<CodAllCnRecord> allCnRows = await LoadCodActivityRowsAsync(
                stationIds,
                fromDate,
                toDate,
                cfg);

            return new CodCommissionExecutionContext
            {
                Year = year,
                Month = month,
                CityCode = cityCode.Trim(),
                FromDate = fromDate,
                ToDate = toDate,
                StationIds = stationIds,
                AllCnRows = allCnRows,
                SourceLoad = sourceLoad,
                Cfg = cfg
            };
        }

        private static async Task EnsureCodCommissionCityAllowedAsync(MySqlConnection connection, string currentUserId, string cityCode)
        {
            int isAllowed = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM lcs_user_location
                  WHERE userid = @UserId
                    AND city_code = @CityCode",
                new
                {
                    UserId = currentUserId,
                    CityCode = cityCode?.Trim()
                });

            if (isAllowed == 0)
            {
                throw new ArgumentException("You are not allowed to process COD commission for the selected city.");
            }
        }

        private async Task<MySqlConnection> OpenExternalCodConnectionAsync(string connectionName)
        {
            string? connectionString = ResolveOptionalConnectionString(connectionName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException($"{connectionName} connection string is not configured.");
            }

            var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            await EnsureExternalConnectionResponsiveAsync(connection, connectionName);
            return connection;
        }

        private static async Task EnsureExternalConnectionResponsiveAsync(
            MySqlConnection connection,
            string connectionName,
            int commandTimeoutSeconds = 30)
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            try
            {
                await connection.ExecuteScalarAsync<int>("SELECT 1;", commandTimeout: commandTimeoutSeconds);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{connectionName} connection is not responsive.",
                    ex);
            }
        }

        private string? ResolveOptionalConnectionString(string connectionName)
        {
            return _configuration?.GetConnectionString(connectionName);
        }

        private async Task<CodCommissionSourceLoadResult> LoadCodCommissionSourceRowsAsync(
            IReadOnlyCollection<string> stationIds,
            DateTime fromDate,
            DateTime toDate)
        {
            var sourceStopwatch = Stopwatch.StartNew();
            List<CodCommissionSourceRow> deduplicatedRows;
            using (var operationsConnection = await OpenExternalCodConnectionAsync("Central_OPS"))
            {
                var srcConnId = await LogConnectionIdAsync(operationsConnection, "CodCommission", "SourceSELECT", "LoadCodCommissionBaseSource_Central_OPS");
                deduplicatedRows = await LoadCodCommissionBaseSourceRowsAsync(
                    operationsConnection,
                    stationIds,
                    fromDate,
                    toDate);
                LogOperationComplete("CodCommission", "SourceSELECT", "LoadCodCommissionBaseSource", srcConnId, sourceStopwatch.Elapsed, deduplicatedRows.Count);
            }

            sourceStopwatch.Stop();
            _logger?.LogInformation(
                "COD source_base completed: {Rows} rows across {StationCount} station(s) in {ElapsedSeconds:F2}s",
                deduplicatedRows.Count,
                stationIds.Count,
                sourceStopwatch.Elapsed.TotalSeconds);

            List<string> cnShortList = deduplicatedRows
                .Where(static row => !row.cr_entry_date.HasValue && string.Equals(row.STATUS, "DV", StringComparison.OrdinalIgnoreCase))
                .Select(static row => NormalizeCnShort(row.CN_NUMBER))
                .Where(static cn => !string.IsNullOrWhiteSpace(cn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var depositStopwatch = Stopwatch.StartNew();
            List<CodCashDepositDateRecord> depositDates;
            using (var depositConnection = await OpenExternalCodConnectionAsync("Central_OPSNew"))
            {
                depositDates = await LoadCodCashDepositDatesAsync(depositConnection, cnShortList);
            }

            depositStopwatch.Stop();
            _logger?.LogInformation(
                "COD deposit_backfill_source completed: {DepositRows} rows for {CandidateCount} candidate CN(s) in {ElapsedSeconds:F2}s",
                depositDates.Count,
                cnShortList.Count,
                depositStopwatch.Elapsed.TotalSeconds);

            int backfilledDepositDates = 0;
            foreach (var row in deduplicatedRows.Where(static item => !item.cr_entry_date.HasValue))
            {
                CodCashDepositDateRecord? depositDate = depositDates.FirstOrDefault(item =>
                    string.Equals(item.cn_short, NormalizeCnShort(row.CN_NUMBER), StringComparison.OrdinalIgnoreCase));

                if (depositDate == null)
                {
                    continue;
                }

                if (!TryParseLegacyCodDate(depositDate.date_credit, out DateTime parsedDate))
                {
                    continue;
                }

                row.cr_entry_date = parsedDate;
                row.cr_depositslip = depositDate.deposit_slip_no;
                row.cr_Amt = depositDate.amount;
                row.cr_Mode = "B";
                backfilledDepositDates++;
            }

            return new CodCommissionSourceLoadResult
            {
                Rows = deduplicatedRows,
                BackfilledDepositDates = backfilledDepositDates
            };
        }

        private static async Task<List<CodCommissionSourceRow>> LoadCodCommissionBaseSourceRowsAsync(
            MySqlConnection operationsConnection,
            IReadOnlyCollection<string> stationIds,
            DateTime fromDate,
            DateTime toDate)
        {
            const string query = @"
WITH arrival(CN_NUMBER,COURIER_ID,ARVL_DEST,COUR_DATE,Cour_Time,DELIVERY_DATE,DELIVERY_TIME,PCS,WEIGHT,REASON,RECEIVER_NAME,BH_REMARKS,cnic_no,CN_TYPE,STATUS,arvl_date,arvl_time,shart_CN)
AS (
    SELECT CN_NUMBER,COURIER_ID,ARVL_DEST,COUR_DATE,Cour_Time,DELIVERY_DATE,DELIVERY_TIME,PCS,WEIGHT,REASON,RECEIVER_NAME,BH_REMARKS,cnic_no,CN_TYPE,STATUS,arvl_date,arvl_time,shart_CN
    FROM lcs_db.arival
    WHERE ARVL_DEST IN @CityIds
      AND COUR_DATE BETWEEN @FromDate AND @ToDate
)
SELECT xb1.*,
       IF(IsPrepaid1 = 'YES' OR IsPrepaid2 = 'YES',
          DATEDIFF(DATE(xb1.Delivery_date), DATE(xb1.COUR_DATE)),
          DATEDIFF(DATE(xb1.cr_entry_date), DATE(xb1.Delivery_date))) AS DateDif
FROM (
    SELECT DISTINCT
        a.CN_NUMBER,
        a.COURIER_ID,
        a.ARVL_DEST,
        a.COUR_DATE AS COUR_DATE,
        a.DELIVERY_DATE AS DELIVERY_DATE,
        a.DELIVERY_TIME,
        a.PCS,
        a.WEIGHT,
        a.REASON,
        a.RECEIVER_NAME,
        a.BH_REMARKS,
        a.cnic_no,
        a.CN_TYPE,
        a.STATUS,
        cr.amount AS cr_Amt,
        cr.entry_date AS cr_entry_date,
        cr.depositslip AS cr_depositslip,
        cr.Mode AS cr_Mode,
        IF(cd.cn_number IS NULL, 'NO', 'YES') AS IsPrepaid1,
        IF(oc.cn_number IS NULL, 'NO', 'YES') AS IsPrepaid2
    FROM arrival a
    INNER JOIN lcs_db.cod_ranges b ON a.SHART_CN BETWEEN b.start_range AND b.end_range
    LEFT JOIN cod_payment_receive.cod_receive cr ON a.CN_NUMBER = cr.cn_number
    LEFT JOIN lcs_db.cod_download cd ON a.CN_NUMBER = cd.cn_number AND cd.collect_amount = 0
    LEFT JOIN lcs_db.oms_cod_download oc ON oc.cn_number = a.CN_NUMBER AND oc.collect_amount = 0
    INNER JOIN (
        SELECT DISTINCT
            a.CN_NUMBER,
            MAX(CONCAT(a.COUR_DATE, ' ', a.Cour_Time)) AS maxdate
        FROM arrival a
        INNER JOIN lcs_db.cod_ranges b ON a.SHART_CN BETWEEN b.start_range AND b.end_range
        LEFT JOIN cod_payment_receive.cod_receive cr ON a.CN_NUMBER = cr.cn_number
        LEFT JOIN lcs_db.cod_download cd ON a.CN_NUMBER = cd.cn_number AND cd.collect_amount = 0
        LEFT JOIN lcs_db.oms_cod_download oc ON oc.cn_number = a.CN_NUMBER AND oc.collect_amount = 0
        WHERE a.ARVL_DEST IN @CityIds
          AND a.COUR_DATE BETWEEN @FromDate AND @ToDate
          AND a.STATUS IN ('DV')
        GROUP BY a.CN_NUMBER
    ) t ON t.CN_NUMBER = a.CN_NUMBER AND CONCAT(a.COUR_DATE, ' ', a.Cour_Time) = t.maxdate
    WHERE a.ARVL_DEST IN @CityIds
      AND a.COUR_DATE BETWEEN @FromDate AND @ToDate
      AND a.STATUS IN ('DV')
    GROUP BY a.CN_NUMBER
) AS xb1
UNION ALL
SELECT ud.CN_NUMBER,
       ud.COURIER_ID,
       ud.ARVL_DEST,
       ud.COUR_DATE,
       ud.DELIVERY_DATE,
       ud.DELIVERY_TIME,
       ud.PCS,
       ud.WEIGHT,
       ud.REASON,
       ud.RECEIVER_NAME,
       ud.BH_REMARKS,
       ud.cnic_no,
       ud.CN_TYPE,
       ud.STATUS,
       NULL,
       NULL,
       NULL,
       NULL,
       NULL,
       NULL,
       0
FROM arrival ud
INNER JOIN lcs_db.cod_ranges b1 ON ud.SHART_CN BETWEEN b1.start_range AND b1.end_range
LEFT JOIN (
    SELECT cn_number, arvl_date, arvl_time
    FROM arrival
    WHERE STATUS IN ('DV','DS','AR','AT','CB','DL','IT','LD','PN','OD','RN','RT','RS','SR','BI','BL','NR','PB','RC','DP','RF','DT','DR','DW')
    GROUP BY cn_number
) AS b USING(cn_number)
WHERE b.cn_number IS NULL;";

            var rows = (await operationsConnection.QueryAsync<CodCommissionSourceRow>(
                query,
                new
                {
                    ToDate = toDate,
                    FromDate = fromDate,
                    CityIds = stationIds.ToArray()
                },
                commandTimeout: CodSourceSelectTimeoutSeconds)).ToList();

            return rows
                .GroupBy(static row => row.CN_NUMBER, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToList();
        }

        private async Task<List<CodAllCnRecord>> LoadCodActivityRowsAsync(
            IReadOnlyCollection<string> stationIds,
            DateTime fromDate,
            DateTime toDate,
            CommissionConfig cfg)
        {
            var activityStopwatch = Stopwatch.StartNew();
            List<CodAllCnRecord> rows;
            using (var operationsConnection = await OpenExternalCodConnectionAsync("Central_OPS"))
            {
                await LogConnectionIdAsync(operationsConnection, "CodCommission", "ActivitySELECT", "LoadCodActivityRows_Central_OPS");
                rows = await LoadCodActivityRowsFromOperationsAsync(
                    operationsConnection,
                    stationIds,
                    fromDate,
                    toDate,
                    cfg);
            }

            activityStopwatch.Stop();
            _logger?.LogInformation(
                "COD activity_source completed: {Rows} rows across {StationCount} station(s) in {ElapsedSeconds:F2}s",
                rows.Count,
                stationIds.Count,
                activityStopwatch.Elapsed.TotalSeconds);

            return rows;
        }

        private static async Task<List<CodAllCnRecord>> LoadCodActivityRowsFromOperationsAsync(
            MySqlConnection operationsConnection,
            IReadOnlyCollection<string> stationIds,
            DateTime fromDate,
            DateTime toDate,
            CommissionConfig cfg)
        {
            string query = $@"
SELECT
    cn.cn_number AS CN_Number,
    cn.arvl_dest AS Arivl_Dest,
    cn.courier_id AS Cour_id,
    DATE_FORMAT(cn.cour_date, '%Y-%m-%d') AS Cour_Date,
    cn.Cour_Time AS Cour_Time,
    cn.maxdate AS Activity_Date,
    cn.STATUS AS STATUS,
    oc.company_id AS Company_Id,
    oc.order_id AS Order_Id,
    oc.client_id AS Client_Id,
    cn.BH_REMARKS
FROM (
    SELECT
        xb.cn_number,
        xb.arvl_dest,
        xb.courier_id,
        xb.cour_date,
        xb.Cour_Time,
        xb.STATUS,
        MAX(xb.maxdate) AS maxdate,
        xb.BH_REMARKS
    FROM (
        SELECT
            a.cn_number,
            a.arvl_dest,
            a.cour_date,
            a.Cour_Time,
            a.courier_id,
            a.ACTIVITY_DATE,
            a.Activity_time,
            a.STATUS,
            a.BH_REMARKS,
            MAX(TIMESTAMP(activity_date, activity_time)) AS maxdate
        FROM lcs_db.arival a
        INNER JOIN lcs_db.cod_ranges b ON a.SHART_CN BETWEEN b.start_range AND b.end_range
        WHERE a.ARVL_DEST IN @StationIds
          AND a.COUR_DATE BETWEEN @FromDate AND @ToDate
          AND a.STATUS NOT IN ('DS','DW','DR','IT','DL')
          AND a.BH_REMARKS NOT IN ('RT','RV','RW','RS')
        GROUP BY a.cn_number, a.courier_id, a.STATUS
        ORDER BY maxdate DESC
    ) AS xb
    GROUP BY xb.cn_number, xb.courier_id
) AS cn
LEFT JOIN lcs_db.cod_download oc ON oc.cn_number = cn.cn_number
WHERE oc.client_id NOT IN ({cfg.CodExcludedClientIdsCsv});";

            return (await operationsConnection.QueryAsync<CodAllCnRecord>(
                query,
                new
                {
                    ToDate = toDate,
                    FromDate = fromDate,
                    StationIds = stationIds.ToArray()
                },
                commandTimeout: 1200)).ToList();
        }

        private static async Task<List<CodCashDepositDateRecord>> LoadCodCashDepositDatesAsync(
            MySqlConnection depositConnection,
            IReadOnlyCollection<string> cnShortList)
        {
            if (cnShortList.Count == 0)
            {
                return new List<CodCashDepositDateRecord>();
            }

            const string query = @"
SELECT b.cn_short, b.deposit_slip_no, b.amount, b.date_credit, b.payment_mode
FROM ecom_bank_transaction_detail_01_31 b
WHERE b.cn_short IN @CNs;";

            var combinedList = new List<CodCashDepositDateRecord>();
            const int chunkSize = 1000;

            for (int i = 0; i < cnShortList.Count; i += chunkSize)
            {
                string[] chunk = cnShortList.Skip(i).Take(chunkSize).ToArray();
                var chunkRows = await depositConnection.QueryAsync<CodCashDepositDateRecord>(
                    query,
                    new { CNs = chunk },
                    commandTimeout: CodLocalQueryTimeoutSeconds);

                combinedList.AddRange(chunkRows);
            }

            return combinedList;
        }

        private static bool TryParseLegacyCodDate(string? value, out DateTime parsedDate)
        {
            string[] possibleFormats = { "yyyy-MM-dd", "dd-MM-yyyy", "MM/dd/yyyy", "dd/MM/yyyy", "d/MM/yyyy", "M/dd/yyyy" };
            return DateTime.TryParseExact(
                value,
                possibleFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate);
        }

        private static string NormalizeCnShort(string? cnNumber)
        {
            string normalized = cnNumber?.Trim() ?? string.Empty;
            return normalized.Length > 2 ? normalized.Substring(2) : normalized;
        }

        private static async Task<CodCommissionStageResult> StageCodCommissionAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            string currentUserId,
            IReadOnlyCollection<string> stationIds,
            DateTime fromDate,
            DateTime toDate,
            List<CodCommissionSourceRow> codRows,
            List<CodAllCnRecord> allCnRows)
        {
            var result = new CodCommissionStageResult();

            await connection.ExecuteAsync(
                $@"DELETE FROM {CodConsignmentsTable}
                  WHERE Cyear = @Year
                    AND CMonth = @Month
                    AND Arivl_Dest IN @Cities;",
                new
                {
                    Year = year,
                    Month = month,
                    Cities = stationIds.ToArray()
                },
                transaction,
                commandTimeout: CodWriteOperationTimeoutSeconds);

            await connection.ExecuteAsync(
                $@"DELETE FROM {AllCodConsignmentTable}
                  WHERE Year = @Year
                    AND Month = @Month
                    AND Arivl_Dest IN @Cities;",
                new
                {
                    Year = year,
                    Month = month,
                    Cities = stationIds.ToArray()
                },
                transaction,
                commandTimeout: CodWriteOperationTimeoutSeconds);

            await connection.ExecuteAsync(
                $@"DELETE FROM {CodReturnShipmentsTable}
                  WHERE Arvl_Dest IN @Cities
                    AND Year = @Year
                    AND Month = @Month;",
                new
                {
                    Year = year,
                    Month = month,
                    Cities = stationIds.ToArray()
                },
                transaction,
                commandTimeout: CodWriteOperationTimeoutSeconds);

            List<CodCommissionSourceRow> rowsToInsert = codRows.ToList();
            result.ExistingConsignmentsSkipped = 0;

            string insertConsignmentQuery = $@"INSERT IGNORE INTO {CodConsignmentsTable} VALUES ";

            if (rowsToInsert.Count > 0)
            {
                result.ConsignmentRowsInserted = await ExecuteMultiValueInsertAsync(
                    connection,
                    transaction,
                    insertConsignmentQuery,
                    rowsToInsert,
                    200,
                    (row, createdDate) => new[]
                    {
                        new KeyValuePair<string, object>("Year", year),
                        new KeyValuePair<string, object>("Month", month),
                        new KeyValuePair<string, object>("CnNumber", row.CN_NUMBER),
                        new KeyValuePair<string, object>("ArrivalDest", row.ARVL_DEST),
                        new KeyValuePair<string, object>("CourierId", row.COURIER_ID),
                        new KeyValuePair<string, object>("CourierDate", row.COUR_DATE.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, object>("DeliveryDate", row.DELIVERY_DATE.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, object>("DeliveryTime", row.DELIVERY_TIME),
                        new KeyValuePair<string, object>("Pieces", row.PCS),
                        new KeyValuePair<string, object>("Weight", row.WEIGHT),
                        new KeyValuePair<string, object>("Reason", row.REASON),
                        new KeyValuePair<string, object>("ReceiverName", row.RECEIVER_NAME),
                        new KeyValuePair<string, object>("BhRemarks", row.BH_REMARKS),
                        new KeyValuePair<string, object>("CnType", row.CN_TYPE),
                        new KeyValuePair<string, object>("Status", row.STATUS),
                        new KeyValuePair<string, object>("CrAmount", row.cr_Amt),
                        new KeyValuePair<string, object>("CrEntryDate", row.cr_entry_date),
                        new KeyValuePair<string, object>("CrDepositSlip", row.cr_depositslip),
                        new KeyValuePair<string, object>("CrMode", row.cr_Mode),
                        new KeyValuePair<string, object>("DateDifference", row.DateDif),
                        new KeyValuePair<string, object>("IsPrepaid", string.Equals(row.IsPrepaid1, "YES", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(row.IsPrepaid2, "YES", StringComparison.OrdinalIgnoreCase)),
                        new KeyValuePair<string, object>("CodBonus", 0),
                        new KeyValuePair<string, object>("Remarks", null),
                        new KeyValuePair<string, object>("CreatedDate", createdDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, object>("CreatedBy", currentUserId)
                    });
            }

            if (allCnRows.Count > 0)
            {
                string insertActivityQuery = $@"INSERT INTO {AllCodConsignmentTable}
(
    CN_Number,
    Arivl_Dest,
    Cour_id,
    Cour_Date,
    Cour_Time,
    Activity_Date,
    STATUS,
    Company_Id,
    Order_Id,
    Client_Id,
    YEAR,
    MONTH,
    createdBy
)
VALUES ";

                result.ActivityRowsInserted = await ExecuteMultiValueInsertAsync(
                    connection,
                    transaction,
                    insertActivityQuery,
                    allCnRows,
                    200,
                    (row, _) => new[]
                    {
                        new KeyValuePair<string, object>("CnNumber", row.CN_Number),
                        new KeyValuePair<string, object>("ArrivalDest", row.Arivl_Dest),
                        new KeyValuePair<string, object>("CourierId", row.Cour_id),
                        new KeyValuePair<string, object>("CourierDate", row.Cour_Date),
                        new KeyValuePair<string, object>("CourierTime", row.Cour_Time),
                        new KeyValuePair<string, object>("ActivityDate", row.Activity_Date),
                        new KeyValuePair<string, object>("Status", row.STATUS),
                        new KeyValuePair<string, object>("CompanyId", row.Company_Id),
                        new KeyValuePair<string, object>("OrderId", row.Order_Id),
                        new KeyValuePair<string, object>("ClientId", row.Client_Id),
                        new KeyValuePair<string, object>("Year", year),
                        new KeyValuePair<string, object>("Month", month),
                        new KeyValuePair<string, object>("CreatedBy", currentUserId)
                    });
            }

            await ApplyCodCommissionRemarksAndExclusionsAsync(
                connection,
                transaction,
                year,
                month,
                fromDate,
                toDate,
                stationIds);

            result.DuplicateStatusRows = await connection.ExecuteScalarAsync<int>(
                $@"SELECT COUNT(*)
                  FROM {AllCodConsignmentTable}
                  WHERE YEAR = @Year
                    AND MONTH = @Month
                    AND Arivl_Dest IN @Stations
                    AND STATUS = 'X';",
                new
                {
                    Year = year,
                    Month = month,
                    Stations = stationIds.ToArray()
                },
                transaction);

            result.RemarksUpdatedRows = await connection.ExecuteScalarAsync<int>(
                $@"SELECT COUNT(*)
                  FROM {CodConsignmentsTable}
                  WHERE Cyear = @Year
                    AND CMonth = @Month
                    AND Arivl_Dest IN @Stations
                    AND Remarks IS NOT NULL;",
                new
                {
                    Year = year,
                    Month = month,
                    Stations = stationIds.ToArray()
                },
                transaction);

            List<CodReturnShipmentRecord> returnShipments = await LoadCodReturnShipmentsAsync(connection, transaction, year, month, stationIds);

            string insertReturnShipmentQuery = $@"INSERT INTO {CodReturnShipmentsTable}
(
    Year,
    Month,
    Arvl_Dest,
    CourierID,
    AC,
    PN,
    RN,
    NR,
    IT,
    DW,
    DR,
    DS,
    DV,
    RT_Packet,
    CreatedBy,
    CreatedDate
)
VALUES ";

            result.ReturnShipmentRowsInserted = await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                insertReturnShipmentQuery,
                returnShipments,
                200,
                (returnShipment, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("Year", year),
                    new KeyValuePair<string, object>("Month", month),
                    new KeyValuePair<string, object>("ArrivalDest", returnShipment.Arvl_Dest),
                    new KeyValuePair<string, object>("CourierId", returnShipment.courier_id),
                    new KeyValuePair<string, object>("Ac", returnShipment.AC),
                    new KeyValuePair<string, object>("Pn", returnShipment.PN),
                    new KeyValuePair<string, object>("Rn", returnShipment.RN),
                    new KeyValuePair<string, object>("Nr", returnShipment.NR),
                    new KeyValuePair<string, object>("It", 0),
                    new KeyValuePair<string, object>("Dw", 0),
                    new KeyValuePair<string, object>("Dr", 0),
                    new KeyValuePair<string, object>("Ds", 0),
                    new KeyValuePair<string, object>("Dv", returnShipment.DV),
                    new KeyValuePair<string, object>("ReturnPacket", 0),
                    new KeyValuePair<string, object>("CreatedBy", currentUserId),
                    new KeyValuePair<string, object>("CreatedDate", createdDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                });

            result.TotalStageRowsAffected = result.ConsignmentRowsInserted + result.ActivityRowsInserted + result.ReturnShipmentRowsInserted;
            return result;
        }

        private static async Task ApplyCodCommissionRemarksAndExclusionsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            DateTime fromDate,
            DateTime toDate,
            IReadOnlyCollection<string> stationIds)
        {
            if (stationIds.Count == 0)
            {
                return;
            }

            var parameters = new
            {
                Year = year,
                Month = month,
                FromDate = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ToDate = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Stations = stationIds.ToArray()
            };

            // Legacy Web Forms had the same inline alternative commented beside the proc calls.
            await connection.ExecuteAsync(
                $@"UPDATE {AllCodConsignmentTable} c
                  INNER JOIN lcs_hr.cod_prepaid_commission p
                          ON p.CN = c.CN_Number
                  SET c.Remarks = 'Exclude From KPI'
                  WHERE c.MONTH = @Month
                    AND c.YEAR = @Year
                    AND c.Arivl_Dest IN @Stations
                    AND c.Cour_Date BETWEEN @FromDate AND @ToDate;",
                parameters,
                transaction,
                commandTimeout: 600);

            await connection.ExecuteAsync(
                $@"UPDATE {AllCodConsignmentTable} c
                  INNER JOIN lcs_hr.cod_upfront_nov n
                          ON n.CN_NUMBER = c.CN_Number
                  SET c.Remarks = 'Exclude From KPI'
                  WHERE c.MONTH = @Month
                    AND c.YEAR = @Year
                    AND c.Arivl_Dest IN @Stations
                    AND c.Cour_Date BETWEEN @FromDate AND @ToDate;",
                parameters,
                transaction,
                commandTimeout: 600);

            await connection.ExecuteAsync(
                $@"UPDATE {CodConsignmentsTable} c
                  INNER JOIN lcs_hr.cod_prepaid_commission p
                          ON p.CN = c.CN_Number
                  SET c.Remarks = 'CN Commission Paid',
                      p.IsMatch = TRUE,
                      p.IncentiveProcessDate = NOW()
                  WHERE c.Cyear = @Year
                    AND c.CMonth = @Month
                    AND c.Arivl_Dest IN @Stations
                    AND c.Cour_date BETWEEN @FromDate AND @ToDate;",
                parameters,
                transaction,
                commandTimeout: 600);

            await connection.ExecuteAsync(
                $@"UPDATE {CodConsignmentsTable} c
                  INNER JOIN lcs_hr.cod_upfront_nov n
                          ON n.CN_NUMBER = c.CN_Number
                  SET c.Remarks = 'CN Commission Paid'
                  WHERE c.Cyear = @Year
                    AND c.CMonth = @Month
                    AND c.Arivl_Dest IN @Stations
                    AND c.Cour_date BETWEEN @FromDate AND @ToDate;",
                parameters,
                transaction,
                commandTimeout: 600);
        }

        private static async Task<HashSet<string>> LoadExistingCodConsignmentNumbersAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            IReadOnlyCollection<CodCommissionSourceRow> rows)
        {
            var existingCnNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> cnNumbers = rows
                .Select(static row => row.CN_NUMBER)
                .Where(static cn => !string.IsNullOrWhiteSpace(cn))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            const int chunkSize = 1000;
            for (int i = 0; i < cnNumbers.Count; i += chunkSize)
            {
                string[] currentChunk = cnNumbers.Skip(i).Take(chunkSize).ToArray();
                IEnumerable<string> chunkRows = await connection.QueryAsync<string>(
                    $@"SELECT CN_Number
                      FROM {CodConsignmentsTable}
                      WHERE CN_Number IN @CNNumbers;",
                    new { CNNumbers = currentChunk },
                    transaction,
                    commandTimeout: CodLocalQueryTimeoutSeconds);

                foreach (string cnNumber in chunkRows)
                {
                    existingCnNumbers.Add(cnNumber);
                }
            }

            return existingCnNumbers;
        }

        private static async Task<List<CodReturnShipmentRecord>> LoadCodReturnShipmentsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            IReadOnlyCollection<string> stationIds)
        {
            string query = $@"
SELECT tbl.Arivl_Dest AS ARVL_DEST,
       tbl.Cour_id AS COURIER_ID,
       SUM(IF(tbl.STATUS = 'PN', 1, 0)) AS PN,
       SUM(IF(tbl.STATUS = 'RN', 1, 0)) AS RN,
       SUM(IF(tbl.STATUS = 'NR', 1, 0)) AS NR,
       SUM(IF(tbl.STATUS = 'IT', 1, 0)) AS IT,
       SUM(IF(tbl.STATUS = 'DW', 1, 0)) AS DW,
       SUM(IF(tbl.STATUS = 'DR', 1, 0)) AS DR,
       SUM(IF(tbl.STATUS = 'DS', 1, 0)) AS DS,
       SUM(IF(tbl.STATUS = 'AC', 1, 0)) AS AC,
       SUM(IF(tbl.STATUS = 'DV', 1, 0)) AS DV
FROM {AllCodConsignmentTable} AS tbl
WHERE tbl.Arivl_Dest IN @StationIds
  AND tbl.year = @Year
  AND tbl.month = @Month
  AND tbl.STATUS <> 'X'
  AND tbl.Remarks IS NULL
GROUP BY tbl.Cour_id;";

            var results = new List<CodReturnShipmentRecord>();
            const int batchSize = 1000;

            for (int i = 0; i < stationIds.Count; i += batchSize)
            {
                string[] batchStationIds = stationIds.Skip(i).Take(batchSize).ToArray();
                var batchRows = await connection.QueryAsync<CodReturnShipmentRecord>(
                    query,
                    new
                    {
                        Year = year,
                        Month = month,
                        StationIds = batchStationIds
                    },
                    transaction,
                    commandTimeout: CodLocalQueryTimeoutSeconds);

                results.AddRange(batchRows);
            }

            return results;
        }

        private static async Task<CodCommissionExecutionResult> ExecuteCodCommissionAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            CodCommissionExecutionContext context,
            string currentUserId,
            Func<int, int, Task>? onProgress = null,
            int sourceTotal = 0)
        {
            CodCommissionStageResult stageResult = await StageCodCommissionAsync(
                connection,
                transaction,
                context.Year,
                context.Month,
                currentUserId,
                context.StationIds,
                context.FromDate,
                context.ToDate,
                context.SourceLoad.Rows,
                context.AllCnRows);

            if (onProgress != null)
            {
                int staged = stageResult.ConsignmentRowsInserted + stageResult.ActivityRowsInserted
                           + stageResult.ReturnShipmentRowsInserted;
                await onProgress(staged, sourceTotal);
            }

            var rebuildResult = new CodCommissionRebuildResult();
            if (stageResult.TotalStageRowsAffected > 0)
            {
                rebuildResult = await RebuildCodCommissionAsync(
                    connection,
                    transaction,
                    context.Year,
                    context.Month,
                    context.CityCode,
                    currentUserId,
                    context.StationIds,
                    context.FromDate,
                    context.ToDate,
                    context.Cfg);
            }

            return new CodCommissionExecutionResult
            {
                ConsignmentRowsInserted = stageResult.ConsignmentRowsInserted,
                ActivityRowsInserted = stageResult.ActivityRowsInserted,
                ReturnShipmentRowsInserted = stageResult.ReturnShipmentRowsInserted,
                CommissionRowsInserted = rebuildResult.InsertedRows,
                GeneratedCodAmountTotal = rebuildResult.CodAmountTotal,
                GeneratedBonusTotal = rebuildResult.BonusTotal,
                GeneratedDeductionTotal = rebuildResult.DeductionTotal,
                ExistingConsignmentsSkipped = stageResult.ExistingConsignmentsSkipped,
                DuplicateStatusRows = stageResult.DuplicateStatusRows,
                RemarksUpdatedRows = stageResult.RemarksUpdatedRows,
                TotalStageRowsAffected = stageResult.TotalStageRowsAffected
            };
        }

        private static async Task<CodCommissionRebuildResult> RebuildCodCommissionAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            IReadOnlyCollection<string> stationIds,
            DateTime fromDate,
            DateTime toDate,
            CommissionConfig cfg)
        {
            string query = $@"
WITH xb (Cn_Number, Arivl_Dest, Cour_id, Diff) AS
(
    SELECT
        c.Cn_Number,
        c.Arivl_Dest,
        c.Cour_id,
        CASE
            WHEN c.DateDif > 1 THEN
                IF(
                    (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)) > 0,
                    c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)),
                    IF(
                        (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)) > 0,
                        c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)),
                        IF(
                            (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)) > 0,
                            c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)),
                            IF(
                                (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)) > 0
                                AND c.cr_mode = 'B',
                                c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)),
                                c.DateDif
                            )
                        )
                    )
                )
            ELSE c.DateDif
        END AS Diff
    FROM {CodConsignmentsTable} c
    WHERE c.Cyear = @Year
      AND c.CMonth = @Month
      AND c.Arivl_Dest IN @Stations
      AND (c.Cr_Entry_Date IS NOT NULL OR c.isprepaid = 1)
      AND c.Remarks IS NULL
),
Dvtab(dvcount, cour_id) AS
(
    SELECT COUNT(cd.CN_Number) dvcount, cd.Cour_id
    FROM {CodConsignmentsTable} cd
    WHERE cd.Arivl_Dest IN @Stations
      AND cd.COUR_DATE BETWEEN @FromDate AND @ToDate
      AND cd.Remarks IS NULL
      AND cd.CN_STATUS = 'DV'
    GROUP BY cd.Cour_id
),
TotalTab(tcount, cour_id, RT_Packet) AS
(
    SELECT (cd.AC + cd.PN + cd.RN + cd.NR + cd.IT + cd.DW + cd.DR + cd.DS + cd.DV) tcount,
           cd.CourierID AS Cour_id,
           cd.RT_Packet
    FROM {CodReturnShipmentsTable} cd
    WHERE cd.Arvl_Dest IN @Stations
      AND cd.Month = MONTH(@ToDate)
      AND cd.year = YEAR(@ToDate)
    GROUP BY cd.CourierID
)
SELECT
    InnerTbl.*,
    CASE
        WHEN (SELECT ((SELECT dvcount FROM Dvtab cd WHERE cd.COUR_ID = InnerTbl.Cour_id) / (SELECT (tcount - RT_Packet) FROM TotalTab cd WHERE cd.COUR_ID = InnerTbl.Cour_id)) * 100) >= (IF((@HrCity = '001' OR @HrCity = '002' OR @HrCity = '003' OR @HrCity = '080'), 85, 80))
            THEN (CODAmt / 100) * 0
        ELSE 0
    END AS Bonus,
    CASE
        WHEN (SELECT ((SELECT dvcount FROM Dvtab cd WHERE cd.COUR_ID = InnerTbl.Cour_id) / (SELECT (tcount - RT_Packet) FROM TotalTab cd WHERE cd.COUR_ID = InnerTbl.Cour_id)) * 100) < (IF((@HrCity = '001' OR @HrCity = '002' OR @HrCity = '003' OR @HrCity = '080'), 85, 80))
            THEN (CODAmt / 100) * 0
        ELSE 0
    END AS Deduction
FROM
(
    SELECT xb.Cour_id,
           COUNT(xb.CN_Number) AS CnCount,
           CASE
               WHEN COUNT(xb.CN_Number) <= {cfg.CodBonusSlab1MaxCn} THEN COUNT(xb.CN_Number) * {cfg.CodBonusSlab1Rate}
               WHEN COUNT(xb.CN_Number) > {cfg.CodBonusSlab1MaxCn} AND COUNT(xb.CN_Number) <= {cfg.CodBonusSlab2MaxCn} THEN COUNT(xb.CN_Number) * {cfg.CodBonusSlab2Rate}
               WHEN COUNT(xb.CN_Number) > {cfg.CodBonusSlab2MaxCn} THEN COUNT(xb.CN_Number) * {cfg.CodBonusSlab3Rate}
               ELSE 0
           END AS CODAmt
    FROM xb
    WHERE xb.Diff <= 1
    GROUP BY xb.Cour_id
) innerTbl;";

            List<CodCommissionSummaryRow> summaryRows = (await connection.QueryAsync<CodCommissionSummaryRow>(
                query,
                new
                {
                    ToDate = toDate,
                    FromDate = fromDate,
                    Year = year,
                    Month = month,
                    Stations = stationIds.ToArray(),
                    HrCity = cityCode
                },
                transaction,
                commandTimeout: CodLocalQueryTimeoutSeconds)).ToList();

            await connection.ExecuteAsync(
                $@"DELETE FROM {CodCommissionTable}
                  WHERE City = @CityCode
                    AND Year = @Year
                    AND Month = @Month;",
                new
                {
                    Year = year,
                    Month = month,
                    CityCode = cityCode
                },
                transaction,
                commandTimeout: CodWriteOperationTimeoutSeconds);

            string insertQuery = $@"INSERT INTO {CodCommissionTable} VALUES ";

            if (summaryRows.Count == 0)
            {
                return new CodCommissionRebuildResult();
            }

            int insertedRows = await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                insertQuery,
                summaryRows,
                200,
                (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("Year", year),
                    new KeyValuePair<string, object>("Month", month),
                    new KeyValuePair<string, object>("CourierId", row.Cour_id),
                    new KeyValuePair<string, object>("CityCode", cityCode),
                    new KeyValuePair<string, object>("CnCount", row.CnCount),
                    new KeyValuePair<string, object>("CodAmount", row.CODAmt),
                    new KeyValuePair<string, object>("Bonus", row.Bonus),
                    new KeyValuePair<string, object>("Deduction", row.Deduction),
                    new KeyValuePair<string, object>("CreatedBy", currentUserId),
                    new KeyValuePair<string, object>("CreatedDate", createdDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                });

            await ApplyCodCommissionKpiAdjustmentsAsync(
                connection,
                transaction,
                year,
                month,
                cityCode,
                stationIds);

            CodCommissionAmountTotals amountTotals = await connection.QuerySingleAsync<CodCommissionAmountTotals>(
                $@"SELECT
                      COALESCE(SUM(CODBonus), 0) AS BonusTotal,
                      COALESCE(SUM(CODDeduction), 0) AS DeductionTotal
                  FROM {CodCommissionTable}
                  WHERE Year = @Year
                    AND Month = @Month
                    AND City = @CityCode;",
                new
                {
                    Year = year,
                    Month = month,
                    CityCode = cityCode
                },
                transaction,
                commandTimeout: CodLocalQueryTimeoutSeconds);

            return new CodCommissionRebuildResult
            {
                InsertedRows = insertedRows,
                CodAmountTotal = summaryRows.Sum(static row => row.CODAmt),
                BonusTotal = amountTotals.BonusTotal,
                DeductionTotal = amountTotals.DeductionTotal
            };
        }

        private static async Task ApplyCodCommissionKpiAdjustmentsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            string cityCode,
            IReadOnlyCollection<string> stationIds)
        {
            if (stationIds.Count == 0)
            {
                return;
            }

            CodCommissionHistoricalBonusSource? historicalBonusSource = await ResolveCodCommissionHistoricalBonusSourceAsync(
                connection,
                transaction,
                year,
                month,
                cityCode);

            if (historicalBonusSource != null)
            {
                await ApplyCodCommissionHistoricalBonusSnapshotAsync(
                    connection,
                    transaction,
                    year,
                    month,
                    cityCode,
                    historicalBonusSource.TableName);

                return;
            }

            int[] normalizedStationIds = NormalizeCodCommissionStationIds(stationIds);
            if (normalizedStationIds.Length == 0)
            {
                return;
            }

            CodCommissionKpiScoreSource? baseScoreSource = await ResolveCodCommissionKpiScoreSourceAsync(
                connection,
                transaction,
                year,
                month,
                cityCode,
                normalizedStationIds,
                CodCommissionKpiSourceSelection.Base);

            if (baseScoreSource != null)
            {
                string baseScoreTableName = EscapeMySqlIdentifier(baseScoreSource.TableName);
                string baseUpdateQuery = $@"
WITH xb (Cn_Number, Arivl_Dest, Cour_id, Diff) AS
(
    SELECT
        c.Cn_Number,
        c.Arivl_Dest,
        c.Cour_id,
        CASE
            WHEN c.DateDif > 1 THEN
                IF(
                    (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)) > 0,
                    c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)),
                    IF(
                        (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)) > 0,
                        c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)),
                        IF(
                            (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)) > 0,
                            c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)),
                            IF(
                                (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)) > 0 AND c.cr_mode = 'B',
                                c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)),
                                c.DateDif
                            )
                        )
                    )
                )
            ELSE
                c.DateDif
        END AS Diff
    FROM {CodConsignmentsTable} c
    WHERE c.Cyear = @Year
      AND c.CMonth = @Month
      AND c.Arivl_Dest IN @Stations
      AND (c.Cr_Entry_Date IS NOT NULL OR c.isprepaid = 1)
      AND c.Remarks IS NULL
),
StationCounts(Arivl_Dest, Cour_id, CnCount) AS
(
    SELECT
        xb.Arivl_Dest,
        xb.Cour_id,
        COUNT(xb.CN_Number) AS CnCount
    FROM xb
    WHERE xb.Diff <= 1
    GROUP BY xb.Arivl_Dest, xb.Cour_id
),
KpiRates(Arivl_Dest, Cour_id, Rate) AS
(
    SELECT
        CAST(src.arvl_dest AS CHAR) AS Arivl_Dest,
        LPAD(CAST(src.courier_id AS CHAR), 5, '0') AS Cour_id,
        CAST(src.Overall_Score AS DECIMAL(10, 2)) AS Rate
    FROM lcs_hr.`{baseScoreTableName}` src
    WHERE CAST(src.arvl_dest AS UNSIGNED) IN @NormalizedStations
      AND src.Overall_Score IS NOT NULL
      AND TRIM(LOWER(COALESCE(src.is_eligible, ''))) = 'eligible'
),
CourierKpi(Cour_id, CODBonus, CODDeduction) AS
(
    SELECT
        counts.Cour_id,
        ROUND(SUM(CASE WHEN rates.Rate > 0 THEN counts.CnCount * rates.Rate ELSE 0 END), 2) AS CODBonus,
        ROUND(SUM(CASE WHEN rates.Rate < 0 THEN counts.CnCount * ABS(rates.Rate) ELSE 0 END), 2) AS CODDeduction
    FROM StationCounts counts
    INNER JOIN KpiRates rates
        ON CAST(rates.Arivl_Dest AS UNSIGNED) = CAST(counts.Arivl_Dest AS UNSIGNED)
       AND rates.Cour_id = LPAD(CAST(counts.Cour_id AS CHAR), 5, '0')
    GROUP BY counts.Cour_id
)
UPDATE {CodCommissionTable} target
LEFT JOIN CourierKpi source
    ON source.Cour_id = target.Cour_id
SET target.CODBonus = COALESCE(source.CODBonus, 0),
    target.CODDeduction = COALESCE(source.CODDeduction, 0)
WHERE target.Year = @Year
  AND target.Month = @Month
  AND target.City = @CityCode;";

                await connection.ExecuteAsync(
                    baseUpdateQuery,
                    new
                    {
                        Year = year,
                        Month = month,
                        CityCode = cityCode,
                        Stations = stationIds.ToArray(),
                        NormalizedStations = normalizedStationIds
                    },
                    transaction,
                    commandTimeout: CodWriteOperationTimeoutSeconds);
            }

            CodCommissionKpiScoreSource? bonusRefreshSource = await ResolveCodCommissionKpiScoreSourceAsync(
                connection,
                transaction,
                year,
                month,
                cityCode,
                normalizedStationIds,
                CodCommissionKpiSourceSelection.BonusRefresh);

            if (bonusRefreshSource == null)
            {
                return;
            }

            string bonusScoreTableName = EscapeMySqlIdentifier(bonusRefreshSource.TableName);
            string bonusRefreshQuery = $@"
WITH xb (Cn_Number, Arivl_Dest, Cour_id, Diff) AS
(
    SELECT
        c.Cn_Number,
        c.Arivl_Dest,
        c.Cour_id,
        CASE
            WHEN c.DateDif > 1 THEN
                IF(
                    (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)) > 0,
                    c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)),
                    IF(
                        (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)) > 0,
                        c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)),
                        IF(
                            (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)) > 0,
                            c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)),
                            IF(
                                (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)) > 0 AND c.cr_mode = 'B',
                                c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)),
                                c.DateDif
                            )
                        )
                    )
                )
            ELSE
                c.DateDif
        END AS Diff
    FROM {CodConsignmentsTable} c
    WHERE c.Cyear = @Year
      AND c.CMonth = @Month
      AND c.Arivl_Dest IN @Stations
      AND (c.Cr_Entry_Date IS NOT NULL OR c.isprepaid = 1)
      AND c.Remarks IS NULL
),
StationCounts(Arivl_Dest, Cour_id, CnCount) AS
(
    SELECT
        xb.Arivl_Dest,
        xb.Cour_id,
        COUNT(xb.CN_Number) AS CnCount
    FROM xb
    WHERE xb.Diff <= 1
    GROUP BY xb.Arivl_Dest, xb.Cour_id
),
BonusOnlyKpi(Cour_id, CODBonus) AS
(
    SELECT
        counts.Cour_id,
        ROUND(SUM(counts.CnCount * CAST(src.Overall_Score AS DECIMAL(10, 2))), 2) AS CODBonus
    FROM StationCounts counts
    INNER JOIN lcs_hr.`{bonusScoreTableName}` src
        ON CAST(src.arvl_dest AS UNSIGNED) = CAST(counts.Arivl_Dest AS UNSIGNED)
       AND LPAD(CAST(src.courier_id AS CHAR), 5, '0') = LPAD(CAST(counts.Cour_id AS CHAR), 5, '0')
    WHERE CAST(src.arvl_dest AS UNSIGNED) IN @NormalizedStations
      AND src.Overall_Score IS NOT NULL
      AND CAST(src.Overall_Score AS DECIMAL(10, 2)) > 0
      AND TRIM(LOWER(COALESCE(src.is_eligible, ''))) = 'eligible'
    GROUP BY counts.Cour_id
)
UPDATE {CodCommissionTable} target
INNER JOIN BonusOnlyKpi source
    ON source.Cour_id = target.Cour_id
SET target.CODBonus = source.CODBonus
WHERE target.Year = @Year
  AND target.Month = @Month
  AND target.City = @CityCode;";

            await connection.ExecuteAsync(
                bonusRefreshQuery,
                new
                {
                    Year = year,
                    Month = month,
                    CityCode = cityCode,
                    Stations = stationIds.ToArray(),
                    NormalizedStations = normalizedStationIds
                },
                transaction,
                commandTimeout: CodLocalQueryTimeoutSeconds);
        }

        private static async Task ApplyCodCommissionHistoricalBonusSnapshotAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            string cityCode,
            string tableName)
        {
            string historicalTableName = EscapeMySqlIdentifier(tableName);
            string historicalUpdateQuery = $@"
UPDATE {CodCommissionTable} target
LEFT JOIN (
    SELECT
        LPAD(CAST(src.Cour_id AS CHAR), 5, '0') AS Cour_id,
        CAST(COALESCE(src.CODBonus, 0) AS DECIMAL(12, 2)) AS CODBonus,
        CAST(COALESCE(src.CODDeduction, 0) AS DECIMAL(12, 2)) AS CODDeduction
    FROM lcs_hr.`{historicalTableName}` src
    WHERE src.year = @Year
      AND src.month = @Month
      AND TRIM(COALESCE(src.City, '')) = @CityCode
) source
    ON source.Cour_id = target.Cour_id
SET target.CODBonus = COALESCE(source.CODBonus, 0),
    target.CODDeduction = COALESCE(source.CODDeduction, 0)
WHERE target.Year = @Year
  AND target.Month = @Month
  AND target.City = @CityCode;";

            await connection.ExecuteAsync(
                historicalUpdateQuery,
                new
                {
                    Year = year,
                    Month = month,
                    CityCode = cityCode
                },
                transaction,
                commandTimeout: CodWriteOperationTimeoutSeconds);
        }

        private static async Task<CodCommissionHistoricalBonusSource?> ResolveCodCommissionHistoricalBonusSourceAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            string cityCode)
        {
            List<CodCommissionHistoricalBonusSource> candidates = (await connection.QueryAsync<CodCommissionHistoricalBonusSource>(
                @"SELECT
                      t.table_name AS TableName,
                      t.create_time AS CreateTime
                  FROM information_schema.tables t
                  INNER JOIN (
                      SELECT table_name
                      FROM information_schema.columns
                      WHERE table_schema = 'lcs_hr'
                        AND table_name LIKE 'hr_codcommission_cod_bonus%'
                        AND LOWER(column_name) IN ('year', 'month', 'cour_id', 'city', 'codbonus', 'coddeduction')
                      GROUP BY table_name
                      HAVING COUNT(DISTINCT LOWER(column_name)) = 6
                  ) cols
                      ON cols.table_name = t.table_name
                  WHERE t.table_schema = 'lcs_hr'
                    AND t.table_name LIKE 'hr_codcommission_cod_bonus%'
                  ORDER BY t.create_time DESC, t.table_name DESC;",
                transaction: transaction,
                commandTimeout: CodLocalQueryTimeoutSeconds)).ToList();

            CodCommissionHistoricalBonusSource? bestSource = null;

            foreach (CodCommissionHistoricalBonusSource candidate in candidates)
            {
                string historicalTableName = EscapeMySqlIdentifier(candidate.TableName);
                CodCommissionHistoricalBonusSource metrics = await connection.QuerySingleAsync<CodCommissionHistoricalBonusSource>(
                    $@"SELECT
                           COUNT(*) AS RowCount,
                           COUNT(DISTINCT LPAD(CAST(src.Cour_id AS CHAR), 5, '0')) AS MatchedCouriers,
                           COUNT(DISTINCT CASE
                               WHEN COALESCE(src.CODBonus, 0) <> 0
                                 OR COALESCE(src.CODDeduction, 0) <> 0
                               THEN LPAD(CAST(src.Cour_id AS CHAR), 5, '0')
                           END) AS AdjustedCouriers,
                           COALESCE(ROUND(SUM(ABS(COALESCE(src.CODBonus, 0)) + ABS(COALESCE(src.CODDeduction, 0))), 2), 0) AS TotalAdjustment
                       FROM lcs_hr.`{historicalTableName}` src
                       WHERE src.year = @Year
                         AND src.month = @Month
                         AND TRIM(COALESCE(src.City, '')) = @CityCode;",
                    new
                    {
                        Year = year,
                        Month = month,
                        CityCode = cityCode
                    },
                    transaction,
                    commandTimeout: CodLocalQueryTimeoutSeconds);

                candidate.RowCount = metrics.RowCount;
                candidate.MatchedCouriers = metrics.MatchedCouriers;
                candidate.AdjustedCouriers = metrics.AdjustedCouriers;
                candidate.TotalAdjustment = metrics.TotalAdjustment;

                if (candidate.RowCount == 0)
                {
                    continue;
                }

                if (bestSource == null || ShouldPreferCodCommissionHistoricalBonusSource(candidate, bestSource))
                {
                    bestSource = candidate;
                }
            }

            return bestSource;
        }

        private static bool ShouldPreferCodCommissionHistoricalBonusSource(
            CodCommissionHistoricalBonusSource candidate,
            CodCommissionHistoricalBonusSource currentBest)
        {
            return candidate.AdjustedCouriers > currentBest.AdjustedCouriers
                || (candidate.AdjustedCouriers == currentBest.AdjustedCouriers && candidate.TotalAdjustment > currentBest.TotalAdjustment)
                || (candidate.AdjustedCouriers == currentBest.AdjustedCouriers && candidate.TotalAdjustment == currentBest.TotalAdjustment && candidate.MatchedCouriers > currentBest.MatchedCouriers)
                || (candidate.AdjustedCouriers == currentBest.AdjustedCouriers && candidate.TotalAdjustment == currentBest.TotalAdjustment && candidate.MatchedCouriers == currentBest.MatchedCouriers && (candidate.CreateTime ?? DateTime.MinValue) > (currentBest.CreateTime ?? DateTime.MinValue));
        }

        private static async Task<CodCommissionKpiScoreSource?> ResolveCodCommissionKpiScoreSourceAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            int year,
            int month,
            string cityCode,
            IReadOnlyCollection<int> normalizedStationIds,
            CodCommissionKpiSourceSelection selection)
        {
            List<CodCommissionKpiScoreSource> candidates = (await connection.QueryAsync<CodCommissionKpiScoreSource>(
                @"SELECT
                      table_name AS TableName,
                      create_time AS CreateTime
                  FROM information_schema.tables
                  WHERE table_schema = 'lcs_hr'
                    AND table_name LIKE 'first_attempt_incentivce_validate%'
                  ORDER BY create_time DESC, table_name DESC;",
                transaction: transaction,
                commandTimeout: CodLocalQueryTimeoutSeconds)).ToList();

            CodCommissionKpiScoreSource? bestSource = null;

            foreach (CodCommissionKpiScoreSource candidate in candidates)
            {
                string scoreTableName = EscapeMySqlIdentifier(candidate.TableName);
                string metricsQuery = $@"
WITH xb (Cn_Number, Arivl_Dest, Cour_id, Diff) AS
(
    SELECT
        c.Cn_Number,
        c.Arivl_Dest,
        c.Cour_id,
        CASE
            WHEN c.DateDif > 1 THEN
                IF(
                    (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)) > 0,
                    c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 1)),
                    IF(
                        (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)) > 0,
                        c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 0, 2)),
                        IF(
                            (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)) > 0,
                            c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 6, 0)),
                            IF(
                                (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)) > 0 AND c.cr_mode = 'B',
                                c.DateDif - (SELECT lcs_hr.count_Holiday(DATE(IF(c.IsPrepaid = 1, c.Cour_date, c.Delivery_date)), DATE_SUB(DATE(IF(c.IsPrepaid = 1, c.Delivery_date, c.Cr_Entry_Date)), INTERVAL 1 DAY), 5, 0)),
                                c.DateDif
                            )
                        )
                    )
                )
            ELSE
                c.DateDif
        END AS Diff
    FROM {CodConsignmentsTable} c
    WHERE c.Cyear = @Year
      AND c.CMonth = @Month
      AND c.Arivl_Dest IN @Stations
      AND (c.Cr_Entry_Date IS NOT NULL OR c.isprepaid = 1)
      AND c.Remarks IS NULL
),
StationCounts(Arivl_Dest, Cour_id, CnCount) AS
(
    SELECT
        xb.Arivl_Dest,
        xb.Cour_id,
        COUNT(xb.CN_Number) AS CnCount
    FROM xb
    WHERE xb.Diff <= 1
    GROUP BY xb.Arivl_Dest, xb.Cour_id
)
SELECT
    COUNT(DISTINCT LPAD(CAST(counts.Cour_id AS CHAR), 5, '0')) AS MatchedCouriers,
    COUNT(DISTINCT CASE WHEN CAST(src.Overall_Score AS DECIMAL(10, 2)) > 0 THEN LPAD(CAST(counts.Cour_id AS CHAR), 5, '0') END) AS PositiveMatchedCouriers,
    COUNT(DISTINCT CASE WHEN CAST(src.Overall_Score AS DECIMAL(10, 2)) < 0 THEN LPAD(CAST(counts.Cour_id AS CHAR), 5, '0') END) AS NegativeMatchedCouriers,
    COALESCE(ROUND(SUM(CASE WHEN CAST(src.Overall_Score AS DECIMAL(10, 2)) > 0 THEN counts.CnCount * CAST(src.Overall_Score AS DECIMAL(10, 2)) ELSE 0 END), 2), 0) AS BonusTotal,
    COALESCE(ROUND(SUM(CASE WHEN CAST(src.Overall_Score AS DECIMAL(10, 2)) < 0 THEN counts.CnCount * ABS(CAST(src.Overall_Score AS DECIMAL(10, 2))) ELSE 0 END), 2), 0) AS DeductionTotal
FROM StationCounts counts
INNER JOIN lcs_hr.`{scoreTableName}` src
    ON CAST(src.arvl_dest AS UNSIGNED) = CAST(counts.Arivl_Dest AS UNSIGNED)
   AND LPAD(CAST(src.courier_id AS CHAR), 5, '0') = LPAD(CAST(counts.Cour_id AS CHAR), 5, '0')
WHERE CAST(src.arvl_dest AS UNSIGNED) IN @NormalizedStations
  AND src.Overall_Score IS NOT NULL
  AND TRIM(LOWER(COALESCE(src.is_eligible, ''))) = 'eligible';";

                CodCommissionKpiScoreSource metrics = await connection.QuerySingleAsync<CodCommissionKpiScoreSource>(
                    metricsQuery,
                    new
                    {
                        Year = year,
                        Month = month,
                        CityCode = cityCode,
                        Stations = normalizedStationIds.Select(id => id.ToString("D5", CultureInfo.InvariantCulture)).ToArray(),
                        NormalizedStations = normalizedStationIds.ToArray()
                    },
                    transaction,
                    commandTimeout: CodLocalQueryTimeoutSeconds);

                candidate.MatchedCouriers = metrics.MatchedCouriers;
                candidate.PositiveMatchedCouriers = metrics.PositiveMatchedCouriers;
                candidate.NegativeMatchedCouriers = metrics.NegativeMatchedCouriers;
                candidate.BonusTotal = metrics.BonusTotal;
                candidate.DeductionTotal = metrics.DeductionTotal;

                if (bestSource == null ||
                    ShouldPreferCodCommissionKpiSource(candidate, bestSource, selection))
                {
                    bestSource = candidate;
                }
            }

            return bestSource;
        }

        private static bool ShouldPreferCodCommissionKpiSource(
            CodCommissionKpiScoreSource candidate,
            CodCommissionKpiScoreSource currentBest,
            CodCommissionKpiSourceSelection selection)
        {
            if (selection == CodCommissionKpiSourceSelection.Base)
            {
                return candidate.NegativeMatchedCouriers > currentBest.NegativeMatchedCouriers
                    || (candidate.NegativeMatchedCouriers == currentBest.NegativeMatchedCouriers && candidate.DeductionTotal > currentBest.DeductionTotal)
                    || (candidate.NegativeMatchedCouriers == currentBest.NegativeMatchedCouriers && candidate.DeductionTotal == currentBest.DeductionTotal && candidate.MatchedCouriers > currentBest.MatchedCouriers)
                    || (candidate.NegativeMatchedCouriers == currentBest.NegativeMatchedCouriers && candidate.DeductionTotal == currentBest.DeductionTotal && candidate.MatchedCouriers == currentBest.MatchedCouriers && (candidate.CreateTime ?? DateTime.MaxValue) < (currentBest.CreateTime ?? DateTime.MaxValue));
            }

            return candidate.PositiveMatchedCouriers > currentBest.PositiveMatchedCouriers
                || (candidate.PositiveMatchedCouriers == currentBest.PositiveMatchedCouriers && (candidate.CreateTime ?? DateTime.MinValue) > (currentBest.CreateTime ?? DateTime.MinValue))
                || (candidate.PositiveMatchedCouriers == currentBest.PositiveMatchedCouriers && (candidate.CreateTime ?? DateTime.MinValue) == (currentBest.CreateTime ?? DateTime.MinValue) && candidate.BonusTotal > currentBest.BonusTotal)
                || (candidate.PositiveMatchedCouriers == currentBest.PositiveMatchedCouriers && (candidate.CreateTime ?? DateTime.MinValue) == (currentBest.CreateTime ?? DateTime.MinValue) && candidate.BonusTotal == currentBest.BonusTotal && candidate.MatchedCouriers > currentBest.MatchedCouriers);
        }

        private static int[] NormalizeCodCommissionStationIds(IEnumerable<string> stationIds)
        {
            return stationIds
                .Select(id => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stationId) ? stationId : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
        }

        private static string EscapeMySqlIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException("Invalid SQL identifier.");
            }

            foreach (char character in identifier)
            {
                if (!(char.IsLetterOrDigit(character) || character == '_'))
                {
                    throw new ArgumentException("Invalid SQL identifier.");
                }
            }

            return identifier;
        }

        private static async Task<CodCommissionPreviewBaseline> CaptureCodCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            IReadOnlyCollection<string> stationIds)
        {
            return await connection.QuerySingleAsync<CodCommissionPreviewBaseline>(
                $@"SELECT
                      (
                          SELECT COUNT(*)
                          FROM {CodCommissionTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND City = @CityCode
                      ) AS CodCommissionRows,
                      (
                          SELECT COUNT(*)
                          FROM {CodReturnShipmentsTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND Arvl_Dest IN @Stations
                      ) AS ReturnShipmentRows,
                      (
                          SELECT COUNT(*)
                          FROM {CodConsignmentsTable}
                          WHERE Cyear = @Year
                            AND CMonth = @Month
                            AND Arivl_Dest IN @Stations
                      ) AS ConsignmentRows,
                      (
                          SELECT COUNT(*)
                          FROM {AllCodConsignmentTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND Arivl_Dest IN @Stations
                      ) AS ActivityRows",
                new
                {
                    Year = year,
                    Month = month,
                    CityCode = cityCode,
                    Stations = stationIds.ToArray()
                });
        }

        private static async Task<bool> VerifyCodCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            IReadOnlyCollection<string> stationIds,
            CodCommissionPreviewBaseline baseline)
        {
            CodCommissionPreviewBaseline current = await CaptureCodCommissionPreviewBaselineAsync(
                connection,
                year,
                month,
                cityCode,
                stationIds);

            return baseline.CodCommissionRows == current.CodCommissionRows
                && baseline.ReturnShipmentRows == current.ReturnShipmentRows
                && baseline.ConsignmentRows == current.ConsignmentRows
                && baseline.ActivityRows == current.ActivityRows;
        }

        private sealed class CodCommissionStageResult
        {
            public int ConsignmentRowsInserted { get; set; }
            public int ActivityRowsInserted { get; set; }
            public int ReturnShipmentRowsInserted { get; set; }
            public int ExistingConsignmentsSkipped { get; set; }
            public int DuplicateStatusRows { get; set; }
            public int RemarksUpdatedRows { get; set; }
            public int TotalStageRowsAffected { get; set; }
        }

        private sealed class CodCommissionExecutionContext
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public string CityCode { get; set; } = string.Empty;
            public DateTime FromDate { get; set; }
            public DateTime ToDate { get; set; }
            public List<string> StationIds { get; set; } = new();
            public CodCommissionSourceLoadResult SourceLoad { get; set; } = new();
            public List<CodAllCnRecord> AllCnRows { get; set; } = new();
            public CommissionConfig Cfg { get; set; } = new CommissionConfig();
        }

        private sealed class CodCommissionExecutionResult
        {
            public int ConsignmentRowsInserted { get; set; }
            public int ActivityRowsInserted { get; set; }
            public int ReturnShipmentRowsInserted { get; set; }
            public int CommissionRowsInserted { get; set; }
            public decimal GeneratedCodAmountTotal { get; set; }
            public decimal GeneratedBonusTotal { get; set; }
            public decimal GeneratedDeductionTotal { get; set; }
            public int ExistingConsignmentsSkipped { get; set; }
            public int DuplicateStatusRows { get; set; }
            public int RemarksUpdatedRows { get; set; }
            public int TotalStageRowsAffected { get; set; }
        }

        private sealed class CodCommissionRebuildResult
        {
            public int InsertedRows { get; set; }
            public decimal CodAmountTotal { get; set; }
            public decimal BonusTotal { get; set; }
            public decimal DeductionTotal { get; set; }
        }

        private sealed class CodCommissionAmountTotals
        {
            public decimal BonusTotal { get; set; }
            public decimal DeductionTotal { get; set; }
        }

        private sealed class CodCommissionKpiScoreSource
        {
            public string TableName { get; set; } = string.Empty;
            public DateTime? CreateTime { get; set; }
            public int MatchedCouriers { get; set; }
            public int PositiveMatchedCouriers { get; set; }
            public int NegativeMatchedCouriers { get; set; }
            public decimal BonusTotal { get; set; }
            public decimal DeductionTotal { get; set; }
        }

        private sealed class CodCommissionHistoricalBonusSource
        {
            public string TableName { get; set; } = string.Empty;
            public DateTime? CreateTime { get; set; }
            public int RowCount { get; set; }
            public int MatchedCouriers { get; set; }
            public int AdjustedCouriers { get; set; }
            public decimal TotalAdjustment { get; set; }
        }

        private enum CodCommissionKpiSourceSelection
        {
            Base,
            BonusRefresh
        }

        private sealed class CodCommissionSourceLoadResult
        {
            public List<CodCommissionSourceRow> Rows { get; set; } = new();
            public int BackfilledDepositDates { get; set; }
        }

        private sealed class CodCommissionPreviewBaseline
        {
            public int CodCommissionRows { get; set; }
            public int ReturnShipmentRows { get; set; }
            public int ConsignmentRows { get; set; }
            public int ActivityRows { get; set; }
        }

        private sealed class CodCommissionSourceRow
        {
            public string CN_NUMBER { get; set; } = string.Empty;
            public string COURIER_ID { get; set; } = string.Empty;
            public string ARVL_DEST { get; set; } = string.Empty;
            public DateTime COUR_DATE { get; set; }
            public DateTime DELIVERY_DATE { get; set; }
            public string DELIVERY_TIME { get; set; } = string.Empty;
            public int PCS { get; set; }
            public decimal WEIGHT { get; set; }
            public string REASON { get; set; } = string.Empty;
            public string RECEIVER_NAME { get; set; } = string.Empty;
            public string BH_REMARKS { get; set; } = string.Empty;
            public string CN_TYPE { get; set; } = string.Empty;
            public string STATUS { get; set; } = string.Empty;
            public decimal cr_Amt { get; set; }
            public DateTime? cr_entry_date { get; set; }
            public string cr_depositslip { get; set; } = string.Empty;
            public string cr_Mode { get; set; } = string.Empty;
            public int DateDif { get; set; }
            public string IsPrepaid1 { get; set; } = string.Empty;
            public string IsPrepaid2 { get; set; } = string.Empty;
        }

        private sealed class CodReturnShipmentRecord
        {
            public string Arvl_Dest { get; set; } = string.Empty;
            public string courier_id { get; set; } = string.Empty;
            public int AC { get; set; }
            public int PN { get; set; }
            public int RN { get; set; }
            public int NR { get; set; }
            public int IT { get; set; }
            public int DW { get; set; }
            public int DR { get; set; }
            public int DS { get; set; }
            public int DV { get; set; }
        }

        private sealed class CodAllCnRecord
        {
            public string CN_Number { get; set; } = string.Empty;
            public string Arivl_Dest { get; set; } = string.Empty;
            public string Cour_id { get; set; } = string.Empty;
            public string Cour_Date { get; set; } = string.Empty;
            public string Cour_Time { get; set; } = string.Empty;
            public string Activity_Date { get; set; } = string.Empty;
            public string STATUS { get; set; } = string.Empty;
            public string Company_Id { get; set; } = string.Empty;
            public string Order_Id { get; set; } = string.Empty;
            public string Client_Id { get; set; } = string.Empty;
        }

        private sealed class CodCashDepositDateRecord
        {
            public string cn_short { get; set; } = string.Empty;
            public string deposit_slip_no { get; set; } = string.Empty;
            public decimal amount { get; set; }
            public string date_credit { get; set; } = string.Empty;
            public string payment_mode { get; set; } = string.Empty;
        }

        private sealed class CodCommissionSummaryRow
        {
            public string Cour_id { get; set; } = string.Empty;
            public int CnCount { get; set; }
            public decimal CODAmt { get; set; }
            public decimal Bonus { get; set; }
            public decimal Deduction { get; set; }
        }
    }
}
