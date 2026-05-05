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
    public class LeavesService : ILeavesService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public LeavesService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<LeaveRequestModel>> GetAllLeaveRequestsAsync(string currentUserId)
        {
            var data = new List<LeaveRequestModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT elr.ELReq_No, epd.NAME, epd.EMP_NO, elr.RequestDate, elr.Reason, elr.AppAuth_PersonName,
                                        (CASE elr.Status WHEN 'UP' THEN 'Under Process' WHEN 'A' THEN 'Approved' ELSE 'Rejected' END) AS STATUS
                                 FROM hr_employeeleaverequest elr 
                                 INNER JOIN hr_employeepersonaldetail epd ON elr.emp_NO = epd.EMP_NO
                                 INNER JOIN lcs_user_location lul ON lul.city_code = epd.P_CITY_CODE
                                 WHERE lul.userid=@UserId ORDER BY elr.ELReq_No DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new LeaveRequestModel
                            {
                                RequestNo = reader["ELReq_No"].ToString() ?? string.Empty,
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                RequestDate = reader["RequestDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["RequestDate"]),
                                Reason = reader["Reason"].ToString() ?? string.Empty,
                                AppAuthPersonName = reader["AppAuth_PersonName"].ToString() ?? string.Empty,
                                Status = reader["STATUS"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<LeaveRequestModel?> GetLeaveRequestByIdAsync(string id)
        {
            LeaveRequestModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT elr.ELReq_No, epd.EMP_NO, elr.LeaveCode, elr.RuleCode, lvS.fullName, atr.LeaveName,
                                        epd.NAME, elr.RequestDate, elr.LeaveFromDate, elr.LeaveToDate, elr.Reason, 
                                        elr.AppAuth_PersonName, elr.Status 
                                 FROM hr_employeeleaverequest elr 
                                 INNER JOIN hr_employeepersonaldetail epd ON elr.emp_NO = epd.EMP_NO 
                                 INNER JOIN hr_leavestructure lvS on elr.LeaveCode=lvS.Code
                                 LEFT JOIN hr_attendancerules atr on elr.RuleCode=atr.RuleCode
                                 WHERE elr.ELReq_No=@ELReq_No LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ELReq_No", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new LeaveRequestModel
                            {
                                RequestNo = reader["ELReq_No"].ToString() ?? string.Empty,
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                LeaveCode = reader["LeaveCode"].ToString() ?? string.Empty,
                                LeaveCategoryName = reader["fullName"].ToString() ?? string.Empty,
                                RuleCode = reader["RuleCode"].ToString() ?? string.Empty,
                                LeaveTypeName = reader["LeaveName"].ToString() ?? string.Empty,
                                RequestDate = reader["RequestDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["RequestDate"]),
                                LeaveFromDate = reader["LeaveFromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["LeaveFromDate"]),
                                LeaveToDate = reader["LeaveToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["LeaveToDate"]),
                                Reason = reader["Reason"].ToString() ?? string.Empty,
                                AppAuthPersonName = reader["AppAuth_PersonName"].ToString() ?? string.Empty,
                                Status = reader["Status"].ToString() ?? "UP"
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<IEnumerable<dynamic>> SearchLeaveCategoriesAsync(string term)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = "SELECT Code, FullName FROM hr_leavestructure WHERE FullName LIKE @term ORDER BY FullName LIMIT 20";
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

        public async Task<IEnumerable<dynamic>> SearchLeaveTypesAsync(string term)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = "SELECT RuleCode, LeaveName FROM hr_attendancerules WHERE LeaveName LIKE @term ORDER BY LeaveName LIMIT 20";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@term", $"%{term}%");
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(new
                            {
                                label = reader["LeaveName"].ToString(),
                                value = reader["RuleCode"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }

        private async Task ValidateLeaveRequestLogicAsync(MySqlConnection connection, LeaveRequestModel model, bool isUpdate)
        {
            if (model.LeaveFromDate > model.LeaveToDate)
            {
                throw new ArgumentException("From Date cannot be greater than To Date.");
            }

            // Minimal checks replicating the core flow of legacy validation
            string empQry = "SELECT EMP_NO, MARITAL_STATUS, GENDER, EMP_STATUS, LEFT_DATE, IsConfirmed FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo";
            DataRow? empData = null;
            using (var cmd = new MySqlCommand(empQry, connection))
            {
                cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        DataTable dt = new DataTable();
                        dt.Load(reader);
                        if (dt.Rows.Count > 0) empData = dt.Rows[0];
                    }
                }
            }

            if (empData == null) throw new ArgumentException("Employee does not exist.");
            if (empData["EMP_STATUS"].ToString() == "I" || empData["LEFT_DATE"] != DBNull.Value) throw new ArgumentException("Employee Left or Inactive.");
            if (empData["IsConfirmed"].ToString() == "0") throw new ArgumentException("Employee not Confirmed.");

            if (empData["GENDER"].ToString() == "M")
            {
                if (model.LeaveCode == "004" || model.LeaveCode == "009") throw new ArgumentException("Employee does not eligible for this category.");
                if (model.LeaveCode == "003" && empData["MARITAL_STATUS"].ToString() == "S") throw new ArgumentException("Employee Marital Status is single.");
            }
            else if (empData["GENDER"].ToString() == "F")
            {
                if (model.LeaveCode == "003") throw new ArgumentException("Employee does not eligible for this category.");
                if ((model.LeaveCode == "004" || model.LeaveCode == "009") && empData["MARITAL_STATUS"].ToString() == "S") throw new ArgumentException("Employee Marital Status is single.");
            }
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

        public async Task<bool> AddLeaveRequestAsync(LeaveRequestModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                await ValidateLeaveRequestLogicAsync(connection, model, false);

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string code = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeleaverequest", "ELReq_No", 3);

                        string query = @"INSERT INTO hr_employeeleaverequest 
                                         (ELReq_No, Emp_No, LeaveCode, RuleCode, RequestDate, LeaveFromDate, LeaveToDate, Reason, AppAuth_PersonName, Status, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                         VALUES (@ELReq_No, @Emp_No, @LeaveCode, @RuleCode, @RequestDate, @LeaveFromDate, @LeaveToDate, @Reason, @AppAuth_PersonName, @Status, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                        using (var command = new MySqlCommand(query, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@ELReq_No", code);
                            command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            command.Parameters.AddWithValue("@LeaveCode", model.LeaveCode);
                            command.Parameters.AddWithValue("@RuleCode", model.RuleCode);
                            command.Parameters.AddWithValue("@RequestDate", model.RequestDate);
                            command.Parameters.AddWithValue("@LeaveFromDate", model.LeaveFromDate);
                            command.Parameters.AddWithValue("@LeaveToDate", model.LeaveToDate);
                            command.Parameters.AddWithValue("@Reason", model.Reason);
                            command.Parameters.AddWithValue("@AppAuth_PersonName", DBNull.Value);
                            command.Parameters.AddWithValue("@Status", "UP"); // Always Under Process first
                            command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

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

        public async Task<bool> UpdateLeaveRequestAsync(LeaveRequestModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Check status before updating
                string stQuery = "SELECT Status FROM hr_employeeleaverequest WHERE ELReq_No=@ELReq_No";
                using (var cmd = new MySqlCommand(stQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@ELReq_No", model.RequestNo);
                    var status = await cmd.ExecuteScalarAsync();
                    if (status != null && status != DBNull.Value)
                    {
                        if (status.ToString() == "R" || status.ToString() == "A")
                        {
                            throw new ArgumentException("Your request has been processed. You cannot Update or Delete this record.");
                        }
                    }
                }

                await ValidateLeaveRequestLogicAsync(connection, model, true);

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string query = @"UPDATE hr_employeeleaverequest 
                                         SET Emp_No=@Emp_No, LeaveCode=@LeaveCode, RuleCode=@RuleCode, RequestDate=@RequestDate, LeaveFromDate=@LeaveFromDate, LeaveToDate=@LeaveToDate, Reason=@Reason, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                         WHERE ELReq_No=@ELReq_No";

                        using (var command = new MySqlCommand(query, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@ELReq_No", model.RequestNo);
                            command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            command.Parameters.AddWithValue("@LeaveCode", model.LeaveCode);
                            command.Parameters.AddWithValue("@RuleCode", model.RuleCode);
                            command.Parameters.AddWithValue("@RequestDate", model.RequestDate);
                            command.Parameters.AddWithValue("@LeaveFromDate", model.LeaveFromDate);
                            command.Parameters.AddWithValue("@LeaveToDate", model.LeaveToDate);
                            command.Parameters.AddWithValue("@Reason", model.Reason);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

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

        public async Task<bool> DeleteLeaveRequestAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Check status before deleting
                string stQuery = "SELECT Status FROM hr_employeeleaverequest WHERE ELReq_No=@ELReq_No";
                using (var cmd = new MySqlCommand(stQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@ELReq_No", id);
                    var status = await cmd.ExecuteScalarAsync();
                    if (status != null && status != DBNull.Value)
                    {
                        if (status.ToString() == "R" || status.ToString() == "A")
                        {
                            throw new ArgumentException("Your request has been processed. You cannot Update or Delete this record.");
                        }
                    }
                }

                string query = "DELETE FROM hr_employeeleaverequest WHERE ELReq_No=@ELReq_No";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ELReq_No", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<(int successCount, string message)> BulkUploadLeaveRequestsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            if (file == null || file.Length == 0) return (0, "Invalid file.");
            
            int insertedRows = 0;
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null) return (0, "Excel contains no worksheets.");

                    using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
                    {
                        if (connection == null) return (0, "DB Error");
                        await connection.OpenAsync();

                        int rowCount = worksheet.Dimension.Rows;

                        for (int row = 2; row <= rowCount; row++)
                        {
                            string empCode = worksheet.Cells[row, 1].Text.Trim();
                            if (empCode.Length == 13) empCode = empCode.PadLeft(14, '0');
                            if (!string.IsNullOrEmpty(empCode) && empCode.Length <= 5) empCode = empCode.PadLeft(14, '0');

                            string requestedDateStr = worksheet.Cells[row, 2].Text.Trim();
                            string fromDateStr = worksheet.Cells[row, 3].Text.Trim();
                            string toDateStr = worksheet.Cells[row, 4].Text.Trim();
                            string status = worksheet.Cells[row, 5].Text.Trim();
                            string comments = worksheet.Cells[row, 6].Text.Trim();
                            string leaveCategory = worksheet.Cells[row, 7].Text.Trim();
                            string leaveType = worksheet.Cells[row, 8].Text.Trim();

                            if (string.IsNullOrEmpty(empCode)) continue;

                            // Fetch codes
                            string lCode = "", rCode = "";
                            using (var cmd = new MySqlCommand("SELECT Code FROM hr_leavestructure WHERE FullName LIKE @Category LIMIT 1", connection))
                            {
                                cmd.Parameters.AddWithValue("@Category", leaveCategory);
                                var r = await cmd.ExecuteScalarAsync();
                                if (r == null) continue;
                                lCode = r.ToString() ?? "";
                            }
                            using (var cmd = new MySqlCommand("SELECT RuleCode FROM hr_attendancerules WHERE LeaveName LIKE @Rule LIMIT 1", connection))
                            {
                                cmd.Parameters.AddWithValue("@Rule", leaveType);
                                var r = await cmd.ExecuteScalarAsync();
                                if (r == null) continue;
                                rCode = r.ToString() ?? "";
                            }

                            string code = await GenerateNewIdAsync(connection, null, "hr_employeeleaverequest", "ELReq_No", 3);

                            string insertQuery = @"INSERT INTO hr_employeeleaverequest 
                                                   (ELReq_No, Emp_No, LeaveCode, RuleCode, RequestDate, LeaveFromDate, LeaveToDate, Reason, AppAuth_PersonName, Status, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                   VALUES (@ELReq_No, @Emp_No, @LeaveCode, @RuleCode, @RequestDate, @LeaveFromDate, @LeaveToDate, @Reason, NULL, @Status, @UserId, NOW(), @UserId, NOW())";
                                                   
                            using (var cmd = new MySqlCommand(insertQuery, connection))
                            {
                                cmd.Parameters.AddWithValue("@ELReq_No", code);
                                cmd.Parameters.AddWithValue("@Emp_No", empCode);
                                cmd.Parameters.AddWithValue("@LeaveCode", lCode);
                                cmd.Parameters.AddWithValue("@RuleCode", rCode);
                                cmd.Parameters.AddWithValue("@RequestDate", Convert.ToDateTime(requestedDateStr));
                                cmd.Parameters.AddWithValue("@LeaveFromDate", Convert.ToDateTime(fromDateStr));
                                cmd.Parameters.AddWithValue("@LeaveToDate", string.IsNullOrEmpty(toDateStr) ? DBNull.Value : Convert.ToDateTime(toDateStr));
                                cmd.Parameters.AddWithValue("@Reason", comments);
                                cmd.Parameters.AddWithValue("@Status", status);
                                cmd.Parameters.AddWithValue("@UserId", currentUserId);

                                insertedRows += await cmd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
            }
            return (insertedRows, "Bulk upload completed successfully.");
        }

        public async Task<IEnumerable<LeaveRequestModel>> GetPendingLeaveRequestsAsync(string currentUserId, DateTime? fromDate, DateTime? toDate)
        {
            var data = new List<LeaveRequestModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT elr.ELReq_No, epd.NAME, elr.RequestDate, elr.LeaveFromDate, elr.LeaveToDate, elr.Reason, elr.AppAuth_PersonName,
                                        (CASE elr.Status WHEN 'UP' THEN 'Under Process' WHEN 'A' THEN 'Approved' ELSE 'Rejected' END) AS STATUS
                                 FROM hr_employeeleaverequest elr
                                 INNER JOIN hr_employeepersonaldetail epd ON elr.emp_NO = epd.EMP_NO
                                 INNER JOIN lcs_user_location lul ON lul.city_code= epd.P_CITY_CODE
                                 WHERE elr.status ='UP' AND lul.userid=@UserId";

                if (fromDate.HasValue && toDate.HasValue)
                {
                    query += " AND elr.RequestDate BETWEEN @FromDate AND @ToDate";
                }

                query += " ORDER BY elr.ELReq_No DESC";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    if (fromDate.HasValue && toDate.HasValue)
                    {
                        command.Parameters.AddWithValue("@FromDate", fromDate.Value);
                        command.Parameters.AddWithValue("@ToDate", toDate.Value);
                    }

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new LeaveRequestModel
                            {
                                RequestNo = reader["ELReq_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                RequestDate = reader["RequestDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["RequestDate"]),
                                LeaveFromDate = reader["LeaveFromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["LeaveFromDate"]),
                                LeaveToDate = reader["LeaveToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["LeaveToDate"]),
                                Reason = reader["Reason"].ToString() ?? string.Empty,
                                AppAuthPersonName = reader["AppAuth_PersonName"].ToString() ?? string.Empty,
                                Status = reader["STATUS"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<(int approved, int failed)> ApproveLeaveRequestsAsync(List<string> requestCodes, string currentUserId, string currentUserName)
        {
            int approved = 0;
            int failed = 0;

            if (requestCodes == null || !requestCodes.Any()) return (approved, failed);

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (approved, failed);
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = "UPDATE hr_employeeleaverequest SET STATUS='A', Updated_Date=NOW(), UpdatedBy=@UserId, AppAuth_PersonName=@UserName WHERE ELReq_No=@ReqNo";
                        
                        foreach (var code in requestCodes)
                        {
                            using (var cmd = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@UserId", currentUserId);
                                cmd.Parameters.AddWithValue("@UserName", currentUserName);
                                cmd.Parameters.AddWithValue("@ReqNo", code);
                                int res = await cmd.ExecuteNonQueryAsync();
                                if (res > 0) approved++;
                                else failed++;
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        return (0, requestCodes.Count);
                    }
                }
            }

            return (approved, failed);
        }

        public async Task<(int rejected, int failed)> RejectLeaveRequestsAsync(List<string> requestCodes, string currentUserId, string currentUserName)
        {
            int rejected = 0;
            int failed = 0;

            if (requestCodes == null || !requestCodes.Any()) return (rejected, failed);

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (rejected, failed);
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = "UPDATE hr_employeeleaverequest SET STATUS='R', Updated_Date=NOW(), UpdatedBy=@UserId, AppAuth_PersonName=@UserName WHERE ELReq_No=@ReqNo";
                        
                        foreach (var code in requestCodes)
                        {
                            using (var cmd = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@UserId", currentUserId);
                                cmd.Parameters.AddWithValue("@UserName", currentUserName);
                                cmd.Parameters.AddWithValue("@ReqNo", code);
                                int res = await cmd.ExecuteNonQueryAsync();
                                if (res > 0) rejected++;
                                else failed++;
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        return (0, requestCodes.Count);
                    }
                }
            }

            return (rejected, failed);
        }

        public async Task<IEnumerable<TakenLeaveModel>> GetAllTakenLeavesAsync()
        {
            var data = new List<TakenLeaveModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT TL.year, TL.emp_no, emp.NAME, TL.LeaveDate, TL.Comments 
                                 FROM TakenLeaves TL
                                 INNER JOIN hr_employeepersonaldetail emp ON tl.emp_no = emp.EMP_NO AND emp.LEFT_DATE IS NULL AND emp.EMP_STATUS <> 'I' 
                                 ORDER BY TL.emp_no, TL.LeaveDate DESC LIMIT 1000";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new TakenLeaveModel
                        {
                            Year = Convert.ToInt32(reader["year"]),
                            EmpNo = reader["emp_no"].ToString() ?? string.Empty,
                            EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                            LeaveDate = reader["LeaveDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["LeaveDate"]),
                            Comments = reader["Comments"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return data;
        }

        public async Task<bool> IsEmployeeOnProbationAsync(string empNo, int year)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "SELECT IsActive FROM TotalLeaves WHERE Year=@Year AND Emp_no=@EmpNo";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Year", year);
                    command.Parameters.AddWithValue("@EmpNo", empNo);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        // Active probation means IsActive is 1, so if it's 0 it means probation is "ON" (disabled leave form) in legacy logic.
                        // "Active Probation Period" button sets it to 1
                        return Convert.ToInt32(result) == 0;
                    }
                }
            }
            return true; // Assume true if no record to prevent errors
        }

        public async Task<bool> ActivateProbationPeriodAsync(string empNo, int year)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "UPDATE TotalLeaves SET IsActive=1 WHERE Year=@Year AND Emp_No=@Emp_No";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Year", year);
                    command.Parameters.AddWithValue("@Emp_No", empNo);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> AddTakenLeavesAsync(TakenLeaveModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                int totalDays = (int)(model.LeaveToDate!.Value - model.LeaveFromDate!.Value).TotalDays + 1;

                // Validate remaining leaves
                string remQuery = "SELECT RemainingLeaves FROM TotalLeaves WHERE Year=@Year AND Emp_no=@Emp_No";
                using (var cmd = new MySqlCommand(remQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Year", model.Year);
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    var remainingObj = await cmd.ExecuteScalarAsync();
                    if (remainingObj != null && remainingObj != DBNull.Value)
                    {
                        int remainingLeaves = Convert.ToInt32(remainingObj);
                        if (remainingLeaves < totalDays)
                        {
                            throw new ArgumentException("Remaining Leaves are smaller than Requested Leaves.");
                        }
                    }
                    else
                    {
                         throw new ArgumentException("No leave quota found for this employee for the given year.");
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        int noOfRowsInserted = 0;
                        string insertLeaveQuery = @"INSERT INTO TakenLeaves (Year, Emp_no, LeaveDate, RequestDate, IsApproved, IsDeducted, IsTaken, Comments, CreatedBy)
                                                    VALUES (@Year, @Emp_no, @LeaveDate, @RequestDate, 1, 1, 1, @Comments, @CreatedBy)";
                        
                        string insertAdjQuery = @"INSERT INTO hr_employeeattandenceadjust (emp_no, adjustmentDate, adjustmentType, year, month, reason, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                  VALUES (@Emp_no, @adjustmentDate, 'A', @Year, @month, @Comments, @CreatedBy, NOW(), @CreatedBy, NOW())";

                        for (DateTime date = model.LeaveFromDate.Value; date <= model.LeaveToDate.Value; date = date.AddDays(1))
                        {
                            using (var cmd = new MySqlCommand(insertLeaveQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Year", model.Year);
                                cmd.Parameters.AddWithValue("@Emp_no", model.EmpNo);
                                cmd.Parameters.AddWithValue("@LeaveDate", date);
                                cmd.Parameters.AddWithValue("@RequestDate", DateTime.Now);
                                cmd.Parameters.AddWithValue("@Comments", model.Comments);
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                noOfRowsInserted += await cmd.ExecuteNonQueryAsync();
                            }

                            using (var cmdAdj = new MySqlCommand(insertAdjQuery, connection, transaction as MySqlTransaction))
                            {
                                cmdAdj.Parameters.AddWithValue("@Year", model.Year);
                                cmdAdj.Parameters.AddWithValue("@Emp_no", model.EmpNo);
                                cmdAdj.Parameters.AddWithValue("@adjustmentDate", date);
                                cmdAdj.Parameters.AddWithValue("@month", date.Month);
                                cmdAdj.Parameters.AddWithValue("@Comments", model.Comments);
                                cmdAdj.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                await cmdAdj.ExecuteNonQueryAsync();
                            }
                        }

                        if (noOfRowsInserted > 0)
                        {
                            string updateQry = "UPDATE TotalLeaves SET RemainingLeaves = RemainingLeaves - @Days, DeductedLeaves = DeductedLeaves + @Days WHERE Year=@Year AND Emp_No=@Emp_No";
                            using (var cmd = new MySqlCommand(updateQry, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Year", model.Year);
                                cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                                cmd.Parameters.AddWithValue("@Days", noOfRowsInserted);
                                await cmd.ExecuteNonQueryAsync();
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

        public async Task<bool> UpdateTakenLeaveAsync(TakenLeaveModel model, DateTime originalLeaveDate, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE TakenLeaves 
                                 SET Comments=@Comments, UpdatedBy=@UpdatedBy, UpdatedDate=NOW(), LeaveDate=@NewLeaveDate 
                                 WHERE Emp_no=@Emp_no AND Year=@Year AND LeaveDate=@OriginalLeaveDate";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Emp_no", model.EmpNo);
                    command.Parameters.AddWithValue("@Year", model.Year);
                    command.Parameters.AddWithValue("@OriginalLeaveDate", originalLeaveDate);
                    command.Parameters.AddWithValue("@NewLeaveDate", model.LeaveFromDate);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteTakenLeaveAsync(string empNo, int year, DateTime leaveDate)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string qryTL = "DELETE FROM TakenLeaves WHERE Emp_no=@Emp_no AND Year=@Year AND LeaveDate=@LeaveDate";
                        int deleted = 0;
                        using (var cmd = new MySqlCommand(qryTL, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Emp_no", empNo);
                            cmd.Parameters.AddWithValue("@Year", year);
                            cmd.Parameters.AddWithValue("@LeaveDate", leaveDate);
                            deleted = await cmd.ExecuteNonQueryAsync();
                        }

                        string qryAdj = "DELETE FROM hr_employeeattandenceadjust WHERE emp_no=@Emp_no AND year=@Year AND adjustmentDate=@LeaveDate AND adjustmentType='A' AND month=@month";
                        using (var cmd = new MySqlCommand(qryAdj, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Emp_no", empNo);
                            cmd.Parameters.AddWithValue("@Year", year);
                            cmd.Parameters.AddWithValue("@LeaveDate", leaveDate);
                            cmd.Parameters.AddWithValue("@month", leaveDate.Month);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        if (deleted > 0)
                        {
                            string updateQry = "UPDATE TotalLeaves SET RemainingLeaves = RemainingLeaves+1, DeductedLeaves = DeductedLeaves-1 WHERE Year=@Year AND Emp_No=@Emp_No";
                            using (var cmd = new MySqlCommand(updateQry, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Year", year);
                                cmd.Parameters.AddWithValue("@Emp_No", empNo);
                                await cmd.ExecuteNonQueryAsync();
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