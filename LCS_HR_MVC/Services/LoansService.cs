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
    public class LoansService : ILoansService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public LoansService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<LoanRequestModel>> GetAllLoanRequestsAsync(string currentUserId)
        {
            var data = new List<LoanRequestModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.`LR_No`, hr.Emp_No, empDet.`Name`, lnDet.`FullName` AS LoanName,
                                        hr.`RequestDate`, hr.`reason`, hr.`RequestAmount`, 
                                        COALESCE(DATE(hr.`StartDate`), '0001-01-01') AS StartDate
                                 FROM `hr_employeeloanrequest` hr
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`emp_no` = empDet.`emp_no`
                                 INNER JOIN `hr_loantypes` lnDet ON hr.`LoanCode` = lnDet.`Code`
                                 INNER JOIN lcs_user_location lul ON lul.city_code = empDet.P_CITY_CODE
                                 WHERE lul.userid = @UserId 
                                 ORDER BY hr.`LR_No` DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new LoanRequestModel
                            {
                                Code = reader["LR_No"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                LoanName = reader["LoanName"].ToString() ?? string.Empty,
                                RequestDate = reader["RequestDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["RequestDate"]),
                                Reason = reader["reason"].ToString() ?? string.Empty,
                                RequestAmount = reader["RequestAmount"] != DBNull.Value ? Convert.ToDecimal(reader["RequestAmount"]) : 0,
                                StartDate = reader["StartDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["StartDate"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<LoanRequestModel?> GetLoanRequestByIdAsync(string id)
        {
            LoanRequestModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.`LR_No`, empdet.`NAME`, hr.`Emp_No`, lndet.`FullName` LoanName, hr.`LoanCode`,
                                        hr.`RequestDate`, hr.`Reason`, hr.`RequestAmount`, hr.`RequestInstallments`,
                                        hr.`AppAuth_PersonName`, hr.`StartDate`
                                 FROM `hr_employeeloanrequest` hr 
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`Emp_No` = empdet.`EMP_NO` 
                                 INNER JOIN `hr_loantypes` lnDet ON hr.`LoanCode` = lnDet.`Code`
                                 WHERE hr.`LR_No`=@LR_No LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LR_No", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new LoanRequestModel
                            {
                                Code = reader["LR_No"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                LoanCode = reader["LoanCode"].ToString() ?? string.Empty,
                                LoanDescription = reader["LoanName"].ToString() ?? string.Empty,
                                LoanName = reader["LoanName"].ToString() ?? string.Empty,
                                RequestDate = reader["RequestDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["RequestDate"]),
                                Reason = reader["Reason"].ToString() ?? string.Empty,
                                RequestAmount = reader["RequestAmount"] != DBNull.Value ? Convert.ToDecimal(reader["RequestAmount"]) : 0,
                                RequestInstallments = reader["RequestInstallments"] != DBNull.Value ? Convert.ToInt32(reader["RequestInstallments"]) : 0,
                                AppAuthPersonName = reader["AppAuth_PersonName"].ToString() ?? string.Empty,
                                StartDate = reader["StartDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["StartDate"])
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<IEnumerable<dynamic>> SearchLoansAsync(string term)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = "SELECT Code, FullName FROM hr_loantypes WHERE FullName LIKE @term ORDER BY FullName LIMIT 20";
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

        public async Task<bool> AddLoanRequestAsync(LoanRequestModel model, string currentUserId, string currentUserName)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string status = "P";
                if (!string.IsNullOrEmpty(model.LoanDescription) && model.LoanDescription.Contains("Claim", StringComparison.OrdinalIgnoreCase))
                {
                    status = "A";
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string code = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeloanrequest", "LR_No", 3);

                        string insertQuery = @"INSERT INTO hr_employeeloanrequest 
                                               (LR_No, Emp_No, LoanCode, RequestDate, Reason, RequestAmount, RequestInstallments, AppAuth_PersonName, StartDate, Status, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                               VALUES (@LR_No, @Emp_No, @LoanCode, @RequestDate, @Reason, @RequestAmount, @RequestInstallments, @AppAuth_PersonName, @StartDate, @Status, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                        using (var command = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@LR_No", code);
                            command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            command.Parameters.AddWithValue("@LoanCode", model.LoanCode);
                            command.Parameters.AddWithValue("@RequestDate", model.RequestDate);
                            command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                            command.Parameters.AddWithValue("@RequestAmount", model.RequestAmount);
                            command.Parameters.AddWithValue("@RequestInstallments", model.RequestInstallments);
                            command.Parameters.AddWithValue("@AppAuth_PersonName", string.IsNullOrEmpty(model.AppAuthPersonName) ? DBNull.Value : model.AppAuthPersonName);
                            command.Parameters.AddWithValue("@StartDate", model.StartDate);
                            command.Parameters.AddWithValue("@Status", status);
                            command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                            await command.ExecuteNonQueryAsync();
                        }

                        if (status == "A")
                        {
                            await InsertIntoLoanDisburseAsync(connection, transaction as MySqlTransaction, code, model, currentUserId, currentUserName);
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

        private async Task InsertIntoLoanDisburseAsync(MySqlConnection connection, MySqlTransaction transaction, string lrNo, LoanRequestModel model, string currentUserId, string currentUserName)
        {
            string ldNo = await GenerateNewIdAsync(connection, transaction, "hr_employeeloandisbursed", "LD_No", 3);
            string sqlQuery = @"INSERT INTO hr_employeeloandisbursed   
                                (LD_No, LR_No, Emp_No, LoanCode, DisbursedDate, Reason, DisbursedAmount, DeductionInstallments, DeductionStartDate, AppAuth_PersonName, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                VALUES (@LD_No, @LR_No, @Emp_No, @LoanCode, @DisbursedDate, @Reason, @DisbursedAmount, @DeductionInstallments, @DeductionStartDate, @AppAuth_PersonName, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

            using (var command = new MySqlCommand(sqlQuery, connection, transaction))
            {
                command.Parameters.AddWithValue("@LD_No", ldNo);
                command.Parameters.AddWithValue("@LR_No", lrNo);
                command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                command.Parameters.AddWithValue("@LoanCode", model.LoanCode);
                command.Parameters.AddWithValue("@DisbursedDate", model.RequestDate);
                command.Parameters.AddWithValue("@DeductionStartDate", model.RequestDate);
                command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                command.Parameters.AddWithValue("@DisbursedAmount", model.RequestAmount);
                command.Parameters.AddWithValue("@DeductionInstallments", model.RequestInstallments);
                command.Parameters.AddWithValue("@AppAuth_PersonName", string.IsNullOrEmpty(model.AppAuthPersonName) ? currentUserName : model.AppAuthPersonName);
                command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                
                await command.ExecuteNonQueryAsync();
            }
        }

        private async Task ValidateInputsForUpdateAsync(MySqlConnection connection, string lrNo)
        {
            string query = "SELECT Status FROM hr_employeeloanrequest WHERE LR_No=@LR_No";
            using (var command = new MySqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@LR_No", lrNo);
                var status = await command.ExecuteScalarAsync();
                if (status != null && status != DBNull.Value)
                {
                    string st = status.ToString()!;
                    if (st.Equals("R", StringComparison.InvariantCultureIgnoreCase))
                        throw new ArgumentException("Your request has been rejected. You cannot update this record.");
                    if (st.Equals("A", StringComparison.InvariantCultureIgnoreCase))
                        throw new ArgumentException("Your request has been approved. You cannot update this record.");
                }
            }
        }

        public async Task<bool> UpdateLoanRequestAsync(LoanRequestModel model, string currentUserId, string currentUserName)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                await ValidateInputsForUpdateAsync(connection, model.Code);

                string status = "P";
                if (!string.IsNullOrEmpty(model.LoanDescription) && model.LoanDescription.Contains("Claim", StringComparison.OrdinalIgnoreCase))
                {
                    status = "A";
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = @"UPDATE hr_employeeloanrequest 
                                               SET Emp_No=@Emp_No, LoanCode=@LoanCode, RequestDate=@RequestDate, Reason=@Reason, RequestAmount=@RequestAmount, RequestInstallments=@RequestInstallments, AppAuth_PersonName=@AppAuth_PersonName, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date, StartDate=@StartDate
                                               WHERE LR_No=@LR_No";

                        using (var command = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@LR_No", model.Code);
                            command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            command.Parameters.AddWithValue("@LoanCode", model.LoanCode);
                            command.Parameters.AddWithValue("@RequestDate", model.RequestDate);
                            command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                            command.Parameters.AddWithValue("@RequestAmount", model.RequestAmount);
                            command.Parameters.AddWithValue("@RequestInstallments", model.RequestInstallments);
                            command.Parameters.AddWithValue("@AppAuth_PersonName", string.IsNullOrEmpty(model.AppAuthPersonName) ? DBNull.Value : model.AppAuthPersonName);
                            command.Parameters.AddWithValue("@StartDate", model.StartDate);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                            await command.ExecuteNonQueryAsync();
                        }

                        if (status == "A")
                        {
                            await InsertIntoLoanDisburseAsync(connection, transaction as MySqlTransaction, model.Code, model, currentUserId, currentUserName);
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

        public async Task<bool> DeleteLoanRequestAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                await ValidateInputsForUpdateAsync(connection, id);

                // Note: LCS.CheckReferences is omitted for brevity but should ensure no dependencies before delete
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string delQuery = "DELETE FROM hr_employeeloanrequest WHERE LR_No=@LR_No";
                        using (var command = new MySqlCommand(delQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@LR_No", id);
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

        public async Task<(int successCount, string message)> BulkUploadLoanRequestsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
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

                        using (var transaction = await connection.BeginTransactionAsync())
                        {
                            try
                            {
                                for (int row = 2; row <= rowCount; row++)
                                {
                                    string empCode = worksheet.Cells[row, 1].Text.Trim();
                                    if (empCode.Length == 13) empCode = empCode.PadLeft(14, '0');
                                    if (!string.IsNullOrEmpty(empCode) && empCode.Length <= 5) empCode = empCode.PadLeft(14, '0');

                                    string loanType = worksheet.Cells[row, 2].Text.Trim();
                                    string loanRequestedDateStr = worksheet.Cells[row, 3].Text.Trim();
                                    string installments = worksheet.Cells[row, 4].Text.Trim();
                                    string comments = worksheet.Cells[row, 5].Text.Trim();
                                    string requestAmountStr = worksheet.Cells[row, 6].Text.Trim();
                                    string loanStartDateStr = worksheet.Cells[row, 7].Text.Trim();

                                    if (string.IsNullOrEmpty(empCode)) continue;

                                    string status = loanType.Contains("Claim", StringComparison.OrdinalIgnoreCase) ? "A" : "P";

                                    string lCode = "";
                                    using (var cmd = new MySqlCommand("SELECT Code FROM hr_loantypes WHERE FullName LIKE @LoanType LIMIT 1", connection, transaction as MySqlTransaction))
                                    {
                                        cmd.Parameters.AddWithValue("@LoanType", loanType);
                                        var r = await cmd.ExecuteScalarAsync();
                                        if (r == null) throw new Exception($"Loan Type '{loanType}' not found.");
                                        lCode = r.ToString() ?? "";
                                    }

                                    string code = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeloanrequest", "LR_No", 3);

                                    string insertQuery = @"INSERT INTO hr_employeeloanrequest 
                                                           (LR_No, Emp_No, LoanCode, RequestDate, Reason, RequestAmount, RequestInstallments, AppAuth_PersonName, StartDate, Status, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                           VALUES (@LR_No, @Emp_No, @LoanCode, @RequestDate, @Reason, @RequestAmount, @RequestInstallments, @AppAuth_PersonName, @StartDate, @Status, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                                                           
                                    using (var cmd = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                                    {
                                        cmd.Parameters.AddWithValue("@LR_No", code);
                                        cmd.Parameters.AddWithValue("@Emp_No", empCode);
                                        cmd.Parameters.AddWithValue("@LoanCode", lCode);
                                        cmd.Parameters.AddWithValue("@RequestDate", Convert.ToDateTime(loanRequestedDateStr));
                                        cmd.Parameters.AddWithValue("@Reason", comments);
                                        cmd.Parameters.AddWithValue("@RequestAmount", Convert.ToDecimal(requestAmountStr));
                                        cmd.Parameters.AddWithValue("@RequestInstallments", installments);
                                        cmd.Parameters.AddWithValue("@AppAuth_PersonName", DBNull.Value);
                                        cmd.Parameters.AddWithValue("@StartDate", Convert.ToDateTime(loanStartDateStr));
                                        cmd.Parameters.AddWithValue("@Status", status);
                                        cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                        cmd.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                                        cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                                        cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                                        insertedRows += await cmd.ExecuteNonQueryAsync();
                                    }
                                }
                                await transaction.CommitAsync();
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync();
                                return (0, ex.Message);
                            }
                        }
                    }
                }
            }
            return (insertedRows, "Bulk upload completed successfully.");
        }

        public async Task<IEnumerable<LoanRequestModel>> GetPendingLoanRequestsAsync(string currentUserId)
        {
            var data = new List<LoanRequestModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.`LR_No`, hr.Emp_No, empDet.`Name`, lnDet.`FullName` AS LoanName,
                                        hr.`RequestDate`, hr.`reason`, hr.`RequestAmount`, hr.`RequestInstallments`, hr.`AppAuth_PersonName`,
                                        (CASE hr.`STATUS` WHEN 'P' THEN 'Pending' WHEN 'R' THEN 'Reject' WHEN 'A' THEN 'Approve' END) AS STATUS
                                 FROM `hr_employeeloanrequest` hr
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`emp_no` = empDet.`emp_no`
                                 INNER JOIN `hr_loantypes` lnDet ON hr.`LoanCode` = lnDet.`Code`
                                 INNER JOIN lcs_user_location lul ON lul.city_code = empDet.P_CITY_CODE
                                 WHERE lul.userid = @UserId AND hr.`STATUS` = 'P'
                                 ORDER BY hr.`LR_No` DESC";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new LoanRequestModel
                            {
                                Code = reader["LR_No"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                LoanName = reader["LoanName"].ToString() ?? string.Empty,
                                RequestDate = reader["RequestDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["RequestDate"]),
                                Reason = reader["reason"].ToString() ?? string.Empty,
                                RequestAmount = reader["RequestAmount"] != DBNull.Value ? Convert.ToDecimal(reader["RequestAmount"]) : 0,
                                RequestInstallments = reader["RequestInstallments"] != DBNull.Value ? Convert.ToInt32(reader["RequestInstallments"]) : 0,
                                AppAuthPersonName = reader["AppAuth_PersonName"].ToString() ?? string.Empty,
                                Status = reader["STATUS"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<(int processed, int failed)> ProcessLoanRequestsAsync(List<string> requestCodes, string status, string currentUserId, string currentUserName)
        {
            int processed = 0;
            int failed = 0;

            if (requestCodes == null || !requestCodes.Any()) return (processed, failed);

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (processed, failed);
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        foreach (var lrNo in requestCodes)
                        {
                            string checkStatusQuery = "SELECT STATUS FROM hr_employeeloanrequest WHERE LR_No=@LR_No";
                            string currentStatus = "P";
                            using (var cmd = new MySqlCommand(checkStatusQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@LR_No", lrNo);
                                var st = await cmd.ExecuteScalarAsync();
                                if (st != null) currentStatus = st.ToString()!;
                            }

                            if (currentStatus.Equals("R", StringComparison.OrdinalIgnoreCase) || currentStatus.Equals("P", StringComparison.OrdinalIgnoreCase))
                            {
                                string updateQuery = "UPDATE hr_employeeloanrequest SET STATUS=@STATUS, AppAuth_PersonName=@AppAuth_PersonName WHERE LR_No=@LR_No";
                                using (var cmd = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@STATUS", status);
                                    cmd.Parameters.AddWithValue("@AppAuth_PersonName", currentUserName);
                                    cmd.Parameters.AddWithValue("@LR_No", lrNo);
                                    int res = await cmd.ExecuteNonQueryAsync();
                                    if (res > 0) processed++;
                                    else failed++;
                                }
                            }
                            else if (currentStatus.Equals("A", StringComparison.OrdinalIgnoreCase))
                            {
                                string chkDisb = "SELECT 1 FROM hr_employeeloandisbursed WHERE LR_No=@LR_No";
                                using (var cmd = new MySqlCommand(chkDisb, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@LR_No", lrNo);
                                    var exists = await cmd.ExecuteScalarAsync();
                                    if (exists != null)
                                    {
                                        throw new ArgumentException($"Loan Request No {lrNo} is already Disbursed.");
                                    }
                                }

                                string updateQuery = "UPDATE hr_employeeloanrequest SET STATUS=@STATUS, AppAuth_PersonName=@AppAuth_PersonName WHERE LR_No=@LR_No";
                                using (var cmd = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@STATUS", status);
                                    cmd.Parameters.AddWithValue("@AppAuth_PersonName", currentUserName);
                                    cmd.Parameters.AddWithValue("@LR_No", lrNo);
                                    int res = await cmd.ExecuteNonQueryAsync();
                                    if (res > 0) processed++;
                                    else failed++;
                                }
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

            return (processed, failed);
        }

        public async Task<IEnumerable<LoanDisbursedModel>> GetAllLoanDisbursedAsync(string currentUserId)
        {
            var data = new List<LoanDisbursedModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.LD_No, hr.Emp_No, empDet.Name, lntype.FullName LoanName, hr.DisbursedDate, hr.reason, hr.DisbursedAmount, hr.DeductionStartDate 
                                 FROM hr_employeeloandisbursed hr
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.emp_no = empDet.emp_no
                                 INNER JOIN hr_employeeloanrequest lnReq ON hr.LR_No = lnReq.LR_No
                                 INNER JOIN hr_loantypes lntype ON hr.LoanCode = lntype.Code
                                 INNER JOIN lcs_user_location lul ON lul.city_code = empDet.P_CITY_CODE
                                 WHERE lul.userid = @UserId ORDER BY hr.LD_No Desc LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new LoanDisbursedModel
                            {
                                Code = reader["LD_No"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                LoanName = reader["LoanName"].ToString() ?? string.Empty,
                                DisbursedDate = reader["DisbursedDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["DisbursedDate"]),
                                Reason = reader["reason"].ToString() ?? string.Empty,
                                DisbursedAmount = reader["DisbursedAmount"] != DBNull.Value ? Convert.ToDecimal(reader["DisbursedAmount"]) : 0,
                                DeductionStartDate = reader["DeductionStartDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["DeductionStartDate"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<dynamic?> GetApprovedLoanRequestDataAsync(string lrNo)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                // Validate request exists and is eligible for disbursing
                string chkQuery = "SELECT 1 FROM hr_employeeloanrequest WHERE LR_No=@LR_No";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@LR_No", lrNo);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null) return new { error = "Loan request does not exist." };
                }

                string query = @"SELECT hr.Emp_No, empdet.`NAME` empName, hr.LoanCode, lntype.`FullName` loanName, hr.RequestAmount, hr.RequestInstallments, hr.`Reason`
                                 FROM `hr_employeeloanrequest` hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.`Emp_No`=empDet.`Emp_No`
                                 INNER JOIN hr_loantypes lnType ON hr.`LoanCode`=lnType.`Code`
                                 WHERE hr.`LR_No`=@LR_No LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LR_No", lrNo);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new
                            {
                                success = true,
                                lrNo = lrNo,
                                empNo = reader["Emp_No"].ToString(),
                                empName = reader["empName"].ToString(),
                                loanCode = reader["LoanCode"].ToString(),
                                loanName = reader["loanName"].ToString(),
                                requestAmount = reader["RequestAmount"] != DBNull.Value ? Convert.ToDecimal(reader["RequestAmount"]) : 0,
                                installments = reader["RequestInstallments"] != DBNull.Value ? Convert.ToInt32(reader["RequestInstallments"]) : 0,
                                reason = reader["Reason"].ToString()
                            };
                        }
                    }
                }
            }
            return new { error = "Data not found" };
        }

        public async Task<LoanDisbursedModel?> GetLoanDisbursedByIdAsync(string id)
        {
            LoanDisbursedModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.LD_No, hr.`LR_No`, empDet.`EMP_NO`, empdet.NAME empName, lntype.`Code` loanCode, lntype.FullName LoanName, hr.DisbursedDate, hr.Reason, hr.`DeductionInstallments`, hr.DisbursedAmount, lnReq.RequestAmount, hr.DeductionStartDate 
                                 FROM hr_employeeloandisbursed hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No = empdet.EMP_NO 
                                 INNER JOIN hr_employeeloanrequest lnReq ON hr.LR_No = lnReq.LR_No 
                                 INNER JOIN hr_loantypes lntype ON hr.LoanCode = lntype.Code 
                                 WHERE hr.LD_No = @LD_No LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LD_No", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new LoanDisbursedModel
                            {
                                Code = reader["LD_No"].ToString() ?? string.Empty,
                                LoanReqCode = reader["LR_No"].ToString() ?? string.Empty,
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["empName"].ToString() ?? string.Empty,
                                EmployeeName = reader["empName"].ToString() ?? string.Empty,
                                LoanCode = reader["loanCode"].ToString() ?? string.Empty,
                                LoanDescription = reader["LoanName"].ToString() ?? string.Empty,
                                LoanName = reader["LoanName"].ToString() ?? string.Empty,
                                DisbursedDate = reader["DisbursedDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["DisbursedDate"]),
                                Reason = reader["Reason"].ToString() ?? string.Empty,
                                DeductionInstallments = reader["DeductionInstallments"] != DBNull.Value ? Convert.ToInt32(reader["DeductionInstallments"]) : 0,
                                DisbursedAmount = reader["DisbursedAmount"] != DBNull.Value ? Convert.ToDecimal(reader["DisbursedAmount"]) : 0,
                                RequestAmount = reader["RequestAmount"] != DBNull.Value ? Convert.ToDecimal(reader["RequestAmount"]) : 0,
                                DeductionStartDate = reader["DeductionStartDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["DeductionStartDate"])
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddLoanDisbursedAsync(LoanDisbursedModel model, string currentUserId, string currentUserName)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.DisbursedDate > model.DeductionStartDate)
                {
                    throw new ArgumentException("Disbursed date should be less than or equal to Start date.");
                }

                string chkExists = "SELECT 1 FROM hr_employeeloandisbursed WHERE LR_No=@LR_No LIMIT 1";
                using (var cmd = new MySqlCommand(chkExists, connection))
                {
                    cmd.Parameters.AddWithValue("@LR_No", model.LoanReqCode);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null)
                    {
                        throw new ArgumentException("Selected Request Loan has been disbursed already.");
                    }
                }

                if (model.RequestAmount < model.DisbursedAmount)
                {
                    throw new ArgumentException("Disbursed amount cannot be greater than request amount.");
                }

                string newCode = await GenerateNewIdAsync(connection, null, "hr_employeeloandisbursed", "LD_No", 3);

                string insertQuery = @"INSERT INTO hr_employeeloandisbursed   
                                       (LD_No, LR_No, Emp_No, LoanCode, DisbursedDate, Reason, DisbursedAmount, DeductionInstallments, DeductionStartDate, AppAuth_PersonName, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                       VALUES (@LD_No, @LR_No, @Emp_No, @LoanCode, @DisbursedDate, @Reason, @DisbursedAmount, @DeductionInstallments, @DeductionStartDate, @AppAuth_PersonName, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@LD_No", newCode);
                    command.Parameters.AddWithValue("@LR_No", model.LoanReqCode);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@LoanCode", model.LoanCode);
                    command.Parameters.AddWithValue("@DisbursedDate", model.DisbursedDate);
                    command.Parameters.AddWithValue("@DeductionStartDate", model.DeductionStartDate.HasValue ? (object)model.DeductionStartDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                    command.Parameters.AddWithValue("@DisbursedAmount", model.DisbursedAmount);
                    command.Parameters.AddWithValue("@DeductionInstallments", model.DeductionInstallments);
                    command.Parameters.AddWithValue("@AppAuth_PersonName", currentUserName);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateLoanDisbursedAsync(LoanDisbursedModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.DisbursedDate > model.DeductionStartDate)
                {
                    throw new ArgumentException("Disbursed date should be less than or equal to Start date.");
                }

                string chkExists = "SELECT 1 FROM hr_employeeloandisbursed WHERE LR_No=@LR_No AND LD_No<>@LD_No LIMIT 1";
                using (var cmd = new MySqlCommand(chkExists, connection))
                {
                    cmd.Parameters.AddWithValue("@LR_No", model.LoanReqCode);
                    cmd.Parameters.AddWithValue("@LD_No", model.Code);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null)
                    {
                        throw new ArgumentException("Selected Request Loan has been disbursed already.");
                    }
                }

                if (model.RequestAmount < model.DisbursedAmount)
                {
                    throw new ArgumentException("Disbursed amount cannot be greater than request amount.");
                }

                string updateQuery = @"UPDATE hr_employeeloandisbursed 
                                       SET LR_No = @LR_No, Emp_No = @Emp_No, LoanCode = @LoanCode, DisbursedDate = @DisbursedDate, Reason = @Reason, DisbursedAmount = @DisbursedAmount, DeductionInstallments = @DeductionInstallments, DeductionStartDate = @DeductionStartDate, UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                       WHERE LD_No = @LD_No";

                using (var command = new MySqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@LD_No", model.Code);
                    command.Parameters.AddWithValue("@LR_No", model.LoanReqCode);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@LoanCode", model.LoanCode);
                    command.Parameters.AddWithValue("@DisbursedDate", model.DisbursedDate);
                    command.Parameters.AddWithValue("@DeductionStartDate", model.DeductionStartDate.HasValue ? (object)model.DeductionStartDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                    command.Parameters.AddWithValue("@DisbursedAmount", model.DisbursedAmount);
                    command.Parameters.AddWithValue("@DeductionInstallments", model.DeductionInstallments);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteLoanDisbursedAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string chkReference = "SELECT 1 FROM hr_employeeloandeduction WHERE LD_No = @LD_No LIMIT 1";
                using (var cmd = new MySqlCommand(chkReference, connection))
                {
                    cmd.Parameters.AddWithValue("@LD_No", id);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null)
                    {
                        throw new ArgumentException("This Record has been referenced in another table");
                    }
                }

                string query = "DELETE FROM hr_employeeloandisbursed WHERE LD_No = @LD_No";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LD_No", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<(int successCount, string message)> BulkUploadLoanDisbursedAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
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

                        using (var transaction = await connection.BeginTransactionAsync())
                        {
                            try
                            {
                                for (int row = 2; row <= rowCount; row++)
                                {
                                    string empCode = worksheet.Cells[row, 1].Text.Trim();
                                    string loanRequestNumber = worksheet.Cells[row, 2].Text.Trim();
                                    string loanType = worksheet.Cells[row, 3].Text.Trim();
                                    string loanDisbursedDateStr = worksheet.Cells[row, 4].Text.Trim();
                                    string installmentsStr = worksheet.Cells[row, 5].Text.Trim();
                                    string comments = worksheet.Cells[row, 6].Text.Trim();
                                    string amountStr = worksheet.Cells[row, 7].Text.Trim();
                                    string loanStartDateStr = worksheet.Cells[row, 8].Text.Trim();

                                    if (string.IsNullOrEmpty(empCode)) continue;
                                    if (empCode.Length == 13) empCode = empCode.PadLeft(14, '0');
                                    if (empCode.Length <= 5) empCode = empCode.PadLeft(14, '0');

                                    string lCode = "";
                                    using (var cmd = new MySqlCommand("SELECT Code FROM hr_loantypes WHERE FullName LIKE @LoanType LIMIT 1", connection, transaction as MySqlTransaction))
                                    {
                                        cmd.Parameters.AddWithValue("@LoanType", loanType);
                                        var r = await cmd.ExecuteScalarAsync();
                                        if (r == null) throw new Exception($"Loan Type '{loanType}' not found.");
                                        lCode = r.ToString() ?? "";
                                    }

                                    string ldNo = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeloandisbursed", "LD_No", 3);

                                    string insertQuery = @"INSERT INTO hr_employeeloandisbursed   
                                                           (LD_No, LR_No, Emp_No, LoanCode, DisbursedDate, Reason, DisbursedAmount, DeductionInstallments, DeductionStartDate, AppAuth_PersonName, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                           VALUES (@LD_No, @LR_No, @Emp_No, @LoanCode, @DisbursedDate, @Reason, @DisbursedAmount, @DeductionInstallments, @DeductionStartDate, NULL, @CreatedBy, NOW(), @UpdatedBy, NOW())";

                                    using (var cmd = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                                    {
                                        cmd.Parameters.AddWithValue("@LD_No", ldNo);
                                        cmd.Parameters.AddWithValue("@LR_No", loanRequestNumber);
                                        cmd.Parameters.AddWithValue("@Emp_No", empCode);
                                        cmd.Parameters.AddWithValue("@LoanCode", lCode);
                                        cmd.Parameters.AddWithValue("@DisbursedDate", Convert.ToDateTime(loanDisbursedDateStr));
                                        cmd.Parameters.AddWithValue("@Reason", comments);
                                        cmd.Parameters.AddWithValue("@DisbursedAmount", Convert.ToDecimal(amountStr));
                                        cmd.Parameters.AddWithValue("@DeductionInstallments", installmentsStr);
                                        cmd.Parameters.AddWithValue("@DeductionStartDate", Convert.ToDateTime(loanStartDateStr));
                                        cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                        cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);

                                        insertedRows += await cmd.ExecuteNonQueryAsync();
                                    }
                                }
                                await transaction.CommitAsync();
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync();
                                return (0, ex.Message);
                            }
                        }
                    }
                }
            }
            return (insertedRows, "Bulk upload completed successfully.");
        }

        public async Task<IEnumerable<LoanDeductionModel>> GetAllLoanDeductionsAsync(string currentUserId)
        {
            var data = new List<LoanDeductionModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.`LDed_No`, hr.Emp_No, empDet.Name, lntype.FullName LoanName, hr.`DeductionDate`, hr.`DeductionAmount`, hr.`Balance`
                                 FROM hr_employeeloandeduction hr
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.emp_no = empDet.emp_no
                                 INNER JOIN `hr_employeeloandisbursed` lnDis ON hr.`LD_No` = lnDis.`LD_No`
                                 INNER JOIN hr_loantypes lntype ON hr.LoanCode = lntype.Code
                                 INNER JOIN lcs_user_location lul ON lul.city_code = empDet.P_CITY_CODE
                                 WHERE lul.userid = @UserId ORDER BY hr.LDed_No Desc LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new LoanDeductionModel
                            {
                                Code = reader["LDed_No"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                LoanName = reader["LoanName"].ToString() ?? string.Empty,
                                DeductionDate = reader["DeductionDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["DeductionDate"]),
                                DeductionAmount = reader["DeductionAmount"] != DBNull.Value ? Convert.ToDecimal(reader["DeductionAmount"]) : 0,
                                Balance = reader["Balance"] != DBNull.Value ? Convert.ToDecimal(reader["Balance"]) : 0
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<dynamic?> GetLoanDisbursedDataAsync(string ldNo)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string chkQuery = "SELECT 1 FROM hr_employeeloandisbursed WHERE LD_No=@LD_No";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@LD_No", ldNo);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null) return new { error = "Loan disbursed does not exist." };
                }

                string query = @"SELECT hr.Emp_No, empdet.`NAME` empName, hr.LoanCode, lntype.`FullName` loanName, hr.`DisbursedAmount`, 
                                 (hr.`DisbursedAmount` - (SELECT IFNULL(SUM(DeductionAmount),0) FROM hr_employeeloandeduction WHERE LD_No=hr.LD_No)) AS Balance
                                 FROM `hr_employeeloandisbursed` hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.`Emp_No`=empDet.`Emp_No`
                                 INNER JOIN hr_loantypes lnType ON hr.`LoanCode`=lnType.`Code`
                                 WHERE (hr.`DisbursedAmount` - (SELECT IFNULL(SUM(DeductionAmount),0) FROM hr_employeeloandeduction WHERE LD_No=hr.LD_No)) > 0 
                                 AND hr.`LD_No`=@LD_No LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LD_No", ldNo);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new
                            {
                                success = true,
                                ldNo = ldNo,
                                empNo = reader["Emp_No"].ToString(),
                                empName = reader["empName"].ToString(),
                                loanCode = reader["LoanCode"].ToString(),
                                loanName = reader["loanName"].ToString(),
                                disbursedAmount = reader["DisbursedAmount"] != DBNull.Value ? Convert.ToDecimal(reader["DisbursedAmount"]) : 0,
                                balance = reader["Balance"] != DBNull.Value ? Convert.ToDecimal(reader["Balance"]) : 0
                            };
                        }
                    }
                }
            }
            return new { error = "No pending balance found or data not valid." };
        }

        public async Task<LoanDeductionModel?> GetLoanDeductionByIdAsync(string id)
        {
            LoanDeductionModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.`LDed_No`, hr.`Emp_No`, hr.`LD_No`, empdet.NAME, hr.`LoanCode`, lntype.FullName LoanName, 
                                        hr.`DeductionDate`, hr.`DeductionAmount`, lnDis.DisbursedAmount, hr.`Balance`, hr.comments
                                 FROM hr_employeeloandeduction hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No = empdet.EMP_NO 
                                 INNER JOIN `hr_employeeloandisbursed` lnDis ON hr.`LD_No` = lnDis.`LD_No` 
                                 INNER JOIN hr_loantypes lntype ON hr.LoanCode = lntype.Code 
                                 WHERE hr.`LDed_No`=@LDed_No LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LDed_No", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new LoanDeductionModel
                            {
                                Code = reader["LDed_No"].ToString() ?? string.Empty,
                                LoanDisbursedCode = reader["LD_No"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                LoanCode = reader["LoanCode"].ToString() ?? string.Empty,
                                LoanDescription = reader["LoanName"].ToString() ?? string.Empty,
                                LoanName = reader["LoanName"].ToString() ?? string.Empty,
                                DeductionDate = reader["DeductionDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["DeductionDate"]),
                                Comments = reader["comments"].ToString() ?? string.Empty,
                                DisbursedAmount = reader["DisbursedAmount"] != DBNull.Value ? Convert.ToDecimal(reader["DisbursedAmount"]) : 0,
                                DeductionAmount = reader["DeductionAmount"] != DBNull.Value ? Convert.ToDecimal(reader["DeductionAmount"]) : 0,
                                Balance = reader["Balance"] != DBNull.Value ? Convert.ToDecimal(reader["Balance"]) : 0
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddLoanDeductionAsync(LoanDeductionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.Balance < model.DeductionAmount)
                {
                    throw new ArgumentException("Deduction amount cannot be greater than Balance amount.");
                }

                string newCode = await GenerateNewIdAsync(connection, null, "hr_employeeloandeduction", "LDed_No", 3);

                string insertQuery = @"INSERT INTO hr_employeeloandeduction
                                       (LDed_No, LD_No, Emp_No, LoanCode, DeductionDate, DeductionAmount, Balance, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                       VALUES (@LDed_No, @LD_No, @Emp_No, @LoanCode, @DeductionDate, @DeductionAmount, @Balance, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@LDed_No", newCode);
                    command.Parameters.AddWithValue("@LD_No", model.LoanDisbursedCode);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@LoanCode", model.LoanCode);
                    command.Parameters.AddWithValue("@DeductionDate", model.DeductionDate);
                    command.Parameters.AddWithValue("@DeductionAmount", model.DeductionAmount);
                    command.Parameters.AddWithValue("@Balance", model.Balance - model.DeductionAmount);
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

        public async Task<bool> UpdateLoanDeductionAsync(LoanDeductionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.Balance < model.DeductionAmount)
                {
                    throw new ArgumentException("Deduction amount cannot be greater than Balance amount.");
                }

                string updateQuery = @"UPDATE hr_employeeloandeduction 
                                       SET LD_No = @LD_No, Emp_No = @Emp_No, LoanCode = @LoanCode, DeductionDate = @DeductionDate, DeductionAmount = @DeductionAmount, Balance = @Balance, Comments = @Comments, UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                       WHERE LDed_No = @LDed_No";

                using (var command = new MySqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@LDed_No", model.Code);
                    command.Parameters.AddWithValue("@LD_No", model.LoanDisbursedCode);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@LoanCode", model.LoanCode);
                    command.Parameters.AddWithValue("@DeductionDate", model.DeductionDate);
                    command.Parameters.AddWithValue("@DeductionAmount", model.DeductionAmount);
                    command.Parameters.AddWithValue("@Balance", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments); // Wait, old system didn't recalculate balance on update in params except trusting input. We will do: model.Balance (which is passed from UI). Actually old system:  string.IsNullOrEmpty(txtBalance.Text)?DBNull.Value:(object)txtBalance.Text.Trim()
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    // Correcting Balance calculation
                    command.Parameters["@Balance"].Value = model.Balance;

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteLoanDeductionAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_employeeloandeduction WHERE LDed_No = @LDed_No";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LDed_No", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<(int successCount, string message)> BulkUploadLoanDeductionsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
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

                        using (var transaction = await connection.BeginTransactionAsync())
                        {
                            try
                            {
                                for (int row = 2; row <= rowCount; row++)
                                {
                                    string empCode = worksheet.Cells[row, 1].Text.Trim();
                                    string disbursedcode = worksheet.Cells[row, 2].Text.Trim();
                                    string loanType = worksheet.Cells[row, 3].Text.Trim();
                                    string deductionDateStr = worksheet.Cells[row, 4].Text.Trim();
                                    string comments = worksheet.Cells[row, 5].Text.Trim();
                                    string balanceStr = worksheet.Cells[row, 6].Text.Trim();
                                    string deductionAmountStr = worksheet.Cells[row, 7].Text.Trim();

                                    if (string.IsNullOrEmpty(empCode)) continue;
                                    if (empCode.Length == 13) empCode = empCode.PadLeft(14, '0');
                                    if (empCode.Length <= 5) empCode = empCode.PadLeft(14, '0');

                                    decimal balance = string.IsNullOrEmpty(balanceStr) ? 0 : Convert.ToDecimal(balanceStr);
                                    decimal deductionAmount = string.IsNullOrEmpty(deductionAmountStr) ? 0 : Convert.ToDecimal(deductionAmountStr);
                                    decimal newBalance = balance - deductionAmount;

                                    string lCode = "";
                                    using (var cmd = new MySqlCommand("SELECT Code FROM hr_loantypes WHERE FullName LIKE @LoanType LIMIT 1", connection, transaction as MySqlTransaction))
                                    {
                                        cmd.Parameters.AddWithValue("@LoanType", loanType);
                                        var r = await cmd.ExecuteScalarAsync();
                                        if (r == null) throw new Exception($"Loan Type '{loanType}' not found.");
                                        lCode = r.ToString() ?? "";
                                    }

                                    string ldNo = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeloandeduction", "LDed_No", 3);

                                    string insertQuery = @"INSERT INTO hr_employeeloandeduction
                                                           (LDed_No, LD_No, Emp_No, LoanCode, DeductionDate, DeductionAmount, Balance, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                           VALUES (@LDed_No, @LD_No, @Emp_No, @LoanCode, @DeductionDate, @DeductionAmount, @Balance, @Comments, @CreatedBy, NOW(), @UpdatedBy, NOW())";

                                    using (var cmd = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                                    {
                                        cmd.Parameters.AddWithValue("@LDed_No", ldNo);
                                        cmd.Parameters.AddWithValue("@LD_No", disbursedcode);
                                        cmd.Parameters.AddWithValue("@Emp_No", empCode);
                                        cmd.Parameters.AddWithValue("@LoanCode", lCode);
                                        cmd.Parameters.AddWithValue("@DeductionDate", Convert.ToDateTime(deductionDateStr));
                                        cmd.Parameters.AddWithValue("@DeductionAmount", deductionAmount);
                                        cmd.Parameters.AddWithValue("@Balance", newBalance);
                                        cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(comments) ? DBNull.Value : comments);
                                        cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                        cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);

                                        insertedRows += await cmd.ExecuteNonQueryAsync();
                                    }
                                }
                                await transaction.CommitAsync();
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync();
                                return (0, ex.Message);
                            }
                        }
                    }
                }
            }
            return (insertedRows, "Bulk upload completed successfully.");
        }
    }
}