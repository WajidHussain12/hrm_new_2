using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class PayrollService
    {
        public async Task<FuelPricesViewModel> GetFuelPricesPageAsync(DateTime workingDate, string? code = null, string? searchField = null, string? searchText = null)
        {
            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                throw new ArgumentException("Database error");
            }

            await connection.OpenAsync();

            var model = new FuelPricesViewModel
            {
                SearchField = string.IsNullOrWhiteSpace(searchField) ? "All" : searchField.Trim(),
                SearchText = searchText?.Trim() ?? string.Empty,
                SearchFields = BuildFuelPriceSearchFields(),
                FuelTypes = (await connection.QueryAsync<SelectListItem>(
                    @"SELECT Code AS Value, Type AS Text
                      FROM hr_fuelType
                      ORDER BY Type ASC")).ToList(),
                Rows = (await connection.QueryAsync<FuelPriceRowViewModel>(
                    @"SELECT
                          hr.Code,
                          ft.Type AS TypeName,
                          hr.FromDate,
                          hr.ToDate,
                          hr.Price,
                          hr.Comments
                      FROM hr_fuelprices hr
                      INNER JOIN hr_fuelType ft ON ft.Code = hr.Type
                      ORDER BY CAST(hr.Code AS UNSIGNED) DESC")).ToList()
            };

            model.Rows = ApplyFuelPriceSearch(model.Rows, model.SearchField, model.SearchText);

            if (!string.IsNullOrWhiteSpace(code))
            {
                var selected = await connection.QuerySingleOrDefaultAsync<FuelPriceEditRecord>(
                    @"SELECT Code, Type AS TypeCode, FromDate, ToDate, Price, Comments
                      FROM hr_fuelprices
                      WHERE Code = @Code",
                    new { Code = code.Trim() });

                if (selected != null)
                {
                    model.Code = selected.Code;
                    model.TypeCode = selected.TypeCode;
                    model.FromDate = selected.FromDate;
                    model.ToDate = selected.ToDate;
                    model.Price = selected.Price;
                    model.Comments = selected.Comments ?? string.Empty;
                    model.IsEditMode = true;
                }
            }

            if (!model.IsEditMode)
            {
                model.FromDate = workingDate.Date;
            }

            return model;
        }

        public async Task<(bool success, string message)> SaveFuelPriceAsync(FuelPricesViewModel model, string currentUserId)
        {
            ValidateFuelPriceForm(model);

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return (false, "Database error");
            }

            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var latestOfSameType = await GetLatestFuelPriceAsync(connection, transaction, model.TypeCode, null);
                EnsureFuelPriceDateGap(latestOfSameType?.FromDate, model.FromDate!.Value);

                var newCode = await GenerateFuelPriceCodeAsync(connection, transaction);
                await connection.ExecuteAsync(
                    @"INSERT INTO hr_fuelprices
                      (Code, Type, FromDate, ToDate, Price, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                      VALUES
                      (@Code, @Type, @FromDate, NULL, @Price, @Comments, @CreatedBy, @CreatedDate, @UpdatedBy, @UpdatedDate)",
                    new
                    {
                        Code = newCode,
                        Type = model.TypeCode,
                        FromDate = model.FromDate!.Value,
                        Price = model.Price!.Value,
                        Comments = model.Comments?.Trim() ?? string.Empty,
                        CreatedBy = currentUserId,
                        CreatedDate = DateTime.Now,
                        UpdatedBy = currentUserId,
                        UpdatedDate = DateTime.Now
                    },
                    transaction);

                if (latestOfSameType != null)
                {
                    await connection.ExecuteAsync(
                        @"UPDATE hr_fuelprices
                          SET ToDate = @ToDate
                          WHERE Code = @Code",
                        new
                        {
                            ToDate = model.FromDate.Value.AddDays(-1),
                            Code = latestOfSameType.Code
                        },
                        transaction);
                }

                await transaction.CommitAsync();
                return (true, "Record Saved Successfully");
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool success, string message)> UpdateFuelPriceAsync(FuelPricesViewModel model, string currentUserId)
        {
            ValidateFuelPriceForm(model);

            if (string.IsNullOrWhiteSpace(model.Code) || string.Equals(model.Code, "Auto Generated", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Fuel code is required.");
            }

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return (false, "Database error");
            }

            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var currentRecord = await connection.QuerySingleOrDefaultAsync<FuelPriceEditRecord>(
                    @"SELECT Code, Type AS TypeCode, FromDate, ToDate, Price, Comments
                      FROM hr_fuelprices
                      WHERE Code = @Code",
                    new { Code = model.Code.Trim() },
                    transaction);

                if (currentRecord == null)
                {
                    throw new ArgumentException("Record not found.");
                }

                if (currentRecord.ToDate.HasValue)
                {
                    throw new ArgumentException("You cannot delete this record.You have to update or delete current Fuel detail record.");
                }

                if (model.ToDate.HasValue && model.ToDate.Value.Date < model.FromDate!.Value.Date)
                {
                    throw new ArgumentException("To date cannot be smaller than From date.");
                }

                var latestOfSameType = await GetLatestFuelPriceAsync(connection, transaction, model.TypeCode, model.Code);
                EnsureFuelPriceDateGap(latestOfSameType?.FromDate, model.FromDate!.Value);

                await connection.ExecuteAsync(
                    @"UPDATE hr_fuelprices
                      SET Type = @Type,
                          FromDate = @FromDate,
                          ToDate = NULL,
                          Price = @Price,
                          Comments = @Comments,
                          UpdatedBy = @UpdatedBy,
                          Updated_Date = @UpdatedDate
                      WHERE Code = @Code",
                    new
                    {
                        Code = model.Code.Trim(),
                        Type = model.TypeCode,
                        FromDate = model.FromDate!.Value,
                        Price = model.Price!.Value,
                        Comments = model.Comments?.Trim() ?? string.Empty,
                        UpdatedBy = currentUserId,
                        UpdatedDate = DateTime.Now
                    },
                    transaction);

                if (latestOfSameType != null)
                {
                    await connection.ExecuteAsync(
                        @"UPDATE hr_fuelprices
                          SET ToDate = @ToDate
                          WHERE Code = @Code",
                        new
                        {
                            ToDate = model.FromDate.Value.AddDays(-1),
                            Code = latestOfSameType.Code
                        },
                        transaction);
                }

                await transaction.CommitAsync();
                return (true, "Record updated Successfully");
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool success, string message)> DeleteFuelPriceAsync(string code, string currentUserId)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return (false, "Fuel code is required.");
            }

            using var connection = _connectionFactory.CreateConnection() as MySqlConnection;
            if (connection == null)
            {
                return (false, "Database error");
            }

            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var currentRecord = await connection.QuerySingleOrDefaultAsync<FuelPriceEditRecord>(
                    @"SELECT Code, Type AS TypeCode, FromDate, ToDate, Price, Comments
                      FROM hr_fuelprices
                      WHERE Code = @Code",
                    new { Code = code.Trim() },
                    transaction);

                if (currentRecord == null)
                {
                    throw new ArgumentException("Record not found.");
                }

                if (currentRecord.ToDate.HasValue)
                {
                    throw new ArgumentException("You cannot delete this record.You have to update or delete Current Fuel detail record.");
                }

                var latestOfSameType = await GetLatestFuelPriceAsync(connection, transaction, currentRecord.TypeCode, currentRecord.Code);

                if (latestOfSameType != null)
                {
                    await connection.ExecuteAsync(
                        @"UPDATE hr_fuelprices
                          SET ToDate = NULL
                          WHERE Code = @Code",
                        new { Code = latestOfSameType.Code },
                        transaction);
                }

                await connection.ExecuteAsync(
                    @"DELETE FROM hr_fuelprices
                      WHERE Code = @Code",
                    new { Code = currentRecord.Code },
                    transaction);

                await transaction.CommitAsync();
                return (true, "Record deleted successfully");
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static List<SelectListItem> BuildFuelPriceSearchFields()
        {
            return new List<SelectListItem>
            {
                new() { Value = "All", Text = "All" },
                new() { Value = "Code", Text = "Code" },
                new() { Value = "Type", Text = "Type" },
                new() { Value = "FromDate", Text = "From Date" },
                new() { Value = "ToDate", Text = "To Date" },
                new() { Value = "Price", Text = "Price" },
                new() { Value = "Comments", Text = "Comments" }
            };
        }

        private static List<FuelPriceRowViewModel> ApplyFuelPriceSearch(IEnumerable<FuelPriceRowViewModel> rows, string searchField, string searchText)
        {
            var list = rows.ToList();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return list;
            }

            var term = searchText.Trim();
            return list.Where(row => MatchesFuelPriceSearch(row, searchField, term)).ToList();
        }

        private static bool MatchesFuelPriceSearch(FuelPriceRowViewModel row, string searchField, string term)
        {
            var comparisons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = row.Code,
                ["Type"] = row.TypeName,
                ["FromDate"] = row.FromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                ["ToDate"] = row.ToDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                ["Price"] = row.Price.ToString(CultureInfo.InvariantCulture),
                ["Comments"] = row.Comments ?? string.Empty
            };

            if (string.Equals(searchField, "All", StringComparison.OrdinalIgnoreCase))
            {
                return comparisons.Values.Any(value => value.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return comparisons.TryGetValue(searchField, out var value)
                && value.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateFuelPriceForm(FuelPricesViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.TypeCode))
            {
                throw new ArgumentException("Fuel type is required.");
            }

            if (!model.FromDate.HasValue)
            {
                throw new ArgumentException("From date is required.");
            }

            if (!model.Price.HasValue || model.Price.Value <= 0)
            {
                throw new ArgumentException("Invalid price.");
            }
        }

        private static void EnsureFuelPriceDateGap(DateTime? latestTime, DateTime selectedFromDate)
        {
            if (!latestTime.HasValue)
            {
                return;
            }

            if (selectedFromDate.Date <= latestTime.Value.Date)
            {
                throw new ArgumentException($"Date from should be greater than \"{latestTime.Value.AddDays(1):dd/MM/yyyy}\"");
            }

            if (selectedFromDate.AddDays(-1).Date == latestTime.Value.Date)
            {
                throw new ArgumentException($"There should be at least 2 days difference between \"{latestTime.Value:dd/MM/yyyy}\" and the date you have selected.");
            }
        }

        private static async Task<FuelPriceEditRecord?> GetLatestFuelPriceAsync(MySqlConnection connection, MySqlTransaction transaction, string typeCode, string? excludedCode)
        {
            var sql = @"SELECT Code, Type AS TypeCode, FromDate, ToDate, Price, Comments
                        FROM hr_fuelprices
                        WHERE Type = @TypeCode";

            if (!string.IsNullOrWhiteSpace(excludedCode))
            {
                sql += " AND Code <> @ExcludedCode";
            }

            sql += " ORDER BY CAST(Code AS UNSIGNED) DESC LIMIT 1";

            return await connection.QuerySingleOrDefaultAsync<FuelPriceEditRecord>(
                sql,
                new
                {
                    TypeCode = typeCode,
                    ExcludedCode = excludedCode
                },
                transaction);
        }

        private static async Task<string> GenerateFuelPriceCodeAsync(MySqlConnection connection, MySqlTransaction transaction)
        {
            var nextValue = await connection.ExecuteScalarAsync<long?>(
                "SELECT MAX(CAST(Code AS UNSIGNED)) FROM hr_fuelprices",
                transaction: transaction) ?? 0;

            var digits = await connection.ExecuteScalarAsync<int?>(
                "SELECT MAX(CHAR_LENGTH(Code)) FROM hr_fuelprices",
                transaction: transaction) ?? 3;

            digits = Math.Max(3, digits);
            return (nextValue + 1).ToString($"D{digits}", CultureInfo.InvariantCulture);
        }

        private sealed class FuelPriceEditRecord
        {
            public string Code { get; set; } = string.Empty;
            public string TypeCode { get; set; } = string.Empty;
            public DateTime FromDate { get; set; }
            public DateTime? ToDate { get; set; }
            public decimal Price { get; set; }
            public string? Comments { get; set; }
        }
    }
}
