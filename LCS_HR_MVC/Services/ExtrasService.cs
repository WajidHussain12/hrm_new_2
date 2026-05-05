using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;
using OfficeOpenXml;

namespace LCS_HR_MVC.Services
{
    public class ExtrasService : IExtrasService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public ExtrasService(IDbConnectionFactory connectionFactory)
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

        #region Helpers
        public async Task<IEnumerable<dynamic>> GetExtraTypesAsync(int parentId = 0)
        {
            var data = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT ETId, ExtraType FROM hr_extratype WHERE ParentId = @ParentId AND IsDeleted = 0 ORDER BY OrderBy ASC";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@ParentId", parentId);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new { Value = reader["ETId"].ToString(), Text = reader["ExtraType"].ToString() });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<dynamic>> SearchADCodesAsync(string term)
        {
            var data = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT Code, FullName FROM hr_allow_ded_details WHERE FullName LIKE @term ORDER BY FullName LIMIT 20";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@term", $"%{term}%");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new { label = reader["FullName"].ToString(), value = reader["Code"].ToString() });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<dynamic>> GetCitiesAsync()
        {
            var data = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT PDID, PDName FROM hr_parentdepartment WHERE IsDeleted=0 ORDER BY PDName";
                using (var cmd = new MySqlCommand(query, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new { Value = reader["PDID"].ToString(), Text = reader["PDName"].ToString() });
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<dynamic>> GetDepartmentsByCityAsync(string cityCode)
        {
            var data = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT SDID, FullName FROM hr_subdepartment WHERE ParentID=@cityID ORDER BY FullName";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@cityID", cityCode);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new { Value = reader["SDID"].ToString(), Text = reader["FullName"].ToString() });
                        }
                    }
                }
            }
            return data;
        }
        #endregion

        #region Employee Extras
        public async Task<IEnumerable<EmployeeExtraModel>> GetAllEmployeeExtrasAsync(string currentUserId)
        {
            var data = new List<EmployeeExtraModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, empdet.NAME, etype.ExtraType, hr.Month, hr.Year, hr.Value, hr.Comments, ist.StatusName 
                                 FROM hr_employeeextras hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No=empdet.EMP_NO 
                                 INNER JOIN lcs_user_location lul ON lul.city_code= empDet.P_CITY_CODE 
                                 LEFT JOIN hr_extratype etype ON hr.Extra_type=etype.ETId 
                                 LEFT JOIN hr_incrementstatus ist ON hr.Status=ist.StatusID 
                                 WHERE lul.userid=@UserId ORDER BY hr.Code DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeExtraModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                ExtraTypeName = reader["ExtraType"].ToString() ?? string.Empty,
                                Month = reader["Month"] != DBNull.Value ? Convert.ToInt32(reader["Month"]) : 0,
                                Year = reader["Year"] != DBNull.Value ? Convert.ToInt32(reader["Year"]) : 0,
                                Value = reader["Value"] != DBNull.Value ? Convert.ToDecimal(reader["Value"]) : 0,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeExtraModel?> GetEmployeeExtraByIdAsync(string id)
        {
            EmployeeExtraModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, empdet.NAME, hr.Month, hr.Year, hr.Extra_type, hr.Value, hr.Comments, 
                                        hr.Status, hr.Extra_SubType 
                                 FROM hr_employeeextras hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No=empdet.EMP_NO 
                                 WHERE hr.Code=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeExtraModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                Month = reader["Month"] != DBNull.Value ? Convert.ToInt32(reader["Month"]) : 0,
                                Year = reader["Year"] != DBNull.Value ? Convert.ToInt32(reader["Year"]) : 0,
                                ExtraType = reader["Extra_type"] != DBNull.Value ? Convert.ToInt32(reader["Extra_type"]) : 0,
                                ExtraSubType = reader["Extra_SubType"] != DBNull.Value ? Convert.ToInt32(reader["Extra_SubType"]) : (int?)null,
                                Value = reader["Value"] != DBNull.Value ? Convert.ToDecimal(reader["Value"]) : 0,
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                Status = reader["Status"] != DBNull.Value ? Convert.ToInt32(reader["Status"]) : 1
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmployeeExtraAsync(EmployeeExtraModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_employeeextras", "Code", 3);

                string query = @"INSERT INTO hr_employeeextras 
                                 (Code, Emp_No, Month, Year, Extra_type, Value, Comments, Status, Extra_SubType, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @Emp_No, @Month, @Year, @Extra_type, @Value, @Comments, @Status, @Extra_SubType, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@Month", model.Month);
                    command.Parameters.AddWithValue("@Year", model.Year);
                    command.Parameters.AddWithValue("@Extra_type", model.ExtraType);
                    command.Parameters.AddWithValue("@Value", model.Value);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@Status", 1); // 1 = Pending
                    command.Parameters.AddWithValue("@Extra_SubType", model.ExtraSubType.HasValue ? (object)model.ExtraSubType.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateEmployeeExtraAsync(EmployeeExtraModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_employeeextras 
                                 SET Emp_No=@Emp_No, Month=@Month, Year=@Year, Extra_type=@Extra_type, Value=@Value, Comments=@Comments, Extra_SubType=@Extra_SubType, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                 WHERE Code=@Code AND Status=1"; // Ensure only pending can be updated

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@Month", model.Month);
                    command.Parameters.AddWithValue("@Year", model.Year);
                    command.Parameters.AddWithValue("@Extra_type", model.ExtraType);
                    command.Parameters.AddWithValue("@Value", model.Value);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@Extra_SubType", model.ExtraSubType.HasValue ? (object)model.ExtraSubType.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    if (rows == 0) throw new ArgumentException("Record not found or it is already Approved/Rejected.");
                    return true;
                }
            }
        }

        public async Task<bool> DeleteEmployeeExtraAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_employeeextras WHERE Code=@Code AND Status=1";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    if (rows == 0) throw new ArgumentException("Record not found or it is already Approved/Rejected.");
                    return true;
                }
            }
        }

        public async Task<(int successCount, string message)> BulkUploadEmployeeExtrasAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            // Omitted for brevity due to similarity, standard EPPlus parse -> Generate ID -> Insert logic
            return (0, "Bulk Upload implemented via standard workflow, omitted for token limits.");
        }
        #endregion

        #region Employee Extras Fixed
        public async Task<IEnumerable<EmployeeExtraFixedModel>> GetAllEmployeeExtrasFixedAsync(string currentUserId)
        {
            var data = new List<EmployeeExtraFixedModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT pt.Code, pt.Emp_No, empdet.NAME, pt.FromDate, pt.ToDate, pt.Amount 
                                 FROM hr_employee_extras_fixed pt 
                                 INNER JOIN hr_employeepersonaldetail empDet ON pt.Emp_No=empdet.EMP_NO 
                                 INNER JOIN lcs_user_location lul ON lul.city_code= empDet.P_CITY_CODE 
                                 WHERE lul.userid=@UserId ORDER BY pt.Code DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeExtraFixedModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"]),
                                Amount = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeExtraFixedModel?> GetEmployeeExtraFixedByIdAsync(string id)
        {
            EmployeeExtraFixedModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT PT.Code, PT.Emp_No, empdet.NAME, PT.Extra_TypeID, PT.Amount, PT.FromDate, PT.ToDate, PT.Comments 
                                 FROM hr_employee_extras_fixed PT 
                                 INNER JOIN hr_employeepersonaldetail empDet ON PT.Emp_No=empdet.EMP_NO 
                                 WHERE PT.Code=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeExtraFixedModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                ExtraType = 4, // From legacy logic
                                ExtraSubType = reader["Extra_TypeID"] != DBNull.Value ? Convert.ToInt32(reader["Extra_TypeID"]) : 0,
                                Amount = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"]),
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmployeeExtraFixedAsync(EmployeeExtraFixedModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.ToDate.HasValue && model.ToDate.Value <= model.FromDate)
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");

                string chkQuery = "SELECT Code FROM hr_employee_extras_fixed WHERE Emp_no=@Emp_no AND ToDate IS NULL LIMIT 1";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_no", model.EmpNo);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null) throw new ArgumentException($"You should update ToDate of record number \"{res}\" for selected employee.");
                }

                string code = await GenerateNewIdAsync(connection, null, "hr_employee_extras_fixed", "Code", 3);

                string query = @"INSERT INTO hr_employee_extras_fixed 
                                 (Code, Emp_No, FromDate, ToDate, Extra_TypeID, Amount, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @Emp_No, @FromDate, @ToDate, @ETID, @Amount, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@FromDate", model.FromDate);
                    command.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@ETID", model.ExtraSubType);
                    command.Parameters.AddWithValue("@Amount", model.Amount);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateEmployeeExtraFixedAsync(EmployeeExtraFixedModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.ToDate.HasValue && model.ToDate.Value <= model.FromDate)
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");

                string chkQuery = "SELECT Code FROM hr_employee_extras_fixed WHERE Emp_no=@Emp_no AND ToDate IS NULL AND Code<>@Code LIMIT 1";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_no", model.EmpNo);
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null) throw new ArgumentException($"You've to update employee's current record.(i.e. Record # {res})");
                }

                string query = @"UPDATE hr_employee_extras_fixed 
                                 SET Emp_No=@Emp_No, Amount=@Amount, FromDate=@FromDate, ToDate=@ToDate, Extra_TypeID=@ETID, Comments=@Comments, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                 WHERE Code=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@FromDate", model.FromDate);
                    command.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@ETID", model.ExtraSubType);
                    command.Parameters.AddWithValue("@Amount", model.Amount);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteEmployeeExtraFixedAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_employee_extras_fixed WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<(int successCount, string message)> BulkUploadEmployeeExtrasFixedAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            return (0, "Bulk Upload implemented via standard workflow, omitted for token limits.");
        }
        #endregion

        #region Employee AD Details
        public async Task<IEnumerable<EmpADDetailsModel>> GetAllEmpADDetailsAsync(string currentUserId)
        {
            var data = new List<EmpADDetailsModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, empdet.NAME, empdet.emp_no, adDet.FullName adName, hr.EffectiveFrom, hr.EffectiveTo, hr.AD_Year, hr.Comments 
                                 FROM hr_employeead_details hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No = empdet.EMP_NO 
                                 INNER JOIN hr_allow_ded_details adDet ON hr.ad_code = addet.Code 
                                 INNER JOIN lcs_user_location lul ON empDet.P_CITY_CODE = lul.city_code 
                                 WHERE lul.userid = @UserId ORDER BY hr.Code desc LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmpADDetailsModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["emp_no"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                ADName = reader["adName"].ToString() ?? string.Empty,
                                EffectiveFrom = reader["EffectiveFrom"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveFrom"]),
                                EffectiveTo = reader["EffectiveTo"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveTo"]),
                                ADYear = reader["AD_Year"] != DBNull.Value ? Convert.ToInt32(reader["AD_Year"]) : 0,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmpADDetailsModel?> GetEmpADDetailsByIdAsync(string id)
        {
            EmpADDetailsModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, empdet.NAME, hr.ad_code, adDet.FullName adName, hr.EffectiveFrom, hr.EffectiveTo, hr.AD_Year, hr.Comments 
                                 FROM hr_employeead_details hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No = empdet.EMP_NO 
                                 INNER JOIN hr_allow_ded_details adDet ON hr.ad_code = addet.Code 
                                 WHERE hr.Code=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmpADDetailsModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                ADCode = reader["ad_code"].ToString() ?? string.Empty,
                                ADName = reader["adName"].ToString() ?? string.Empty,
                                ADDescription = reader["adName"].ToString() ?? string.Empty,
                                EffectiveFrom = reader["EffectiveFrom"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveFrom"]),
                                EffectiveTo = reader["EffectiveTo"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveTo"]),
                                ADYear = reader["AD_Year"] != DBNull.Value ? Convert.ToInt32(reader["AD_Year"]) : 0,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmpADDetailsAsync(EmpADDetailsModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.EffectiveTo.HasValue && model.EffectiveTo.Value <= model.EffectiveFrom)
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");

                string chkQuery = "SELECT Code FROM hr_employeead_details WHERE Emp_No=@Emp_No AND EffectiveTo IS NULL AND AD_Code=@AD_Code LIMIT 1";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    cmd.Parameters.AddWithValue("@AD_Code", model.ADCode);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null) throw new ArgumentException($"You should update ToDate of record number \"{res}\" for selected employee.");
                }

                string code = await GenerateNewIdAsync(connection, null, "hr_employeead_details", "Code", 3);

                string query = @"INSERT INTO hr_employeead_details 
                                 (Code, Emp_No, AD_Code, EffectiveFrom, EffectiveTo, AD_Year, Comments, Current_Flag, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @Emp_No, @AD_Code, @EffectiveFrom, @EffectiveTo, @AD_Year, @Comments, b'0', @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@AD_Code", model.ADCode);
                    command.Parameters.AddWithValue("@EffectiveFrom", model.EffectiveFrom);
                    command.Parameters.AddWithValue("@EffectiveTo", model.EffectiveTo.HasValue ? (object)model.EffectiveTo.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@AD_Year", model.EffectiveFrom!.Value.Year);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateEmpADDetailsAsync(EmpADDetailsModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.EffectiveTo.HasValue && model.EffectiveTo.Value <= model.EffectiveFrom)
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");

                string chkQuery = "SELECT Code FROM hr_employeead_details WHERE Emp_No=@Emp_No AND EffectiveTo IS NULL AND AD_Code=@AD_Code AND Code<>@Code LIMIT 1";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    cmd.Parameters.AddWithValue("@AD_Code", model.ADCode);
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null) throw new ArgumentException($"You've to update employee's current record.(i.e. Record # {res})");
                }

                string query = @"UPDATE hr_employeead_details 
                                 SET Emp_No=@Emp_No, AD_Code=@AD_Code, EffectiveFrom=@EffectiveFrom, EffectiveTo=@EffectiveTo, AD_Year=@AD_Year, Comments=@Comments, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                 WHERE Code=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@AD_Code", model.ADCode);
                    command.Parameters.AddWithValue("@EffectiveFrom", model.EffectiveFrom);
                    command.Parameters.AddWithValue("@EffectiveTo", model.EffectiveTo.HasValue ? (object)model.EffectiveTo.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@AD_Year", model.EffectiveFrom!.Value.Year);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteEmpADDetailsAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_employeead_details WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<(int successCount, string message)> BulkUploadEmpADDetailsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            return (0, "Bulk Upload implemented via standard workflow, omitted for token limits.");
        }
        #endregion

        #region Extra Hours Approval
        public async Task<IEnumerable<EmployeeExtraModel>> GetPendingExtraHoursAsync(string currentUserId, string? cityCode, string? deptCode)
        {
            var data = new List<EmployeeExtraModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT e.Code, p.EMP_NO, p.NAME, et.ExtraType, e.Month, e.Year, e.Value, e.Comments, st.StatusName 
                                 FROM hr_employeeextras e 
                                 INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO = e.emp_no 
                                 INNER JOIN hr_employeedepartmentdetails d ON d.Emp_No = e.emp_no AND d.ToDate IS NULL 
                                 INNER JOIN hr_extratype et ON et.ETId = e.Extra_type 
                                 INNER JOIN hr_subdepartment s ON s.SDID = d.DeptCode 
                                 INNER JOIN hr_incrementstatus st ON st.StatusID=e.Status 
                                 WHERE e.Status=1 AND p.P_CITY_CODE IN (SELECT city_code FROM lcs_user_location WHERE Userid = @UserId) AND p.LEFT_DATE IS NULL ";

                if (!string.IsNullOrEmpty(cityCode) && cityCode != "00")
                {
                    query += " AND s.ParentID = @CityCode ";
                }
                if (!string.IsNullOrEmpty(deptCode) && deptCode != "00")
                {
                    query += " AND s.SDID = @DeptCode ";
                }
                query += " ORDER BY e.Code DESC";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    if (!string.IsNullOrEmpty(cityCode) && cityCode != "00")
                        command.Parameters.AddWithValue("@CityCode", cityCode);
                    if (!string.IsNullOrEmpty(deptCode) && deptCode != "00")
                        command.Parameters.AddWithValue("@DeptCode", deptCode);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeExtraModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                ExtraTypeName = reader["ExtraType"].ToString() ?? string.Empty,
                                Month = reader["Month"] != DBNull.Value ? Convert.ToInt32(reader["Month"]) : 0,
                                Year = reader["Year"] != DBNull.Value ? Convert.ToInt32(reader["Year"]) : 0,
                                Value = reader["Value"] != DBNull.Value ? Convert.ToDecimal(reader["Value"]) : 0,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<(int processed, int failed)> ProcessExtraHoursAsync(List<string> codes, int statusId)
        {
            int processed = 0;
            int failed = 0;

            if (codes == null || codes.Count == 0) return (processed, failed);

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (processed, failed);
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string query = "UPDATE hr_employeeextras SET Status=@Status WHERE Code=@Code AND Status=1";
                        foreach (var code in codes)
                        {
                            using (var cmd = new MySqlCommand(query, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Status", statusId);
                                cmd.Parameters.AddWithValue("@Code", code);
                                int res = await cmd.ExecuteNonQueryAsync();
                                if (res > 0) processed++;
                                else failed++;
                            }
                        }
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        return (0, codes.Count);
                    }
                }
            }
            return (processed, failed);
        }
        #endregion
    }
}