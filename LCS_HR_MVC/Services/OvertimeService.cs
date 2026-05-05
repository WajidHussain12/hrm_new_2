using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class OvertimeService : IOvertimeService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public OvertimeService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        private async Task<string> GenerateNewIdAsync(MySqlConnection connection, MySqlTransaction? transaction, string table, string column, int digits)
        {
            string query = $"SELECT MAX(CAST({column} AS UNSIGNED)) FROM {table}";
            using (var command = new MySqlCommand(query, connection, transaction))
            {
                var result = await command.ExecuteScalarAsync();
                if (result != DBNull.Value && result != null)
                {
                    int maxId = Convert.ToInt32(result);
                    return (maxId + 1).ToString($"D{digits}");
                }
            }
            return "".PadLeft(digits - 1, '0') + "1";
        }

        public async Task<IEnumerable<EmployeeOvertimeModel>> GetAllEmployeeOvertimesAsync(string currentUserId)
        {
            var data = new List<EmployeeOvertimeModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT es.CODE, es.Emp_No, ep.NAME empname, es.DATE, es.Duration, es.Unit, es.TOTALAMOUNT amount 
                                 FROM hr_employeeovertimedetails es 
                                 INNER JOIN hr_employeepersonaldetail ep ON es.Emp_No=ep.EMP_NO
                                 INNER JOIN lcs_user_location lul ON lul.city_code= ep.P_CITY_CODE 
                                 WHERE lul.userid=@UserId ORDER BY es.CODE DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeOvertimeModel
                            {
                                Code = reader["CODE"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["empname"].ToString() ?? string.Empty,
                                Date = reader["DATE"] == DBNull.Value ? null : Convert.ToDateTime(reader["DATE"]),
                                Duration = reader["Duration"] != DBNull.Value ? Convert.ToDecimal(reader["Duration"]) : 0,
                                Unit = reader["Unit"].ToString() ?? string.Empty,
                                TotalAmount = reader["amount"] != DBNull.Value ? Convert.ToDecimal(reader["amount"]) : 0
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeOvertimeModel?> GetEmployeeOvertimeByIdAsync(string id)
        {
            EmployeeOvertimeModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT es.CODE, es.Emp_No, ep.NAME, es.DATE, es.Duration, es.Unit, es.TotalAmount, es.Reason, es.AppAuth_PersonName 
                                 FROM hr_employeeovertimedetails es 
                                 INNER JOIN hr_employeepersonaldetail ep ON es.Emp_No = ep.EMP_NO 
                                 WHERE es.CODE = @CODE LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CODE", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeOvertimeModel
                            {
                                Code = reader["CODE"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                Date = reader["DATE"] == DBNull.Value ? null : Convert.ToDateTime(reader["DATE"]),
                                Duration = reader["Duration"] != DBNull.Value ? Convert.ToDecimal(reader["Duration"]) : 0,
                                Unit = reader["Unit"].ToString() ?? string.Empty,
                                TotalAmount = reader["TotalAmount"] != DBNull.Value ? Convert.ToDecimal(reader["TotalAmount"]) : 0,
                                Reason = reader["Reason"].ToString() ?? string.Empty,
                                AppAuthPersonName = reader["AppAuth_PersonName"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmployeeOvertimeAsync(EmployeeOvertimeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string checkQry = "SELECT 1 FROM hr_employeeovertimedetails WHERE DATE=@Date AND EMP_NO=@EmpNo";
                using (var cmd = new MySqlCommand(checkQry, connection))
                {
                    cmd.Parameters.AddWithValue("@Date", model.Date);
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null) throw new ArgumentException("Record already exists for this date and employee.");
                }

                string code = await GenerateNewIdAsync(connection, null, "hr_employeeovertimedetails", "CODE", 3);

                string insertQry = @"INSERT INTO hr_employeeovertimedetails 
                                     (CODE, Emp_No, DATE, Duration, Unit, TotalAmount, Reason, AppAuth_PersonName, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                     VALUES (@CODE, @Emp_No, @DATE, @Duration, @Unit, @TotalAmount, @Reason, @AppAuth_PersonName, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(insertQry, connection))
                {
                    command.Parameters.AddWithValue("@CODE", code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@DATE", model.Date);
                    command.Parameters.AddWithValue("@Duration", model.Duration);
                    command.Parameters.AddWithValue("@Unit", model.Unit);
                    command.Parameters.AddWithValue("@TotalAmount", model.TotalAmount);
                    command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                    command.Parameters.AddWithValue("@AppAuth_PersonName", model.AppAuthPersonName);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateEmployeeOvertimeAsync(EmployeeOvertimeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string checkQry = "SELECT 1 FROM hr_employeeovertimedetails WHERE DATE=@Date AND EMP_NO=@EmpNo AND CODE<>@CODE";
                using (var cmd = new MySqlCommand(checkQry, connection))
                {
                    cmd.Parameters.AddWithValue("@Date", model.Date);
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@CODE", model.Code);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null) throw new ArgumentException("Record already exists for this date and employee.");
                }

                string updateQry = @"UPDATE hr_employeeovertimedetails 
                                     SET Emp_No=@Emp_No, DATE=@DATE, Duration=@Duration, Unit=@Unit, TotalAmount=@TotalAmount, Reason=@Reason, AppAuth_PersonName=@AppAuth_PersonName, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                     WHERE CODE=@CODE";

                using (var command = new MySqlCommand(updateQry, connection))
                {
                    command.Parameters.AddWithValue("@CODE", model.Code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@DATE", model.Date);
                    command.Parameters.AddWithValue("@Duration", model.Duration);
                    command.Parameters.AddWithValue("@Unit", model.Unit);
                    command.Parameters.AddWithValue("@TotalAmount", model.TotalAmount);
                    command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                    command.Parameters.AddWithValue("@AppAuth_PersonName", model.AppAuthPersonName);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteEmployeeOvertimeAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_employeeovertimedetails WHERE CODE=@CODE";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CODE", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }
    }
}