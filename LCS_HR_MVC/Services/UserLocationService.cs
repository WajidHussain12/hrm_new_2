using System.Data;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class UserLocationService : IUserLocationService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public UserLocationService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<LocationItem>> GetUserLocationsAsync(string userId)
        {
            var locations = new List<LocationItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return locations;
                await connection.OpenAsync();

                string query = @"
                    SELECT ul.city_code AS code, c.fullname
                    FROM lcs_user_location ul 
                    INNER JOIN hr_city c ON ul.city_code = c.CODE
                    INNER JOIN lcs_users u ON u.userID = ul.userid
                    WHERE ul.userid = @userId";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            locations.Add(new LocationItem
                            {
                                Code = reader["code"].ToString() ?? string.Empty,
                                FullName = reader["fullname"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return locations;
        }

        public async Task<IEnumerable<LocationItem>> GetAllLocationsAsync()
        {
            var locations = new List<LocationItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return locations;
                await connection.OpenAsync();

                string query = "SELECT CODE, FullName FROM hr_city";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        locations.Add(new LocationItem
                        {
                            Code = reader["CODE"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return locations;
        }

        public async Task<bool> UpdateUserLocationsAsync(string userId, IEnumerable<string> locationCodes)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        // Delete existing
                        using (var deleteCmd = new MySqlCommand("DELETE FROM lcs_user_location WHERE userid = @userid", connection, transaction as MySqlTransaction))
                        {
                            deleteCmd.Parameters.AddWithValue("@userid", userId);
                            await deleteCmd.ExecuteNonQueryAsync();
                        }

                        // Insert new ones
                        if (locationCodes.Any())
                        {
                            using (var insertCmd = new MySqlCommand("INSERT INTO lcs_user_location (userid, city_code) VALUES (@userid, @city_code)", connection, transaction as MySqlTransaction))
                            {
                                insertCmd.Parameters.AddWithValue("@userid", userId);
                                insertCmd.Parameters.Add("@city_code", MySqlDbType.VarChar);

                                foreach (var code in locationCodes.Distinct())
                                {
                                    insertCmd.Parameters["@city_code"].Value = code;
                                    await insertCmd.ExecuteNonQueryAsync();
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
    }
}
