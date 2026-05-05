using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class TaxSetupService : ITaxSetupService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public TaxSetupService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<TaxHeadModel>> GetAllTaxesAsync()
        {
            var taxes = new List<TaxHeadModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return taxes;
                await connection.OpenAsync();

                string query = @"SELECT CODE, TaxYear, DateFrom, DateTo, Comments FROM hr_tax_hdr ORDER BY CODE DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        taxes.Add(new TaxHeadModel
                        {
                            Code = reader["CODE"].ToString() ?? string.Empty,
                            TaxYear = Convert.ToInt32(reader["TaxYear"]),
                            DateFrom = reader["DateFrom"] == DBNull.Value ? null : Convert.ToDateTime(reader["DateFrom"]),
                            DateTo = reader["DateTo"] == DBNull.Value ? null : Convert.ToDateTime(reader["DateTo"]),
                            Comments = reader["Comments"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return taxes;
        }

        public async Task<TaxHeadModel?> GetTaxByCodeAsync(string code)
        {
            TaxHeadModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT TaxYear, DateFrom, DateTo, Comments FROM hr_tax_hdr WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new TaxHeadModel
                            {
                                Code = code,
                                TaxYear = Convert.ToInt32(reader["TaxYear"]),
                                DateFrom = reader["DateFrom"] == DBNull.Value ? null : Convert.ToDateTime(reader["DateFrom"]),
                                DateTo = reader["DateTo"] == DBNull.Value ? null : Convert.ToDateTime(reader["DateTo"]),
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }

                if (model != null)
                {
                    string detailsQuery = @"SELECT LimitFrom, LimitTo, Pct_Amount, Fix_Amount, Comments FROM hr_tax_dtl WHERE TaxCode = @Code ORDER BY sno";
                    using (var command = new MySqlCommand(detailsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@Code", code);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                model.Details.Add(new TaxDetailModel
                                {
                                    LimitFrom = Convert.ToDecimal(reader["LimitFrom"]),
                                    LimitTo = Convert.ToDecimal(reader["LimitTo"]),
                                    PctAmount = Convert.ToDecimal(reader["Pct_Amount"]),
                                    FixAmount = Convert.ToDecimal(reader["Fix_Amount"]),
                                    Comments = reader["Comments"].ToString() ?? string.Empty
                                });
                            }
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> IsTaxYearExistsAsync(int taxYear, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_tax_hdr WHERE TaxYear = @TaxYear LIMIT 1"
                    : "SELECT 1 FROM hr_tax_hdr WHERE TaxYear = @TaxYear AND Code <> @Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TaxYear", taxYear);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        private async Task<string> GenerateNewCodeAsync(MySqlConnection connection, MySqlTransaction transaction)
        {
            string query = "SELECT MAX(CAST(Code AS UNSIGNED)) FROM hr_tax_hdr";
            using (var command = new MySqlCommand(query, connection, transaction))
            {
                var result = await command.ExecuteScalarAsync();
                if (result != DBNull.Value && result != null)
                {
                    int maxId = Convert.ToInt32(result);
                    return (maxId + 1).ToString("D6");
                }
            }
            return "000001";
        }

        public async Task<bool> SaveTaxAsync(TaxHeadModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string code = await GenerateNewCodeAsync(connection, transaction as MySqlTransaction);

                        string insertHdr = @"INSERT INTO hr_tax_hdr (Code, TaxYear, DateFrom, DateTo, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                             VALUES (@Code, @TaxYear, @DateFrom, @DateTo, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                        using (var command = new MySqlCommand(insertHdr, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            command.Parameters.AddWithValue("@TaxYear", model.TaxYear);
                            command.Parameters.AddWithValue("@DateFrom", model.DateFrom);
                            command.Parameters.AddWithValue("@DateTo", model.DateTo);
                            command.Parameters.AddWithValue("@Comments", model.Comments);
                            command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            await command.ExecuteNonQueryAsync();
                        }

                        if (model.Details != null && model.Details.Count > 0)
                        {
                            string insertDtl = @"INSERT INTO hr_tax_dtl (TaxCode, sno, LimitFrom, LimitTo, Pct_Amount, Fix_Amount, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                 VALUES (@TaxCode, @sno, @LimitFrom, @LimitTo, @Pct_Amount, @Fix_Amount, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                            
                            int sno = 1;
                            foreach (var detail in model.Details)
                            {
                                using (var command = new MySqlCommand(insertDtl, connection, transaction as MySqlTransaction))
                                {
                                    command.Parameters.AddWithValue("@TaxCode", code);
                                    command.Parameters.AddWithValue("@sno", sno++);
                                    command.Parameters.AddWithValue("@LimitFrom", detail.LimitFrom);
                                    command.Parameters.AddWithValue("@LimitTo", detail.LimitTo);
                                    command.Parameters.AddWithValue("@Pct_Amount", detail.PctAmount);
                                    command.Parameters.AddWithValue("@Fix_Amount", detail.FixAmount);
                                    command.Parameters.AddWithValue("@Comments", detail.Comments);
                                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                                    await command.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        await transaction.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        return false;
                    }
                }
            }
        }

        public async Task<bool> UpdateTaxAsync(TaxHeadModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateHdr = @"UPDATE hr_tax_hdr SET TaxYear=@TaxYear, DateFrom=@DateFrom, DateTo=@DateTo, Comments=@Comments, 
                                             UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date WHERE Code=@Code";

                        using (var command = new MySqlCommand(updateHdr, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", model.Code);
                            command.Parameters.AddWithValue("@TaxYear", model.TaxYear);
                            command.Parameters.AddWithValue("@DateFrom", model.DateFrom);
                            command.Parameters.AddWithValue("@DateTo", model.DateTo);
                            command.Parameters.AddWithValue("@Comments", model.Comments);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            await command.ExecuteNonQueryAsync();
                        }

                        using (var deleteDtl = new MySqlCommand("DELETE FROM hr_tax_dtl WHERE TaxCode=@Code", connection, transaction as MySqlTransaction))
                        {
                            deleteDtl.Parameters.AddWithValue("@Code", model.Code);
                            await deleteDtl.ExecuteNonQueryAsync();
                        }

                        if (model.Details != null && model.Details.Count > 0)
                        {
                            string insertDtl = @"INSERT INTO hr_tax_dtl (TaxCode, sno, LimitFrom, LimitTo, Pct_Amount, Fix_Amount, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                 VALUES (@TaxCode, @sno, @LimitFrom, @LimitTo, @Pct_Amount, @Fix_Amount, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                            
                            int sno = 1;
                            foreach (var detail in model.Details)
                            {
                                using (var command = new MySqlCommand(insertDtl, connection, transaction as MySqlTransaction))
                                {
                                    command.Parameters.AddWithValue("@TaxCode", model.Code);
                                    command.Parameters.AddWithValue("@sno", sno++);
                                    command.Parameters.AddWithValue("@LimitFrom", detail.LimitFrom);
                                    command.Parameters.AddWithValue("@LimitTo", detail.LimitTo);
                                    command.Parameters.AddWithValue("@Pct_Amount", detail.PctAmount);
                                    command.Parameters.AddWithValue("@Fix_Amount", detail.FixAmount);
                                    command.Parameters.AddWithValue("@Comments", detail.Comments);
                                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                                    await command.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        await transaction.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        return false;
                    }
                }
            }
        }

        public async Task<bool> DeleteTaxAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        using (var command = new MySqlCommand("DELETE FROM hr_tax_dtl WHERE TaxCode = @Code", connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            await command.ExecuteNonQueryAsync();
                        }

                        using (var command = new MySqlCommand("DELETE FROM hr_tax_hdr WHERE Code = @Code", connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            await command.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        return false;
                    }
                }
            }
        }
    }
}
