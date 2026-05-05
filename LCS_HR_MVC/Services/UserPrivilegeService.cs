using System.Data;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class UserPrivilegeService : IUserPrivilegeService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public UserPrivilegeService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<PrivilegeItem>> GetPrivilegesAsync(string roleId)
        {
            var privileges = new List<PrivilegeItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return privileges;
                await connection.OpenAsync();

                string sqlQuery = @"
SELECT
     (SELECT
            IF(EXISTS( SELECT ID FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                     (SELECT ID FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID)
                    ,'NILL')
        ) privilegesID  ,

    d.Description,
    d.menuid,
    d.SubMenuID,
    d.SubmenudetID,
    (select description from lcs_menu where menuid=d.menuid ) as menu,
    (select description from lcs_submenu where menuid=d.menuid and SubMenuID=d.SubMenuID) as subMenu,
    concat((select description from lcs_menu where menuid=d.menuid),'>>',(select description from lcs_submenu where menuid=d.menuid and SubMenuID=d.SubMenuID),'>>',d.Description) menuLocation,
    (SELECT
            IF(EXISTS( SELECT can_View FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    bin(( SELECT bin(can_View) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID)),
                    bin(0))
        ) can_View,
    (SELECT
            IF(EXISTS( SELECT BIN(can_Insert) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    (SELECT BIN(can_Insert) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    bin(0))
        ) can_Insert,
    (SELECT
            IF(EXISTS( SELECT BIN(can_Update) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    (SELECT BIN(can_Update) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    bin(0))
        ) can_Update,
    (SELECT
            IF(EXISTS( SELECT BIN(can_Delete) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    (SELECT BIN(can_Delete) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    bin(0))
        ) can_Delete,
 (SELECT
            IF(EXISTS( SELECT BIN(can_Delete) FROM lcs_roles_privileges WHERE RoleID = @RoleID AND MenuID = d.MenuID AND SubMenuID = d.SubMenuID AND SubmenudetID = d.SubmenudetID),
                    0,1)
        ) ShouldInsert        
FROM
    lcs_submenu_det d";

                using (var command = new MySqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@RoleID", roleId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            privileges.Add(new PrivilegeItem
                            {
                                PrivilegesID = reader["privilegesID"].ToString() ?? string.Empty,
                                ShouldInsert = Convert.ToInt32(reader["ShouldInsert"]),
                                MenuID = reader["menuid"].ToString() ?? string.Empty,
                                SubMenuID = reader["SubMenuID"].ToString() ?? string.Empty,
                                SubmenudetID = reader["SubmenudetID"].ToString() ?? string.Empty,
                                MenuLocation = reader["menuLocation"].ToString() ?? string.Empty,
                                FormName = reader["Description"].ToString() ?? string.Empty,
                                CanView = reader["can_View"].ToString() == "1",
                                CanInsert = reader["can_Insert"].ToString() == "1",
                                CanUpdate = reader["can_Update"].ToString() == "1",
                                CanDelete = reader["can_Delete"].ToString() == "1"
                            });
                        }
                    }
                }
            }
            return privileges;
        }

        private async Task<string> GenerateNewPrivilegeIdAsync(MySqlConnection connection, MySqlTransaction transaction)
        {
            string query = "SELECT MAX(CAST(ID AS UNSIGNED)) FROM lcs_roles_privileges";
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

        public async Task<bool> UpdatePrivilegesAsync(string roleId, IEnumerable<PrivilegeItem> privileges)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string currentNewId = "";

                        foreach (var item in privileges)
                        {
                            string id = item.PrivilegesID;
                            if (item.ShouldInsert == 1)
                            {
                                if (string.IsNullOrEmpty(currentNewId))
                                {
                                    currentNewId = await GenerateNewPrivilegeIdAsync(connection, transaction as MySqlTransaction);
                                }
                                else
                                {
                                    currentNewId = (Convert.ToInt64(currentNewId) + 1).ToString("D6");
                                }
                                id = currentNewId;

                                string insertQuery = @"
                                    INSERT INTO lcs_roles_privileges (ID, RoleID, MenuID, SubMenuID, SubmenudetID, can_View, can_Insert, can_Update, can_Delete)
                                    VALUES (@ID, @RoleID, @MenuID, @SubMenuID, @SubmenudetID, @can_View, @can_Insert, @can_Update, @can_Delete)";

                                using (var command = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                                {
                                    command.Parameters.AddWithValue("@ID", id);
                                    command.Parameters.AddWithValue("@RoleID", roleId);
                                    command.Parameters.AddWithValue("@MenuID", item.MenuID);
                                    command.Parameters.AddWithValue("@SubMenuID", item.SubMenuID);
                                    command.Parameters.AddWithValue("@SubmenudetID", item.SubmenudetID);
                                    command.Parameters.AddWithValue("@can_View", item.CanView);
                                    command.Parameters.AddWithValue("@can_Insert", item.CanInsert);
                                    command.Parameters.AddWithValue("@can_Update", item.CanUpdate);
                                    command.Parameters.AddWithValue("@can_Delete", item.CanDelete);
                                    
                                    await command.ExecuteNonQueryAsync();
                                }
                            }
                            else
                            {
                                string updateQuery = @"
                                    UPDATE lcs_roles_privileges SET 
                                    can_View = @can_View, can_Insert = @can_Insert, can_Update = @can_Update, can_Delete = @can_Delete 
                                    WHERE ID = @ID";

                                using (var command = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                                {
                                    command.Parameters.AddWithValue("@ID", id);
                                    command.Parameters.AddWithValue("@can_View", item.CanView);
                                    command.Parameters.AddWithValue("@can_Insert", item.CanInsert);
                                    command.Parameters.AddWithValue("@can_Update", item.CanUpdate);
                                    command.Parameters.AddWithValue("@can_Delete", item.CanDelete);
                                    
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
    }
}
