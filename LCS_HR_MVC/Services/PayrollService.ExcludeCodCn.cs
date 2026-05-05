using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        public Task<ExcludeCodCnViewModel> GetExcludeCodCnPageAsync()
        {
            return Task.FromResult(new ExcludeCodCnViewModel());
        }

        public async Task<ExcludeCodCnViewModel> ProcessExcludeCodCnUploadAsync(Stream fileStream, string fileName, long fileSize, string currentUserId, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(currentUserId, "163", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("You are not allowed to access this page.");
            }

            if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(Path.GetExtension(fileName), ".csv", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("File should have .csv extension.");
            }

            if (fileSize > 10_000_000)
            {
                throw new ArgumentException("File Size should be less than 10 MB.");
            }

            var rows = await ParseExcludeCodCnCsvAsync(fileStream, cancellationToken);
            if (rows.Count == 0)
            {
                throw new ArgumentException("File is not in correct format! Please review sample file.");
            }

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync(cancellationToken);
            using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await connection.ExecuteAsync("DROP TEMPORARY TABLE IF EXISTS TempCNs;", transaction: transaction);
                await connection.ExecuteAsync("CREATE TEMPORARY TABLE TempCNs (CN VARCHAR(50));", transaction: transaction);

                foreach (var row in rows)
                {
                    await connection.ExecuteAsync(
                        "INSERT INTO TempCNs (CN) VALUES (@CN);",
                        new { CN = row.CnNumber },
                        transaction);
                }

                var duplicates = (await connection.QueryAsync<ExcludeCodCnRowViewModel>(
                    @"SELECT
                          0 AS RowNumber,
                          c.CN AS CnNumber,
                          c.CourierID AS CourierId,
                          c.StationID AS ArrivalDestination
                      FROM cod_prepaid_commission c
                      INNER JOIN TempCNs t ON t.CN = c.CN;",
                    transaction: transaction)).ToList();

                await connection.ExecuteAsync("DROP TEMPORARY TABLE IF EXISTS TempCNs;", transaction: transaction);

                var duplicateLookup = new HashSet<string>(duplicates.Select(x => x.CnNumber), StringComparer.OrdinalIgnoreCase);
                var duplicateRows = rows
                    .Where(x => duplicateLookup.Contains(x.CnNumber))
                    .ToList();

                rows.RemoveAll(x => duplicateLookup.Contains(x.CnNumber));

                var inserted = 0;
                if (rows.Count > 0)
                {
                    inserted = await connection.ExecuteAsync(
                        @"INSERT INTO cod_prepaid_commission
                          SELECT @CnNumber, @CourierId, @ArrivalDestination, NOW(), b'0', NOW(), NULL, @CreatedBy
                          WHERE NOT EXISTS (
                              SELECT 1
                              FROM cod_prepaid_commission
                              WHERE CN = @CnNumber
                          );",
                        rows.Select(x => new
                        {
                            x.CnNumber,
                            x.CourierId,
                            x.ArrivalDestination,
                            CreatedBy = currentUserId
                        }),
                        transaction,
                        commandTimeout: 300);
                }

                await transaction.CommitAsync(cancellationToken);

                return new ExcludeCodCnViewModel
                {
                    InsertedCount = inserted,
                    SkippedCount = duplicateRows.Count,
                    DuplicateRows = duplicateRows
                };
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private static async Task<List<ExcludeCodCnRowViewModel>> ParseExcludeCodCnCsvAsync(Stream fileStream, CancellationToken cancellationToken)
        {
            var rows = new List<ExcludeCodCnRowViewModel>();
            using var reader = new StreamReader(fileStream, leaveOpen: true);

            var rowNumber = 0;
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    continue;
                }

                rowNumber++;
                var values = line.Split(',');
                if (values.Length != 3)
                {
                    throw new ArgumentException("File is not in correct format! Please review sample file.");
                }

                if (rowNumber == 1)
                {
                    continue;
                }

                rows.Add(new ExcludeCodCnRowViewModel
                {
                    RowNumber = rowNumber,
                    CnNumber = values[0].Trim(),
                    CourierId = string.IsNullOrWhiteSpace(values[1]) ? string.Empty : values[1].Trim().PadLeft(5, '0'),
                    ArrivalDestination = string.IsNullOrWhiteSpace(values[2]) ? string.Empty : values[2].Trim().PadLeft(5, '0')
                });
            }

            fileStream.Position = 0;
            return rows.Where(x => !string.IsNullOrWhiteSpace(x.CnNumber)).ToList();
        }
    }
}
