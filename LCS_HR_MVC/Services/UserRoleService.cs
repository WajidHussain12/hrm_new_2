using System.Data;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class UserRoleService : IUserRoleService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public UserRoleService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<UserRoleModel>> GetAllRolesAsync()
        {
            var roles = new List<UserRoleModel>();
            
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return roles;
                await connection.OpenAsync();

                string sqlQuery = "SELECT RoleID, Description, Remarks FROM lcs_users_roles ORDER BY RoleID ASC;";
                using (var command = new MySqlCommand(sqlQuery, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        roles.Add(new UserRoleModel
                        {
                            RoleID = reader["RoleID"].ToString() ?? string.Empty,
                            Description = reader["Description"].ToString() ?? string.Empty,
                            Remarks = reader["Remarks"] != DBNull.Value ? reader["Remarks"].ToString() : string.Empty
                        });
                    }
                }
            }

            return roles;
        }

        public async Task<bool> IsDescriptionExistsAsync(string description, string? excludeRoleId = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string sqlQuery = string.IsNullOrEmpty(excludeRoleId) 
                    ? "SELECT 1 FROM lcs_users_roles WHERE Description=@description LIMIT 1;"
                    : "SELECT 1 FROM lcs_users_roles WHERE Description=@description AND RoleID<>@RoleID LIMIT 1;";

                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@description", description);
                    if (!string.IsNullOrEmpty(excludeRoleId))
                    {
                        command.Parameters.AddWithValue("@RoleID", excludeRoleId);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<string> GenerateNewRoleIdAsync()
        {
            // Migrating LCS.GetID("lcs_users_roles", "roleid") logic. Usually gets max + 1 formatted.
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return "001";
                await connection.OpenAsync();
                
                string query = "SELECT MAX(CAST(RoleID AS UNSIGNED)) FROM lcs_users_roles";
                using (var command = new MySqlCommand(query, connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != DBNull.Value && result != null)
                    {
                        int maxId = Convert.ToInt32(result);
                        return (maxId + 1).ToString("D3");
                    }
                }
                return "001";
            }
        }

        public async Task<bool> AddRoleAsync(UserRoleModel role)
        {
            role.RoleID = await GenerateNewRoleIdAsync();

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string sqlQuery = "INSERT INTO lcs_users_roles (RoleID, Description, Remarks) VALUES (@RoleID, @Description, @Remarks)";
                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@RoleID", role.RoleID);
                    command.Parameters.AddWithValue("@Description", role.Description);
                    command.Parameters.AddWithValue("@Remarks", role.Remarks ?? (object)DBNull.Value);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateRoleAsync(UserRoleModel role)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string sqlQuery = "UPDATE lcs_users_roles SET Description = @Description, Remarks = @Remarks WHERE RoleID = @RoleID";
                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@RoleID", role.RoleID);
                    command.Parameters.AddWithValue("@Description", role.Description);
                    command.Parameters.AddWithValue("@Remarks", role.Remarks ?? (object)DBNull.Value);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteRoleAsync(string roleId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string sqlQuery = "DELETE FROM lcs_users_roles WHERE RoleID = @RoleID";
                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@RoleID", roleId);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }
    }
}
