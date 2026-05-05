using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using LCS_HR_MVC.Data;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        public async Task<ReturnCodCommissionViewModel> GetReturnCodCommissionPageAsync(
            DateTime workingDate,
            string currentUserId,
            ReturnCodCommissionViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var model = existingModel ?? new ReturnCodCommissionViewModel();
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
            model.Cities = await BuildUserCitySelectItemsAsync(connection, currentUserId, model.ZoneId, "Please Select", "00", includeAllCity: false);

            return model;
        }

        public async Task<ReturnCodCommissionProcessResult> ProcessReturnCodCommissionAsync(
            ReturnCodCommissionViewModel model,
            string currentUserId,
            Func<int, int, Task>? onProgress = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();
            var rcConnId = await LogConnectionIdAsync(connection, "ReturnCodCommission", model.CityCode, "ProcessReturnCodCommissionAsync_Start");
            var rcOverallStart = System.Diagnostics.Stopwatch.StartNew();

            await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

            var rcPrepStart = System.Diagnostics.Stopwatch.StartNew();
            ReturnCodCommissionExecutionContext context = await PrepareReturnCodCommissionExecutionContextAsync(
                connection,
                model.Year,
                model.Month,
                model.CityCode,
                currentUserId);
            rcPrepStart.Stop();
            LogOperationComplete("ReturnCodCommission", model.CityCode, "PrepareContext", rcConnId, rcPrepStart.Elapsed, context.SourceRows.Count);

            if (onProgress != null) await onProgress(0, context.SourceRows.Count); // source loaded — total now known

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var rcExecStart = System.Diagnostics.Stopwatch.StartNew();
                ReturnCodCommissionProcessResult result = await ExecuteReturnCodCommissionAsync(
                    connection,
                    transaction as MySqlTransaction,
                    context,
                    currentUserId,
                    model.BillingStatus,
                    onProgress,
                    context.SourceRows.Count);

                await transaction.CommitAsync();
                rcExecStart.Stop();
                LogOperationComplete("ReturnCodCommission", model.CityCode, "Execute+Commit", rcConnId, rcExecStart.Elapsed,
                    result.ConsignmentRowsInserted + result.CommissionRowsInserted + result.ProcessRowsInserted);
                rcOverallStart.Stop();
                _logger?.LogInformation(
                    "[DbDiag] ReturnCodCommission City={CityCode} TOTAL duration={TotalSec:F2}s ConnId={ConnId}",
                    model.CityCode, rcOverallStart.Elapsed.TotalSeconds, rcConnId);

                if (onProgress != null) await onProgress(
                    result.ConsignmentRowsInserted + result.CommissionRowsInserted + result.ProcessRowsInserted,
                    context.SourceRows.Count); // all rows committed

                return result;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<ReturnCodCommissionPreviewResult> PreviewReturnCodCommissionAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId,
            bool billingStatus = false)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();
            await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

            ReturnCodCommissionExecutionContext context = await PrepareReturnCodCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId);

            ReturnCodCommissionPreviewBaseline baseline = await CaptureReturnCodCommissionPreviewBaselineAsync(
                connection,
                context.Year,
                context.Month,
                context.StationIds,
                context.LocationIds,
                context.Cfg);

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                ReturnCodCommissionProcessResult result = await ExecuteReturnCodCommissionAsync(
                    connection,
                    transaction as MySqlTransaction,
                    context,
                    currentUserId,
                    billingStatus);

                await transaction.RollbackAsync();

                return new ReturnCodCommissionPreviewResult
                {
                    Year = context.Year,
                    Month = context.Month,
                    CityCode = context.CityCode,
                    StationCount = context.StationIds.Count,
                    LocationCount = context.LocationIds.Count,
                    FromDate = context.FromDate,
                    ToDate = context.ToDate,
                    SourceRowsRetrieved = result.SourceRowsRetrieved,
                    GroupedRowsGenerated = result.GroupedRowsGenerated,
                    ConsignmentRowsInserted = result.ConsignmentRowsInserted,
                    CommissionRowsInserted = result.CommissionRowsInserted,
                    ProcessRowsInserted = result.ProcessRowsInserted,
                    GeneratedProcessAmountTotal = result.GeneratedProcessAmountTotal,
                    RollbackIntegrityPreserved = await VerifyReturnCodCommissionPreviewBaselineAsync(
                        connection,
                        context.Year,
                        context.Month,
                        context.StationIds,
                        context.LocationIds,
                        baseline,
                        context.Cfg)
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task<ReturnCodCommissionProcessResult> ExecuteReturnCodCommissionAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            ReturnCodCommissionExecutionContext context,
            string currentUserId,
            bool billingStatus,
            Func<int, int, Task>? onProgress = null,
            int progressTotal = 0)
        {
            int consignmentRowsInserted = await SaveReturnCodConsignmentRowsAsync(
                connection,
                transaction,
                context.SourceRows,
                context.Year,
                context.Month,
                currentUserId,
                context.StationIds,
                onProgress,
                progressTotal);

            int commissionRowsInserted = await SaveReturnCodCommissionRowsAsync(
                connection,
                transaction,
                context.GroupedRows,
                context.Year,
                context.Month,
                currentUserId,
                context.StationIds);

            ReturnCodProcessWriteResult processWrite = await SaveReturnCodProcessRowsAsync(
                connection,
                transaction,
                context,
                currentUserId);

            await connection.ExecuteAsync(
                $@"INSERT INTO {AcTestTableNames.T_Acknowledgment}
                  (ScreenID, UserID, CreatedDate, IsBillingConfirm, IsAttendanceProcessed, AllCommProcessed, OneTimeActivity)
                  VALUES(2, @UserId, NOW(), @BillingStatus, NULL, NULL, NULL);",
                new
                {
                    UserId = currentUserId,
                    BillingStatus = billingStatus ? 1 : 0
                },
                transaction,
                commandTimeout: 60);

            return new ReturnCodCommissionProcessResult
            {
                Success = true,
                Message = "COD Return Process Execute Successfully!",
                ConsignmentRowsInserted = consignmentRowsInserted,
                CommissionRowsInserted = commissionRowsInserted,
                ProcessRowsInserted = processWrite.InsertedRows,
                StationCount = context.StationIds.Count,
                LocationCount = context.LocationIds.Count,
                FromDate = context.FromDate,
                ToDate = context.ToDate,
                SourceRowsRetrieved = context.SourceRows.Count,
                GroupedRowsGenerated = context.GroupedRows.Count,
                GeneratedProcessAmountTotal = processWrite.GeneratedProcessAmountTotal
            };
        }

        private static async Task<ReturnCodCommissionPreviewBaseline> CaptureReturnCodCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            int year,
            int month,
            IReadOnlyCollection<string> stationIds,
            IReadOnlyCollection<int> locationIds,
            CommissionConfig cfg)
        {
            return await connection.QuerySingleAsync<ReturnCodCommissionPreviewBaseline>(
                $@"SELECT
                      (
                          SELECT COUNT(*)
                          FROM {CodReturnConsignmentsTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND Station_id IN @StationIds
                      ) AS ConsignmentRows,
                      (
                          SELECT COUNT(*)
                          FROM {CodReturnCommissionTable}
                          WHERE ComYear = @Year
                            AND ComMonth = @Month
                            AND StationId IN @StationIds
                      ) AS CommissionRows,
                      (
                          SELECT COUNT(*)
                          FROM {CodReturnCommissionProcessTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND GlLocationId IN @LocationIds
                            AND RateId IN ({cfg.ReturnCodPolicyRateIdsCsv})
                      ) AS ProcessRows,
                      (
                          SELECT IFNULL(SUM(OleCommission), 0)
                          FROM {CodReturnCommissionProcessTable}
                          WHERE Year = @Year
                            AND Month = @Month
                            AND GlLocationId IN @LocationIds
                            AND RateId IN ({cfg.ReturnCodPolicyRateIdsCsv})
                      ) AS ProcessAmountTotal;",
                new
                {
                    Year = year,
                    Month = month,
                    StationIds = stationIds.ToArray(),
                    LocationIds = locationIds.ToArray()
                },
                commandTimeout: 120);
        }

        private static async Task<bool> VerifyReturnCodCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            int year,
            int month,
            IReadOnlyCollection<string> stationIds,
            IReadOnlyCollection<int> locationIds,
            ReturnCodCommissionPreviewBaseline baseline,
            CommissionConfig cfg)
        {
            ReturnCodCommissionPreviewBaseline current = await CaptureReturnCodCommissionPreviewBaselineAsync(
                connection,
                year,
                month,
                stationIds,
                locationIds,
                cfg);

            return baseline.ConsignmentRows == current.ConsignmentRows
                && baseline.CommissionRows == current.CommissionRows
                && baseline.ProcessRows == current.ProcessRows
                && baseline.ProcessAmountTotal == current.ProcessAmountTotal;
        }

        private async Task<ReturnCodCommissionExecutionContext> PrepareReturnCodCommissionExecutionContextAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            string currentUserId)
        {
            if (year <= 0 || month <= 0)
            {
                throw new ArgumentException("Year and month are required.");
            }

            string normalizedCityCode = cityCode?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedCityCode) || normalizedCityCode == "00")
            {
                throw new ArgumentException("City is required.");
            }

            var cfg = await CommissionConfig.LoadAsync(connection);
            DateTime fromDate = new DateTime(year, month, cfg.CommissionEndDay);
            DateTime toDate = new DateTime(year, month, cfg.CommissionStartDay).AddMonths(-1);
            if (new DateTime(year, month, cfg.CommissionEndDay) > DateTime.Now)
            {
                throw new ArgumentException("Process Can not run on current working Month");
            }

            await EnsureUserCityAllowedAsync(connection, currentUserId, normalizedCityCode);
            await EnsureReturnCodProcessesOpenAsync(connection, year, month, normalizedCityCode);

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
                new { CityCode = normalizedCityCode },
                commandTimeout: 30)).ToList();

            if (stationIds.Count == 0)
            {
                throw new ArgumentException("Station ID is not define for the selected city");
            }

            var info = await connection.QueryFirstOrDefaultAsync(
                $@"SELECT t.CreatedDate AS ProcessedDate, t.CreatedBy, u.UserName
                  FROM {CodReturnCommissionTable} t
                  LEFT JOIN lcs_users u ON u.userID = t.CreatedBy
                  WHERE t.ComYear = @Year AND t.ComMonth = @Month AND t.StationId IN @Stations
                  ORDER BY t.CreatedDate DESC LIMIT 1",
                new { Year = year, Month = month, Stations = stationIds.ToArray() },
                commandTimeout: 30);
            if (info?.ProcessedDate != null)
            {
                throw new ArgumentException($"Already Processed on {((DateTime)info.ProcessedDate):dd-MMM-yyyy}.{BuildProcessedByInfo(info.CreatedBy?.ToString(), info.UserName?.ToString())}");
            }

            List<int> locationIds = (await connection.QueryAsync<int>(
                @"SELECT DISTINCT GlLocationId
                  FROM lcs_hr.hr_locationmapping
                  WHERE BStationId IN @Stations;",
                new { Stations = stationIds.ToArray() },
                commandTimeout: 30)).ToList();

            if (locationIds.Count == 0)
            {
                throw new ArgumentException("Location ID is not define for the selected city");
            }

            List<ReturnCodCommissionPolicyRow> commissionPolicies = (await connection.QueryAsync<ReturnCodCommissionPolicyRow>(
                @"SELECT RateID, Type, RateType, Rate
                  FROM hr_commissionpolicy;",
                commandTimeout: 30)).ToList();

            if (commissionPolicies.Count == 0)
            {
                throw new ArgumentException("Commission Rates not defined.");
            }

            using var operationsConnection = await OpenExternalCodConnectionAsync("Central_OPS");
            await LogConnectionIdAsync(operationsConnection, "ReturnCodCommission", normalizedCityCode, "LoadReturnCodSource_Central_OPS");
            var rcSourceStart = System.Diagnostics.Stopwatch.StartNew();
            List<ReturnCodDeliveryCommissionRow> sourceRows = await LoadReturnCodSourceRowsAsync(
                operationsConnection,
                toDate,
                fromDate,
                stationIds,
                cfg);
            rcSourceStart.Stop();
            _logger?.LogInformation(
                "[DbDiag] ReturnCodCommission City={CityCode} SourceSELECT duration={DurationSec:F2}s rows={Rows}",
                normalizedCityCode, rcSourceStart.Elapsed.TotalSeconds, sourceRows.Count);

            List<ReturnCodGroupedCommissionRow> groupedRows = sourceRows
                .Where(static row => !string.IsNullOrWhiteSpace(row.CN_NUMBER))
                .GroupBy(static row => new { row.Station_id, row.COURIER_ID, row.RateID })
                .Select(static group =>
                {
                    ReturnCodDeliveryCommissionRow first = group.First();
                    return new ReturnCodGroupedCommissionRow
                    {
                        StationId = group.Key.Station_id,
                        CourierId = group.Key.COURIER_ID,
                        CourierName = first.Cour_Name,
                        NoOfShipment = group.Count(),
                        NoOfPieces = group.Count(),
                        TotalCommission = group.Sum(item => item.TotalCommission),
                        RateId = group.Key.RateID
                    };
                })
                .ToList();

            return new ReturnCodCommissionExecutionContext
            {
                Year = year,
                Month = month,
                CityCode = normalizedCityCode,
                FromDate = toDate,
                ToDate = fromDate,
                StationIds = stationIds,
                LocationIds = locationIds,
                CommissionPolicies = commissionPolicies,
                SourceRows = sourceRows,
                GroupedRows = groupedRows,
                Cfg = cfg
            };
        }

        private static async Task EnsureUserCityAllowedAsync(
            MySqlConnection connection,
            string currentUserId,
            string cityCode)
        {
            int isAllowed = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*)
                  FROM lcs_user_location
                  WHERE userid = @UserId
                    AND city_code = @CityCode;",
                new
                {
                    UserId = currentUserId,
                    CityCode = cityCode
                });

            if (isAllowed == 0)
            {
                throw new ArgumentException("You are not allowed to process return COD commission for the selected city.");
            }
        }

        private static async Task EnsureReturnCodProcessesOpenAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode)
        {
            // In test mode: skip both PreviousClosed and CurrentClosed guards.
            // Previous month may not be closed yet in test scenarios;
            // current month may already be closed in production.
            if (AcTestTableNames.IsTestMode) return;

            DateTime currentMonth = new DateTime(year, month, 1);

            var state = await connection.QuerySingleAsync<ReturnCodProcessState>(
                @"SELECT
                      IF(EXISTS(
                          SELECT 1
                          FROM hr_closeprocesses clp
                          WHERE clp.City = @City
                            AND clp.Year = @Year
                            AND clp.Month = @Month
                      ), 1, 0) AS CurrentClosed,
                      IF(EXISTS(
                          SELECT 1
                          FROM hr_closeprocesses clp
                          WHERE clp.City = @City
                            AND clp.Year = @PreviousYear
                            AND clp.Month = @PreviousMonth
                      ), 1, 0) AS PreviousClosed;",
                new
                {
                    City = cityCode,
                    Year = currentMonth.Year,
                    Month = currentMonth.Month,
                    PreviousYear = currentMonth.AddMonths(-1).Year,
                    PreviousMonth = currentMonth.AddMonths(-1).Month
                });

            if (state.PreviousClosed == 0)
            {
                throw new ArgumentException(
                    $"You should first close processes for the month \"{currentMonth.AddMonths(-1):MMMM,yyyy}\" .");
            }

            if (state.CurrentClosed == 1)
            {
                throw new ArgumentException("You cannot run Commission Process for the selected month. All processes for selected month have been locked!");
            }
        }

        private async Task<List<ReturnCodDeliveryCommissionRow>> LoadReturnCodSourceRowsAsync(
            MySqlConnection operationsConnection,
            DateTime fromDate,
            DateTime toDate,
            IReadOnlyCollection<string> stationIds,
            CommissionConfig cfg)
        {
            DateTime startedAt = DateTime.UtcNow;
            string query = $@"
WITH cn_data_raw AS (
    SELECT
        LPAD(l.BILLINGCITYID, 5, 0) AS Station_id,
        d.CITY_NAME AS HubName,
        a.COUR_DATE,
        a.COUR_Time,
        a.CN_NUMBER,
        a.COURIER_ID,
        a.Cour_Name,
        hr_rc.Emp_No,
        hr_cc.Id AS CodeTypeId,
        hr_cc.Name AS CodeType,
        cod.book_date,
        cod.client_id,
        cod.company_id,
        cod.shipment_name,
        cod.brand_name,
        cod.shipment_address,
        cod.consignment_name,
        cod.consignment_address,
        cod.return_address,
        cod.return_location,
        a.STATUS,
        a.DELIVERY_DATE,
        a.DELIVERY_TIME,
        ROW_NUMBER() OVER (PARTITION BY a.CN_NUMBER ORDER BY a.COUR_DATE DESC) AS rn
    FROM lcs_db.arival a
    INNER JOIN lcs_db.city d ON a.ARVL_DEST = d.CITY_ID
    INNER JOIN lcs_db.cod_ranges r ON a.SHART_CN BETWEEN r.start_range AND r.end_range
    LEFT JOIN lcs_db.cod_download cod
        ON a.SHART_CN = cod.short_cn
       AND cod.client_id NOT IN ({cfg.CodExcludedClientIdsCsv})
    INNER JOIN lcs_setup.locations l
        ON l.HubId = a.ARVL_DEST
       AND l.LocationTypeID NOT IN ({cfg.ReturnCodExcludeLocationTypeIdsCsv})
       AND l.IsActive = 1
       AND l.IsDeleted = 0
    INNER JOIN lcs_hr.hr_city hr_c ON l.CityID = hr_c.station_id
    LEFT JOIN lcs_hr.hr_employeeroutecode hr_rc
        ON a.COURIER_ID = hr_rc.routecode
       AND hr_c.code = hr_rc.citycode
       AND hr_rc.todate IS NULL
       AND hr_rc.CodeType NOT IN ({cfg.ReturnCodRouteExcludeCodeTypesCsv})
    LEFT JOIN lcs_hr.couriercodetype hr_cc ON hr_rc.CodeType = hr_cc.id
    WHERE a.COUR_DATE BETWEEN @FromDate AND @ToDate
      AND a.STATUS IN ('DS','DR','DW')
      AND a.ARVL_DEST IN @Stations
),
cn_data AS (
    SELECT
        Station_id,
        HubName,
        COUR_DATE,
        COUR_Time,
        CN_NUMBER,
        COURIER_ID,
        Cour_Name,
        Emp_No,
        CodeTypeId,
        CodeType,
        book_date,
        client_id,
        company_id,
        shipment_name,
        brand_name,
        shipment_address,
        consignment_name,
        consignment_address,
        return_address,
        return_location,
        STATUS,
        DELIVERY_DATE,
        DELIVERY_TIME
    FROM cn_data_raw
    WHERE rn = 1
),
courier_cn_counts AS (
    SELECT
        COURIER_ID,
        MAX(CodeTypeId) AS CodeTypeId,
        COUNT(DISTINCT CN_NUMBER) AS CN_Count
    FROM cn_data
    GROUP BY COURIER_ID
),
courier_rates AS (
    SELECT
        COURIER_ID,
        CN_Count,
        CodeTypeId,
        CASE
            WHEN CodeTypeId IN ({cfg.ReturnCodInHouseCodeTypesCsv}) THEN 103
            WHEN CN_Count <= 3000 THEN 100
            WHEN CN_Count <= 10000 THEN 101
            ELSE 102
        END AS RateID
    FROM courier_cn_counts
),
rates_with_policy AS (
    SELECT
        cr.COURIER_ID,
        cr.CN_Count,
        cr.RateID,
        p.Rate AS RatePerShipment,
        ops.Rate AS OpsInc
    FROM courier_rates cr
    LEFT JOIN lcs_hr.hr_commissionpolicy p ON cr.RateID = p.RateID
    LEFT JOIN lcs_hr.hr_commissionpolicy ops ON ops.RateID = 104
)
SELECT
    cnd.*,
    rwp.RateID,
    CAST(rwp.RatePerShipment AS DECIMAL(10,2)) AS RatePerShipment,
    CAST(rwp.OpsInc AS DECIMAL(10,2)) AS OpsInc
FROM cn_data cnd
LEFT JOIN rates_with_policy rwp ON cnd.COURIER_ID = rwp.COURIER_ID
ORDER BY cnd.COURIER_ID, cnd.CN_NUMBER;";

            List<ReturnCodDeliveryCommissionRow> rows = (await operationsConnection.QueryAsync<ReturnCodDeliveryCommissionRow>(
                query,
                new
                {
                    FromDate = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ToDate = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Stations = stationIds.ToArray()
                },
                commandTimeout: 1800)).ToList();

            _logger?.LogInformation(
                "ReturnCOD source completed: {Rows} row(s) in {Seconds:F1}s",
                rows.Count,
                (DateTime.UtcNow - startedAt).TotalSeconds);

            return rows;
        }

        private static async Task<int> SaveReturnCodConsignmentRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            IReadOnlyCollection<ReturnCodDeliveryCommissionRow> sourceRows,
            int year,
            int month,
            string currentUserId,
            IReadOnlyCollection<string> stationIds,
            Func<int, int, Task>? onProgress = null,
            int progressTotal = 0)
        {
            await connection.ExecuteAsync(
                $@"DELETE FROM {CodReturnConsignmentsTable}
                  WHERE Year = @Year
                    AND Month = @Month
                    AND Station_id IN @StationIds;",
                new
                {
                    Year = year,
                    Month = month,
                    StationIds = stationIds.ToArray()
                },
                transaction,
                commandTimeout: 300);

            if (sourceRows.Count == 0)
            {
                return 0;
            }

            string insertPrefix = $@"INSERT INTO {CodReturnConsignmentsTable}
(
    Station_id,
    HubName,
    COUR_DATE,
    COUR_Time,
    CN_NUMBER,
    COURIER_ID,
    Cour_Name,
    Emp_No,
    CodeTypeId,
    CodeType,
    book_date,
    client_id,
    company_id,
    shipment_name,
    brand_name,
    shipment_address,
    consignment_name,
    consignment_address,
    return_address,
    return_location,
    STATUS,
    DELIVERY_DATE,
    DELIVERY_TIME,
    RateID,
    RatePerShipment,
    OpsInc,
    Year,
    Month,
    Create_By,
    Created_Date
)
VALUES ";

            return await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                insertPrefix,
                sourceRows,
                250,
                (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("Station_id", row.Station_id),
                    new KeyValuePair<string, object>("HubName", row.HubName),
                    new KeyValuePair<string, object>("COUR_DATE", row.COUR_DATE),
                    new KeyValuePair<string, object>("COUR_Time", row.COUR_Time),
                    new KeyValuePair<string, object>("CN_NUMBER", row.CN_NUMBER),
                    new KeyValuePair<string, object>("COURIER_ID", row.COURIER_ID),
                    new KeyValuePair<string, object>("Cour_Name", row.Cour_Name),
                    new KeyValuePair<string, object>("Emp_No", row.Emp_No),
                    new KeyValuePair<string, object>("CodeTypeId", row.CodeTypeId),
                    new KeyValuePair<string, object>("CodeType", row.CodeType),
                    new KeyValuePair<string, object>("book_date", row.book_date ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("client_id", row.client_id),
                    new KeyValuePair<string, object>("company_id", row.company_id ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("shipment_name", row.shipment_name),
                    new KeyValuePair<string, object>("brand_name", row.brand_name),
                    new KeyValuePair<string, object>("shipment_address", TruncateForColumn(row.shipment_address, 500)),
                    new KeyValuePair<string, object>("consignment_name", row.consignment_name),
                    new KeyValuePair<string, object>("consignment_address", TruncateForColumn(row.consignment_address, 500)),
                    new KeyValuePair<string, object>("return_address", TruncateForColumn(row.return_address, 500)),
                    new KeyValuePair<string, object>("return_location", row.return_location),
                    new KeyValuePair<string, object>("STATUS", row.STATUS),
                    new KeyValuePair<string, object>("DELIVERY_DATE", row.DELIVERY_DATE ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("DELIVERY_TIME", row.DELIVERY_TIME),
                    new KeyValuePair<string, object>("RateID", row.RateID),
                    new KeyValuePair<string, object>("RatePerShipment", row.RatePerShipment),
                    new KeyValuePair<string, object>("OpsInc", row.OpsInc),
                    new KeyValuePair<string, object>("Year", year),
                    new KeyValuePair<string, object>("Month", month),
                    new KeyValuePair<string, object>("CreateBy", currentUserId),
                    new KeyValuePair<string, object>("CreateDate", createdDate)
                },
                onProgress,
                0,
                progressTotal > 0 ? progressTotal : sourceRows.Count);
        }

        private static async Task<int> SaveReturnCodCommissionRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            IReadOnlyCollection<ReturnCodGroupedCommissionRow> groupedRows,
            int year,
            int month,
            string currentUserId,
            IReadOnlyCollection<string> stationIds)
        {
            await connection.ExecuteAsync(
                $@"DELETE FROM {CodReturnCommissionTable}
                  WHERE ComYear = @Year
                    AND ComMonth = @Month
                    AND StationId IN @Stations;",
                new
                {
                    Year = year,
                    Month = month,
                    Stations = stationIds.ToArray()
                },
                transaction,
                commandTimeout: 300);

            if (groupedRows.Count == 0)
            {
                return 0;
            }

            string insertPrefix = $@"INSERT INTO {CodReturnCommissionTable}
VALUES ";

            return await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                insertPrefix,
                groupedRows,
                500,
                (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("StationId", row.StationId),
                    new KeyValuePair<string, object>("CourierId", row.CourierId),
                    new KeyValuePair<string, object>("CourierName", row.CourierName),
                    new KeyValuePair<string, object>("NoOfShipment", row.NoOfShipment),
                    new KeyValuePair<string, object>("NoOfPieces", row.NoOfShipment),
                    new KeyValuePair<string, object>("TotalCommission", row.TotalCommission),
                    new KeyValuePair<string, object>("RateId", row.RateId),
                    new KeyValuePair<string, object>("Year", year),
                    new KeyValuePair<string, object>("Month", month),
                    new KeyValuePair<string, object>("CreatedBy", currentUserId),
                    new KeyValuePair<string, object>("CreatedDate", createdDate),
                    new KeyValuePair<string, object>("UpdatedBy", DBNull.Value),
                    new KeyValuePair<string, object>("UpdatedDate", DBNull.Value)
                });
        }

        private static async Task<ReturnCodProcessWriteResult> SaveReturnCodProcessRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            ReturnCodCommissionExecutionContext context,
            string currentUserId)
        {
            List<ReturnCodProcessBaseRow> baseRows = (await connection.QueryAsync<ReturnCodProcessBaseRow>(
                $@"SELECT
                      el.LocationId AS GlLocationId,
                      l.LocationName,
                      lm.BStationId,
                      a.StationId,
                      a.CourierId,
                      a.Courier_Name,
                      r.Emp_No,
                      a.RateID,
                      SUM(a.No_Of_Shipment) AS No_Of_Shipment,
                      SUM(a.No_Of_Pieces) AS No_Of_Pieces,
                      SUM(a.TotalCommission) AS TotalCommission
                  FROM {CodReturnCommissionTable} a
                  INNER JOIN hr_locationmapping lm ON a.StationId = lm.BStationId
                  INNER JOIN lcs_hr.hr_employeeroutecode r
                      ON r.LocationId = lm.GlLocationId
                     AND a.CourierId = r.RouteCode
                     AND r.ToDate IS NULL
                  INNER JOIN lcs_hr.hr_employeepersonaldetail e ON r.Emp_No = e.EMP_NO
                  INNER JOIN lcs_hr.hr_employeelocationdetails el
                      ON e.EMP_NO = el.Emp_No
                     AND el.ToDate IS NULL
                  INNER JOIN lcs_setup.locations l ON l.LocationID = el.LocationId
                  WHERE a.ComMonth = @Month
                    AND a.ComYear = @Year
                    AND IFNULL(r.CodeType, 0) NOT IN ({context.Cfg.ReturnCodRouteExcludeCodeTypesCsv})
                    AND r.citycode = @CityCode
                    AND a.RateID IN ({context.Cfg.ReturnCodPolicyRateIdsCsv})
                  GROUP BY a.StationId, a.CourierId, a.RateID
                  ORDER BY a.RateID;",
                new
                {
                    context.Year,
                    context.Month,
                    context.CityCode
                },
                transaction,
                commandTimeout: 120)).ToList();

            var processRows = new List<ReturnCodProcessRow>();
            foreach (ReturnCodProcessBaseRow row in baseRows)
            {
                ReturnCodCommissionPolicyRow? rate = context.CommissionPolicies.FirstOrDefault(item => item.RateID == row.RateID);
                if (rate == null)
                {
                    continue;
                }

                processRows.Add(new ReturnCodProcessRow
                {
                    GlLocationId = row.GlLocationId,
                    RateID = row.RateID,
                    CourierID = row.CourierId,
                    CommissionAmount = row.No_Of_Shipment * rate.Rate
                });
            }

            await connection.ExecuteAsync(
                $@"DELETE FROM {CodReturnCommissionProcessTable}
                  WHERE Year = @Year
                    AND Month = @Month
                    AND GlLocationId IN @LocationIds
                    AND FIND_IN_SET(RateId, @RateIds) > 0;",
                new
                {
                    context.Year,
                    context.Month,
                    LocationIds = context.LocationIds.ToArray(),
                    RateIds = context.Cfg.ReturnCodPolicyRateIdsCsv
                },
                transaction,
                commandTimeout: 300);

            if (processRows.Count == 0)
            {
                return new ReturnCodProcessWriteResult();
            }

            int insertedRows = await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                $@"INSERT INTO {CodReturnCommissionProcessTable}
                  VALUES ",
                processRows,
                500,
                (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("Year", context.Year),
                    new KeyValuePair<string, object>("Month", context.Month),
                    new KeyValuePair<string, object>("GlLocationId", row.GlLocationId),
                    new KeyValuePair<string, object>("CourierID", row.CourierID),
                    new KeyValuePair<string, object>("RateID", row.RateID),
                    new KeyValuePair<string, object>("TotalAmt", row.CommissionAmount),
                    new KeyValuePair<string, object>("CreatedBy", currentUserId),
                    new KeyValuePair<string, object>("CreatedDate", createdDate)
                },
                null,
                0,
                0);

            return new ReturnCodProcessWriteResult
            {
                InsertedRows = insertedRows,
                GeneratedProcessAmountTotal = processRows.Sum(static row => row.CommissionAmount)
            };
        }

        private static string TruncateForColumn(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= maxLength
                ? value
                : value[..maxLength];
        }

        private sealed class ReturnCodCommissionExecutionContext
        {
            public int Year { get; set; }
            public int Month { get; set; }
            public string CityCode { get; set; } = string.Empty;
            public DateTime FromDate { get; set; }
            public DateTime ToDate { get; set; }
            public List<string> StationIds { get; set; } = new();
            public List<int> LocationIds { get; set; } = new();
            public List<ReturnCodCommissionPolicyRow> CommissionPolicies { get; set; } = new();
            public List<ReturnCodDeliveryCommissionRow> SourceRows { get; set; } = new();
            public List<ReturnCodGroupedCommissionRow> GroupedRows { get; set; } = new();
            public CommissionConfig Cfg { get; set; } = new CommissionConfig();
        }

        private sealed class ReturnCodProcessState
        {
            public int CurrentClosed { get; set; }
            public int PreviousClosed { get; set; }
        }

        private sealed class ReturnCodCommissionPolicyRow
        {
            public int RateID { get; set; }
            public string Type { get; set; } = string.Empty;
            public int RateType { get; set; }
            public decimal Rate { get; set; }
        }

        private sealed class ReturnCodDeliveryCommissionRow
        {
            public string Station_id { get; set; } = string.Empty;
            public string HubName { get; set; } = string.Empty;
            public DateTime COUR_DATE { get; set; }
            public string COUR_Time { get; set; } = string.Empty;
            public string CN_NUMBER { get; set; } = string.Empty;
            public string COURIER_ID { get; set; } = string.Empty;
            public string Cour_Name { get; set; } = string.Empty;
            public string Emp_No { get; set; } = string.Empty;
            public int CodeTypeId { get; set; }
            public string CodeType { get; set; } = string.Empty;
            public DateTime? book_date { get; set; }
            public string client_id { get; set; } = string.Empty;
            public int? company_id { get; set; }
            public string shipment_name { get; set; } = string.Empty;
            public string brand_name { get; set; } = string.Empty;
            public string shipment_address { get; set; } = string.Empty;
            public string consignment_name { get; set; } = string.Empty;
            public string consignment_address { get; set; } = string.Empty;
            public string return_address { get; set; } = string.Empty;
            public string return_location { get; set; } = string.Empty;
            public string STATUS { get; set; } = string.Empty;
            public DateTime? DELIVERY_DATE { get; set; }
            public string DELIVERY_TIME { get; set; } = string.Empty;
            public int RateID { get; set; }
            public decimal RatePerShipment { get; set; }
            public decimal OpsInc { get; set; }
            public decimal TotalCommission { get; set; }
        }

        private sealed class ReturnCodGroupedCommissionRow
        {
            public string StationId { get; set; } = string.Empty;
            public string CourierId { get; set; } = string.Empty;
            public string CourierName { get; set; } = string.Empty;
            public int NoOfShipment { get; set; }
            public int NoOfPieces { get; set; }
            public decimal TotalCommission { get; set; }
            public int RateId { get; set; }
        }

        private sealed class ReturnCodProcessBaseRow
        {
            public int GlLocationId { get; set; }
            public string LocationName { get; set; } = string.Empty;
            public string BStationId { get; set; } = string.Empty;
            public string StationId { get; set; } = string.Empty;
            public string CourierId { get; set; } = string.Empty;
            public string Courier_Name { get; set; } = string.Empty;
            public string Emp_No { get; set; } = string.Empty;
            public int RateID { get; set; }
            public int No_Of_Shipment { get; set; }
            public int No_Of_Pieces { get; set; }
            public decimal TotalCommission { get; set; }
        }

        private sealed class ReturnCodProcessRow
        {
            public int GlLocationId { get; set; }
            public int RateID { get; set; }
            public string CourierID { get; set; } = string.Empty;
            public decimal CommissionAmount { get; set; }
        }

        private sealed class ReturnCodProcessWriteResult
        {
            public int InsertedRows { get; set; }
            public decimal GeneratedProcessAmountTotal { get; set; }
        }

        private sealed class ReturnCodCommissionPreviewBaseline
        {
            public int ConsignmentRows { get; set; }
            public int CommissionRows { get; set; }
            public int ProcessRows { get; set; }
            public decimal ProcessAmountTotal { get; set; }
        }
    }
}
