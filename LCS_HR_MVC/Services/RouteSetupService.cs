using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class RouteSetupService : IRouteSetupService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public RouteSetupService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<RouteModel>> GetAllRoutesAsync(string currentUserId)
        {
            var routes = new List<RouteModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return routes;
                await connection.OpenAsync();

                string query = @"SELECT hdr.RouteCode, hdr.citycode, c.`FullName` cityName, hdr.FromDate, hdr.ToDate, hdr.Description
                                 FROM hr_routecodes_hdr hdr 
                                 INNER JOIN `hr_city` c ON hdr.`CityCode`=c.`Code`   
                                 INNER JOIN lcs_user_location lul ON c.Code=lul.city_code  
                                 WHERE lul.userid = @UserId";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            routes.Add(new RouteModel
                            {
                                RouteCode = reader["RouteCode"].ToString() ?? string.Empty,
                                CityCode = reader["citycode"].ToString() ?? string.Empty,
                                CityName = reader["cityName"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"]),
                                Description = reader["Description"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return routes;
        }

        public async Task<RouteModel?> GetRouteByCodeAndCityAsync(string routeCode, string cityCode)
        {
            RouteModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT RouteCode, CityCode, FromDate, ToDate, Description, Comments, porter_comm 
                                 FROM hr_routecodes_hdr 
                                 WHERE RouteCode=@RouteCode AND citycode=@citycode";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@RouteCode", routeCode);
                    command.Parameters.AddWithValue("@citycode", cityCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new RouteModel
                            {
                                RouteCode = reader["RouteCode"].ToString() ?? string.Empty,
                                CityCode = reader["CityCode"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"]),
                                Description = reader["Description"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                IsPorter = reader["porter_comm"].ToString() == "Y"
                            };
                        }
                    }
                }

                if (model != null)
                {
                    string detailsQuery = @"SELECT AreaName, address_type, description, PostalCode, Comments  
                                            FROM hr_routecodes_dtl
                                            WHERE RouteCode=@RouteCode AND citycode=@citycode ORDER BY sno";
                    
                    using (var command = new MySqlCommand(detailsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@RouteCode", routeCode);
                        command.Parameters.AddWithValue("@citycode", cityCode);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                model.Details.Add(new RouteDetailModel
                                {
                                    AreaName = reader["AreaName"].ToString() ?? string.Empty,
                                    AddressType = reader["address_type"].ToString() ?? string.Empty,
                                    Description = reader["description"].ToString() ?? string.Empty,
                                    PostalCode = reader["PostalCode"].ToString() ?? string.Empty,
                                    Comments = reader["Comments"].ToString() ?? string.Empty
                                });
                            }
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> IsRouteCodeExistsAsync(string routeCode, string cityCode)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "SELECT 1 FROM hr_routecodes_hdr WHERE RouteCode=@RouteCode AND CityCode=@CityCode LIMIT 1";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@RouteCode", routeCode);
                    command.Parameters.AddWithValue("@CityCode", cityCode);
                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> IsRouteDescriptionExistsAsync(string description, string cityCode, string? excludeRouteCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeRouteCode)
                    ? "SELECT 1 FROM hr_routecodes_hdr WHERE Description=@Description AND citycode=@CityCode LIMIT 1"
                    : "SELECT 1 FROM hr_routecodes_hdr WHERE Description=@Description AND citycode=@CityCode AND RouteCode<>@RouteCode LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Description", description);
                    command.Parameters.AddWithValue("@CityCode", cityCode);
                    if (!string.IsNullOrEmpty(excludeRouteCode))
                    {
                        command.Parameters.AddWithValue("@RouteCode", excludeRouteCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> SaveRouteAsync(RouteModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string insertHdr = @"INSERT INTO hr_routecodes_hdr 
                                             (RouteCode, CityCode, FromDate, ToDate, Description, Comments, porter_comm, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                             VALUES (@RouteCode, @CityCode, @FromDate, @ToDate, @Description, @Comments, @porter, @UserId, @Date, @UserId, @Date)";

                        using (var command = new MySqlCommand(insertHdr, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                            command.Parameters.AddWithValue("@CityCode", model.CityCode);
                            command.Parameters.AddWithValue("@FromDate", model.FromDate);
                            command.Parameters.AddWithValue("@ToDate", model.ToDate);
                            command.Parameters.AddWithValue("@Description", model.Description);
                            command.Parameters.AddWithValue("@Comments", model.Comments);
                            command.Parameters.AddWithValue("@porter", model.IsPorter ? "Y" : "N");
                            command.Parameters.AddWithValue("@UserId", currentUserId);
                            command.Parameters.AddWithValue("@Date", DateTime.Now);
                            await command.ExecuteNonQueryAsync();
                        }

                        if (model.Details != null && model.Details.Count > 0)
                        {
                            string insertDtl = @"INSERT INTO hr_routecodes_dtl 
                                                 (RouteCode, citycode, sno, AreaName, Address_type, Description, PostalCode, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date) 
                                                 VALUES (@RouteCode, @citycode, @sno, @AreaName, @Address_type, @Description, @PostalCode, @Comments, @UserId, @Date, @UserId, @Date)";
                            
                            int sno = 1;
                            foreach (var detail in model.Details)
                            {
                                using (var command = new MySqlCommand(insertDtl, connection, transaction as MySqlTransaction))
                                {
                                    command.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                                    command.Parameters.AddWithValue("@citycode", model.CityCode);
                                    command.Parameters.AddWithValue("@sno", sno++);
                                    command.Parameters.AddWithValue("@AreaName", detail.AreaName);
                                    command.Parameters.AddWithValue("@Address_type", detail.AddressType);
                                    command.Parameters.AddWithValue("@Description", detail.Description);
                                    command.Parameters.AddWithValue("@PostalCode", string.IsNullOrEmpty(detail.PostalCode) ? "0" : detail.PostalCode);
                                    command.Parameters.AddWithValue("@Comments", detail.Comments);
                                    command.Parameters.AddWithValue("@UserId", currentUserId);
                                    command.Parameters.AddWithValue("@Date", DateTime.Now);
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

        public async Task<bool> UpdateRouteAsync(RouteModel model, string oldCityCode, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateHdr = @"UPDATE hr_routecodes_hdr 
                                             SET CityCode=@CityCode, porter_comm=@porter, FromDate=@FromDate, ToDate=@ToDate, 
                                                 Description=@Description, Comments=@Comments, UpdatedBy=@UserId, Updated_Date=@Date 
                                             WHERE RouteCode=@RouteCode AND citycode=@oldcitycode";

                        using (var command = new MySqlCommand(updateHdr, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                            command.Parameters.AddWithValue("@oldcitycode", oldCityCode);
                            command.Parameters.AddWithValue("@CityCode", model.CityCode);
                            command.Parameters.AddWithValue("@FromDate", model.FromDate);
                            command.Parameters.AddWithValue("@ToDate", model.ToDate);
                            command.Parameters.AddWithValue("@porter", model.IsPorter ? "Y" : "N");
                            command.Parameters.AddWithValue("@Description", model.Description);
                            command.Parameters.AddWithValue("@Comments", model.Comments);
                            command.Parameters.AddWithValue("@UserId", currentUserId);
                            command.Parameters.AddWithValue("@Date", DateTime.Now);
                            await command.ExecuteNonQueryAsync();
                        }

                        using (var deleteDtl = new MySqlCommand("DELETE FROM hr_routecodes_dtl WHERE RouteCode=@RouteCode AND citycode=@oldcitycode", connection, transaction as MySqlTransaction))
                        {
                            deleteDtl.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                            deleteDtl.Parameters.AddWithValue("@oldcitycode", oldCityCode);
                            await deleteDtl.ExecuteNonQueryAsync();
                        }

                        if (model.Details != null && model.Details.Count > 0)
                        {
                            string insertDtl = @"INSERT INTO hr_routecodes_dtl 
                                                 (RouteCode, citycode, sno, AreaName, Address_type, Description, PostalCode, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date) 
                                                 VALUES (@RouteCode, @citycode, @sno, @AreaName, @Address_type, @Description, @PostalCode, @Comments, @UserId, @Date, @UserId, @Date)";
                            
                            int sno = 1;
                            foreach (var detail in model.Details)
                            {
                                using (var command = new MySqlCommand(insertDtl, connection, transaction as MySqlTransaction))
                                {
                                    command.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                                    command.Parameters.AddWithValue("@citycode", model.CityCode);
                                    command.Parameters.AddWithValue("@sno", sno++);
                                    command.Parameters.AddWithValue("@AreaName", detail.AreaName);
                                    command.Parameters.AddWithValue("@Address_type", detail.AddressType);
                                    command.Parameters.AddWithValue("@Description", detail.Description);
                                    command.Parameters.AddWithValue("@PostalCode", string.IsNullOrEmpty(detail.PostalCode) ? "0" : detail.PostalCode);
                                    command.Parameters.AddWithValue("@Comments", detail.Comments);
                                    command.Parameters.AddWithValue("@UserId", currentUserId);
                                    command.Parameters.AddWithValue("@Date", DateTime.Now);
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

        public async Task<bool> DeleteRouteAsync(string routeCode, string cityCode)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        using (var command = new MySqlCommand("DELETE FROM hr_routecodes_dtl WHERE RouteCode = @RouteCode AND citycode=@citycode", connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@RouteCode", routeCode);
                            command.Parameters.AddWithValue("@citycode", cityCode);
                            await command.ExecuteNonQueryAsync();
                        }

                        using (var command = new MySqlCommand("DELETE FROM hr_routecodes_hdr WHERE RouteCode = @RouteCode AND citycode=@citycode", connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@RouteCode", routeCode);
                            command.Parameters.AddWithValue("@citycode", cityCode);
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
