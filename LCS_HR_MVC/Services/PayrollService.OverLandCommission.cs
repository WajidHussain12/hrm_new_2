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
        private static readonly int[] OverLandProcessRateIds = { 1, 2, 3, 4, 5, 8, 11, 12, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95 };

        public async Task<OverLandCommissionViewModel> GetOverLandCommissionPageAsync(
            DateTime workingDate,
            string currentUserId,
            OverLandCommissionViewModel? existingModel = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();

            var model = existingModel ?? new OverLandCommissionViewModel();
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

        public async Task<OverLandCommissionProcessResult> ProcessOverLandCommissionAsync(
            OverLandCommissionViewModel model,
            string currentUserId,
            Func<int, int, Task>? onProgress = null)
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                    ?? throw new ArgumentException("Database error");

                await connection.OpenAsync();
                var oleConnId = await LogConnectionIdAsync(connection, "OverLandCommission", model.CityCode, "ProcessOverLandCommissionAsync_Start");
                var oleOverallStart = Stopwatch.StartNew();

                await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

                OverLandCommissionExecutionContext context = await PrepareOverLandCommissionExecutionContextAsync(
                    connection,
                    model.Year,
                    model.Month,
                    model.CityCode,
                    currentUserId);

                using var misConnection = await OpenExternalCodConnectionAsync("MIS");
                await LogConnectionIdAsync(misConnection, "OverLandCommission", model.CityCode, "MIS_SourceConnection");

                var oleSrcStart = Stopwatch.StartNew();
                List<OverLandCommissionRawRow> oleSourceRows = await LoadOverLandSourceRowsAsync(
                    misConnection,
                    context.StartDate,
                    context.EndDate,
                    context.StationIds,
                    context.CityCode,
                    context.Cfg);

                List<OverLandRbiSourceRow> rbiSourceRows = await LoadOverLandRbiSourceRowsAsync(
                    misConnection,
                    context.StartDate,
                    context.EndDate,
                    context.StationIds,
                    context.Cfg);
                oleSrcStart.Stop();
                LogOperationComplete("OverLandCommission", model.CityCode, "SourceLoad_MIS", oleConnId, oleSrcStart.Elapsed, oleSourceRows.Count + rbiSourceRows.Count);

                int totalSourceRows = oleSourceRows.Count + rbiSourceRows.Count;
                if (onProgress != null) await onProgress(0, totalSourceRows); // source data loaded — total known

                int oleRowsInserted;
                using (var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted))
                {
                    try
                    {
                        oleRowsInserted = await SaveOverLandRawRowsAsync(connection, transaction as MySqlTransaction, context, oleSourceRows);
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                        throw;
                    }
                }
                if (onProgress != null) await onProgress(oleRowsInserted, totalSourceRows); // OLE rows committed

                int rbiRowsInserted;
                using (var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted))
                {
                    try
                    {
                        rbiRowsInserted = await SaveOverLandRbiRowsAsync(connection, transaction as MySqlTransaction, context, rbiSourceRows, currentUserId);
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                        throw;
                    }
                }
                if (onProgress != null) await onProgress(oleRowsInserted + rbiRowsInserted, totalSourceRows); // RBI rows committed

                List<OverLandProcessRow> processRows = await LoadAllOverLandProcessRowsAsync(connection, context);
                int processRowsInserted = await SaveOverLandProcessRowsAsync(
                    connection,
                    context,
                    processRows,
                    context.Cfg.OverlandProcessRateIds,
                    true,
                    currentUserId);
                if (onProgress != null) await onProgress(oleRowsInserted + rbiRowsInserted + processRowsInserted, totalSourceRows); // all process rows committed

                using (var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted))
                {
                    try
                    {
                        await InsertOverLandAcknowledgmentAsync(connection, transaction as MySqlTransaction, currentUserId, model.BillingStatus, model.AttendanceStatus);
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                        throw;
                    }
                }

                oleOverallStart.Stop();
                LogOperationComplete("OverLandCommission", model.CityCode, "ProcessOverLand_TOTAL", oleConnId, oleOverallStart.Elapsed, oleRowsInserted + rbiRowsInserted + processRowsInserted);

                return new OverLandCommissionProcessResult
                {
                    Success = true,
                    Message = "Commission Process Execute Successfully!",
                    OleRowsInserted = oleRowsInserted,
                    RbiRowsInserted = rbiRowsInserted,
                    ProcessRowsInserted = processRowsInserted,
                    StationCount = context.StationIds.Count,
                    LocationCount = context.LocationIds.Count,
                    FromDate = context.StartDate,
                    ToDate = context.EndDate
                };
            }
            catch (ArgumentException ex)
            {
                return new OverLandCommissionProcessResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public async Task<OverLandCommissionPreviewResult> PreviewOverLandCommissionAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId,
            bool billingStatus = false,
            bool attendanceStatus = false)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();
            await connection.ExecuteAsync("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED;");

            OverLandCommissionExecutionContext context = await PrepareOverLandCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId);

            using var misConnection = await OpenExternalCodConnectionAsync("MIS");

            Console.WriteLine($"[OverLandPreview] Loading OLE source rows for {context.CityCode} {context.Year}-{context.Month:00}...");
            Console.Out.Flush();
            List<OverLandCommissionRawRow> oleSourceRows = await LoadOverLandSourceRowsAsync(
                misConnection,
                context.StartDate,
                context.EndDate,
                context.StationIds,
                context.CityCode,
                context.Cfg,
                logProgress: true);
            Console.WriteLine($"[OverLandPreview] OLE source rows loaded: {oleSourceRows.Count}");
            Console.Out.Flush();

            Console.WriteLine($"[OverLandPreview] Loading RBI source rows for {context.CityCode} {context.Year}-{context.Month:00}...");
            Console.Out.Flush();
            List<OverLandRbiSourceRow> rbiSourceRows = await LoadOverLandRbiSourceRowsAsync(
                misConnection,
                context.StartDate,
                context.EndDate,
                context.StationIds,
                context.Cfg);
            Console.WriteLine($"[OverLandPreview] RBI source rows loaded: {rbiSourceRows.Count}");
            Console.Out.Flush();

            OverLandCommissionPreviewBaseline baseline = await CaptureOverLandPreviewBaselineAsync(connection, context, currentUserId);

            using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                Console.WriteLine("[OverLandPreview] Writing raw OLE rows...");
                Console.Out.Flush();
                int oleRowsInserted = await SaveOverLandRawRowsAsync(connection, transaction as MySqlTransaction, context, oleSourceRows);
                Console.WriteLine($"[OverLandPreview] Raw OLE rows written: {oleRowsInserted}");
                Console.Out.Flush();

                Console.WriteLine("[OverLandPreview] Writing RBI detail rows...");
                Console.Out.Flush();
                int rbiRowsInserted = await SaveOverLandRbiRowsAsync(connection, transaction as MySqlTransaction, context, rbiSourceRows, currentUserId);
                Console.WriteLine($"[OverLandPreview] RBI detail rows written: {rbiRowsInserted}");
                Console.Out.Flush();

                Console.WriteLine("[OverLandPreview] Building combined process rows...");
                Console.Out.Flush();
                List<OverLandProcessRow> processRows = await LoadAllOverLandProcessRowsAsync(connection, context, transaction as MySqlTransaction);
                int processRowsInserted = await SaveOverLandProcessRowsAsync(
                    connection,
                    transaction as MySqlTransaction,
                    context,
                    processRows,
                    context.Cfg.OverlandProcessRateIds,
                    true,
                    currentUserId,
                    null);
                Console.WriteLine($"[OverLandPreview] Combined process rows written: {processRowsInserted}");
                Console.Out.Flush();
                await InsertOverLandAcknowledgmentAsync(connection, transaction as MySqlTransaction, currentUserId, billingStatus, attendanceStatus);

                decimal generatedProcessAmountTotal = processRows.Sum(static row => row.OleCommission);
                Console.WriteLine("[OverLandPreview] Rolling back preview transaction...");
                Console.Out.Flush();
                await transaction.RollbackAsync();

                return new OverLandCommissionPreviewResult
                {
                    Year = context.Year,
                    Month = context.Month,
                    CityCode = context.CityCode,
                    StationCount = context.StationIds.Count,
                    LocationCount = context.LocationIds.Count,
                    FromDate = context.StartDate,
                    ToDate = context.EndDate,
                    OleSourceRowsRetrieved = oleSourceRows.Count,
                    RbiSourceRowsRetrieved = rbiSourceRows.Count,
                    OleRowsInserted = oleRowsInserted,
                    RbiRowsInserted = rbiRowsInserted,
                    ProcessRowsInserted = processRowsInserted,
                    GeneratedProcessAmountTotal = generatedProcessAmountTotal,
                    RollbackIntegrityPreserved = await VerifyOverLandPreviewBaselineAsync(connection, context, currentUserId, baseline)
                };
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                throw;
            }
        }

        public async Task<OverLandCommissionAuditResult> AuditOverLandCommissionAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();

            OverLandCommissionExecutionContext context = await PrepareOverLandCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId,
                validateUserAccess: false,
                validateProcessOpen: false,
                rejectExistingRows: false);

            using var misConnection = await OpenExternalCodConnectionAsync("MIS");

            List<OverLandCommissionRawRow> oleSourceRows = await LoadOverLandSourceRowsAsync(
                misConnection,
                context.StartDate,
                context.EndDate,
                context.StationIds,
                context.CityCode,
                context.Cfg);

            List<OverLandRbiSourceRow> rbiSourceRows = await LoadOverLandRbiSourceRowsAsync(
                misConnection,
                context.StartDate,
                context.EndDate,
                context.StationIds,
                context.Cfg);

            OverLandCommissionAuditSnapshot historical = await CaptureOverLandAuditSnapshotAsync(connection, context);
            List<OverLandOleAuditRow> historicalOleRows = await LoadOverLandOleAuditRowsAsync(connection, context);
            List<OverLandRbiAuditRow> historicalRbiRows = await LoadOverLandRbiAuditRowsAsync(connection, context);
            List<OverLandProcessAuditRow> historicalProcessRows = await LoadOverLandProcessAuditRowsAsync(connection, context);
            OverLandCommissionPreviewBaseline previewBaseline = await CaptureOverLandPreviewBaselineAsync(connection, context, currentUserId);

            using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                await SaveOverLandRawRowsAsync(connection, transaction as MySqlTransaction, context, oleSourceRows);
                await SaveOverLandRbiRowsAsync(connection, transaction as MySqlTransaction, context, rbiSourceRows, currentUserId);

                List<OverLandProcessRow> processRows = await LoadAllOverLandProcessRowsAsync(connection, context, transaction as MySqlTransaction);
                await SaveOverLandProcessRowsAsync(
                    connection,
                    transaction as MySqlTransaction,
                    context,
                    processRows,
                    context.Cfg.OverlandProcessRateIds,
                    true,
                    currentUserId,
                    null);
                await InsertOverLandAcknowledgmentAsync(connection, transaction as MySqlTransaction, currentUserId, billingStatus: false, attendanceStatus: false);

                OverLandCommissionAuditSnapshot generated = await CaptureOverLandAuditSnapshotAsync(connection, context, transaction as MySqlTransaction);
                List<OverLandOleAuditRow> generatedOleRows = await LoadOverLandOleAuditRowsAsync(connection, context, transaction as MySqlTransaction);
                List<OverLandRbiAuditRow> generatedRbiRows = await LoadOverLandRbiAuditRowsAsync(connection, context, transaction as MySqlTransaction);
                List<OverLandProcessAuditRow> generatedProcessRows = await LoadOverLandProcessAuditRowsAsync(connection, context, transaction as MySqlTransaction);
                AuditDiffSummary oleRowDiff = BuildAuditDiffSummary(
                    historicalOleRows,
                    generatedOleRows,
                    static row => $"{row.StationId}|{row.CourierId}|{row.RateId.ToString(CultureInfo.InvariantCulture)}",
                    static row => string.Join("|",
                        row.EntryCount.ToString(CultureInfo.InvariantCulture),
                        row.NoOfShipment.ToString(CultureInfo.InvariantCulture),
                        row.NoOfPieces.ToString(CultureInfo.InvariantCulture),
                        row.TotalWeight.ToString("0.00", CultureInfo.InvariantCulture)),
                    static row => $"Station={row.StationId}, Cour={row.CourierId}, RateID={row.RateId}, Count={row.EntryCount}, Shipments={row.NoOfShipment}, Pieces={row.NoOfPieces}, Weight={row.TotalWeight:0.00}");
                AuditDiffSummary rbiRowDiff = BuildAuditDiffSummary(
                    historicalRbiRows,
                    generatedRbiRows,
                    static row => $"{row.CnNumber}|{row.CourierId}|{row.RateId.ToString(CultureInfo.InvariantCulture)}|{row.EmpNo}",
                    static row => string.Join("|",
                        row.EntryCount.ToString(CultureInfo.InvariantCulture),
                        row.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture),
                        row.TotalWeight.ToString("0.00", CultureInfo.InvariantCulture),
                        row.OldIncentive.ToString("0.00", CultureInfo.InvariantCulture),
                        row.NewIncentive.ToString("0.00", CultureInfo.InvariantCulture),
                        row.FinalIncentive.ToString("0.00", CultureInfo.InvariantCulture)),
                    static row => $"CN={row.CnNumber}, Cour={row.CourierId}, RateID={row.RateId}, Emp={row.EmpNo}, Count={row.EntryCount}, Amount={row.TotalAmount:0.00}, Weight={row.TotalWeight:0.00}, Old={row.OldIncentive:0.00}, New={row.NewIncentive:0.00}, Final={row.FinalIncentive:0.00}");
                AuditDiffSummary processRowDiff = BuildAuditDiffSummary(
                    historicalProcessRows,
                    generatedProcessRows,
                    static row => $"{row.GlLocationId.ToString(CultureInfo.InvariantCulture)}|{row.CourierId}|{row.RateId.ToString(CultureInfo.InvariantCulture)}",
                    static row => string.Join("|",
                        row.EntryCount.ToString(CultureInfo.InvariantCulture),
                        row.OleCommission.ToString("0.00", CultureInfo.InvariantCulture)),
                    static row => $"Location={row.GlLocationId}, Cour={row.CourierId}, RateID={row.RateId}, Count={row.EntryCount}, Commission={row.OleCommission:0.00}");

                await transaction.RollbackAsync();

                return new OverLandCommissionAuditResult
                {
                    Year = context.Year,
                    Month = context.Month,
                    CityCode = context.CityCode,
                    StationCount = context.StationIds.Count,
                    LocationCount = context.LocationIds.Count,
                    FromDate = context.StartDate,
                    ToDate = context.EndDate,
                    HistoricalOleRows = historical.OleRows,
                    GeneratedOleRows = generated.OleRows,
                    HistoricalOleWeightTotal = historical.OleWeightTotal,
                    GeneratedOleWeightTotal = generated.OleWeightTotal,
                    HistoricalOleDuplicateGroups = historical.OleDuplicateGroups,
                    GeneratedOleDuplicateGroups = generated.OleDuplicateGroups,
                    HistoricalRbiRows = historical.RbiRows,
                    GeneratedRbiRows = generated.RbiRows,
                    HistoricalRbiFinalIncentiveTotal = historical.RbiFinalIncentiveTotal,
                    GeneratedRbiFinalIncentiveTotal = generated.RbiFinalIncentiveTotal,
                    HistoricalProcessRows = historical.ProcessRows,
                    GeneratedProcessRows = generated.ProcessRows,
                    HistoricalProcessAmountTotal = historical.ProcessAmountTotal,
                    GeneratedProcessAmountTotal = generated.ProcessAmountTotal,
                    HistoricalProcessDuplicateGroups = historical.ProcessDuplicateGroups,
                    GeneratedProcessDuplicateGroups = generated.ProcessDuplicateGroups,
                    OleRowDiff = oleRowDiff,
                    RbiRowDiff = rbiRowDiff,
                    ProcessRowDiff = processRowDiff,
                    HistoricalParityMatch = generated.OleRows == historical.OleRows
                        && generated.OleWeightTotal == historical.OleWeightTotal
                        && generated.OleDuplicateGroups == historical.OleDuplicateGroups
                        && generated.RbiRows == historical.RbiRows
                        && generated.RbiFinalIncentiveTotal == historical.RbiFinalIncentiveTotal
                        && generated.ProcessRows == historical.ProcessRows
                        && generated.ProcessAmountTotal == historical.ProcessAmountTotal
                        && generated.ProcessDuplicateGroups == historical.ProcessDuplicateGroups
                        && oleRowDiff.HistoricalOnlyCount == 0
                        && oleRowDiff.GeneratedOnlyCount == 0
                        && oleRowDiff.ValueMismatchCount == 0
                        && rbiRowDiff.HistoricalOnlyCount == 0
                        && rbiRowDiff.GeneratedOnlyCount == 0
                        && rbiRowDiff.ValueMismatchCount == 0
                        && processRowDiff.HistoricalOnlyCount == 0
                        && processRowDiff.GeneratedOnlyCount == 0
                        && processRowDiff.ValueMismatchCount == 0,
                    RollbackIntegrityPreserved = await VerifyOverLandPreviewBaselineAsync(connection, context, currentUserId, previewBaseline)
                };
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                throw;
            }
        }

        public async Task<OverLandSourceAuditResult> AuditOverLandSourceAsync(
            int year,
            int month,
            string cityCode,
            string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection
                ?? throw new ArgumentException("Database error");

            await connection.OpenAsync();

            OverLandCommissionExecutionContext context = await PrepareOverLandCommissionExecutionContextAsync(
                connection,
                year,
                month,
                cityCode,
                currentUserId,
                validateUserAccess: false,
                validateProcessOpen: false,
                rejectExistingRows: false);

            List<OverLandOleAuditRow> historicalOleRows = await LoadOverLandOleAuditRowsAsync(connection, context);
            List<OverLandRbiAuditRow> historicalRbiRows = await LoadOverLandRbiAuditRowsAsync(connection, context);

            using var misConnection = await OpenExternalCodConnectionAsync("MIS");

            List<OverLandCommissionRawRow> currentOleSourceRows = await LoadOverLandSourceRowsAsync(
                misConnection,
                context.StartDate,
                context.EndDate,
                context.StationIds,
                context.CityCode,
                context.Cfg);

            List<OverLandRbiSourceRow> currentRbiSourceRows = await LoadOverLandRbiSourceRowsAsync(
                misConnection,
                context.StartDate,
                context.EndDate,
                context.StationIds,
                context.Cfg);

            List<OverLandOleAuditRow> currentOleAuditRows = currentOleSourceRows
                .GroupBy(static row => new
                {
                    StationId = NullSafe(row.Station_id),
                    CourierId = NullSafe(row.cour_id),
                    row.RateID
                })
                .Select(static group => new OverLandOleAuditRow
                {
                    StationId = group.Key.StationId,
                    CourierId = group.Key.CourierId,
                    RateId = group.Key.RateID,
                    EntryCount = group.Count(),
                    NoOfShipment = group.Sum(static row => row.No_Of_Shipment),
                    NoOfPieces = group.Sum(static row => row.No_Of_Pieces),
                    TotalWeight = Math.Round(group.Sum(static row => row.Total_Weight), 2)
                })
                .OrderBy(static row => row.StationId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.CourierId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.RateId)
                .ToList();

            List<OverLandRbiAuditRow> currentRbiAuditRows = currentRbiSourceRows
                .GroupBy(static row => new
                {
                    CnNumber = NullSafe(row.cn_number),
                    CourierId = NullSafe(row.cour_id),
                    row.RateID,
                    EmpNo = NullSafe(row.Emp_No)
                })
                .Select(static group => new OverLandRbiAuditRow
                {
                    CnNumber = group.Key.CnNumber,
                    CourierId = group.Key.CourierId,
                    RateId = group.Key.RateID,
                    EmpNo = group.Key.EmpNo,
                    EntryCount = group.Count(),
                    TotalAmount = Math.Round(group.Sum(static row => row.Total_Amount), 2),
                    TotalWeight = Math.Round(group.Sum(static row => row.Total_Weight), 2),
                    OldIncentive = Math.Round(group.Sum(static row => row.OldIncentive), 2),
                    NewIncentive = Math.Round(group.Sum(static row => row.NewIncentive), 2),
                    FinalIncentive = Math.Round(group.Sum(static row => row.FinalIncentive), 2)
                })
                .OrderBy(static row => row.CnNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.CourierId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.RateId)
                .ThenBy(static row => row.EmpNo, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AuditDiffSummary oleRowDiff = BuildAuditDiffSummary(
                historicalOleRows,
                currentOleAuditRows,
                static row => $"{row.StationId}|{row.CourierId}|{row.RateId.ToString(CultureInfo.InvariantCulture)}",
                static row => string.Join("|",
                    row.EntryCount.ToString(CultureInfo.InvariantCulture),
                    row.NoOfShipment.ToString(CultureInfo.InvariantCulture),
                    row.NoOfPieces.ToString(CultureInfo.InvariantCulture),
                    row.TotalWeight.ToString("0.00", CultureInfo.InvariantCulture)),
                static row => $"Station={row.StationId}, Cour={row.CourierId}, RateID={row.RateId}, Count={row.EntryCount}, Shipments={row.NoOfShipment}, Pieces={row.NoOfPieces}, Weight={row.TotalWeight:0.00}");

            AuditDiffSummary rbiRowDiff = BuildAuditDiffSummary(
                historicalRbiRows,
                currentRbiAuditRows,
                static row => $"{row.CnNumber}|{row.CourierId}|{row.RateId.ToString(CultureInfo.InvariantCulture)}|{row.EmpNo}",
                static row => string.Join("|",
                    row.EntryCount.ToString(CultureInfo.InvariantCulture),
                    row.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture),
                    row.TotalWeight.ToString("0.00", CultureInfo.InvariantCulture),
                    row.OldIncentive.ToString("0.00", CultureInfo.InvariantCulture),
                    row.NewIncentive.ToString("0.00", CultureInfo.InvariantCulture),
                    row.FinalIncentive.ToString("0.00", CultureInfo.InvariantCulture)),
                static row => $"CN={row.CnNumber}, Cour={row.CourierId}, RateID={row.RateId}, Emp={row.EmpNo}, Count={row.EntryCount}, Amount={row.TotalAmount:0.00}, Weight={row.TotalWeight:0.00}, Old={row.OldIncentive:0.00}, New={row.NewIncentive:0.00}, Final={row.FinalIncentive:0.00}");

            return new OverLandSourceAuditResult
            {
                Year = context.Year,
                Month = context.Month,
                CityCode = context.CityCode,
                FromDate = context.StartDate,
                ToDate = context.EndDate,
                StationCount = context.StationIds.Count,
                HistoricalOleRows = historicalOleRows.Count,
                CurrentOleRows = currentOleAuditRows.Count,
                HistoricalOleWeightTotal = historicalOleRows.Sum(static row => row.TotalWeight),
                CurrentOleWeightTotal = currentOleAuditRows.Sum(static row => row.TotalWeight),
                HistoricalRbiRows = historicalRbiRows.Count,
                CurrentRbiRows = currentRbiAuditRows.Count,
                HistoricalRbiFinalIncentiveTotal = historicalRbiRows.Sum(static row => row.FinalIncentive),
                CurrentRbiFinalIncentiveTotal = currentRbiAuditRows.Sum(static row => row.FinalIncentive),
                OleRowDiff = oleRowDiff,
                RbiRowDiff = rbiRowDiff
            };
        }

        private async Task<OverLandCommissionExecutionContext> PrepareOverLandCommissionExecutionContextAsync(
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
                commandTimeout: 30)).ToList();

            if (stationIds.Count == 0)
            {
                throw new ArgumentException("Record Not Found");
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

            if (rejectExistingRows)
            {
                var info = await connection.QueryFirstOrDefaultAsync(
                    $@"SELECT t.CreatedDate AS ProcessedDate, t.CreatedBy, u.UserName
                      FROM {OleCommissionProcessTable} t
                      LEFT JOIN lcs_users u ON u.userID = t.CreatedBy
                      WHERE t.Year = @Year AND t.Month = @Month AND t.GlLocationId IN @LocationIds AND t.RateId IN @RateIds
                      ORDER BY t.CreatedDate DESC LIMIT 1",
                    new { Year = year, Month = month, LocationIds = locationIds.ToArray(), RateIds = OverLandProcessRateIds },
                    commandTimeout: 30);
                if (info?.ProcessedDate != null)
                {
                    throw new ArgumentException($"Already Processed on {((DateTime)info.ProcessedDate):dd-MMM-yyyy}.{BuildProcessedByInfo(info.CreatedBy?.ToString(), info.UserName?.ToString())}");
                }
            }

            Dictionary<int, OverLandPolicyRow> policies = (await connection.QueryAsync<OverLandPolicyRow>(
                @"SELECT RateID, Type, RateType, Rate, IsPercent FROM hr_commissionpolicy;",
                commandTimeout: 30))
                .ToDictionary(static row => row.RateID);

            if (policies.Count == 0)
            {
                throw new ArgumentException("Commission Rates not defined.");
            }

            return new OverLandCommissionExecutionContext
            {
                Year = year,
                Month = month,
                CityCode = normalizedCityCode,
                StartDate = startDate,
                EndDate = endDate,
                StationIds = stationIds,
                LocationIds = locationIds,
                Policies = policies,
                UserId = ParsePayrollUserId(currentUserId),
                Cfg = cfg
            };
        }

        private async Task<List<OverLandCommissionRawRow>> LoadOverLandSourceRowsAsync(
            MySqlConnection connection,
            DateTime startDate,
            DateTime endDate,
            IReadOnlyCollection<string> stationIds,
            string cityCode,
            CommissionConfig cfg,
            bool logProgress = false)
        {
            const int sourceQueryTimeoutSeconds = 1800;
            const int longRunningQueryTimeoutSeconds = 3600;
            var parameters = new { StartDate = startDate, EndDate = endDate, StationIds = stationIds.ToArray(), CityCode = cityCode };
            var rows = new List<OverLandCommissionRawRow>();

            void LogBranchCompleted(string branchName, int rowCount, Stopwatch stopwatch)
            {
                _logger?.LogInformation(
                    "OLE {Branch} completed: {Rows} rows in {Seconds:F1}s",
                    branchName,
                    rowCount,
                    stopwatch.Elapsed.TotalSeconds);
            }

            await EnsureExternalConnectionResponsiveAsync(connection, "MIS");
            var baseQueries = new (string Name, string Query)[]
            {
                (
                    "rate_1_credit_overland",
                    $@"SELECT a.Station_id,a.cour_id,a.cour_name,COUNT(DISTINCT a.cn_number) AS No_Of_Shipment,COUNT(a.no_of_peices) AS No_Of_Pieces,SUM(a.weight/1000) AS Total_Weight,1 AS RateID
FROM lcs_billing_download.billing_details a
WHERE a.shipment_type_id IN ({cfg.OverlandExpressIncludeShipmentTypesCsv}) AND a.client_id<>'{cfg.DummyClientId}' AND a.Billing_Type='CREDIT' AND a.billing_date BETWEEN @StartDate AND @EndDate AND a.Station_id IN @StationIds AND a.amount<>0 AND a.cour_id<>'{cfg.DummyCourierId}' AND a.is_deleted=0
GROUP BY a.Station_id,a.cour_id"
                ),
                (
                    "rate_2_3_dispatch",
                    $@"SELECT x.Station_id,'' AS cour_id,'' AS cour_name,COUNT(DISTINCT x.cn_number) AS No_Of_Shipment,SUM(x.NUMBER_PIECES) AS No_Of_Pieces,SUM(x.Weight) AS Total_Weight,x.RateID
FROM (
    SELECT d.Station_id,d.cn_number,MAX(d.NUMBER_PIECES) AS NUMBER_PIECES,MAX(t.weight/1000) AS Weight,CASE WHEN d.Station_id=d.ORIGON_CITY_ID THEN 2 ELSE 3 END AS RateID
    FROM (
        SELECT d.Station_id,d.cn_number,b.weight,MAX(CONCAT(d.BOOK_DATE,'',d.BOOK_TIME)) AS MaxTime
        FROM lcs_db.book_dispatch d
        INNER JOIN lcs_billing_download.billing_details b ON d.CN_NUMBER=b.cn_number AND b.is_deleted=0
        INNER JOIN lcs_billing_download.shipment_codes s ON b.shipment_type_id=s.shipment_code_id
        WHERE b.shipment_type_id IN ({cfg.OverlandExpressIncludeShipmentTypesCsv}) AND d.STATUS_CODE IN ('DP','FW') AND d.BOOK_DATE BETWEEN @StartDate AND @EndDate AND d.Station_id IN @StationIds
        GROUP BY d.Station_id,d.cn_number
    ) t
    INNER JOIN lcs_db.book_dispatch d ON d.CN_NUMBER=t.CN_NUMBER AND CONCAT(d.BOOK_DATE,'',d.BOOK_TIME)=t.MaxTime
    WHERE d.STATUS_CODE IN ('DP','FW')
      AND d.Station_id NOT IN (IF(@CityCode='001','00592',IF(@CityCode='002','00789',0)))
    GROUP BY d.Station_id,d.cn_number
) x
INNER JOIN lcs.city c ON x.Station_id=c.CITY_ID
GROUP BY x.Station_id,RateID"
                ),
                (
                    "rate_4_delivery",
                    $@"SELECT ar.ARVL_DEST,'' AS COURIER_ID,'' AS Cour_Name,COUNT(DISTINCT ar.cn_number) AS No_Of_Shipment,COUNT(ar.PCS) AS No_Of_Pieces,SUM(b.weight/1000) AS Total_Weight,4 AS RateID
FROM lcs_db.arival ar
INNER JOIN lcs_billing_download.billing_details b ON ar.CN_NUMBER=b.cn_number AND b.is_deleted=0
INNER JOIN lcs_billing_download.shipment_codes s ON b.shipment_type_id=s.shipment_code_id
WHERE b.shipment_type_id IN ({cfg.OverlandExpressIncludeShipmentTypesCsv}) AND ar.STATUS='DV' AND ar.DELIVERY_DATE BETWEEN @StartDate AND @EndDate AND ar.ARVL_DEST IN @StationIds
GROUP BY ar.ARVL_DEST,RateID"
                ),
                (
                    "rate_6_credit_domestic",
                    $@"SELECT a.Station_id,a.cour_id,MAX(a.cour_name) AS cour_name,COUNT(DISTINCT a.cn_number) AS No_Of_Shipment,COUNT(a.no_of_peices) AS No_Of_Pieces,SUM(a.weight/1000) AS Total_Weight,6 AS RateID
FROM lcs_billing_download.billing_details a
WHERE a.shipment_type_id NOT IN ({cfg.OverlandCreditExcludeShipmentTypesCsv}) AND a.client_id<>'{cfg.DummyClientId}' AND a.Billing_Type='CREDIT' AND a.Station_id<>a.dest_City_id AND a.billing_date BETWEEN @StartDate AND @EndDate AND a.Station_id IN @StationIds AND a.amount<>0 AND a.cour_id<>'{cfg.DummyCourierId}' AND a.is_deleted=0
GROUP BY a.cour_id,a.Station_id"
                ),
                (
                    "rate_7_credit_local",
                    $@"SELECT a.Station_id,a.cour_id,MAX(a.cour_name) AS cour_name,COUNT(DISTINCT a.cn_number) AS No_Of_Shipment,COUNT(a.no_of_peices) AS No_Of_Pieces,SUM(a.weight/1000) AS Total_Weight,7 AS RateID
FROM lcs_billing_download.billing_details a
WHERE a.shipment_type_id NOT IN ({cfg.OverlandCreditExcludeShipmentTypesCsv}) AND a.client_id<>'{cfg.DummyClientId}' AND a.Billing_Type='CREDIT' AND a.Station_id=a.dest_City_id AND a.billing_date BETWEEN @StartDate AND @EndDate AND a.Station_id IN @StationIds AND a.amount<>0 AND a.cour_id<>'{cfg.DummyCourierId}' AND a.is_deleted=0
GROUP BY a.cour_id,a.Station_id"
                ),
                (
                    "rate_8_credit_express",
                    $@"SELECT a.Station_id,a.cour_id,MAX(a.cour_name) AS cour_name,COUNT(DISTINCT a.cn_number) AS No_Of_Shipment,COUNT(a.no_of_peices) AS No_Of_Pieces,SUM(a.weight/1000) AS Total_Weight,8 AS RateID
FROM lcs_billing_download.billing_details a
WHERE a.shipment_type_id IN ({cfg.OverlandDeliveryOpsShipmentTypesCsv}) AND a.client_id<>'{cfg.DummyClientId}' AND a.Billing_Type='CREDIT' AND a.billing_date BETWEEN @StartDate AND @EndDate AND a.Station_id IN @StationIds AND a.amount<>0 AND a.cour_id<>'{cfg.DummyCourierId}' AND a.is_deleted=0
GROUP BY a.cour_id,a.Station_id"
                )
            };

            foreach ((string name, string query) in baseQueries)
            {
                Stopwatch branchStopwatch = Stopwatch.StartNew();
                if (logProgress)
                {
                    Console.WriteLine($"[OverLandPreview] OLE source branch {name} starting...");
                    Console.Out.Flush();
                }

                await EnsureExternalConnectionResponsiveAsync(connection, "MIS");
                List<OverLandCommissionRawRow> branchRows = (await connection.QueryAsync<OverLandCommissionRawRow>(
                    query,
                    parameters,
                    commandTimeout: sourceQueryTimeoutSeconds)).ToList();
                branchStopwatch.Stop();
                LogBranchCompleted(name, branchRows.Count, branchStopwatch);

                if (logProgress)
                {
                    Console.WriteLine($"[OverLandPreview] OLE source branch {name} returned {branchRows.Count} row(s) in {branchStopwatch.Elapsed.TotalSeconds:F1}s");
                    Console.Out.Flush();
                }

                rows.AddRange(branchRows);
            }

            Stopwatch dropTempStopwatch = Stopwatch.StartNew();
            await connection.ExecuteAsync(
                "DROP TEMPORARY TABLE IF EXISTS tmp_overland_delivery_candidates;",
                commandTimeout: sourceQueryTimeoutSeconds);
            dropTempStopwatch.Stop();
            LogBranchCompleted("delivery_candidates_drop", 0, dropTempStopwatch);

            await EnsureExternalConnectionResponsiveAsync(connection, "MIS");
            Stopwatch createTempStopwatch = Stopwatch.StartNew();
            await connection.ExecuteAsync(
                @"CREATE TEMPORARY TABLE tmp_overland_delivery_candidates AS
                  SELECT cn_number,Arvl_Origin,STATUS,delivery_date,ACTIVITY_DATE,ACTIVITY_TIME,COURIER_ID,Cour_Name,pcs,WEIGHT,ARVL_DEST
                  FROM lcs_db.arival
                  WHERE COUR_DATE BETWEEN @StartDate AND @EndDate
                    AND STATUS IN ('DV','DS','DR','DW')
                    AND ARVL_DEST IN @StationIds
                    AND NOT EXISTS (SELECT 1 FROM lcs_db.cod_ranges r WHERE SHART_CN BETWEEN r.start_range AND r.end_range)
                    AND NOT EXISTS (SELECT 1 FROM lcs_hr.temp_cn tc WHERE tc.cn = cn_number)
                    AND SUBSTR(CN_NUMBER,1,2) <> 'ZZ'
                    AND LENGTH(CN_NUMBER) = 12;",
                new { StartDate = startDate, EndDate = endDate, StationIds = stationIds.ToArray() },
                commandTimeout: longRunningQueryTimeoutSeconds);
            createTempStopwatch.Stop();

            int deliveryCandidateCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM tmp_overland_delivery_candidates;",
                commandTimeout: sourceQueryTimeoutSeconds);
            LogBranchCompleted("delivery_candidates_temp_create", deliveryCandidateCount, createTempStopwatch);

            Stopwatch indexTempStopwatch = Stopwatch.StartNew();
            await connection.ExecuteAsync(
                @"ALTER TABLE tmp_overland_delivery_candidates
                    ADD INDEX idx_tmp_overland_delivery_cn (cn_number),
                    ADD INDEX idx_tmp_overland_delivery_origin (cn_number, Arvl_Origin),
                    ADD INDEX idx_tmp_overland_delivery_dest (ARVL_DEST, COURIER_ID);",
                commandTimeout: sourceQueryTimeoutSeconds);
            indexTempStopwatch.Stop();
            LogBranchCompleted("delivery_candidates_index", deliveryCandidateCount, indexTempStopwatch);

            const string deliveryQuery = @"
SELECT * FROM (
    SELECT g.shipment_type_id AS BillingShipmentTypeId,a.CN_NUMBER,g.cn_number AS BillingCN,e.CN_NUMBER AS BookIssueCn,a.STATUS AS Status,
           delivery_date AS DeliveryDate,a.courier_id AS CourId,a.cour_name AS CourName,a.Pcs AS Pieces,(g.Weight/1000) AS Weight,
           e.book_type_code AS IssueBookType,g.Client_id AS ClientId,h.clnt_name AS ClientName,g.amount AS BillingAmount,
           g.Station_id AS BillingOrigin,g.dest_City_id AS BillingDestination,e.ORIGON_CITY_ID AS BookTypeOriginCity,
           e.DEST_CITY_ID AS BookTypeDestCity,a.Arvl_Origin AS ArrivalOrgCity,a.ARVL_DEST AS ArrivalDestCity,(g.Weight/1000) AS Billing_Weight
    FROM tmp_overland_delivery_candidates a
    LEFT JOIN lcs_db.book_dispatch e ON a.cn_number=e.cn_number AND a.arvl_origin=e.station_id AND e.STATUS_CODE='CB'
    LEFT JOIN lcs_billing_download.billing_details g ON a.cn_number=g.cn_number AND is_deleted=0
    LEFT JOIN lcs_billing_download.client h ON g.client_id=h.clnt_id AND g.station_id=h.city_id
    LEFT JOIN lcs_db.client_vas v ON h.clnt_id=v.CLNT_ID AND h.city_id=v.City_id
    LEFT JOIN lcs_hr.hr_city c ON a.ARVL_DEST=c.station_id
    LEFT JOIN lcs_hr.hr_employeeroutecode rc ON c.Code=rc.citycode AND a.COURIER_ID=rc.RouteCode AND rc.ToDate IS NULL
    WHERE ((rc.CodeType = 4 AND v.CLNT_ID IS NOT NULL) OR (v.CLNT_ID IS NULL))
    ORDER BY CONCAT(a.ACTIVITY_DATE,' ',a.ACTIVITY_TIME) DESC
) xb
    GROUP BY xb.CN_NUMBER;";

            await EnsureExternalConnectionResponsiveAsync(connection, "MIS");
            Stopwatch deliveryStopwatch = Stopwatch.StartNew();
            if (logProgress)
            {
                Console.WriteLine("[OverLandPreview] OLE delivery-source branch starting...");
                Console.Out.Flush();
            }

            List<OverLandDeliverySourceRow> deliveryRows = (await connection.QueryAsync<OverLandDeliverySourceRow>(
                deliveryQuery,
                new { StartDate = startDate, EndDate = endDate, StationIds = stationIds.ToArray() },
                commandTimeout: longRunningQueryTimeoutSeconds)).ToList();
            deliveryStopwatch.Stop();
            LogBranchCompleted("delivery_source", deliveryRows.Count, deliveryStopwatch);

            if (logProgress)
            {
                Console.WriteLine($"[OverLandPreview] OLE delivery-source branch returned {deliveryRows.Count} row(s) in {deliveryStopwatch.Elapsed.TotalSeconds:F1}s");
                Console.Out.Flush();
            }

            Stopwatch deliveryAggregationStopwatch = Stopwatch.StartNew();
            List<OverLandCommissionRawRow> deliveryCommissionRows = BuildDeliveryCommissionRows(deliveryRows);
            deliveryAggregationStopwatch.Stop();
            LogBranchCompleted("delivery_aggregation", deliveryCommissionRows.Count, deliveryAggregationStopwatch);
            rows.AddRange(deliveryCommissionRows);
            return rows;
        }

        private static List<OverLandCommissionRawRow> BuildDeliveryCommissionRows(IEnumerable<OverLandDeliverySourceRow> sourceRows)
        {
            List<OverLandDeliverySourceRow> rows = sourceRows.ToList();
            foreach (OverLandDeliverySourceRow row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.BillingCN))
                {
                    row.RateId = new[] { 3, 8 }.Contains(row.BillingShipmentTypeId) ? 5 : row.ArrivalOrgCity == row.ArrivalDestCity ? 11 : 12;
                }
                else if (!string.IsNullOrWhiteSpace(row.BookIssueCn))
                {
                    row.RateId = new[] { "OVERLAND", "LOGISTIC OVERLAND" }.Contains(row.IssueBookType ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        ? 5
                        : row.ArrivalOrgCity == row.ArrivalDestCity ? 11 : 12;
                }
                else
                {
                    row.RateId = row.Weight > 2 ? 5 : row.ArrivalOrgCity == row.ArrivalDestCity ? 11 : 12;
                }
            }

            return rows
                .GroupBy(static row => new { row.ArrivalDestCity, row.CourId, row.RateId })
                .Select(static group =>
                {
                    OverLandDeliverySourceRow first = group.First();
                    return new OverLandCommissionRawRow
                    {
                        Station_id = group.Key.ArrivalDestCity ?? string.Empty,
                        cour_id = group.Key.CourId ?? string.Empty,
                        cour_name = first.CourName ?? string.Empty,
                        No_Of_Shipment = group.Count(),
                        No_Of_Pieces = group.Sum(static row => row.Pieces),
                        Total_Weight = group.Sum(static row => row.Weight),
                        RateID = group.Key.RateId
                    };
                })
                .ToList();
        }

        private async Task<List<OverLandRbiSourceRow>> LoadOverLandRbiSourceRowsAsync(
            MySqlConnection connection,
            DateTime startDate,
            DateTime endDate,
            IReadOnlyCollection<string> stationIds,
            CommissionConfig cfg)
        {
            await EnsureExternalConnectionResponsiveAsync(connection, "MIS");
            Stopwatch rbiStopwatch = Stopwatch.StartNew();
           string cebSplitDateStr = cfg.CebSplitDate.ToString("yyyy-MM-dd");
            // Real March 2026 RBI data shows Pickup Leopards (CodeType 5) on nominal-value
            // corporate bookings use a flat legacy old incentive of 5.00 instead of the
            // generic RBI local/domestic or weight-based formulas.
            string query = $@"
WITH rbi_lookup AS (
    SELECT
        Client_ID,
        Station_Id,
        MAX(Local_Rate) AS Local_Rate,
        MAX(Domestic_Rate) AS Domestic_Rate
    FROM lcs_billing_download.rbi_clients
    GROUP BY Client_ID, Station_Id
),
incentives AS (
    SELECT sub.Station_id,sub.cour_id,sub.cour_name,sub.ShipmentType,sub.WeightSlab,sub.cn_number,sub.shipment_type_id,
           sub.ShipmentCategory,sub.client_id,sub.ClientName,sub.Total_Amount,sub.Total_Weight,sub.RateID,sub.OldRateID,
           sub.ClientAcntOpen,sub.ClientStatus,sub.code,sub.CodeType,sub.RBIClinetID,sub.Emp_No,sub.RBIExclude,
           (CASE
               WHEN sub.CodeType IN (3,10,11) THEN 0
               WHEN sub.CodeType = 9 AND sub.RateID IN (84,85,86,87,88,89) THEN 0
               WHEN sub.CodeType = 5 AND sub.Total_Amount < 1 AND sub.RateID IN (85,87,89,91) THEN 5
               WHEN sub.ShipmentType='Local' AND sub.RBIClinetID IS NULL AND sub.RateID IN (84,85,86,87,88,89) THEN COALESCE(sub.RbiLocalRate,1)
               WHEN sub.ShipmentType='Domestic' AND sub.RBIClinetID IS NULL AND sub.RateID IN (84,85,86,87,88,89) THEN COALESCE(sub.RbiDomesticRate,2)
               WHEN sub.RateID = 92 THEN 0.2 WHEN sub.RateID = 93 THEN 0.35 WHEN sub.RateID = 94 THEN 0.35 WHEN sub.RateID = 95 THEN 0.5
               WHEN sub.RateID IN (90,91) AND sub.CodeType IN (4,9) AND sub.RBIClinetID IS NOT NULL THEN sub.Total_Weight * 0.2
               WHEN sub.RateID IN (90,91) THEN sub.Total_Weight * 0.2
               ELSE 0
           END) AS OldIncentive,
           (CASE
               WHEN sub.CodeType IN (3,10,11) THEN 0
               WHEN sub.RateID IN (90,91) AND sub.CodeType IN (4,9) THEN 0
               WHEN sub.RateID = 90 AND sub.RBIExclude = 0 AND sub.RBIClinetID IS NULL THEN sub.Total_Amount * 1.75 / 100
               WHEN sub.RateID = 91 AND sub.RBIExclude = 0 AND sub.RBIClinetID IS NULL THEN sub.Total_Amount * 2 / 100
               WHEN sub.CodeType IN (4) AND sub.RBIClinetID IS NULL THEN 0
               WHEN sub.RateID = 84 AND sub.RBIExclude = 0 THEN sub.Total_Amount * 5.5 / 100
               WHEN sub.RateID = 85 AND sub.RBIExclude = 0 THEN sub.Total_Amount * 10 / 100
               WHEN sub.RateID = 86 AND sub.RBIExclude = 0 THEN sub.Total_Amount * 1.75 / 100
               WHEN sub.RateID = 87 AND sub.RBIExclude = 0 THEN sub.Total_Amount * 2.5 / 100
               WHEN sub.RateID = 88 AND sub.RBIExclude = 0 THEN sub.Total_Amount * 1.5 / 100
               WHEN sub.RateID = 89 AND sub.RBIExclude = 0 THEN sub.Total_Amount * 2 / 100
               ELSE 0
           END) AS NewIncentive
    FROM (
        SELECT a.Station_id,a.cour_id,a.cour_name,IF(a.Station_Id=a.dest_City_id,'Local','Domestic') AS ShipmentType,
               IF(a.weight/1000 <= 2,'UpTo2Kg','Above2Kg') AS WeightSlab,a.cn_number,a.shipment_type_id,
               CASE WHEN a.shipment_type_id=1 THEN 'Overnight' WHEN a.shipment_type_id=2 THEN 'Detain' WHEN a.shipment_type_id=3 THEN 'Overland' WHEN a.shipment_type_id=8 THEN 'LOGISTIC OVERLAND' WHEN a.shipment_type_id=9 THEN 'GR' WHEN a.shipment_type_id=10 THEN 'Flyer' ELSE 'Unknown' END AS ShipmentCategory,
               a.client_id,c.clnt_name AS ClientName,a.amount AS Total_Amount,a.weight/1000 AS Total_Weight,c.acnt_open_date AS ClientAcntOpen,
               IF(c.acnt_open_date <= '{cebSplitDateStr} 00:00:00','Existing','New Acquisition') AS ClientStatus,hr_c.code,IFNULL(hr_rc.codetype,0) AS CodeType,
               r.Client_ID AS RBIClinetID,r.Local_Rate AS RbiLocalRate,r.Domestic_Rate AS RbiDomesticRate,hr_rc.EMP_NO AS Emp_No,IF(hr_rc.rbiexclude=b'1',1,0) AS RBIExclude,
               (CASE
                   WHEN a.shipment_type_id IN (3,8) AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN 90
                   WHEN a.shipment_type_id IN (3,8) AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN 91
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType NOT IN (9) AND a.weight/1000 <= 2 AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN CASE WHEN r.Client_ID IS NOT NULL AND a.Station_Id = a.dest_City_id THEN 92 WHEN r.Client_ID IS NOT NULL AND a.Station_Id <> a.dest_City_id THEN 94 ELSE 84 END
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType NOT IN (9) AND a.weight/1000 <= 2 AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN CASE WHEN r.Client_ID IS NOT NULL AND a.Station_Id = a.dest_City_id THEN 93 WHEN r.Client_ID IS NOT NULL AND a.Station_Id <> a.dest_City_id THEN 95 ELSE 85 END
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType NOT IN (9) AND a.weight/1000 > 2 AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN CASE WHEN r.Client_ID IS NOT NULL AND a.Station_Id = a.dest_City_id THEN 92 WHEN r.Client_ID IS NOT NULL AND a.Station_Id <> a.dest_City_id THEN 94 ELSE 86 END
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType NOT IN (9) AND a.weight/1000 > 2 AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN CASE WHEN r.Client_ID IS NOT NULL AND a.Station_Id = a.dest_City_id THEN 93 WHEN r.Client_ID IS NOT NULL AND a.Station_Id <> a.dest_City_id THEN 95 ELSE 87 END
                   WHEN a.shipment_type_id = 2 AND CodeType NOT IN (9) AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN CASE WHEN r.Client_ID IS NOT NULL AND a.Station_Id = a.dest_City_id THEN 92 WHEN r.Client_ID IS NOT NULL AND a.Station_Id <> a.dest_City_id THEN 94 ELSE 88 END
                   WHEN a.shipment_type_id = 2 AND CodeType NOT IN (9) AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN CASE WHEN r.Client_ID IS NOT NULL AND a.Station_Id = a.dest_City_id THEN 93 WHEN r.Client_ID IS NOT NULL AND a.Station_Id <> a.dest_City_id THEN 95 ELSE 89 END
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='Local' AND a.weight/1000 <= 2 AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN 92
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='Domestic' AND a.weight/1000 <= 2 AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN 94
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='local' AND a.weight/1000 <= 2 AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN 93
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='Domestic' AND a.weight/1000 <= 2 AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN 95
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='local' AND a.weight/1000 > 2 AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN 92
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='Domestic' AND a.weight/1000 > 2 AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN 94
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='local' AND a.weight/1000 > 2 AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN 93
                   WHEN a.shipment_type_id IN (1,9,10) AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='Domestic' AND a.weight/1000 > 2 AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN 95
                   WHEN a.shipment_type_id = 2 AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='local' AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN 92
                   WHEN a.shipment_type_id = 2 AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='Domestic' AND c.ACNT_OPEN_DATE <= '{cebSplitDateStr} 00:00:00' THEN 94
                   WHEN a.shipment_type_id = 2 AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='local' AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN 93
                   WHEN a.shipment_type_id = 2 AND CodeType IN (4,9) AND IF(a.Station_Id=a.dest_City_id,'Local','Domestic')='Domestic' AND c.ACNT_OPEN_DATE > '{cebSplitDateStr} 00:00:00' THEN 95
                   ELSE 0
               END) AS RateID,
               (CASE
                   WHEN a.shipment_type_id IN (3,8) THEN 1
                   WHEN a.Station_Id = a.dest_City_id AND r.Client_ID IS NULL AND a.shipment_type_id IN (1,2,9,10) THEN 7
                   WHEN a.Station_Id <> a.dest_City_id AND r.Client_ID IS NULL AND a.shipment_type_id IN (1,2,9,10) THEN 6
                   ELSE 0
               END) AS OldRateID
        FROM lcs_billing_download.billing_details a
        INNER JOIN lcs_billing_download.client c ON c.clnt_id = a.client_id AND a.Station_id = c.city_id
        INNER JOIN lcs_hr.hr_city hr_c ON a.Station_Id = hr_c.Station_Id
        LEFT JOIN lcs_hr.hr_employeeroutecode hr_rc ON a.cour_id = hr_rc.routecode AND hr_c.code = hr_rc.citycode
        LEFT JOIN rbi_lookup r ON r.Client_ID = a.client_id AND r.Station_Id = a.Station_ID
        WHERE a.shipment_type_id IN (1,9,10,2,3,8) AND hr_rc.todate IS NULL AND a.Billing_Type='CREDIT'
          AND a.billing_date BETWEEN @StartDate AND @EndDate AND a.Station_id IN @StationIds
          AND a.amount<>0 AND a.cour_id<>'00000' AND a.is_deleted=0
    ) sub
),
ranked AS (
    SELECT *, ROW_NUMBER() OVER (PARTITION BY cn_number ORDER BY IF(OldIncentive > NewIncentive, OldIncentive, NewIncentive) DESC) AS rn
    FROM incentives
)
SELECT *, IF(OldIncentive > NewIncentive, OldIncentive, NewIncentive) AS FinalIncentive
FROM ranked
WHERE rn = 1;";

            List<OverLandRbiSourceRow> rows = (await connection.QueryAsync<OverLandRbiSourceRow>(
                query,
                new { StartDate = startDate, EndDate = endDate, StationIds = stationIds.ToArray() },
                commandTimeout: 12000)).ToList();
            rbiStopwatch.Stop();
            _logger?.LogInformation(
                "OLE {Branch} completed: {Rows} rows in {Seconds:F1}s",
                "rbi_source",
                rows.Count,
                rbiStopwatch.Elapsed.TotalSeconds);
            return rows;
        }

        private static async Task<int> SaveOverLandRawRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            OverLandCommissionExecutionContext context,
            IReadOnlyCollection<OverLandCommissionRawRow> rows)
        {
            await connection.ExecuteAsync(
                $@"DELETE FROM {OleCommissionTable}
                  WHERE ComYear = @Year
                    AND ComMonth = @Month
                    AND StationId IN @Stations;",
                new { context.Year, context.Month, Stations = context.StationIds.ToArray() },
                transaction,
                commandTimeout: 300);

            string query = $@"INSERT INTO {OleCommissionTable}
(StationId,CourierId,Courier_Name,No_Of_Shipment,No_Of_Pieces,Total_Weight,RateID,ComYear,ComMonth,CreatedBy,CreatedDate,UpdatedBy,UpdatedDate)
VALUES ";

            return await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                query,
                rows,
                200,
                (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("StationId", row.Station_id),
                    new KeyValuePair<string, object>("CourierId", row.cour_id),
                    new KeyValuePair<string, object>("CourierName", row.cour_name),
                    new KeyValuePair<string, object>("ShipmentCount", row.No_Of_Shipment),
                    new KeyValuePair<string, object>("PieceCount", row.No_Of_Pieces),
                    new KeyValuePair<string, object>("TotalWeight", row.Total_Weight),
                    new KeyValuePair<string, object>("RateId", row.RateID),
                    new KeyValuePair<string, object>("Year", context.Year),
                    new KeyValuePair<string, object>("Month", context.Month),
                    new KeyValuePair<string, object>("CreatedBy", context.UserId),
                    new KeyValuePair<string, object>("CreatedDate", createdDate),
                    new KeyValuePair<string, object>("UpdatedBy", null),
                    new KeyValuePair<string, object>("UpdatedDate", null)
                });
        }

        private static async Task<int> SaveOverLandRbiRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            OverLandCommissionExecutionContext context,
            IReadOnlyCollection<OverLandRbiSourceRow> rows,
            string currentUserId)
        {
            await connection.ExecuteAsync(
                $@"DELETE FROM {RbiIncentiveDetailTable}
                  WHERE year = @Year
                    AND month = @Month
                    AND Station_id IN @Stations;",
                new { context.Year, context.Month, Stations = context.StationIds.ToArray() },
                transaction,
                commandTimeout: 300);

            string query = $@"INSERT INTO {RbiIncentiveDetailTable}
(Station_Id,Cour_Id,cour_name,ShimpmentType,WeightSlab,cn_number,ShipmentTypeID,client_id,ClientName,ClientStatus,RBIExclude,ShipmentCategory,Total_Amount,Total_Weight,RateID,OldRateID,ClientAcntOpen,CodeType,RbiClinetID,Emp_No,OldIncentive,NewIncentive,FinalIncentive,Create_By,Created_Date,Year,Month)
VALUES ";

            return await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                query,
                rows,
                100,
                (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("StationId", row.Station_id),
                    new KeyValuePair<string, object>("CourierId", row.cour_id),
                    new KeyValuePair<string, object>("CourierName", row.cour_name),
                    new KeyValuePair<string, object>("ShipmentType", row.ShipmentType),
                    new KeyValuePair<string, object>("WeightSlab", row.WeightSlab),
                    new KeyValuePair<string, object>("CnNumber", row.cn_number),
                    new KeyValuePair<string, object>("ShipmentTypeId", row.shipment_type_id),
                    new KeyValuePair<string, object>("ClientId", row.client_id),
                    new KeyValuePair<string, object>("ClientName", row.ClientName),
                    new KeyValuePair<string, object>("ClientStatus", row.ClientStatus),
                    new KeyValuePair<string, object>("RbiExclude", row.RBIExclude),
                    new KeyValuePair<string, object>("ShipmentCategory", row.ShipmentCategory),
                    new KeyValuePair<string, object>("TotalAmount", row.Total_Amount),
                    new KeyValuePair<string, object>("TotalWeight", row.Total_Weight),
                    new KeyValuePair<string, object>("RateId", row.RateID),
                    new KeyValuePair<string, object>("OldRateId", row.OldRateID),
                    new KeyValuePair<string, object>("ClientAcntOpen", row.ClientAcntOpen?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, object>("CodeType", row.CodeType),
                    new KeyValuePair<string, object>("RbiClientId", row.RBIClinetID),
                    new KeyValuePair<string, object>("EmpNo", row.Emp_No),
                    new KeyValuePair<string, object>("OldIncentive", row.OldIncentive),
                    new KeyValuePair<string, object>("NewIncentive", row.NewIncentive),
                    new KeyValuePair<string, object>("FinalIncentive", row.FinalIncentive),
                    new KeyValuePair<string, object>("CreatedBy", ParsePayrollUserId(currentUserId)),
                    new KeyValuePair<string, object>("CreatedDate", createdDate),
                    new KeyValuePair<string, object>("Year", context.Year),
                    new KeyValuePair<string, object>("Month", context.Month)
                });
        }

        private static async Task<List<OverLandProcessRow>> LoadAllOverLandProcessRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            var rows = new List<OverLandProcessRow>();
            rows.AddRange(await LoadOpsStaffProcessRowsAsync(connection, context, transaction));
            rows.AddRange(await LoadOleLeopardsProcessRowsAsync(connection, context, transaction));
            rows.AddRange(await LoadExpressBookingIntlProcessRowsAsync(connection, context, transaction));
            rows.AddRange(await LoadRbiProcessRowsAsync(connection, context, transaction));
            rows.AddRange(await LoadDeliveryCreditProcessRowsAsync(connection, context, transaction));
            return rows;
        }

        private static async Task<List<OverLandProcessRow>> LoadOpsStaffProcessRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            IEnumerable<OverLandAggregateRow> rows = await connection.QueryAsync<OverLandAggregateRow>(
                $@"SELECT lm1.GlLocationId,l.LocationName,SUM(com.No_Of_Shipment) AS No_Of_Shipment,SUM(com.No_Of_Pieces) AS No_Of_Pieces,SUM(com.Total_Weight) AS Total_Weight,com.RateID
                  FROM {OleCommissionTable} com
                  INNER JOIN hr_locationMapping lm1 ON com.StationId = lm1.BStationId
                  INNER JOIN lcs_setup.locations l ON lm1.GlLocationId = l.LocationID
                  WHERE ComYear = @Year AND ComMonth = @Month AND StationId IN @Stations AND RateID IN (2,3,4)
                  GROUP BY lm1.GlLocationId, RateID
                  ORDER BY RateID ASC;",
                new { context.Year, context.Month, Stations = context.StationIds.ToArray() },
                transaction,
                commandTimeout: 120);

            return BuildWeightBasedProcessRows(rows, context.Policies, includeCourier: false);
        }

        private static async Task<List<OverLandProcessRow>> LoadOleLeopardsProcessRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            List<OverLandAggregateRow> rows = new();
            rows.AddRange(await connection.QueryAsync<OverLandAggregateRow>(
                $@"SELECT el.LocationId AS GlLocationId,l.LocationName,a.CourierId AS CourierID,a.RateID,SUM(a.No_Of_Shipment) AS No_Of_Shipment,SUM(a.No_Of_Pieces) AS No_Of_Pieces,SUM(a.Total_Weight) AS Total_Weight
                  FROM {OleCommissionTable} a
                  INNER JOIN hr_locationmapping lm ON a.StationId = lm.BStationId
                  INNER JOIN lcs_hr.hr_employeeroutecode r ON r.LocationId = lm.GlLocationId AND a.CourierId = r.RouteCode AND r.ToDate IS NULL
                  INNER JOIN lcs_hr.hr_employeepersonaldetail e ON r.Emp_No = e.EMP_NO
                  INNER JOIN lcs_hr.hr_employeelocationdetails el ON e.EMP_NO = el.Emp_No AND el.ToDate IS NULL
                  INNER JOIN lcs_setup.locations l ON l.LocationID = el.LocationId
                  WHERE a.ComMonth = @Month AND a.ComYear = @Year AND IFNULL(r.CodeType,0) NOT IN (3,10,11) AND r.citycode = @CityCode AND a.RateID = 5
                  GROUP BY a.StationId,a.CourierId,a.RateID;",
                new { context.Year, context.Month, context.CityCode },
                transaction,
                commandTimeout: 120));
            rows.AddRange(await connection.QueryAsync<OverLandAggregateRow>(
                $@"SELECT e.LocationId AS GlLocationId,f.LocationName,c.RouteCode AS CourierID,a.RateID,SUM(a.No_Of_Shipment) AS No_Of_Shipment,SUM(a.No_Of_Pieces) AS No_Of_Pieces,SUM(a.Total_Weight) AS Total_Weight
                  FROM {OleCommissionTable} a
                  INNER JOIN lcs_hr.hr_city b ON a.StationId = b.station_id
                  INNER JOIN lcs_hr.hr_employeeroutecode c ON c.citycode = b.Code AND a.CourierId = c.RouteCode AND c.ToDate IS NULL
                  INNER JOIN lcs_hr.hr_employeepersonaldetail d ON c.Emp_No = d.EMP_NO
                  INNER JOIN lcs_hr.hr_employeelocationdetails e ON d.EMP_NO = e.Emp_No AND e.ToDate IS NULL
                  INNER JOIN lcs_setup.locations f ON e.LocationId = f.LocationID
                  WHERE a.ComMonth = @Month AND a.ComYear = @Year AND IFNULL(c.CodeType,0) NOT IN (3,10,11) AND b.Code = @CityCode AND a.RateID = 1
                  GROUP BY a.StationId,a.CourierId,a.RateID;",
                new { context.Year, context.Month, context.CityCode },
                transaction,
                commandTimeout: 120));

            return BuildWeightBasedProcessRows(rows, context.Policies, includeCourier: true);
        }

        private static async Task<List<OverLandProcessRow>> LoadExpressBookingIntlProcessRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            IEnumerable<OverLandAggregateRow> rows = await connection.QueryAsync<OverLandAggregateRow>(
                $@"SELECT e.LocationId AS GlLocationId,f.LocationName,c.RouteCode AS CourierID,a.RateID,SUM(a.No_Of_Shipment) AS No_Of_Shipment,SUM(a.No_Of_Pieces) AS No_Of_Pieces,SUM(a.Total_Weight) AS Total_Weight
                  FROM {OleCommissionTable} a
                  INNER JOIN lcs_hr.hr_city b ON a.StationId = b.station_id
                  INNER JOIN lcs_hr.hr_employeeroutecode c ON c.citycode = b.Code AND a.CourierId = c.RouteCode AND c.ToDate IS NULL
                  INNER JOIN lcs_hr.hr_employeepersonaldetail d ON c.Emp_No = d.EMP_NO
                  INNER JOIN lcs_hr.hr_employeelocationdetails e ON d.EMP_NO = e.Emp_No AND e.ToDate IS NULL
                  INNER JOIN lcs_setup.locations f ON e.LocationId = f.LocationID
                  WHERE a.ComMonth = @Month AND a.ComYear = @Year AND IFNULL(c.CodeType,0) NOT IN (3,9) AND b.Code = @CityCode AND a.RateID = 8
                  GROUP BY a.StationId,a.CourierId,a.RateID;",
                new { context.Year, context.Month, context.CityCode },
                transaction,
                commandTimeout: 120);

            return BuildShipmentBasedProcessRows(rows, context.Policies);
        }

        private static async Task<List<OverLandProcessRow>> LoadDeliveryCreditProcessRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            IEnumerable<OverLandAggregateRow> rows = await connection.QueryAsync<OverLandAggregateRow>(
                $@"SELECT el.LocationId AS GlLocationId,l.LocationName,a.CourierId AS CourierID,a.RateID,SUM(a.No_Of_Shipment) AS No_Of_Shipment,SUM(a.No_Of_Pieces) AS No_Of_Pieces,SUM(a.Total_Weight) AS Total_Weight
                  FROM {OleCommissionTable} a
                  INNER JOIN hr_locationmapping lm ON a.StationId = lm.BStationId
                  INNER JOIN lcs_hr.hr_employeeroutecode r ON r.LocationId = lm.GlLocationId AND a.CourierId = r.RouteCode AND r.ToDate IS NULL
                  INNER JOIN lcs_hr.hr_employeepersonaldetail e ON r.Emp_No = e.EMP_NO
                  INNER JOIN lcs_hr.hr_employeelocationdetails el ON e.EMP_NO = el.Emp_No AND el.ToDate IS NULL
                  INNER JOIN lcs_setup.locations l ON l.LocationID = el.LocationId
                  WHERE a.ComMonth = @Month AND a.ComYear = @Year AND IFNULL(r.CodeType,0) NOT IN (3,10,11) AND r.citycode = @CityCode AND a.RateID IN (11,12)
                  GROUP BY a.StationId,a.CourierId,a.RateID
                  ORDER BY a.RateID;",
                new { context.Year, context.Month, context.CityCode },
                transaction,
                commandTimeout: 120);

            return BuildShipmentBasedProcessRows(rows, context.Policies);
        }

        private static async Task<List<OverLandProcessRow>> LoadRbiProcessRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return (await connection.QueryAsync<OverLandProcessRow>(
                $@"SELECT e.LocationId AS GlLocationId,a.Cour_Id AS CourierID,a.RateID,
                         CASE WHEN c.RBIExclude = b'1' OR c.CodeType IN (4,10,11) THEN SUM(a.OldIncentive) ELSE SUM(a.FinalIncentive) END AS OleCommission
                  FROM {RbiIncentiveDetailTable} a
                  INNER JOIN lcs_hr.hr_city b ON a.Station_id = b.station_id
                  INNER JOIN lcs_hr.hr_employeeroutecode c ON c.citycode = b.Code AND a.Cour_Id = c.RouteCode AND c.ToDate IS NULL
                  INNER JOIN lcs_hr.hr_employeepersonaldetail d ON c.Emp_No = d.EMP_NO
                  INNER JOIN lcs_hr.hr_employeelocationdetails e ON d.EMP_NO = e.Emp_No AND e.ToDate IS NULL
                  INNER JOIN lcs_setup.locations f ON e.LocationId = f.LocationID
                  WHERE a.Month = @Month AND a.Year = @Year AND IFNULL(c.CodeType,0) NOT IN (3,10,11) AND b.Code = @CityCode AND a.RateID IN (84,85,86,87,88,89,90,91,92,93,94,95)
                  GROUP BY a.Station_id,a.Cour_Id,a.RateID;",
                new { context.Year, context.Month, context.CityCode },
                transaction,
                commandTimeout: 120)).ToList();
        }

        private static List<OverLandProcessRow> BuildWeightBasedProcessRows(
            IEnumerable<OverLandAggregateRow> rows,
            IReadOnlyDictionary<int, OverLandPolicyRow> policies,
            bool includeCourier)
        {
            return rows
                .Where(row => policies.ContainsKey(row.RateID))
                .Select(row => new OverLandProcessRow
                {
                    GlLocationId = row.GlLocationId,
                    CourierID = includeCourier ? row.CourierID : null,
                    RateID = row.RateID,
                    OleCommission = row.Total_Weight * policies[row.RateID].Rate
                })
                .ToList();
        }

        private static List<OverLandProcessRow> BuildShipmentBasedProcessRows(
            IEnumerable<OverLandAggregateRow> rows,
            IReadOnlyDictionary<int, OverLandPolicyRow> policies)
        {
            return rows
                .Where(row => policies.ContainsKey(row.RateID))
                .Select(row => new OverLandProcessRow
                {
                    GlLocationId = row.GlLocationId,
                    CourierID = row.CourierID,
                    RateID = row.RateID,
                    OleCommission = row.No_Of_Shipment * policies[row.RateID].Rate
                })
                .ToList();
        }

        private static async Task<int> SaveOverLandProcessRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            IReadOnlyCollection<OverLandProcessRow> rows,
            IReadOnlyCollection<int> rateIds,
            bool deleteExisting,
            string currentUserId)
        {
            using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                int affectedRows = await SaveOverLandProcessRowsAsync(connection, transaction as MySqlTransaction, context, rows, rateIds, deleteExisting, currentUserId, null);
                await transaction.CommitAsync();
                return affectedRows;
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { /* suppress secondary rollback error when connection is dead */ }
                throw;
            }
        }

        private static async Task<int> SaveOverLandProcessRowsAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            OverLandCommissionExecutionContext context,
            IReadOnlyCollection<OverLandProcessRow> rows,
            IReadOnlyCollection<int> rateIds,
            bool deleteExisting,
            string currentUserId,
            List<OverLandProcessRow>? capturedRows)
        {
            if (deleteExisting)
            {
                await connection.ExecuteAsync(
                    $@"DELETE FROM {OleCommissionProcessTable}
                      WHERE Year = @Year
                        AND Month = @Month
                        AND GlLocationId IN @LocationIds
                        AND RateId IN @RateIds;",
                    new
                    {
                        context.Year,
                        context.Month,
                        LocationIds = context.LocationIds.ToArray(),
                        RateIds = rateIds.ToArray()
                    },
                    transaction,
                    commandTimeout: 300);
            }

            if (rows.Count == 0)
            {
                return 0;
            }

            capturedRows?.AddRange(rows);

            string query = $@"INSERT INTO {OleCommissionProcessTable}
(Year,Month,GlLocationId,CourierID,RateId,OleCommission,CreatedBy,CreatedDate)
VALUES ";

            return await ExecuteMultiValueInsertAsync(
                connection,
                transaction,
                query,
                rows,
                200,
                (row, createdDate) => new[]
                {
                    new KeyValuePair<string, object>("Year", context.Year),
                    new KeyValuePair<string, object>("Month", context.Month),
                    new KeyValuePair<string, object>("GlLocationId", row.GlLocationId),
                    new KeyValuePair<string, object>("CourierId", row.CourierID),
                    new KeyValuePair<string, object>("RateId", row.RateID),
                    new KeyValuePair<string, object>("OleCommission", row.OleCommission),
                    new KeyValuePair<string, object>("CreatedBy", ParsePayrollUserId(currentUserId)),
                    new KeyValuePair<string, object>("CreatedDate", createdDate)
                });
        }

        private static async Task<int> InsertOverLandAcknowledgmentAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string currentUserId,
            bool billingStatus,
            bool attendanceStatus)
        {
            return await connection.ExecuteAsync(
                $@"INSERT INTO {AcknowledgmentTable}
                  (ScreenID, UserID, CreatedDate, IsBillingConfirm, IsAttendanceProcessed, AllCommProcessed, OneTimeActivity)
                  VALUES (2, @UserId, NOW(), @BillingStatus, @AttendanceStatus, NULL, NULL);",
                new
                {
                    UserId = ParsePayrollUserId(currentUserId),
                    BillingStatus = billingStatus ? 1 : 0,
                    AttendanceStatus = attendanceStatus ? 1 : 0
                },
                transaction,
                commandTimeout: 60);
        }

        private static async Task<OverLandCommissionPreviewBaseline> CaptureOverLandPreviewBaselineAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            string currentUserId)
        {
            return await connection.QuerySingleAsync<OverLandCommissionPreviewBaseline>(
                $@"SELECT
                      (SELECT COUNT(*) FROM {OleCommissionTable} WHERE ComYear = @Year AND ComMonth = @Month AND StationId IN @Stations) AS OleRows,
                      (SELECT COUNT(*) FROM {RbiIncentiveDetailTable} WHERE Year = @Year AND Month = @Month AND Station_id IN @Stations) AS RbiRows,
                      (SELECT COUNT(*) FROM {OleCommissionProcessTable} WHERE Year = @Year AND Month = @Month AND GlLocationId IN @LocationIds AND RateId IN @RateIds) AS ProcessRows,
                      (SELECT IFNULL(SUM(OleCommission),0) FROM {OleCommissionProcessTable} WHERE Year = @Year AND Month = @Month AND GlLocationId IN @LocationIds AND RateId IN @RateIds) AS ProcessAmountTotal,
                      (SELECT COUNT(*) FROM {AcknowledgmentTable} WHERE ScreenID = 2 AND UserID = @UserId) AS AcknowledgmentRows;",
                new
                {
                    context.Year,
                    context.Month,
                    Stations = context.StationIds.ToArray(),
                    LocationIds = context.LocationIds.ToArray(),
                    RateIds = OverLandProcessRateIds,
                    UserId = ParsePayrollUserId(currentUserId)
                },
                commandTimeout: 120);
        }

        private static async Task<OverLandCommissionAuditSnapshot> CaptureOverLandAuditSnapshotAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return await connection.QuerySingleAsync<OverLandCommissionAuditSnapshot>(
                $@"SELECT
                      (SELECT COUNT(*)
                       FROM {OleCommissionTable}
                       WHERE ComYear = @Year
                         AND ComMonth = @Month
                         AND StationId IN @Stations) AS OleRows,
                      (SELECT IFNULL(ROUND(SUM(Total_Weight), 2), 0)
                       FROM {OleCommissionTable}
                       WHERE ComYear = @Year
                         AND ComMonth = @Month
                         AND StationId IN @Stations) AS OleWeightTotal,
                      (SELECT COUNT(*)
                       FROM (
                           SELECT StationId, CourierId, RateID
                           FROM {OleCommissionTable}
                           WHERE ComYear = @Year
                             AND ComMonth = @Month
                             AND StationId IN @Stations
                           GROUP BY StationId, CourierId, RateID
                           HAVING COUNT(*) > 1
                       ) ole_duplicates) AS OleDuplicateGroups,
                      (SELECT COUNT(*)
                       FROM {RbiIncentiveDetailTable}
                       WHERE Year = @Year
                         AND Month = @Month
                         AND Station_id IN @Stations) AS RbiRows,
                      (SELECT IFNULL(ROUND(SUM(FinalIncentive), 2), 0)
                       FROM {RbiIncentiveDetailTable}
                       WHERE Year = @Year
                         AND Month = @Month
                         AND Station_id IN @Stations) AS RbiFinalIncentiveTotal,
                      (SELECT COUNT(*)
                       FROM {OleCommissionProcessTable}
                       WHERE Year = @Year
                         AND Month = @Month
                         AND GlLocationId IN @LocationIds
                         AND RateId IN @RateIds) AS ProcessRows,
                      (SELECT IFNULL(ROUND(SUM(OleCommission), 2), 0)
                       FROM {OleCommissionProcessTable}
                       WHERE Year = @Year
                         AND Month = @Month
                         AND GlLocationId IN @LocationIds
                         AND RateId IN @RateIds) AS ProcessAmountTotal,
                      (SELECT COUNT(*)
                       FROM (
                           SELECT GlLocationId, IFNULL(CourierID, ''), RateId
                           FROM {OleCommissionProcessTable}
                           WHERE Year = @Year
                             AND Month = @Month
                             AND GlLocationId IN @LocationIds
                             AND RateId IN @RateIds
                           GROUP BY GlLocationId, IFNULL(CourierID, ''), RateId
                           HAVING COUNT(*) > 1
                       ) process_duplicates) AS ProcessDuplicateGroups;",
                new
                {
                    context.Year,
                    context.Month,
                    Stations = context.StationIds.ToArray(),
                    LocationIds = context.LocationIds.ToArray(),
                    RateIds = OverLandProcessRateIds
                },
                transaction,
                commandTimeout: 120);
        }

        private static async Task<List<OverLandOleAuditRow>> LoadOverLandOleAuditRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return (await connection.QueryAsync<OverLandOleAuditRow>(
                $@"SELECT IFNULL(StationId, '') AS StationId,
                         IFNULL(CourierId, '') AS CourierId,
                         RateID AS RateId,
                         COUNT(*) AS EntryCount,
                         IFNULL(SUM(No_Of_Shipment), 0) AS NoOfShipment,
                         IFNULL(SUM(No_Of_Pieces), 0) AS NoOfPieces,
                         IFNULL(ROUND(SUM(Total_Weight), 2), 0) AS TotalWeight
                  FROM {OleCommissionTable}
                  WHERE ComYear = @Year
                    AND ComMonth = @Month
                    AND StationId IN @Stations
                  GROUP BY StationId, IFNULL(CourierId, ''), RateID
                  ORDER BY StationId, CourierId, RateID;",
                new
                {
                    context.Year,
                    context.Month,
                    Stations = context.StationIds.ToArray()
                },
                transaction,
                commandTimeout: 120)).ToList();
        }

        private static async Task<List<OverLandRbiAuditRow>> LoadOverLandRbiAuditRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return (await connection.QueryAsync<OverLandRbiAuditRow>(
                $@"SELECT IFNULL(cn_number, '') AS CnNumber,
                         IFNULL(Cour_Id, '') AS CourierId,
                         RateID AS RateId,
                         IFNULL(Emp_No, '') AS EmpNo,
                         COUNT(*) AS EntryCount,
                         IFNULL(ROUND(SUM(Total_Amount), 2), 0) AS TotalAmount,
                         IFNULL(ROUND(SUM(Total_Weight), 2), 0) AS TotalWeight,
                         IFNULL(ROUND(SUM(OldIncentive), 2), 0) AS OldIncentive,
                         IFNULL(ROUND(SUM(NewIncentive), 2), 0) AS NewIncentive,
                         IFNULL(ROUND(SUM(FinalIncentive), 2), 0) AS FinalIncentive
                  FROM {RbiIncentiveDetailTable}
                  WHERE Year = @Year
                    AND Month = @Month
                    AND Station_id IN @Stations
                  GROUP BY IFNULL(cn_number, ''), IFNULL(Cour_Id, ''), RateID, IFNULL(Emp_No, '')
                  ORDER BY CnNumber, CourierId, RateID, EmpNo;",
                new
                {
                    context.Year,
                    context.Month,
                    Stations = context.StationIds.ToArray()
                },
                transaction,
                commandTimeout: 120)).ToList();
        }

        private static async Task<List<OverLandProcessAuditRow>> LoadOverLandProcessAuditRowsAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            MySqlTransaction? transaction = null)
        {
            return (await connection.QueryAsync<OverLandProcessAuditRow>(
                $@"SELECT GlLocationId,
                         IFNULL(CourierID, '') AS CourierId,
                         RateId AS RateId,
                         COUNT(*) AS EntryCount,
                         IFNULL(ROUND(SUM(OleCommission), 2), 0) AS OleCommission
                  FROM {OleCommissionProcessTable}
                  WHERE Year = @Year
                    AND Month = @Month
                    AND GlLocationId IN @LocationIds
                    AND RateId IN @RateIds
                  GROUP BY GlLocationId, IFNULL(CourierID, ''), RateId
                  ORDER BY GlLocationId, CourierId, RateId;",
                new
                {
                    context.Year,
                    context.Month,
                    LocationIds = context.LocationIds.ToArray(),
                    RateIds = OverLandProcessRateIds
                },
                transaction,
                commandTimeout: 120)).ToList();
        }

        private static async Task<bool> VerifyOverLandPreviewBaselineAsync(
            MySqlConnection connection,
            OverLandCommissionExecutionContext context,
            string currentUserId,
            OverLandCommissionPreviewBaseline baseline)
        {
            OverLandCommissionPreviewBaseline current = await CaptureOverLandPreviewBaselineAsync(connection, context, currentUserId);
            return current.OleRows == baseline.OleRows
                && current.RbiRows == baseline.RbiRows
                && current.ProcessRows == baseline.ProcessRows
                && current.ProcessAmountTotal == baseline.ProcessAmountTotal
                && current.AcknowledgmentRows == baseline.AcknowledgmentRows;
        }

        private sealed class OverLandCommissionExecutionContext
        {
            public int Year { get; init; }
            public int Month { get; init; }
            public string CityCode { get; init; } = string.Empty;
            public DateTime StartDate { get; init; }
            public DateTime EndDate { get; init; }
            public int UserId { get; init; }
            public List<string> StationIds { get; init; } = new();
            public List<int> LocationIds { get; init; } = new();
            public Dictionary<int, OverLandPolicyRow> Policies { get; init; } = new();
            public CommissionConfig Cfg { get; init; } = new CommissionConfig();
        }

        private sealed class OverLandPolicyRow
        {
            public int RateID { get; init; }
            public string? Type { get; init; }
            public int RateType { get; init; }
            public decimal Rate { get; init; }
            public bool IsPercent { get; init; }
        }

        private sealed class OverLandCommissionPreviewBaseline
        {
            public int OleRows { get; init; }
            public int RbiRows { get; init; }
            public int ProcessRows { get; init; }
            public decimal ProcessAmountTotal { get; init; }
            public int AcknowledgmentRows { get; init; }
        }

        private sealed class OverLandCommissionAuditSnapshot
        {
            public int OleRows { get; init; }
            public decimal OleWeightTotal { get; init; }
            public int OleDuplicateGroups { get; init; }
            public int RbiRows { get; init; }
            public decimal RbiFinalIncentiveTotal { get; init; }
            public int ProcessRows { get; init; }
            public decimal ProcessAmountTotal { get; init; }
            public int ProcessDuplicateGroups { get; init; }
        }

        private sealed class OverLandOleAuditRow
        {
            public string StationId { get; init; } = string.Empty;
            public string CourierId { get; init; } = string.Empty;
            public int RateId { get; init; }
            public int EntryCount { get; init; }
            public int NoOfShipment { get; init; }
            public int NoOfPieces { get; init; }
            public decimal TotalWeight { get; init; }
        }

        private sealed class OverLandRbiAuditRow
        {
            public string CnNumber { get; init; } = string.Empty;
            public string CourierId { get; init; } = string.Empty;
            public int RateId { get; init; }
            public string EmpNo { get; init; } = string.Empty;
            public int EntryCount { get; init; }
            public decimal TotalAmount { get; init; }
            public decimal TotalWeight { get; init; }
            public decimal OldIncentive { get; init; }
            public decimal NewIncentive { get; init; }
            public decimal FinalIncentive { get; init; }
        }

        private sealed class OverLandProcessAuditRow
        {
            public int GlLocationId { get; init; }
            public string CourierId { get; init; } = string.Empty;
            public int RateId { get; init; }
            public int EntryCount { get; init; }
            public decimal OleCommission { get; init; }
        }

        private sealed class OverLandCommissionRawRow
        {
            public string Station_id { get; init; } = string.Empty;
            public string cour_id { get; init; } = string.Empty;
            public string cour_name { get; init; } = string.Empty;
            public int No_Of_Shipment { get; init; }
            public int No_Of_Pieces { get; init; }
            public decimal Total_Weight { get; init; }
            public int RateID { get; init; }
            public int ComYear { get; init; }
            public int ComMonth { get; init; }
            public int CreatedBy { get; init; }
        }

        private sealed class OverLandDeliverySourceRow
        {
            public int BillingShipmentTypeId { get; set; }
            public string? CN_NUMBER { get; set; }
            public string? BillingCN { get; set; }
            public string? BookIssueCn { get; set; }
            public string? Status { get; set; }
            public DateTime DeliveryDate { get; set; }
            public string? CourId { get; set; }
            public string? CourName { get; set; }
            public int Pieces { get; set; }
            public decimal Weight { get; set; }
            public decimal Billing_Weight { get; set; }
            public string? IssueBookType { get; set; }
            public string? ClientId { get; set; }
            public string? ClientName { get; set; }
            public decimal BillingAmount { get; set; }
            public string? BillingOrigin { get; set; }
            public string? BillingDestination { get; set; }
            public string? BookTypeOriginCity { get; set; }
            public string? BookTypeDestCity { get; set; }
            public string? ArrivalOrgCity { get; set; }
            public string? ArrivalDestCity { get; set; }
            public int RateId { get; set; }
        }

        private sealed class OverLandRbiSourceRow
        {
            public string Station_id { get; init; } = string.Empty;
            public string cour_id { get; init; } = string.Empty;
            public string cour_name { get; init; } = string.Empty;
            public string ShipmentType { get; init; } = string.Empty;
            public string WeightSlab { get; init; } = string.Empty;
            public string cn_number { get; init; } = string.Empty;
            public int shipment_type_id { get; init; }
            public string client_id { get; init; } = string.Empty;
            public decimal Total_Amount { get; init; }
            public decimal Total_Weight { get; init; }
            public int RateID { get; init; }
            public int? OldRateID { get; init; }
            public DateTime? ClientAcntOpen { get; init; }
            public string ClientStatus { get; init; } = string.Empty;
            public int CodeType { get; init; }
            public int? RBIClinetID { get; init; }
            public string Emp_No { get; init; } = string.Empty;
            public int RBIExclude { get; init; }
            public string ClientName { get; init; } = string.Empty;
            public string ShipmentCategory { get; init; } = string.Empty;
            public decimal OldIncentive { get; init; }
            public decimal NewIncentive { get; init; }
            public decimal FinalIncentive { get; init; }
            public int Year { get; init; }
            public int Month { get; init; }
        }

        private sealed class OverLandAggregateRow
        {
            public int GlLocationId { get; init; }
            public string? LocationName { get; init; }
            public decimal Total_Weight { get; init; }
            public int No_Of_Shipment { get; init; }
            public int No_Of_Pieces { get; init; }
            public int RateID { get; init; }
            public string? CourierID { get; init; }
        }

        private sealed class OverLandProcessRow
        {
            public int GlLocationId { get; init; }
            public string? CourierID { get; init; }
            public int RateID { get; init; }
            public decimal OleCommission { get; init; }
        }
    }
}
