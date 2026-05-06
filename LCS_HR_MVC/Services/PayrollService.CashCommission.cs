using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
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
        private const int CashAutomationSourceTimeoutSeconds = 600;
        public async Task<CashCommissionViewModel> GetCashCommissionPageAsync(
            DateTime workingDate,
            string currentUserId,
            CashCommissionViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();

            var model = existingModel ?? new CashCommissionViewModel();
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

        public async Task<CashCommissionProcessResult> ProcessCashCommissionAsync(
      CashCommissionViewModel model,
      string currentUserId,
      Func<int, int, Task>? onProgress = null)
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new ArgumentException("Database error");

                await connection.OpenAsync();
                var cashConnId = await LogConnectionIdAsync(
                    connection,
                    "CashCommission",
                    model.CityCode,
                    "ProcessCashCommissionAsync_Start");

                var cashOverallStart = Stopwatch.StartNew();

                if (onProgress != null)
                {
                    await onProgress(1, 100);
                }

                CashCommissionExecutionContext context = await PrepareCashCommissionExecutionContextAsync(
                    connection,
                    model.Year,
                    model.Month,
                    model.CityCode,
                    currentUserId);

                if (onProgress != null)
                {
                    await onProgress(5, 100);
                }

                List<CashCommissionSourceRow> cashSourceRows;
                using (var billingConnection = await OpenExternalCodConnectionAsync("LHR_Billing"))
                {
                    cashSourceRows = await LoadCashCommissionSourceRowsAsync(
                        billingConnection,
                        context.StartDate,
                        context.EndDate,
                        context.StationIds,
                        context.Cfg,
                        onProgress: onProgress);
                }

                await KeepConnectionAliveAsync(connection);

                if (onProgress != null)
                {
                    await onProgress(25, 100);
                }

                List<CashVasCommissionRow> vasSourceRows;
                using (var operationsConnection = await OpenExternalCodConnectionAsync("Central_OPS"))
                {
                    vasSourceRows = await LoadCashVasCommissionSourceRowsAsync(
                        operationsConnection,
                        context.StartDate,
                        context.EndDate,
                        context.StationIds,
                        cfg: context.Cfg);
                }

                await KeepConnectionAliveAsync(connection);

                if (onProgress != null)
                {
                    await onProgress(40, 100);
                }

                int totalSourceRows = cashSourceRows.Count + vasSourceRows.Count;
                if (totalSourceRows <= 0)
                {
                    totalSourceRows = 100;
                }

                int cashRowsInserted;
                int vasRowsInserted;

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        cashRowsInserted = await SaveCashCommissionRowsAsync(
                            connection,
                            transaction as MySqlTransaction,
                            context,
                            cashSourceRows,
                            onProgress,
                            totalSourceRows);

                        if (onProgress != null)
                        {
                            await onProgress(70, 100);
                        }

                        vasRowsInserted = await SaveCashVasRowsAsync(
                            connection,
                            transaction as MySqlTransaction,
                            context,
                            vasSourceRows,
                            onProgress,
                            cashRowsInserted,
                            totalSourceRows);

                        if (onProgress != null)
                        {
                            await onProgress(90, 100);
                        }

                        await InsertCashCommissionAcknowledgmentAsync(
                            connection,
                            transaction as MySqlTransaction,
                            currentUserId,
                            model.BillingStatus);

                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        try
                        {
                            await transaction.RollbackAsync();
                        }
                        catch (Exception rollbackEx)
                        {
                            _logger?.LogError(
                                rollbackEx,
                                "CashCommission rollback failed for City={CityCode}, Year={Year}, Month={Month}",
                                model.CityCode,
                                model.Year,
                                model.Month);
                        }

                        throw;
                    }
                }

                if (onProgress != null)
                {
                    await onProgress(100, 100);
                }

                cashOverallStart.Stop();

                LogOperationComplete(
                    "CashCommission",
                    model.CityCode,
                    "ProcessCashCommission_TOTAL",
                    cashConnId,
                    cashOverallStart.Elapsed,
                    cashRowsInserted + vasRowsInserted);

                return new CashCommissionProcessResult
                {
                    Success = true,
                    Message = $"{cashRowsInserted} Cash Commission and {vasRowsInserted} VAS Commission Record(s) inserted",
                    CashSourceRowsRetrieved = cashSourceRows.Count,
                    VasSourceRowsRetrieved = vasSourceRows.Count,
                    CashRowsInserted = cashRowsInserted,
                    VasRowsInserted = vasRowsInserted,
                    StationCount = context.StationIds.Count,
                    FromDate = context.StartDate,
                    ToDate = context.EndDate
                };
            }
            catch (ArgumentException ex)
            {
                return new CashCommissionProcessResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "CashCommission failed for City={CityCode}, Year={Year}, Month={Month}",
                    model.CityCode,
                    model.Year,
                    model.Month);

                return new CashCommissionProcessResult
                {
                    Success = false,
                    Message = ex.GetBaseException().Message
                };
            }
        }
        public async Task<CashCommissionPreviewResult> PreviewCashCommissionAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId,
            bool billingStatus = false)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();

            CashCommissionExecutionContext context = await PrepareCashCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId,
                rejectExistingRows: false);

            Console.WriteLine($"[CashPreview] Loading cash source rows for {context.CityCode} {context.Year}-{context.Month:00}...");
            Console.Out.Flush();
            List<CashCommissionSourceRow> cashSourceRows;
            using (var billingConnection = await OpenExternalCodConnectionAsync("LHR_Billing"))
            {
                cashSourceRows = await LoadCashCommissionSourceRowsAsync(
                    billingConnection,
                    context.StartDate,
                    context.EndDate,
                    context.StationIds,
                    context.Cfg,
                    logProgress: true);
            }
            await KeepConnectionAliveAsync(connection);
            Console.WriteLine($"[CashPreview] Cash source rows loaded: {cashSourceRows.Count}");
            Console.Out.Flush();

            Console.WriteLine($"[CashPreview] Loading VAS source rows for {context.CityCode} {context.Year}-{context.Month:00}...");
            Console.Out.Flush();
            List<CashVasCommissionRow> vasSourceRows;
            using (var operationsConnection = await OpenExternalCodConnectionAsync("Central_OPS"))
            {
                vasSourceRows = await LoadCashVasCommissionSourceRowsAsync(
                    operationsConnection,
                    context.StartDate,
                    context.EndDate,
                    context.StationIds,
                    context.Cfg,
                    logProgress: true);
            }
            await KeepConnectionAliveAsync(connection);
            Console.WriteLine($"[CashPreview] VAS source rows loaded: {vasSourceRows.Count}");
            Console.Out.Flush();

            CashCommissionPreviewBaseline baseline = await CaptureCashCommissionPreviewBaselineAsync(connection, context, currentUserId);

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                Console.WriteLine("[CashPreview] Writing staged rows inside rollback transaction...");
                Console.Out.Flush();
                int cashRowsInserted = await SaveCashCommissionRowsAsync(connection, transaction as MySqlTransaction, context, cashSourceRows);
                int vasRowsInserted = await SaveCashVasRowsAsync(connection, transaction as MySqlTransaction, context, vasSourceRows);
                int acknowledgmentRows = 0;
                acknowledgmentRows += await InsertCashCommissionAcknowledgmentAsync(connection, transaction as MySqlTransaction, currentUserId, billingStatus);
                acknowledgmentRows += await InsertCashCommissionAcknowledgmentAsync(connection, transaction as MySqlTransaction, currentUserId, billingStatus);

                Console.WriteLine("[CashPreview] Rolling back preview transaction...");
                Console.Out.Flush();
                await transaction.RollbackAsync();

                return new CashCommissionPreviewResult
                {
                    Year = context.Year,
                    Month = context.Month,
                    CityCode = context.CityCode,
                    StationCount = context.StationIds.Count,
                    FromDate = context.StartDate,
                    ToDate = context.EndDate,
                    CashSourceRowsRetrieved = cashSourceRows.Count,
                    VasSourceRowsRetrieved = vasSourceRows.Count,
                    CashRowsInserted = cashRowsInserted,
                    VasRowsInserted = vasRowsInserted,
                    AcknowledgmentRowsInserted = acknowledgmentRows,
                    RollbackIntegrityPreserved = await VerifyCashCommissionPreviewBaselineAsync(connection, context, currentUserId, baseline)
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<CashCommissionAuditResult> AuditCashCommissionAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();

            CashCommissionExecutionContext context = await PrepareCashCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId,
                validateUserAccess: false,
                validateProcessOpen: false,
                rejectExistingRows: false);

            List<CashCommissionSourceRow> cashSourceRows;
            using (var billingConnection = await OpenExternalCodConnectionAsync("LHR_Billing"))
            {
                cashSourceRows = await LoadCashCommissionSourceRowsAsync(
                    billingConnection,
                    context.StartDate,
                    context.EndDate,
                    context.StationIds,
                    context.Cfg);
            }
            await KeepConnectionAliveAsync(connection);

            List<CashVasCommissionRow> vasSourceRows;
            using (var operationsConnection = await OpenExternalCodConnectionAsync("Central_OPS"))
            {
                vasSourceRows = await LoadCashVasCommissionSourceRowsAsync(
                    operationsConnection,
                    context.StartDate,
                    context.EndDate,
                    context.StationIds,
                    context.Cfg);
            }
            await KeepConnectionAliveAsync(connection);

            CashCommissionAuditSnapshot historical = await CaptureCashCommissionAuditSnapshotAsync(connection, context);
            List<CashAuditRow> historicalCashRows = await LoadCashAuditRowsAsync(connection, context);
            List<CashVasAuditRow> historicalVasRows = await LoadCashVasAuditRowsAsync(connection, context);
            CashCommissionPreviewBaseline previewBaseline = await CaptureCashCommissionPreviewBaselineAsync(connection, context, currentUserId);

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await SaveCashCommissionRowsAsync(connection, transaction as MySqlTransaction, context, cashSourceRows);
                await SaveCashVasRowsAsync(connection, transaction as MySqlTransaction, context, vasSourceRows);
                await InsertCashCommissionAcknowledgmentAsync(connection, transaction as MySqlTransaction, currentUserId, billingStatus: false);
                await InsertCashCommissionAcknowledgmentAsync(connection, transaction as MySqlTransaction, currentUserId, billingStatus: false);

                CashCommissionAuditSnapshot generated = await CaptureCashCommissionAuditSnapshotAsync(connection, context, transaction as MySqlTransaction);
                List<CashAuditRow> generatedCashRows = await LoadCashAuditRowsAsync(connection, context, transaction as MySqlTransaction);
                List<CashVasAuditRow> generatedVasRows = await LoadCashVasAuditRowsAsync(connection, context, transaction as MySqlTransaction);
                AuditDiffSummary cashRowDiff = BuildAuditDiffSummary(
                    historicalCashRows,
                    generatedCashRows,
                    static row => row.CnNumber,
                    static row => string.Join("|",
                        row.StationId,
                        row.BillingType,
                        row.ShipmentId.ToString(CultureInfo.InvariantCulture),
                        row.EntryCount.ToString(CultureInfo.InvariantCulture),
                        row.TotalCommission.ToString("0.00", CultureInfo.InvariantCulture),
                        row.BaseCommission.ToString("0.00", CultureInfo.InvariantCulture),
                        row.InsuranceCommission.ToString("0.00", CultureInfo.InvariantCulture),
                        row.VasCommission.ToString("0.00", CultureInfo.InvariantCulture)),
                    static row => $"CN={row.CnNumber}, Station={row.StationId}, Billing={row.BillingType}, Shipment={row.ShipmentId}, Count={row.EntryCount}, Total={row.TotalCommission:0.00}, Base={row.BaseCommission:0.00}, Insurance={row.InsuranceCommission:0.00}, VAS={row.VasCommission:0.00}");
                AuditDiffSummary vasRowDiff = BuildAuditDiffSummary(
                    historicalVasRows,
                    generatedVasRows,
                    static row => row.CnNumber,
                    static row => string.Join("|",
                        row.StationId,
                        row.EmpNo,
                        row.CourierId,
                        row.RateId.ToString(CultureInfo.InvariantCulture),
                        row.EntryCount.ToString(CultureInfo.InvariantCulture),
                        row.IncentivePayable,
                        row.DeliveryVia,
                        row.CnicDelivery,
                        row.IncentiveRate.ToString("0.00", CultureInfo.InvariantCulture),
                        row.FinalIncentive.ToString("0.00", CultureInfo.InvariantCulture)),
                    static row => $"CN={row.CnNumber}, Station={row.StationId}, Emp={row.EmpNo}, Cour={row.CourierId}, RateID={row.RateId}, Count={row.EntryCount}, Payable={row.IncentivePayable}, Via={row.DeliveryVia}, Cnic={row.CnicDelivery}, Rate={row.IncentiveRate:0.00}, Final={row.FinalIncentive:0.00}");

                await transaction.RollbackAsync();

                return new CashCommissionAuditResult
                {
                    Year = context.Year,
                    Month = context.Month,
                    CityCode = context.CityCode,
                    StationCount = context.StationIds.Count,
                    FromDate = context.StartDate,
                    ToDate = context.EndDate,
                    HistoricalCashRows = historical.CashRows,
                    GeneratedCashRows = generated.CashRows,
                    HistoricalCashDistinctCn = historical.CashDistinctCn,
                    GeneratedCashDistinctCn = generated.CashDistinctCn,
                    HistoricalCashDuplicateGroups = historical.CashDuplicateGroups,
                    GeneratedCashDuplicateGroups = generated.CashDuplicateGroups,
                    HistoricalCashTotalCommission = historical.CashTotalCommission,
                    GeneratedCashTotalCommission = generated.CashTotalCommission,
                    HistoricalVasRows = historical.VasRows,
                    GeneratedVasRows = generated.VasRows,
                    HistoricalVasDistinctCn = historical.VasDistinctCn,
                    GeneratedVasDistinctCn = generated.VasDistinctCn,
                    HistoricalVasDuplicateGroups = historical.VasDuplicateGroups,
                    GeneratedVasDuplicateGroups = generated.VasDuplicateGroups,
                    HistoricalVasTotalIncentive = historical.VasTotalIncentive,
                    GeneratedVasTotalIncentive = generated.VasTotalIncentive,
                    CashRowDiff = cashRowDiff,
                    VasRowDiff = vasRowDiff,
                    HistoricalParityMatch = generated.CashRows == historical.CashRows
                        && generated.CashDistinctCn == historical.CashDistinctCn
                        && generated.CashDuplicateGroups == historical.CashDuplicateGroups
                        && generated.CashTotalCommission == historical.CashTotalCommission
                        && generated.VasRows == historical.VasRows
                        && generated.VasDistinctCn == historical.VasDistinctCn
                        && generated.VasDuplicateGroups == historical.VasDuplicateGroups
                        && generated.VasTotalIncentive == historical.VasTotalIncentive
                        && cashRowDiff.HistoricalOnlyCount == 0
                        && cashRowDiff.GeneratedOnlyCount == 0
                        && cashRowDiff.ValueMismatchCount == 0
                        && vasRowDiff.HistoricalOnlyCount == 0
                        && vasRowDiff.GeneratedOnlyCount == 0
                        && vasRowDiff.ValueMismatchCount == 0,
                    RollbackIntegrityPreserved = await VerifyCashCommissionPreviewBaselineAsync(connection, context, currentUserId, previewBaseline)
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<CashVasSourceAuditResult> AuditCashVasSourceAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId,
            int cnLimit = 50)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();

            CashCommissionExecutionContext context = await PrepareCashCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId,
                validateUserAccess: false,
                validateProcessOpen: false,
                rejectExistingRows: false);

            List<CashVasAuditRow> historicalRows = await LoadCashVasAuditRowsAsync(connection, context);
            List<string> requestedCnNumbers = historicalRows
                .OrderBy(static row => row.CnNumber, StringComparer.OrdinalIgnoreCase)
                .Take(cnLimit > 0 ? cnLimit : historicalRows.Count)
                .Select(static row => row.CnNumber)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requestedCnNumbers.Count == 0)
            {
                return new CashVasSourceAuditResult
                {
                    Year = context.Year,
                    Month = context.Month,
                    CityCode = context.CityCode,
                    FromDate = context.StartDate,
                    ToDate = context.EndDate,
                    StationCount = context.StationIds.Count,
                    RequestedCnCount = 0,
                    HistoricalRows = 0,
                    CurrentRows = 0,
                    HistoricalTotalIncentive = 0,
                    CurrentTotalIncentive = 0,
                    VasRowDiff = new AuditDiffSummary()
                };
            }

            using var operationsConnection = await OpenExternalCodConnectionAsync("Central_OPS");
            List<CashVasCommissionRow> currentSourceRows = await LoadCashVasCommissionSourceRowsAsync(
                operationsConnection,
                context.StartDate,
                context.EndDate,
                context.StationIds,
                context.Cfg,
                logProgress: false,
                cnNumbers: requestedCnNumbers);

            List<CashVasAuditRow> filteredHistoricalRows = historicalRows
                .Where(row => requestedCnNumbers.Contains(row.CnNumber, StringComparer.OrdinalIgnoreCase))
                .ToList();

            List<CashVasAuditRow> currentAuditRows = currentSourceRows
                .Select(static row => new CashVasAuditRow
                {
                    CnNumber = row.Cn_number,
                    StationId = row.Station_id,
                    EmpNo = NullSafe(row.Emp_No),
                    CourierId = NullSafe(row.Cour_id),
                    RateId = row.RateID ?? 0,
                    EntryCount = 1,
                    IncentivePayable = NullSafe(row.IncentivePayable),
                    DeliveryVia = NullSafe(row.Delivery_Via),
                    CnicDelivery = NullSafe(row.Cnic_delivery),
                    IncentiveRate = row.IncentiveRate,
                    FinalIncentive = row.Final_Incentive
                })
                .OrderBy(static row => row.CnNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AuditDiffSummary diff = BuildAuditDiffSummary(
                filteredHistoricalRows,
                currentAuditRows,
                static row => row.CnNumber,
                static row => string.Join("|",
                    row.StationId,
                    row.EmpNo,
                    row.CourierId,
                    row.RateId.ToString(CultureInfo.InvariantCulture),
                    row.EntryCount.ToString(CultureInfo.InvariantCulture),
                    row.IncentivePayable,
                    row.DeliveryVia,
                    row.CnicDelivery,
                    row.IncentiveRate.ToString("0.00", CultureInfo.InvariantCulture),
                    row.FinalIncentive.ToString("0.00", CultureInfo.InvariantCulture)),
                static row => $"CN={row.CnNumber}, Station={row.StationId}, Emp={row.EmpNo}, Cour={row.CourierId}, RateID={row.RateId}, Count={row.EntryCount}, Payable={row.IncentivePayable}, Via={row.DeliveryVia}, Cnic={row.CnicDelivery}, Rate={row.IncentiveRate:0.00}, Final={row.FinalIncentive:0.00}");

            return new CashVasSourceAuditResult
            {
                Year = context.Year,
                Month = context.Month,
                CityCode = context.CityCode,
                FromDate = context.StartDate,
                ToDate = context.EndDate,
                StationCount = context.StationIds.Count,
                RequestedCnCount = requestedCnNumbers.Count,
                HistoricalRows = filteredHistoricalRows.Count,
                CurrentRows = currentAuditRows.Count,
                HistoricalTotalIncentive = filteredHistoricalRows.Sum(static row => row.FinalIncentive),
                CurrentTotalIncentive = currentAuditRows.Sum(static row => row.FinalIncentive),
                VasRowDiff = diff
            };
        }

        private async Task<CashCommissionExecutionContext> PrepareCashCommissionExecutionContextAsync(
            MySqlConnection connection,
            int year,
            int month,
            string cityCode,
            string currentUserId,
            bool validateUserAccess = true,
            bool validateProcessOpen = true,
            bool rejectExistingRows = true)
        {
            if (year <= 0 || month <= 0)
            {
                throw new ArgumentException("Year and month are required.");
            }

            string normalizedCityCode = cityCode?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedCityCode) || normalizedCityCode == "0")
            {
                throw new ArgumentException("City is required.");
            }

            var cfg = await CommissionConfig.LoadAsync(connection);
            DateTime startDate = new DateTime(year, month, cfg.CommissionStartDay).AddMonths(-1);
            DateTime endDate = new DateTime(year, month, cfg.CommissionEndDay);
            if (endDate > DateTime.Now)
            {
                throw new ArgumentException("Process Can not run on current working Month");
            }

            if (validateUserAccess)
            {
                await EnsureUserCityAllowedAsync(connection, currentUserId, normalizedCityCode);
            }

            if (validateProcessOpen)
            {
                await EnsureProcessesOpenAsync(connection, year, month, normalizedCityCode);
            }

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
                commandTimeout: CashCommandTimeoutSeconds)).ToList();

            if (stationIds.Count == 0)
            {
                throw new ArgumentException("Station ID is not define for the selected city");
            }

            if (rejectExistingRows)
            {
                DateTime? processedOn = await connection.QueryFirstOrDefaultAsync<DateTime?>(
                    $@"SELECT MAX(CreatedDate) FROM {CashConsignmentsTable}
                      WHERE Station_id IN @Stations AND billing_date BETWEEN @StartDate AND @EndDate",
                    new { Stations = stationIds, StartDate = startDate, EndDate = endDate },
                    commandTimeout: 30);
                if (processedOn.HasValue)
                {
                    // Cash consignments have no CreatedBy; look up the user from the
                    // matching acknowledgment row written in the same transaction.
                    // which is inserted in the same transaction (ScreenID = 1 = CashCommission)
                    var userInfo = await connection.QueryFirstOrDefaultAsync(
                        $@"SELECT a.UserID AS CreatedBy, u.UserName
                          FROM {AcknowledgmentTable} a
                          LEFT JOIN lcs_users u ON u.userID = a.UserID
                          WHERE a.ScreenID = 1
                            AND a.CreatedDate BETWEEN DATE_SUB(@ProcessedDate, INTERVAL 10 MINUTE)
                                                  AND DATE_ADD(@ProcessedDate, INTERVAL 10 MINUTE)
                          ORDER BY ABS(TIMESTAMPDIFF(SECOND, a.CreatedDate, @ProcessedDate))
                          LIMIT 1",
                        new { ProcessedDate = processedOn.Value },
                        commandTimeout: 30);
                    throw new ArgumentException($"Already Processed on {processedOn.Value:dd-MMM-yyyy}.{BuildProcessedByInfo(userInfo?.CreatedBy?.ToString(), userInfo?.UserName?.ToString())}");
                }
            }

            return new CashCommissionExecutionContext
            {
                Year = year,
                Month = month,
                CityCode = normalizedCityCode,
                StartDate = startDate,
                EndDate = endDate,
                StationIds = stationIds,
                Cfg = cfg
            };
        }

        private async Task<List<CashCommissionSourceRow>> LoadCashCommissionSourceRowsAsync(
          MySqlConnection connection,
          DateTime startDate,
          DateTime endDate,
          IReadOnlyCollection<string> stationIds,
          CommissionConfig cfg,
          bool logProgress = false,
          Func<int, int, Task>? onProgress = null)
        {
            const string queryPrefix = @"
SELECT Billing_Type,cn_number,Station_id,Station,billing_date,no_of_peices,weight,Weight_KG,Weight_Type,Weight_Bucket,
shipment_id AS Shipment_id,shipment_type,dest_City_id,Overnight_Zone,Overland_Zone,Destination,Dest_P,vendor_id,vendor_name,
Leopard_id,Leopard_Name,express_id,express_name,remarks,client_id,company_id,activity_date,rate,dec_value,Insurance_Rate,
Insurance_Premium_Amt,Gross_Amount,status,Criteria,Commission_Rate,BaseCommission,MTDWeight_Bucket,MTDCommission,
InsuranceCommission,VASCommission_Rate,VASAmount,VASShipment,VASCommission,mtd_Amount,
(BaseCommission + MTDCommission + InsuranceCommission + VASCommission) AS TotalCommission
FROM (
SELECT xb.Billing_Type,xb.cn_number,xb.Station_id,o.CITY_NAME AS Station,xb.billing_date,xb.no_of_peices,xb.weight,xb.Weight_KG,
IF(xb.shipment_id=4,'Item',IF(xb.shipment_id IN (7,27,28,31,34,37,44),'Dox',IF(xb.shipment_id IN (2,3,6,8,11,13,14,15,16,21,22,23,24,25,30,32,33,38,39,40,41,42,43,45,47,52,53,62,63,64),'Non Dox',IF(xb.shipment_id IN (1,9,10,17,18,19,20,48,49,61) AND xb.weight<=1000,'Dox',IF(xb.shipment_id IN (1,9,10,17,18,19,20,48,49,61) AND xb.weight>1000,'Non Dox',''))))) AS Weight_Type,
IF(xb.Weight<=500,'0.5 KG',IF(xb.Weight>500 AND xb.Weight<1001,'1 KG',IF(xb.Weight>1000 AND xb.Weight<2001,'2 KG',IF(xb.Weight>2000 AND xb.Weight<5001,'5 KG',IF(xb.Weight>5000 AND xb.Weight<10001,'10 KG',IF(xb.Weight>10000 AND xb.Weight<15001,'15 KG','25 KG & Above')))))) AS Weight_Bucket,
xb.shipment_id,CASE WHEN xb.shipment_id=999 THEN 'My Collect' ELSE sc.shipment_type END AS shipment_type,xb.dest_City_id,
zc.color_zone AS Overnight_Zone,o.OVERLAND_ZONE AS Overland_Zone,d.CITY_NAME AS Destination,d.PROVINCE AS Dest_P,
sa.vendor_id,v.vendor_name,xb.Leopard_id,xb.Leopard_Name,CASE WHEN xb.shipment_id=999 THEN xb.ec_number ELSE b.express_id END AS express_id,
b.express_name,b.remarks,xb.client_id,xb.company_id,xb.activity_date,xb.rate,IFNULL(b.dec_value,0) AS dec_value,
CONCAT(ROUND((b.Insurance_Amount/b.dec_value)*100,2),'%') AS Insurance_Rate,IFNULL(b.Insurance_Amount,0) AS Insurance_Premium_Amt,
xb.Gross_Amount,b.status,
IF(sa.booking_type<>'','Per Shipment',IF(xb.weight<=1000 AND xb.shipment_id IN (1,9,17),'Per Shipment',IF(xb.weight>1000 AND xb.shipment_id IN (1,9,17),'PER KG',IF(xb.shipment_id IN (2,42,33,3,8,55,56,57,58,59,60),'PER KG',IF(xb.shipment_id IN (10,20,48,49,21,22,23,24,32,25,7,34,27,28,999,52,53,61,6,47,62,63,64,65,66,67,68,69,70,71,72,73),'Per Shipment',IF(xb.shipment_id IN (4),'On Revenue','')))))) AS Criteria,
IF(sa.vendor_id<>'','10',IF(xb.weight<=1000 AND xb.shipment_id IN (1,9,17),'3',IF(xb.weight>1000 AND xb.shipment_id IN (1,9,17),'5',IF(xb.shipment_id IN (2,42,33,55,56,57,58,59,60),'3',IF(xb.shipment_id IN (10,20,61),'8',IF(xb.shipment_id IN (48,49),'8',IF(xb.shipment_id IN (21),'4',IF(xb.shipment_id IN (22),'8',IF(xb.shipment_id IN (23),'20',IF(xb.shipment_id IN (24),'40',IF(xb.shipment_id IN (32),'60',IF(xb.shipment_id IN (25),'100',IF(xb.shipment_id IN (7,34,62),'200',IF(xb.shipment_id IN (6,63,64),'300',IF(xb.shipment_id IN (47),'250',IF(xb.shipment_id IN (4),'3%',IF(xb.shipment_id IN (3,8),'2',IF(xb.shipment_id IN (999),'3',IF(xb.shipment_id IN (52),'12',IF(xb.shipment_id IN (53),'100',IF(xb.shipment_id IN (27,28),'10',IF(xb.shipment_id IN (65),'2',IF(xb.shipment_id IN (66),'4',IF(xb.shipment_id IN (67),'6',IF(xb.shipment_id IN (68),'4',IF(xb.shipment_id IN (69),'8',IF(xb.shipment_id IN (70),'12',IF(xb.shipment_id IN (71),'2',IF(xb.shipment_id IN (72),'7',IF(xb.shipment_id IN (73),'0','')))))))))))))))))))))))))))))) AS Commission_Rate,
IF(sa.vendor_id<>'',10,IF(xb.weight<=1000 AND xb.shipment_id IN (1,9,17),3,IF(xb.weight>1000 AND xb.shipment_id IN (1,9,17),IF(MOD(xb.weight/1000,1)<=0.5,FLOOR(xb.weight/1000)*5,CEIL(xb.weight/1000)*5),IF(xb.shipment_id IN (2) AND xb.weight<=5000,15,IF(xb.shipment_id IN (2) AND xb.weight>5000,(xb.weight/1000)*3,IF(xb.shipment_id IN (42) AND xb.weight<=10000,30,IF(xb.shipment_id IN (42) AND xb.weight>10000,(xb.weight/1000)*3,IF(xb.shipment_id IN (33) AND xb.weight<=25000,75,IF(xb.shipment_id IN (33) AND xb.weight>25000,(xb.weight/1000)*3,IF(xb.shipment_id IN (55) AND xb.weight<=2000,6,IF(xb.shipment_id IN (55) AND xb.weight>2000,(xb.weight/1000)*3,IF(xb.shipment_id IN (56) AND xb.weight<=3000,9,IF(xb.shipment_id IN (56) AND xb.weight>3000,(xb.weight/1000)*3,IF(xb.shipment_id IN (57) AND xb.weight<=15000,45,IF(xb.shipment_id IN (57) AND xb.weight>15000,(xb.weight/1000)*3,IF(xb.shipment_id IN (58) AND xb.weight<=20000,60,IF(xb.shipment_id IN (58) AND xb.weight>20000,(xb.weight/1000)*3,IF(xb.shipment_id IN (59) AND xb.weight<=50000,150,IF(xb.shipment_id IN (59) AND xb.weight>50000,(xb.weight/1000)*3,IF(xb.shipment_id IN (60) AND xb.weight<=100000,300,IF(xb.shipment_id IN (60) AND xb.weight>100000,(xb.weight/1000)*3,IF(xb.shipment_id IN (10,20,61),8,IF(xb.shipment_id IN (48,49),8,IF(xb.shipment_id IN (21),4,IF(xb.shipment_id IN (22),8,IF(xb.shipment_id IN (23),20,IF(xb.shipment_id IN (24),40,IF(xb.shipment_id IN (32),60,IF(xb.shipment_id IN (25),100,IF(xb.shipment_id IN (7,34,62),200,IF(xb.shipment_id IN (6,63,64),300,IF(xb.shipment_id IN (47),250,IF(xb.shipment_id IN (4),(COALESCE(xb.Gross_Amount,0)-COALESCE(b.Insurance_Amount,0)-COALESCE(VASAmount,0)-COALESCE(mtd_Amount,0))*(3/100),IF(xb.shipment_id IN (3,8) AND xb.weight<=10000,20,IF(xb.shipment_id IN (3,8) AND xb.weight>10000,(xb.weight/1000)*2,IF(xb.shipment_id IN (999),3,IF(xb.shipment_id IN (52),12,IF(xb.shipment_id IN (53),100,IF(xb.shipment_id IN (27,28),10,IF(xb.shipment_id IN (65),2,IF(xb.shipment_id IN (66),4,IF(xb.shipment_id IN (67),6,IF(xb.shipment_id IN (68),4,IF(xb.shipment_id IN (69),8,IF(xb.shipment_id IN (70),12,IF(xb.shipment_id IN (71),2,IF(xb.shipment_id IN (72),7,IF(xb.shipment_id IN (73),0,0)))))))))))))))))))))))))))))))))))))))))))))))) AS BaseCommission,
IF(mtd.CN_Number<>'' AND xb.Weight<=500,'0.5 KG',IF(mtd.CN_Number<>'' AND xb.Weight>500 AND xb.Weight<1001,'1 KG',IF(mtd.CN_Number<>'' AND xb.Weight>1000 AND xb.Weight<2001,'2 KG',IF(mtd.CN_Number<>'' AND xb.Weight>2000 AND xb.Weight<5001,'5 KG',IF(mtd.CN_Number<>'' AND xb.Weight>5000 AND xb.Weight<10001,'10 KG',IF(mtd.CN_Number<>'' AND xb.Weight>10000 AND xb.Weight<15001,'15 KG',IF(mtd.CN_Number<>'' AND xb.Weight>=15001,'25 KG & Above',''))))))) AS MTDWeight_Bucket,
IF(mtd.CN_Number<>'' AND xb.Weight<=500,3,IF(mtd.CN_Number<>'' AND xb.Weight>500 AND xb.Weight<1001,3,IF(mtd.CN_Number<>'' AND xb.Weight>1000 AND xb.Weight<2001,3,IF(mtd.CN_Number<>'' AND xb.Weight>2000 AND xb.Weight<5001,15,IF(mtd.CN_Number<>'' AND xb.Weight>5000 AND xb.Weight<10001,30,IF(mtd.CN_Number<>'' AND xb.Weight>10000 AND xb.Weight<15001,45,IF(mtd.CN_Number<>'' AND xb.Weight>=15001,75,0))))))) AS MTDCommission,
IFNULL(b.Insurance_Amount,0)*(3/100) AS InsuranceCommission,IF(vas.CN_NUMBER<>'',10,0) AS VASCommission_Rate,
IF(vas.VASAmount<>'' AND vas.VASAmount IS NOT NULL,vas.VASAmount,0) AS VASAmount,
IF(vas.VASShipment<>'' AND vas.VASShipment IS NOT NULL,vas.VASShipment,0) AS VASShipment,
IF(vas.CN_NUMBER<>'' AND vas.VASAmount<>0,VASShipment*10,0) AS VASCommission,
IF(mtd.mtd_Amount<>'' AND mtd.mtd_Amount IS NOT NULL,mtd.mtd_Amount,0) AS mtd_Amount
FROM (
";

            string querySuffix = $@"
) xb
LEFT JOIN lcs.booking_info b ON xb.cn_number=b.CN_NUMBER AND b.is_deleted=0 AND b.status<>'CL'
LEFT JOIN lcs_billing.shipment_codes sc ON xb.shipment_id=sc.shipment_code_id
LEFT JOIN lcs.rms_mtd_booking mtd ON xb.cn_number=mtd.CN_Number
LEFT JOIN lcs.rms_mtd_times mtdt ON mtd.Mtd_Time_Id=mtdt.Mtd_Time_Id
LEFT JOIN lcs.city o ON xb.Station_id=o.CITY_ID
LEFT JOIN lcs.city d ON xb.dest_City_id=d.CITY_ID
LEFT JOIN lcs_billing.zone_color zc ON zc.city_id=d.ZONE_ID
LEFT JOIN lcs.rms_service_attest_booking sa ON xb.cn_number=sa.CN_Number AND sa.is_deleted=0
LEFT JOIN lcs.rms_noncore_vendors v ON sa.vendor_id=v.vendor_id
LEFT JOIN (
SELECT vas.CN_NUMBER,CASE WHEN COALESCE(SUM(vas.vas_amount),0)=0 THEN 0 ELSE COUNT(vas.CN_NUMBER) END AS VASShipment,COALESCE(SUM(vas.vas_amount),0) AS VASAmount
FROM lcs.booking_info b
INNER JOIN lcs.rms_vas_booking_detail vas ON b.CN_NUMBER=vas.CN_NUMBER
INNER JOIN lcs.rms_vas v ON vas.vas_id=v.id AND v.is_active=1 AND v.is_deleted=0 AND v.id NOT IN (5,6,12,13,15,16,31,32,48)
WHERE b.is_deleted=0 AND b.Book_date BETWEEN @StartDate AND @EndDate AND b.Amount<>0 AND b.station_id IN @StationIds AND b.shipment_type_id NOT IN ({cfg.BillingExcludedShipmentTypesCsv})
GROUP BY vas.CN_NUMBER
) vas ON xb.cn_number=vas.CN_NUMBER
) AS CalculatedCommissions;";

            var parameters = new { StartDate = startDate, EndDate = endDate, StationIds = stationIds.ToArray() };
            var sourceQueries = new (string Name, string Query)[]
            {
                (
                    "billing_details",
                    $@"SELECT a.Billing_Type,a.cn_number,a.Station_id,a.billing_date,a.no_of_peices,a.weight,(a.weight/1000) AS Weight_KG,a.shipment_type_id AS shipment_id,a.dest_City_id,a.cour_id AS Leopard_id,a.cour_name AS Leopard_Name,'' AS ec_number,a.remarks,'' AS client_id,'' AS company_id,a.activity_date,a.rate,a.amount AS Gross_Amount
FROM lcs_billing.billing_details a
WHERE a.Billing_Type='CASH' AND a.BILLING_DATE BETWEEN @StartDate AND @EndDate AND a.Amount<>0 AND a.is_deleted=0 AND a.station_id IN @StationIds AND a.shipment_type_id NOT IN ({cfg.BillingExcludedShipmentTypesCsv})"
                ),
                (
                    "billing_details_hist",
                    $@"SELECT a.Billing_Type,a.cn_number,a.Station_id,a.billing_date,a.no_of_peices,a.weight,(a.weight/1000) AS Weight_KG,a.shipment_type_id AS shipment_id,a.dest_City_id,a.cour_id AS Leopard_id,a.cour_name AS Leopard_Name,'' AS ec_number,a.remarks,'' AS client_id,'' AS company_id,a.activity_date,a.rate,a.amount AS Gross_Amount
FROM lcs_billing.billing_details_hist a
WHERE a.Billing_Type='CASH' AND a.BILLING_DATE BETWEEN @StartDate AND @EndDate AND a.Amount<>0 AND a.is_deleted=0 AND a.station_id IN @StationIds AND a.shipment_type_id NOT IN ({cfg.BillingExcludedShipmentTypesCsv})"
                ),
                (
                    "retail_cod",
                    $@"SELECT 'Retail COD',a.cn_number,LPAD(a.origin_id,5,0),a.Book_date,a.pcs,a.weight,(a.weight/1000),a.shipment_type_id,LPAD(a.dest_id,5,0),LPAD(a.cour_id,5,0),a.cour_name,'' AS ec_number,a.remarks,'' AS client_id,'' AS company_id,a.activity,a.rate,a.leopard_total_charges
FROM lcs.rms_cod_booking a
WHERE a.origin_id IN @StationIds AND a.is_deleted=0 AND a.status<>'CL' AND a.express_id NOT IN ({cfg.ExcludedExpressIdsCsv}) AND a.Book_date BETWEEN @StartDate AND @EndDate AND a.shipment_type_id NOT IN ({cfg.BillingExcludedShipmentTypesCsv})"
                ),
                (
                    "my_collect",
                    $@"SELECT 'My Collect',da.booked_packet_cn,LPAD(da.express_city_id,5,0),da.created_date,da.booked_packet_no_piece,da.booked_packet_weight,(da.booked_packet_weight/1000),999,LPAD(da.destination_city_id,5,0),LPAD(da.staff_id,5,0),e.name,da.ec_number,'' AS remarks,da.client_id,da.company_id,da.booked_packet_date,0,da.booked_packet_collect_amount
FROM lcs_eshipment.rms_cod_booking_dropship da
LEFT JOIN lcs_hr.hr_employeepersonaldetail e ON LPAD(da.staff_id,14,'0')=e.emp_no
WHERE da.express_city_id IN @StationIds AND da.ec_number NOT IN ({cfg.ExcludedExpressIdsCsv}) AND da.ec_number IS NOT NULL AND da.ec_number<>0 AND da.ec_number<>'' AND da.created_date BETWEEN @StartDate AND @EndDate"
                )
            };

            await connection.ExecuteAsync(
                "DROP TEMPORARY TABLE IF EXISTS tmp_cash_commission_source;",
                commandTimeout: 60);

            await connection.ExecuteAsync(
                @"CREATE TEMPORARY TABLE tmp_cash_commission_source (
                    Billing_Type varchar(50) NOT NULL,
                    cn_number varchar(20) NOT NULL,
                    Station_id varchar(10) NOT NULL,
                    billing_date datetime NOT NULL,
                    no_of_peices int NOT NULL,
                    weight decimal(18,3) NOT NULL,
                    Weight_KG decimal(18,6) NOT NULL,
                    shipment_id int NOT NULL,
                    dest_City_id varchar(10) NULL,
                    Leopard_id varchar(20) NULL,
                    Leopard_Name varchar(100) NULL,
                    ec_number varchar(50) NULL,
                    remarks varchar(255) NULL,
                    client_id varchar(50) NULL,
                    company_id varchar(50) NULL,
                    activity_date datetime NULL,
                    rate decimal(18,4) NOT NULL,
                    Gross_Amount decimal(18,4) NOT NULL,
                    KEY idx_tmp_cash_cn (cn_number),
                    KEY idx_tmp_cash_station (Station_id),
                    KEY idx_tmp_cash_dest (dest_City_id),
                    KEY idx_tmp_cash_shipment (shipment_id)
                ) ENGINE=InnoDB;",
                commandTimeout: 60);

            foreach ((string name, string sourceQuery) in sourceQueries)
            {
                DateTime startedAt = DateTime.UtcNow;
                if (logProgress)
                {
                    Console.WriteLine($"[CashPreview] Cash source branch {name} starting...");
                    Console.Out.Flush();
                }
                int insertedRows = await connection.ExecuteAsync(
                    @"INSERT INTO tmp_cash_commission_source
    (Billing_Type,cn_number,Station_id,billing_date,no_of_peices,weight,Weight_KG,shipment_id,dest_City_id,Leopard_id,Leopard_Name,ec_number,remarks,client_id,company_id,activity_date,rate,Gross_Amount)
" + sourceQuery,
                    parameters,
                    commandTimeout: CashAutomationSourceTimeoutSeconds);
                _logger?.LogInformation(
                    "Cash source {Branch} staged {Rows} row(s) in {Seconds:F1}s",
                    name,
                    insertedRows,
                    (DateTime.UtcNow - startedAt).TotalSeconds);

                if (onProgress != null)
                {
                    await onProgress(10, 100);
                }

                if (logProgress)
                {
                    Console.WriteLine($"[CashPreview] Cash source branch {name} staged {insertedRows} row(s) in {(DateTime.UtcNow - startedAt).TotalSeconds:F1}s");
                    Console.Out.Flush();
                }
            }

            var rows = (await connection.QueryAsync<CashCommissionSourceRow>(
       queryPrefix + "SELECT * FROM tmp_cash_commission_source" + querySuffix,
       parameters,
       commandTimeout: CashAutomationSourceTimeoutSeconds)).ToList();
            if (onProgress != null)
            {
                await onProgress(20, 100);
            }
            _logger?.LogInformation(
                "Cash source final query returned {Rows} raw row(s)",
                rows.Count);

            List<CashCommissionSourceRow> dedupedRows = rows
                .GroupBy(static row => row.cn_number)
                .Select(static group => group.First())
                .ToList();

            _logger?.LogInformation(
                "Cash source completed: {Rows} unique CN row(s)",
                dedupedRows.Count);

            return dedupedRows;
        }

        private async Task<List<CashVasCommissionRow>> LoadCashVasCommissionSourceRowsAsync(
            MySqlConnection connection,
            DateTime startDate,
            DateTime endDate,
            IReadOnlyCollection<string> stationIds,
            CommissionConfig cfg,
            bool logProgress = false,
            IReadOnlyCollection<string>? cnNumbers = null)
        {
            const string queryPrefix = @"
SELECT DISTINCT
    base.Billing_Type,
    base.Cn_number,
    base.Station_id,
    base.Station,
    base.Zone,
    base.Delivery_Date,
    base.Delivery_Time,
    base.Client_id,
    base.Client_Name,
    base.Shipment_id,
    base.Shipment_type,
    base.No_of_peices,
    base.Weight,
    base.Cour_id,
    base.Cour_Name,
    base.Status,
    base.Receiver_Name,
    base.Reason,
    base.Emp_No,
    base.Emp_Name,
    base.APPOINT_DATE,
    base.LEFT_DATE,
    base.CodeType,
    base.Cnic_No,
    base.Arvl_Via,
    base.RateID,
    base.Category,
    base.Cnic_delivery,
    base.Delivery_Via,
    CASE
        WHEN base.Cnic_delivery = 'YES' AND base.Delivery_Via = 'MOBILE' THEN 'YES'
        ELSE 'NO'
    END AS IncentivePayable,
    base.Incentive AS IncentiveRate,
    base.Incentive AS Final_Incentive
FROM (
";

            const string querySuffix = @"
) base;";

            bool filterByCn = cnNumbers is { Count: > 0 };
            string cnFilterSql = filterByCn ? " AND cn_number IN @CnNumbers" : string.Empty;
            var parameters = new
            {
                StartDate = startDate,
                EndDate = endDate,
                StationIds = stationIds.ToArray(),
                CnNumbers = filterByCn ? cnNumbers!.ToArray() : Array.Empty<string>()
            };
            await connection.ExecuteAsync(
                "DROP TEMPORARY TABLE IF EXISTS tmp_cash_vas_arrivals;",
                commandTimeout: CashCommandTimeoutSeconds);

            await connection.ExecuteAsync(
                @"CREATE TEMPORARY TABLE tmp_cash_vas_arrivals AS
                  SELECT cn_number,ARVL_DEST,DELIVERY_DATE,DELIVERY_TIME,COURIER_ID,Cour_Name,STATUS,RECEIVER_NAME,REASON,cnic_no,ARVL_VIA
                  FROM lcs_db.arival
                  WHERE DELIVERY_DATE BETWEEN @StartDate AND @EndDate
                    AND ARVL_DEST IN @StationIds
                    AND STATUS = 'DV'"
                  + cnFilterSql
                  + ";",
                parameters,
                commandTimeout: CashCommandTimeoutSeconds);

            await connection.ExecuteAsync(
                @"ALTER TABLE tmp_cash_vas_arrivals
                    ADD INDEX idx_tmp_cash_vas_cn (cn_number),
                    ADD INDEX idx_tmp_cash_vas_station (ARVL_DEST, COURIER_ID),
                    ADD INDEX idx_tmp_cash_vas_delivery (DELIVERY_DATE);",
                commandTimeout: CashCommandTimeoutSeconds);

            await connection.ExecuteAsync(
                @"DROP TEMPORARY TABLE IF EXISTS tmp_cash_vas_station_context;
                  DROP TEMPORARY TABLE IF EXISTS tmp_cash_vas_routes;",
                commandTimeout: CashCommandTimeoutSeconds);

            await connection.ExecuteAsync(
                $@"CREATE TEMPORARY TABLE tmp_cash_vas_station_context AS
                  SELECT station.ARVL_DEST AS Station_id,
                         ci.billing_station AS Station,
                         ci.area AS Zone,
                         (
                             SELECT hr_c.Code
                             FROM lcs_setup.locations l
                             LEFT JOIN lcs_hr.hr_city hr_c ON l.CityID = hr_c.station_id
                             WHERE l.HubId = station.ARVL_DEST
                               AND l.LocationTypeID IN ({cfg.CashLocationTypeIdsCsv})
                               AND l.IsActive = 1
                               AND l.IsDeleted = 0
                             LIMIT 1
                         ) AS CityCode
                  FROM (
                      SELECT DISTINCT ARVL_DEST
                      FROM tmp_cash_vas_arrivals
                  ) station
                  INNER JOIN oms_leopards.branch ci ON station.ARVL_DEST = ci.branch_id;",
                commandTimeout: CashCommandTimeoutSeconds);

            await connection.ExecuteAsync(
                @"ALTER TABLE tmp_cash_vas_station_context
                    ADD PRIMARY KEY (Station_id),
                    ADD INDEX idx_tmp_cash_vas_city (CityCode);",
                commandTimeout: CashCommandTimeoutSeconds);

            await connection.ExecuteAsync(
                $@"CREATE TEMPORARY TABLE tmp_cash_vas_routes AS
                  SELECT RouteCode, citycode, EMP_NO, CodeType
                  FROM lcs_hr.hr_employeeroutecode
                  WHERE todate IS NULL
                    AND CodeType NOT IN ({cfg.CashVasRouteExcludeCodeTypesCsv})
                    AND citycode IN (
                        SELECT DISTINCT CityCode
                        FROM tmp_cash_vas_station_context
                        WHERE CityCode IS NOT NULL
                    );",
                commandTimeout: CashCommandTimeoutSeconds);

            await connection.ExecuteAsync(
                @"ALTER TABLE tmp_cash_vas_routes
                    ADD INDEX idx_tmp_cash_vas_route_city (RouteCode, citycode),
                    ADD INDEX idx_tmp_cash_vas_emp (EMP_NO);",
                commandTimeout: CashCommandTimeoutSeconds);

            var sourceQueries = new (string Name, string Query)[]
            {
                (
                    "ecommerce_cod_download",
                    @"SELECT STRAIGHT_JOIN
        'E-Commerce' AS Billing_Type,
        a.cn_number AS Cn_number,
        a.ARVL_DEST AS Station_id,
        st.Station,
        st.Zone,
        a.DELIVERY_DATE AS Delivery_Date,
        a.DELIVERY_TIME AS Delivery_Time,
        c.client_id AS Client_id,
        c.shipment_name AS Client_Name,
        c.shipment_type_id AS Shipment_id,
        s.shipment_type AS Shipment_type,
        c.booked_packet_no_piece AS No_of_peices,
        c.booked_packet_weight AS Weight,
        a.COURIER_ID AS Cour_id,
        a.Cour_Name AS Cour_Name,
        a.STATUS,
        a.RECEIVER_NAME AS Receiver_Name,
        a.REASON AS Reason,
        hr_rc.EMP_NO AS Emp_No,
        hr_ep.name AS Emp_Name,
        hr_ep.APPOINT_DATE,
        hr_ep.LEFT_DATE,
        hr_cc.name AS CodeType,
        a.cnic_no AS Cnic_No,
        a.ARVL_VIA AS Arvl_Via,
        cv.RateID,
        cv.Category,
        CASE
            WHEN a.cnic_no IN ('0000000000000','1111111111111','9999999999999') THEN 'NO'
            WHEN LENGTH(a.cnic_no) = 13 AND a.cnic_no REGEXP '^[0-9]+$' THEN 'YES'
            ELSE 'NO'
        END AS Cnic_delivery,
        CASE
            WHEN a.ARVL_VIA IN ('API', 'LMD') THEN 'MOBILE'
            ELSE 'Manual'
        END AS Delivery_Via,
        cv.Incentive
    FROM tmp_cash_vas_arrivals a
    INNER JOIN tmp_cash_vas_station_context st ON a.ARVL_DEST = st.Station_id
    INNER JOIN lcs_db.cod_download c ON a.cn_number = c.CN_NUMBER
    INNER JOIN lcs_billing.shipment_codes s ON c.shipment_type_id = s.shipment_code_id
    INNER JOIN lcs_db.client_vas cv ON c.client_id = cv.CLNT_ID
        AND a.ARVL_DEST = cv.City_id
    LEFT JOIN tmp_cash_vas_routes hr_rc ON hr_rc.RouteCode = a.COURIER_ID
        AND hr_rc.citycode = st.CityCode
    LEFT JOIN lcs_hr.couriercodetype hr_cc ON hr_rc.CodeType = hr_cc.id
    LEFT JOIN lcs_hr.hr_employeepersonaldetail hr_ep ON hr_rc.Emp_No = hr_ep.Emp_No"
                ),
                (
                    "billing_details_vas",
                    @"SELECT STRAIGHT_JOIN
        b.Billing_Type,
        a.cn_number AS Cn_number,
        a.ARVL_DEST AS Station_id,
        st.Station,
        st.Zone,
        a.DELIVERY_DATE AS Delivery_Date,
        a.DELIVERY_TIME AS Delivery_Time,
        b.client_id AS Client_id,
        cv.CLNT_NAME AS Client_Name,
        b.shipment_type_id AS Shipment_id,
        s.shipment_type AS Shipment_type,
        b.no_of_peices AS No_of_peices,
        b.weight AS Weight,
        a.COURIER_ID AS Cour_id,
        a.Cour_Name AS Cour_Name,
        a.STATUS,
        a.RECEIVER_NAME AS Receiver_Name,
        a.REASON AS Reason,
        hr_rc.EMP_NO AS Emp_No,
        hr_ep.name AS Emp_Name,
        hr_ep.APPOINT_DATE,
        hr_ep.LEFT_DATE,
        hr_cc.name AS CodeType,
        a.cnic_no AS Cnic_No,
        a.ARVL_VIA AS Arvl_Via,
        cv.RateID,
        cv.Category,
        CASE
            WHEN a.cnic_no IN ('0000000000000','1111111111111','9999999999999') THEN 'NO'
            WHEN LENGTH(a.cnic_no) = 13 AND a.cnic_no REGEXP '^[0-9]+$' THEN 'YES'
            ELSE 'NO'
        END AS Cnic_delivery,
        CASE
            WHEN a.ARVL_VIA IN ('API', 'LMD') THEN 'MOBILE'
            ELSE 'Manual'
        END AS Delivery_Via,
        cv.Incentive
    FROM tmp_cash_vas_arrivals a
    INNER JOIN tmp_cash_vas_station_context st ON a.ARVL_DEST = st.Station_id
    INNER JOIN lcs_billing_download.billing_details b ON a.cn_number = b.CN_NUMBER
        AND b.is_deleted = 0
    INNER JOIN lcs_billing.client c ON b.client_id = c.CLNT_ID
        AND b.station_id = c.City_id
    INNER JOIN lcs_billing.shipment_codes s ON b.shipment_type_id = s.shipment_code_id
    INNER JOIN lcs_db.client_vas cv ON b.client_id = cv.CLNT_ID
        AND b.Station_id = cv.City_id
    LEFT JOIN tmp_cash_vas_routes hr_rc ON hr_rc.RouteCode = a.COURIER_ID
        AND hr_rc.citycode = st.CityCode
    LEFT JOIN lcs_hr.couriercodetype hr_cc ON hr_rc.CodeType = hr_cc.id
    LEFT JOIN lcs_hr.hr_employeepersonaldetail hr_ep ON hr_rc.Emp_No = hr_ep.Emp_No"
                )
            };

            var rows = new List<CashVasCommissionRow>();
            foreach ((string name, string sourceQuery) in sourceQueries)
            {
                DateTime startedAt = DateTime.UtcNow;
                if (logProgress)
                {
                    Console.WriteLine($"[CashPreview] VAS source branch {name} starting...");
                    Console.Out.Flush();
                }

                List<CashVasCommissionRow> branchRows = (await connection.QueryAsync<CashVasCommissionRow>(
                    queryPrefix + sourceQuery + querySuffix,
                    parameters,
                    commandTimeout: CashCommandTimeoutSeconds)).ToList();
                _logger?.LogInformation(
                    "Cash VAS source {Branch} returned {Rows} row(s) in {Seconds:F1}s",
                    name,
                    branchRows.Count,
                    (DateTime.UtcNow - startedAt).TotalSeconds);

                if (logProgress)
                {
                    Console.WriteLine($"[CashPreview] VAS source branch {name} returned {branchRows.Count} row(s) in {(DateTime.UtcNow - startedAt).TotalSeconds:F1}s");
                    Console.Out.Flush();
                }

                rows.AddRange(branchRows);
            }

            List<CashVasCommissionRow> dedupedRows = rows
                .GroupBy(static row => row.Cn_number, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(row => string.Equals(row.Billing_Type, "E-Commerce", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .First())
                .ToList();

            _logger?.LogInformation(
                "Cash VAS source completed: {Rows} unique CN row(s)",
                dedupedRows.Count);

            return dedupedRows;
        }

        private static async Task<int> SaveCashCommissionRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            CashCommissionExecutionContext context,
            IReadOnlyCollection<CashCommissionSourceRow> rows,
            Func<int, int, Task>? onProgress = null,
            int progressTotal = 0)
        {
            await connection.ExecuteAsync(
                $@"DELETE FROM {CashConsignmentsTable}
                  WHERE Station_id IN @Stations
                    AND billing_date BETWEEN @StartDate AND @EndDate;",
                new { Stations = context.StationIds.ToArray(), StartDate = context.StartDate, EndDate = context.EndDate },
                transaction,
                commandTimeout: CashCommandTimeoutSeconds);

            string insertPrefix = $@"REPLACE INTO {CashConsignmentsTable}
(Billing_Type,cn_number,Station_id,Station,billing_date,no_of_peices,weight,Weight_KG,Weight_Type,Weight_Bucket,Shipment_id,
shipment_type,dest_City_id,Overnight_Zone,Overland_Zone,Destination,Dest_P,vendor_id,vendor_name,cour_id,cour_Name,express_id,
express_name,remarks,client_id,company_id,activity_date,rate,dec_value,Insurance_Rate,Insurance_Premium_Amt,Gross_Amount,status,
Criteria,Commission_Rate,BaseCommission,MTDWeight_Bucket,MTDCommission,InsuranceCommission,VASCommission_Rate,VASAmount,VASShipment,
VASCommission,mtd_Amount,TotalCommission,CreatedDate)
VALUES ";

            return await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                insertPrefix,
                rows,
                100,
                static (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("Billing_Type", row.Billing_Type),
                    new KeyValuePair<string, object>("cn_number", row.cn_number),
                    new KeyValuePair<string, object>("Station_id", row.Station_id),
                    new KeyValuePair<string, object>("Station", row.Station ?? string.Empty),
                    new KeyValuePair<string, object>("billing_date", row.billing_date),
                    new KeyValuePair<string, object>("no_of_peices", row.no_of_peices),
                    new KeyValuePair<string, object>("weight", row.weight),
                    new KeyValuePair<string, object>("Weight_KG", row.Weight_KG),
                    new KeyValuePair<string, object>("Weight_Type", row.Weight_Type ?? string.Empty),
                    new KeyValuePair<string, object>("Weight_Bucket", row.Weight_Bucket ?? string.Empty),
                    new KeyValuePair<string, object>("Shipment_id", row.Shipment_id),
                    new KeyValuePair<string, object>("shipment_type", row.shipment_type ?? string.Empty),
                    new KeyValuePair<string, object>("dest_City_id", row.dest_City_id ?? string.Empty),
                    new KeyValuePair<string, object>("Overnight_Zone", row.Overnight_Zone ?? string.Empty),
                    new KeyValuePair<string, object>("Overland_Zone", row.Overland_Zone ?? string.Empty),
                    new KeyValuePair<string, object>("Destination", row.Destination ?? string.Empty),
                    new KeyValuePair<string, object>("Dest_P", row.Dest_P ?? string.Empty),
                    new KeyValuePair<string, object>("vendor_id", row.vendor_id ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("vendor_name", row.vendor_name ?? string.Empty),
                    new KeyValuePair<string, object>("Leopard_id", row.Leopard_id ?? string.Empty),
                    new KeyValuePair<string, object>("Leopard_Name", row.Leopard_Name ?? string.Empty),
                    new KeyValuePair<string, object>("express_id", row.express_id ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("express_name", row.express_name ?? string.Empty),
                    new KeyValuePair<string, object>("remarks", row.remarks ?? string.Empty),
                    new KeyValuePair<string, object>("client_id", row.client_id ?? string.Empty),
                    new KeyValuePair<string, object>("company_id", row.company_id ?? string.Empty),
                    new KeyValuePair<string, object>("activity_date", row.activity_date ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("rate", row.rate),
                    new KeyValuePair<string, object>("dec_value", row.dec_value),
                    new KeyValuePair<string, object>("Insurance_Rate", row.Insurance_Rate ?? string.Empty),
                    new KeyValuePair<string, object>("Insurance_Premium_Amt", row.Insurance_Premium_Amt),
                    new KeyValuePair<string, object>("Gross_Amount", row.Gross_Amount),
                    new KeyValuePair<string, object>("status", row.status ?? string.Empty),
                    new KeyValuePair<string, object>("Criteria", row.Criteria ?? string.Empty),
                    new KeyValuePair<string, object>("Commission_Rate", row.Commission_Rate ?? string.Empty),
                    new KeyValuePair<string, object>("BaseCommission", row.BaseCommission),
                    new KeyValuePair<string, object>("MTDWeight_Bucket", row.MTDWeight_Bucket ?? string.Empty),
                    new KeyValuePair<string, object>("MTDCommission", row.MTDCommission),
                    new KeyValuePair<string, object>("InsuranceCommission", row.InsuranceCommission),
                    new KeyValuePair<string, object>("VASCommission_Rate", row.VASCommission_Rate),
                    new KeyValuePair<string, object>("VASAmount", row.VASAmount),
                    new KeyValuePair<string, object>("VASShipment", row.VASShipment),
                    new KeyValuePair<string, object>("VASCommission", row.VASCommission),
                    new KeyValuePair<string, object>("mtd_Amount", row.mtd_Amount),
                    new KeyValuePair<string, object>("TotalCommission", row.TotalCommission),
                    new KeyValuePair<string, object>("CreatedDate", createdDate)
                },
                onProgress, 0, progressTotal);
        }

        private static async Task<int> SaveCashVasRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            CashCommissionExecutionContext context,
            IReadOnlyCollection<CashVasCommissionRow> rows,
            Func<int, int, Task>? onProgress = null,
            int progressOffset = 0,
            int progressTotal = 0)
        {
            await connection.ExecuteAsync(
                $@"DELETE FROM {VasIncentiveDetailTable}
                  WHERE Station_id IN @Stations
                    AND Delivery_Date BETWEEN @StartDate AND @EndDate;",
                new { Stations = context.StationIds.ToArray(), StartDate = context.StartDate, EndDate = context.EndDate },
                transaction,
                commandTimeout: CashCommandTimeoutSeconds);

            string insertPrefix = $@"REPLACE INTO {VasIncentiveDetailTable}
(Billing_Type,Cn_number,Station_id,Station,Zone,Delivery_Date,Delivery_Time,Client_id,Client_Name,Shipment_id,Shipment_type,
No_of_peices,Weight,Cour_id,Cour_Name,Status,Receiver_Name,Reason,Emp_No,Emp_Name,APPOINT_DATE,LEFT_DATE,CodeType,Cnic_No,
Arvl_Via,RateID,Category,Cnic_delivery,Delivery_Via,IncentivePayable,IncentiveRate,Final_Incentive,CreatedDate)
VALUES ";

            return await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                insertPrefix,
                rows,
                200,
                static (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("Billing_Type", row.Billing_Type ?? string.Empty),
                    new KeyValuePair<string, object>("Cn_number", row.Cn_number),
                    new KeyValuePair<string, object>("Station_id", row.Station_id),
                    new KeyValuePair<string, object>("Station", row.Station ?? string.Empty),
                    new KeyValuePair<string, object>("Zone", row.Zone ?? string.Empty),
                    new KeyValuePair<string, object>("Delivery_Date", row.Delivery_Date),
                    new KeyValuePair<string, object>("Delivery_Time", row.Delivery_Time ?? string.Empty),
                    new KeyValuePair<string, object>("Client_id", row.Client_id ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("Client_Name", row.Client_Name ?? string.Empty),
                    new KeyValuePair<string, object>("Shipment_id", row.Shipment_id ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("Shipment_type", row.Shipment_type ?? string.Empty),
                    new KeyValuePair<string, object>("No_of_peices", row.No_of_peices),
                    new KeyValuePair<string, object>("Weight", row.Weight),
                    new KeyValuePair<string, object>("Cour_id", row.Cour_id ?? string.Empty),
                    new KeyValuePair<string, object>("Cour_Name", row.Cour_Name ?? string.Empty),
                    new KeyValuePair<string, object>("Status", row.Status ?? string.Empty),
                    new KeyValuePair<string, object>("Receiver_Name", row.Receiver_Name ?? string.Empty),
                    new KeyValuePair<string, object>("Reason", row.Reason ?? string.Empty),
                    new KeyValuePair<string, object>("Emp_No", row.Emp_No ?? string.Empty),
                    new KeyValuePair<string, object>("Emp_Name", row.Emp_Name ?? string.Empty),
                    new KeyValuePair<string, object>("APPOINT_DATE", row.APPOINT_DATE ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("LEFT_DATE", row.LEFT_DATE ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("CodeType", row.CodeType ?? string.Empty),
                    new KeyValuePair<string, object>("Cnic_No", row.Cnic_No ?? string.Empty),
                    new KeyValuePair<string, object>("Arvl_Via", row.Arvl_Via ?? string.Empty),
                    new KeyValuePair<string, object>("RateID", row.RateID ?? (object)DBNull.Value),
                    new KeyValuePair<string, object>("Category", row.Category ?? string.Empty),
                    new KeyValuePair<string, object>("Cnic_delivery", row.Cnic_delivery ?? string.Empty),
                    new KeyValuePair<string, object>("Delivery_Via", row.Delivery_Via ?? string.Empty),
                    new KeyValuePair<string, object>("IncentivePayable", row.IncentivePayable ?? string.Empty),
                    new KeyValuePair<string, object>("IncentiveRate", row.IncentiveRate),
                    new KeyValuePair<string, object>("Final_Incentive", row.Final_Incentive),
                    new KeyValuePair<string, object>("CreatedDate", createdDate)
                },
                onProgress, progressOffset, progressTotal);
        }

        private static async Task<int> InsertCashCommissionAcknowledgmentAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string currentUserId,
            bool billingStatus)
        {
            return await connection.ExecuteAsync(
                $@"INSERT INTO {AcknowledgmentTable}
                  (ScreenID, UserID, CreatedDate, IsBillingConfirm, IsAttendanceProcessed, AllCommProcessed, OneTimeActivity)
                  VALUES (1, @UserId, NOW(), @BillingStatus, NULL, NULL, NULL);",
                new
                {
                    UserId = ParsePayrollUserId(currentUserId),
                    BillingStatus = billingStatus ? 1 : 0
                },
                transaction,
                commandTimeout: CashCommandTimeoutSeconds);
        }

        private static async Task<CashCommissionPreviewBaseline> CaptureCashCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            CashCommissionExecutionContext context,
            string currentUserId)
        {
            return await connection.QuerySingleAsync<CashCommissionPreviewBaseline>(
                $@"SELECT
                      (SELECT COUNT(*) FROM {CashConsignmentsTable} WHERE Station_id IN @Stations AND billing_date BETWEEN @StartDate AND @EndDate) AS CashRows,
                      (SELECT COUNT(*) FROM {VasIncentiveDetailTable} WHERE Station_id IN @Stations AND Delivery_Date BETWEEN @StartDate AND @EndDate) AS VasRows,
                      (SELECT COUNT(*) FROM {AcknowledgmentTable} WHERE ScreenID = 1 AND UserID = @UserId) AS AcknowledgmentRows;",
                new
                {
                    Stations = context.StationIds.ToArray(),
                    StartDate = context.StartDate,
                    EndDate = context.EndDate,
                    UserId = ParsePayrollUserId(currentUserId)
                },
                commandTimeout: CashCommandTimeoutSeconds);
        }

        private static async Task<CashCommissionAuditSnapshot> CaptureCashCommissionAuditSnapshotAsync(
            MySqlConnection connection,
            CashCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return await connection.QuerySingleAsync<CashCommissionAuditSnapshot>(
                $@"SELECT
                      (SELECT COUNT(*)
                       FROM {CashConsignmentsTable}
                       WHERE Station_id IN @Stations
                         AND billing_date BETWEEN @StartDate AND @EndDate) AS CashRows,
                      (SELECT COUNT(DISTINCT cn_number)
                       FROM {CashConsignmentsTable}
                       WHERE Station_id IN @Stations
                         AND billing_date BETWEEN @StartDate AND @EndDate) AS CashDistinctCn,
                      (SELECT COUNT(*)
                       FROM (
                           SELECT cn_number
                           FROM {CashConsignmentsTable}
                           WHERE Station_id IN @Stations
                             AND billing_date BETWEEN @StartDate AND @EndDate
                           GROUP BY cn_number
                           HAVING COUNT(*) > 1
                       ) cash_duplicates) AS CashDuplicateGroups,
                      (SELECT IFNULL(ROUND(SUM(TotalCommission), 2), 0)
                       FROM {CashConsignmentsTable}
                       WHERE Station_id IN @Stations
                         AND billing_date BETWEEN @StartDate AND @EndDate) AS CashTotalCommission,
                      (SELECT COUNT(*)
                       FROM {VasIncentiveDetailTable}
                       WHERE Station_id IN @Stations
                         AND Delivery_Date BETWEEN @StartDate AND @EndDate) AS VasRows,
                      (SELECT COUNT(DISTINCT Cn_number)
                       FROM {VasIncentiveDetailTable}
                       WHERE Station_id IN @Stations
                         AND Delivery_Date BETWEEN @StartDate AND @EndDate) AS VasDistinctCn,
                      (SELECT COUNT(*)
                       FROM (
                           SELECT Cn_number
                           FROM {VasIncentiveDetailTable}
                           WHERE Station_id IN @Stations
                             AND Delivery_Date BETWEEN @StartDate AND @EndDate
                           GROUP BY Cn_number
                           HAVING COUNT(*) > 1
                       ) vas_duplicates) AS VasDuplicateGroups,
                      (SELECT IFNULL(ROUND(SUM(Final_Incentive), 2), 0)
                       FROM {VasIncentiveDetailTable}
                       WHERE Station_id IN @Stations
                         AND Delivery_Date BETWEEN @StartDate AND @EndDate) AS VasTotalIncentive;",
                new
                {
                    Stations = context.StationIds.ToArray(),
                    StartDate = context.StartDate,
                    EndDate = context.EndDate
                },
                transaction,
                commandTimeout: CashCommandTimeoutSeconds);
        }

        private static async Task<List<CashAuditRow>> LoadCashAuditRowsAsync(
            MySqlConnection connection,
            CashCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return (await connection.QueryAsync<CashAuditRow>(
                $@"SELECT cn_number AS CnNumber,
                         MAX(IFNULL(Station_id, '')) AS StationId,
                         MAX(IFNULL(Billing_Type, '')) AS BillingType,
                         MAX(IFNULL(Shipment_id, 0)) AS ShipmentId,
                         COUNT(*) AS EntryCount,
                         IFNULL(ROUND(SUM(TotalCommission), 2), 0) AS TotalCommission,
                         IFNULL(ROUND(SUM(BaseCommission), 2), 0) AS BaseCommission,
                         IFNULL(ROUND(SUM(InsuranceCommission), 2), 0) AS InsuranceCommission,
                         IFNULL(ROUND(SUM(VASCommission), 2), 0) AS VasCommission
                  FROM {CashConsignmentsTable}
                  WHERE Station_id IN @Stations
                    AND billing_date BETWEEN @StartDate AND @EndDate
                  GROUP BY cn_number
                  ORDER BY cn_number;",
                new
                {
                    Stations = context.StationIds.ToArray(),
                    StartDate = context.StartDate,
                    EndDate = context.EndDate
                },
                transaction,
                commandTimeout: CashCommandTimeoutSeconds)).ToList();
        }

        private static async Task<List<CashVasAuditRow>> LoadCashVasAuditRowsAsync(
            MySqlConnection connection,
            CashCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return (await connection.QueryAsync<CashVasAuditRow>(
                $@"SELECT Cn_number AS CnNumber,
                         MAX(IFNULL(Station_id, '')) AS StationId,
                         MAX(IFNULL(Emp_No, '')) AS EmpNo,
                         MAX(IFNULL(Cour_id, '')) AS CourierId,
                         MAX(IFNULL(RateID, 0)) AS RateId,
                         COUNT(*) AS EntryCount,
                         MAX(IFNULL(IncentivePayable, '')) AS IncentivePayable,
                         MAX(IFNULL(Delivery_Via, '')) AS DeliveryVia,
                         MAX(IFNULL(Cnic_delivery, '')) AS CnicDelivery,
                         IFNULL(ROUND(SUM(IncentiveRate), 2), 0) AS IncentiveRate,
                         IFNULL(ROUND(SUM(Final_Incentive), 2), 0) AS FinalIncentive
                  FROM {VasIncentiveDetailTable}
                  WHERE Station_id IN @Stations
                    AND Delivery_Date BETWEEN @StartDate AND @EndDate
                  GROUP BY Cn_number
                  ORDER BY Cn_number;",
                new
                {
                    Stations = context.StationIds.ToArray(),
                    StartDate = context.StartDate,
                    EndDate = context.EndDate
                },
                transaction,
                commandTimeout: CashCommandTimeoutSeconds)).ToList();
        }

        private static async Task<bool> VerifyCashCommissionPreviewBaselineAsync(
            MySqlConnection connection,
            CashCommissionExecutionContext context,
            string currentUserId,
            CashCommissionPreviewBaseline baseline)
        {
            CashCommissionPreviewBaseline current = await CaptureCashCommissionPreviewBaselineAsync(connection, context, currentUserId);
            return current.CashRows == baseline.CashRows
                && current.VasRows == baseline.VasRows
                && current.AcknowledgmentRows == baseline.AcknowledgmentRows;
        }

        private static async Task<int> ExecuteChunkedInsertAsync<T>(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string query,
            IReadOnlyCollection<T> rows,
            int batchSize,
            Func<T, DateTime, object> map,
            Func<int, int, Task>? onProgress = null,
            int progressOffset = 0,
            int progressTotal = 0)
        {
            int affectedRows = 0;
            int reportTotal = progressTotal > 0 ? progressTotal : rows.Count;
            DateTime createdDate = DateTime.Now;
            foreach (List<T> chunk in Chunk(rows, batchSize))
            {
                affectedRows += await connection.ExecuteAsync(
                    query,
                    chunk.Select(item => map(item, createdDate)),
                    transaction,
                    commandTimeout: CashCommandTimeoutSeconds);
                if (onProgress != null) await onProgress(progressOffset + affectedRows, reportTotal);
            }

            return affectedRows;
        }

        /// <summary>General command timeout for Cash commission DB operations (seconds).</summary>
        private const int CashCommandTimeoutSeconds = 600;

        /// <summary>Batch INSERT timeout per chunk (seconds). Each chunk is 250–500 rows —
        /// should complete well within this limit unless lock contention or I/O stall occurs.</summary>
        private const int BatchInsertCommandTimeoutSeconds = 300;

        private static async Task<int> ExecuteMultiValueInsertAsync<T>(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string insertPrefix,
            IReadOnlyCollection<T> rows,
            int batchSize,
            Func<T, DateTime, IReadOnlyList<KeyValuePair<string, object>>> map,
            Func<int, int, Task>? onProgress = null,
            int progressOffset = 0,
            int progressTotal = 0)
        {
            int affectedRows = 0;
            int reportTotal = progressTotal > 0 ? progressTotal : rows.Count;
            DateTime createdDate = DateTime.Now;

            foreach (List<T> chunk in Chunk(rows, batchSize))
            {
                var sql = new StringBuilder(insertPrefix);
                var parameters = new DynamicParameters();

                for (int rowIndex = 0; rowIndex < chunk.Count; rowIndex++)
                {
                    IReadOnlyList<KeyValuePair<string, object>> values = map(chunk[rowIndex], createdDate);
                    if (rowIndex > 0)
                    {
                        sql.Append(',');
                    }

                    sql.Append('(');
                    for (int columnIndex = 0; columnIndex < values.Count; columnIndex++)
                    {
                        if (columnIndex > 0)
                        {
                            sql.Append(',');
                        }

                        string parameterName = $"{values[columnIndex].Key}_{rowIndex}";
                        sql.Append('@').Append(parameterName);
                        parameters.Add(parameterName, NormalizeBatchInsertParameterValue(values[columnIndex].Value));
                    }

                    sql.Append(')');
                }

                affectedRows += await connection.ExecuteAsync(
                    sql.ToString(),
                    parameters,
                    transaction,
                    commandTimeout: BatchInsertCommandTimeoutSeconds);

                if (onProgress != null)
                {
                    await onProgress(progressOffset + affectedRows, reportTotal);
                }
            }

            return affectedRows;
        }

        private static object? NormalizeBatchInsertParameterValue(object value)
        {
            return ReferenceEquals(value, DBNull.Value) ? null : value;
        }

        private static async Task KeepConnectionAliveAsync(MySqlConnection connection)
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await connection.ExecuteScalarAsync<int>("SELECT 1;", commandTimeout: 30);
        }

        private static int ParsePayrollUserId(string currentUserId)
        {
            return int.TryParse(currentUserId, out int userId) ? userId : 0;
        }

        private static IEnumerable<List<T>> Chunk<T>(IReadOnlyCollection<T> source, int size)
        {
            List<T> buffer = new(size);
            foreach (T item in source)
            {
                buffer.Add(item);
                if (buffer.Count == size)
                {
                    yield return buffer;
                    buffer = new List<T>(size);
                }
            }

            if (buffer.Count > 0)
            {
                yield return buffer;
            }
        }

        private sealed class CashCommissionExecutionContext
        {
            public int Year { get; init; }
            public int Month { get; init; }
            public string CityCode { get; init; } = string.Empty;
            public DateTime StartDate { get; init; }
            public DateTime EndDate { get; init; }
            public List<string> StationIds { get; init; } = new();
            public CommissionConfig Cfg { get; init; } = new CommissionConfig();
        }

        private sealed class CashCommissionPreviewBaseline
        {
            public int CashRows { get; init; }
            public int VasRows { get; init; }
            public int AcknowledgmentRows { get; init; }
        }

        private sealed class CashCommissionAuditSnapshot
        {
            public int CashRows { get; init; }
            public int CashDistinctCn { get; init; }
            public int CashDuplicateGroups { get; init; }
            public decimal CashTotalCommission { get; init; }
            public int VasRows { get; init; }
            public int VasDistinctCn { get; init; }
            public int VasDuplicateGroups { get; init; }
            public decimal VasTotalIncentive { get; init; }
        }

        private sealed class CashAuditRow
        {
            public string CnNumber { get; init; } = string.Empty;
            public string StationId { get; init; } = string.Empty;
            public string BillingType { get; init; } = string.Empty;
            public int ShipmentId { get; init; }
            public int EntryCount { get; init; }
            public decimal TotalCommission { get; init; }
            public decimal BaseCommission { get; init; }
            public decimal InsuranceCommission { get; init; }
            public decimal VasCommission { get; init; }
        }

        private sealed class CashVasAuditRow
        {
            public string CnNumber { get; init; } = string.Empty;
            public string StationId { get; init; } = string.Empty;
            public string EmpNo { get; init; } = string.Empty;
            public string CourierId { get; init; } = string.Empty;
            public int RateId { get; init; }
            public int EntryCount { get; init; }
            public string IncentivePayable { get; init; } = string.Empty;
            public string DeliveryVia { get; init; } = string.Empty;
            public string CnicDelivery { get; init; } = string.Empty;
            public decimal IncentiveRate { get; init; }
            public decimal FinalIncentive { get; init; }
        }

        private sealed class CashCommissionSourceRow
        {
            public string Billing_Type { get; init; } = string.Empty;
            public string cn_number { get; init; } = string.Empty;
            public string Station_id { get; init; } = string.Empty;
            public string? Station { get; init; }
            public DateTime billing_date { get; init; }
            public int no_of_peices { get; init; }
            public decimal weight { get; init; }
            public decimal Weight_KG { get; init; }
            public string? Weight_Type { get; init; }
            public string? Weight_Bucket { get; init; }
            public int Shipment_id { get; init; }
            public string? shipment_type { get; init; }
            public string? dest_City_id { get; init; }
            public string? Overnight_Zone { get; init; }
            public string? Overland_Zone { get; init; }
            public string? Destination { get; init; }
            public string? Dest_P { get; init; }
            public int? vendor_id { get; init; }
            public string? vendor_name { get; init; }
            public string? Leopard_id { get; init; }
            public string? Leopard_Name { get; init; }
            public int? express_id { get; init; }
            public string? express_name { get; init; }
            public string? remarks { get; init; }
            public string? client_id { get; init; }
            public string? company_id { get; init; }
            public DateTime? activity_date { get; init; }
            public decimal rate { get; init; }
            public decimal dec_value { get; init; }
            public string? Insurance_Rate { get; init; }
            public decimal Insurance_Premium_Amt { get; init; }
            public decimal Gross_Amount { get; init; }
            public string? status { get; init; }
            public string? Criteria { get; init; }
            public string? Commission_Rate { get; init; }
            public decimal BaseCommission { get; init; }
            public string? MTDWeight_Bucket { get; init; }
            public int MTDCommission { get; init; }
            public decimal InsuranceCommission { get; init; }
            public int VASCommission_Rate { get; init; }
            public int VASAmount { get; init; }
            public int VASShipment { get; init; }
            public int VASCommission { get; init; }
            public decimal mtd_Amount { get; init; }
            public decimal TotalCommission { get; init; }
        }

        private sealed class CashVasCommissionRow
        {
            public string? Billing_Type { get; init; }
            public string Cn_number { get; init; } = string.Empty;
            public string Station_id { get; init; } = string.Empty;
            public string? Station { get; init; }
            public string? Zone { get; init; }
            public DateTime Delivery_Date { get; init; }
            public string? Delivery_Time { get; init; }
            public int? Client_id { get; init; }
            public string? Client_Name { get; init; }
            public string? Shipment_id { get; init; }
            public string? Shipment_type { get; init; }
            public int No_of_peices { get; init; }
            public decimal Weight { get; init; }
            public string? Cour_id { get; init; }
            public string? Cour_Name { get; init; }
            public string? Status { get; init; }
            public string? Receiver_Name { get; init; }
            public string? Reason { get; init; }
            public string? Emp_No { get; init; }
            public string? Emp_Name { get; init; }
            public DateTime? APPOINT_DATE { get; init; }
            public DateTime? LEFT_DATE { get; init; }
            public string? CodeType { get; init; }
            public string? Cnic_No { get; init; }
            public string? Arvl_Via { get; init; }
            public int? RateID { get; init; }
            public string? Category { get; init; }
            public string? Cnic_delivery { get; init; }
            public string? Delivery_Via { get; init; }
            public string? IncentivePayable { get; init; }
            public decimal IncentiveRate { get; init; }
            public decimal Final_Incentive { get; init; }
        }
    }
}
