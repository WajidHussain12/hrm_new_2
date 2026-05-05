using System.Data;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Utilities;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class AdminUserService : IAdminUserService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public AdminUserService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<UserAdminModel>> GetAllUsersAsync()
        {
            var users = new List<UserAdminModel>();
            
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return users;
                await connection.OpenAsync();

                string sqlQuery = "SELECT userID, username, Name, job_desc, roleDescription FROM v_getusers ORDER BY userID ASC;";
                using (var command = new MySqlCommand(sqlQuery, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        users.Add(new UserAdminModel
                        {
                            UserID = reader["userID"].ToString() ?? string.Empty,
                            UserName = reader["username"].ToString() ?? string.Empty,
                            FullName = reader["Name"].ToString() ?? string.Empty,
                            JobDescription = reader["job_desc"].ToString() ?? string.Empty,
                            RoleDescription = reader["roleDescription"].ToString() ?? string.Empty
                        });
                    }
                }
            }

            return users;
        }

        public async Task<UserAdminModel?> GetUserByIdAsync(string userId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string sqlQuery = @"
                    SELECT userID, password, username, Name, job_desc, signature, active, Expiry_date, user_role, roleDescription, Loc_Code, locDescription, amt_limit 
                    FROM v_getusers 
                    WHERE userID=@userID LIMIT 1;";
                    
                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@userID", userId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var model = new UserAdminModel
                            {
                                UserID = reader["userID"].ToString() ?? string.Empty,
                                LocationID = reader["Loc_Code"].ToString() ?? string.Empty,
                                LocationDescription = reader["locDescription"].ToString() ?? string.Empty,
                                UserName = reader["username"].ToString() ?? string.Empty,
                                FullName = reader["Name"].ToString() ?? string.Empty,
                                JobDescription = reader["job_desc"].ToString() ?? string.Empty,
                                Active = reader["active"].ToString() == "1",
                                UserRole = reader["user_role"].ToString() ?? string.Empty,
                                RoleDescription = reader["roleDescription"].ToString() ?? string.Empty,
                                ExpiryDate = reader.GetDateTime("Expiry_date"),
                                AmtLimit = Convert.ToDecimal(reader["amt_limit"] == DBNull.Value ? 0 : reader["amt_limit"])
                            };

                            // Do not decrypt password to UI in standard MVC, we'll keep it empty and only update if changed.
                            // However, the legacy code decrypted it. If we need to, we can just supply a dummy value.
                            model.Password = "********";

                            if (reader["signature"] != DBNull.Value)
                            {
                                model.SignatureBase64 = Convert.ToBase64String((byte[])reader["signature"]);
                            }

                            return model;
                        }
                    }
                }
            }
            return null;
        }

        private async Task<string> GenerateNewUserIdAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return "001";
                await connection.OpenAsync();
                
                string query = "SELECT MAX(CAST(userID AS UNSIGNED)) FROM lcs_users";
                using (var command = new MySqlCommand(query, connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != DBNull.Value && result != null)
                    {
                        int maxId = Convert.ToInt32(result);
                        return (maxId + 1).ToString("D2");
                    }
                }
                return "01";
            }
        }

        public async Task<bool> IsUserNameExistsAsync(string userName, string locationId, string? excludeUserId = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string sqlQuery = string.IsNullOrEmpty(excludeUserId) 
                    ? "SELECT 1 FROM lcs_users WHERE username = @username AND Loc_Code = @loc_code LIMIT 1;"
                    : "SELECT 1 FROM lcs_users WHERE username = @username AND Loc_Code = @loc_code AND userID <> @userID LIMIT 1;";

                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@username", userName);
                    command.Parameters.AddWithValue("@loc_code", locationId);
                    if (!string.IsNullOrEmpty(excludeUserId))
                    {
                        command.Parameters.AddWithValue("@userID", excludeUserId);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> IsLocationValidAsync(string locationId, string locationDescription)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string sqlQuery = "SELECT 1 FROM hr_city WHERE code = @loc_code AND fullname = @locDescription LIMIT 1;";
                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@loc_code", locationId);
                    command.Parameters.AddWithValue("@locDescription", locationDescription);
                    
                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> CheckReferencesAsync(string userId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                const string metadataQuery = @"
                    SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, REFERENCED_COLUMN_NAME
                    FROM information_schema.KEY_COLUMN_USAGE
                    WHERE REFERENCED_TABLE_SCHEMA = DATABASE()
                      AND REFERENCED_TABLE_NAME = 'lcs_users'
                      AND REFERENCED_COLUMN_NAME IS NOT NULL";

                var references = new List<(string SchemaName, string TableName, string ColumnName, string ReferencedColumnName)>();
                using (var command = new MySqlCommand(metadataQuery, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        references.Add((
                            reader["TABLE_SCHEMA"].ToString() ?? string.Empty,
                            reader["TABLE_NAME"].ToString() ?? string.Empty,
                            reader["COLUMN_NAME"].ToString() ?? string.Empty,
                            reader["REFERENCED_COLUMN_NAME"].ToString() ?? string.Empty));
                    }
                }

                if (!references.Any())
                {
                    return false;
                }

                var valuePairs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["userID"] = userId
                };

                foreach (var tableGroup in references.GroupBy(r => new { r.SchemaName, r.TableName }))
                {
                    var filters = new List<string>();
                    using (var referenceCommand = new MySqlCommand())
                    {
                        referenceCommand.Connection = connection;

                        int index = 0;
                        foreach (var reference in tableGroup)
                        {
                            if (!valuePairs.TryGetValue(reference.ReferencedColumnName, out var value))
                            {
                                continue;
                            }

                            string parameterName = $"@p{index++}";
                            filters.Add($"`{reference.ColumnName}` = {parameterName}");
                            referenceCommand.Parameters.AddWithValue(parameterName, value);
                        }

                        if (filters.Count == 0)
                        {
                            continue;
                        }

                        referenceCommand.CommandText =
                            $"SELECT 1 FROM `{tableGroup.Key.SchemaName}`.`{tableGroup.Key.TableName}` WHERE {string.Join(" AND ", filters)} LIMIT 1";

                        var result = await referenceCommand.ExecuteScalarAsync();
                        if (result != null)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private byte[]? GetFileBytes(IFormFile? file)
        {
            if (file == null || file.Length == 0) return null;
            using (var ms = new MemoryStream())
            {
                file.CopyTo(ms);
                // Note: Image resizing is omitted for simplicity, can be added via ImageSharp if needed.
                return ms.ToArray();
            }
        }

        public async Task<bool> AddUserAsync(UserAdminModel model, string currentUserId)
        {
            model.UserID = await GenerateNewUserIdAsync();
            byte[]? signatureBytes = GetFileBytes(model.SignatureFile);

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string _insertQuery = @"
                            INSERT INTO lcs_users (userID, username, password, Name, job_desc, signature, photo, active, createdby, createddate, updatedby, updateddate, Expiry_date, user_role, Loc_Code, amt_limit)
                            VALUES (@userID, @username, @password, @Name, @job_desc, @signature, @photo, @active, @createdby, @createddate, @updatedby, @updateddate, @Expiry_date, @user_role, @Loc_Code, @amt_limit)";

                        using (var command = new MySqlCommand(_insertQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@userID", model.UserID);
                            command.Parameters.AddWithValue("@username", model.UserName);
                            // Using hash instead of old Encrypt for better security
                            command.Parameters.AddWithValue("@password", string.IsNullOrEmpty(model.Password) ? DBNull.Value : SecurityHelper.HashPassword(model.Password));
                            command.Parameters.AddWithValue("@Name", model.FullName);
                            command.Parameters.AddWithValue("@job_desc", model.JobDescription);
                            command.Parameters.AddWithValue("@signature", signatureBytes != null ? (object)signatureBytes : DBNull.Value);
                            command.Parameters.AddWithValue("@photo", DBNull.Value);
                            command.Parameters.AddWithValue("@active", model.Active ? "1" : "0");
                            command.Parameters.AddWithValue("@createdby", currentUserId);
                            command.Parameters.AddWithValue("@createddate", DateTime.Now);
                            command.Parameters.AddWithValue("@updatedby", currentUserId);
                            command.Parameters.AddWithValue("@updateddate", DateTime.Now);
                            command.Parameters.AddWithValue("@Expiry_date", model.ExpiryDate);
                            command.Parameters.AddWithValue("@user_role", model.UserRole);
                            command.Parameters.AddWithValue("@Loc_Code", model.LocationID);
                            command.Parameters.AddWithValue("@amt_limit", model.AmtLimit);

                            await command.ExecuteNonQueryAsync();
                        }

                        string _locInsert = "INSERT INTO lcs_user_location VALUES(@userID, @Loc_Code)";
                        using (var command = new MySqlCommand(_locInsert, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@userID", model.UserID);
                            command.Parameters.AddWithValue("@Loc_Code", model.LocationID);
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

        public async Task<bool> UpdateUserAsync(UserAdminModel model, string currentUserId)
        {
            byte[]? signatureBytes = GetFileBytes(model.SignatureFile);

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string updateQuery = @"
                    UPDATE lcs_users
                    SET username = @username, Name = @Name, job_desc = @job_desc, active = @active, updatedby = @updatedby, updateddate = @updateddate, 
                    Expiry_date = @Expiry_date, user_role = @user_role, Loc_Code = @Loc_Code, amt_limit = @amt_limit";

                if (!string.IsNullOrEmpty(model.Password) && model.Password != "********")
                {
                    updateQuery += ", password = @password";
                }

                if (signatureBytes != null)
                {
                    updateQuery += ", signature = @signature";
                }

                updateQuery += " WHERE userID = @userID;";

                using (var command = new MySqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@userID", model.UserID);
                    command.Parameters.AddWithValue("@username", model.UserName);
                    if (!string.IsNullOrEmpty(model.Password) && model.Password != "********")
                    {
                        command.Parameters.AddWithValue("@password", SecurityHelper.HashPassword(model.Password));
                    }
                    command.Parameters.AddWithValue("@Name", model.FullName);
                    command.Parameters.AddWithValue("@job_desc", model.JobDescription);
                    if (signatureBytes != null)
                    {
                        command.Parameters.AddWithValue("@signature", signatureBytes);
                    }
                    command.Parameters.AddWithValue("@active", model.Active ? "1" : "0");
                    command.Parameters.AddWithValue("@updatedby", currentUserId);
                    command.Parameters.AddWithValue("@updateddate", DateTime.Now);
                    command.Parameters.AddWithValue("@Expiry_date", model.ExpiryDate);
                    command.Parameters.AddWithValue("@user_role", model.UserRole);
                    command.Parameters.AddWithValue("@Loc_Code", model.LocationID);
                    command.Parameters.AddWithValue("@amt_limit", model.AmtLimit);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var command = new MySqlCommand("DELETE FROM lcs_users WHERE userID = @userID", connection))
                {
                    command.Parameters.AddWithValue("@userID", userId);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<dynamic>> SearchLocationsAsync(string term)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = "SELECT FullName, Code FROM hr_city WHERE FullName LIKE @term ORDER BY FullName LIMIT 20";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@term", $"%{term}%");
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(new
                            {
                                label = reader["FullName"].ToString(),
                                value = reader["Code"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }

        public async Task<IEnumerable<dynamic>> SearchUsersAsync(string term)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = "SELECT userID, Name FROM lcs_users WHERE Name LIKE @term OR userID LIKE @term ORDER BY Name LIMIT 20";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@term", $"%{term}%");
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(new
                            {
                                label = $"{reader["userID"]} - {reader["Name"]}",
                                value = reader["userID"].ToString(),
                                desc = reader["Name"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }
    }
}
