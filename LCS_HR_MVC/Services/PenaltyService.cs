using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models.Penalty;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class PenaltyService : IPenaltyService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public PenaltyService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<dynamic>> GetPenaltyTypesAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync("SELECT PTID as Value, PenaltyType as Text FROM hr_penaltytype WHERE IsDeleted = 0");
            }
        }

        public async Task<IEnumerable<dynamic>> GetDivisionsAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync("SELECT BUID as Value, Name as Text FROM lcs_setup.businessunit WHERE IsDeleted=0 ORDER BY BUID DESC");
            }
        }

        public async Task<IEnumerable<dynamic>> GetDepartmentsByDivisionAsync(int divisionId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync(
                    "SELECT PDID as Value, PDName as Text FROM hr_parentdepartment WHERE BUID=@divisionId AND IsDeleted=0 ORDER BY PDName ASC",
                    new { divisionId });
            }
        }

        public async Task<IEnumerable<dynamic>> GetSubDepartmentsByDepartmentAsync(int departmentId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();
                return await connection.QueryAsync("SELECT SDID as Value, FullName as Text FROM hr_subdepartment WHERE ParentID=@departmentId AND IsDeleted=0", new { departmentId });
            }
        }

        public async Task<IEnumerable<PenaltyFineModel>> GetAllPenaltyFinesAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<PenaltyFineModel>();
                await connection.OpenAsync();

                string query = @"SELECT fine.ID, fine.code, 
                                 (CASE LENGTH(TRIM(fine.code)) WHEN 3 THEN (SELECT FullName FROM hr_department WHERE Code=TRIM(fine.code)) ELSE (SELECT NAME FROM hr_employeepersonaldetail WHERE emp_no=TRIM(fine.code)) END) AS Employee,
                                 (CASE flag WHEN 'E' THEN 'Employee Wise' ELSE 'Department Wise' END) AS flag,
                                 pt.PenaltyType AS type, FineDate, amount, u.UserName
                                 FROM hr_penalty_fine fine 
                                 INNER JOIN lcs_user_location lul ON lul.city_code= fine.city_id
                                 INNER JOIN hr_penaltytype pt ON pt.PTID = fine.Type
                                 INNER JOIN lcs_users u ON u.UserID = fine.CreatedBy
                                 WHERE lul.userid=@UserId ORDER BY ID Desc LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                return results.Select(r => new PenaltyFineModel
                {
                    ID = r.ID.ToString(),
                    EmpNo = r.code.ToString(),
                    EmployeeName = r.Employee?.ToString() ?? "",
                    Mode = r.flag?.ToString() ?? "",
                    PenaltyTypeName = r.type?.ToString() ?? "",
                    FineDate = r.FineDate,
                    Amount = Convert.ToDecimal(r.amount),
                    CreatorName = r.UserName?.ToString() ?? ""
                });
            }
        }

        public async Task<PenaltyFineModel?> GetPenaltyFineByIdAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT ID, hr_pen.Code, (SELECT NAME FROM hr_employeepersonaldetail WHERE Emp_No = TRIM(hr_pen.Code)) AS Employee, flag, TYPE, hr_pen.Remarks, FineDate, amount, pd.BUID, sd.SDID, sd.ParentID
                                 FROM hr_penalty_fine hr_pen
                                 LEFT JOIN hr_employeedepartmentdetails dp ON dp.Emp_No = hr_pen.Code AND hr_pen.flag='D'
                                 LEFT JOIN hr_subdepartment sd ON sd.SDID = dp.DeptCode AND hr_pen.flag='D'
                                 LEFT JOIN hr_parentdepartment pd ON pd.PDID = sd.ParentID AND hr_pen.flag='D'
                                 WHERE hr_pen.ID=@Id LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Id = id });
                if (r == null) return null;

                return new PenaltyFineModel
                {
                    ID = r.ID.ToString(),
                    EmpNo = r.Code?.ToString() ?? "",
                    EmployeeName = r.Employee?.ToString() ?? "",
                    EmployeeDescription = r.Employee?.ToString() ?? "",
                    Mode = r.flag?.ToString() ?? "E",
                    PenaltyType = r.TYPE?.ToString() ?? "",
                    Reason = r.Remarks?.ToString() ?? "",
                    FineDate = r.FineDate,
                    Amount = Convert.ToDecimal(r.amount),
                    DivisionId = r.BUID?.ToString(),
                    DepartmentId = r.ParentID?.ToString(),
                    SubDepartmentId = r.SDID?.ToString()
                };
            }
        }

        private async Task<string> GenerateNewIdAsync(MySqlConnection connection, MySqlTransaction transaction, string table, string column, int digits)
        {
            string query = $"SELECT MAX(CAST({column} AS UNSIGNED)) FROM {table}";
            var result = await connection.ExecuteScalarAsync(query, transaction: transaction);
            if (result != DBNull.Value && result != null)
            {
                int maxId = Convert.ToInt32(result);
                return (maxId + 1).ToString($"D{digits}");
            }
            return "".PadLeft(digits - 1, '0') + "1";
        }

        public async Task<bool> AddPenaltyFineAsync(PenaltyFineModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newId = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_penalty_fine", "ID", 6);
                        
                        if (model.Mode == "D")
                        {
                            // Department wise logic
                            string dQ = @"SELECT p.EMP_NO, p.P_CITY_CODE FROM hr_employeepersonaldetail p
                                          INNER JOIN hr_employeedepartmentdetails d ON p.EMP_NO = d.Emp_No
                                          WHERE p.EMP_STATUS <> 'I' AND p.LEFT_DATE IS NULL AND d.ToDate IS NULL AND d.DeptCode=@SDID";
                            
                            var employees = await connection.QueryAsync(dQ, new { SDID = model.SubDepartmentId }, transaction);
                            if (!employees.Any()) throw new ArgumentException("There is no employee in selected department.");

                            int counter = 0;
                            foreach(var emp in employees)
                            {
                                // Check duplicate
                                int exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_penalty_fine WHERE code=@code AND FineDate=@FineDate AND Remarks=@Remarks", 
                                    new { code = emp.EMP_NO, FineDate = model.FineDate, Remarks = model.Reason }, transaction);
                                if (exists > 0) throw new ArgumentException($"Emp_NO '{emp.EMP_NO}' has a same entry on the selected date.");

                                string iQ = @"INSERT INTO hr_penalty_fine (ID, code, city_id, flag, type, FineDate, Remarks, CreatedBy, Created_Date, UpdatedBy, Updated_Date, amount)
                                              VALUES (@ID, @code, @city_id, 'D', @type, @FineDate, @Remarks, @CreatedBy, NOW(), @UpdatedBy, NOW(), @amount)";

                                await connection.ExecuteAsync(iQ, new {
                                    ID = (int.Parse(newId) + counter).ToString("000000"),
                                    code = emp.EMP_NO,
                                    city_id = emp.P_CITY_CODE,
                                    type = model.PenaltyType,
                                    FineDate = model.FineDate,
                                    Remarks = model.Reason,
                                    CreatedBy = currentUserId,
                                    UpdatedBy = currentUserId,
                                    amount = model.Amount
                                }, transaction);
                                counter++;
                            }
                        }
                        else
                        {
                            // Employee wise logic
                            int exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_penalty_fine WHERE code=@code AND FineDate=@FineDate AND Remarks=@Remarks", 
                                new { code = model.EmpNo, FineDate = model.FineDate, Remarks = model.Reason }, transaction);
                            if (exists > 0) throw new ArgumentException($"Emp_no: {model.EmpNo} has a same entry on the selected date.");

                            string cQ = "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo";
                            string city = await connection.ExecuteScalarAsync<string>(cQ, new { EmpNo = model.EmpNo }, transaction);
                            if (string.IsNullOrEmpty(city)) throw new ArgumentException("Employee City has not defined yet.");

                            string iQ = @"INSERT INTO hr_penalty_fine (ID, code, city_id, flag, type, FineDate, Remarks, CreatedBy, Created_Date, UpdatedBy, Updated_Date, amount)
                                          VALUES (@ID, @code, @city_id, 'E', @type, @FineDate, @Remarks, @CreatedBy, NOW(), @UpdatedBy, NOW(), @amount)";

                            await connection.ExecuteAsync(iQ, new {
                                ID = newId,
                                code = model.EmpNo,
                                city_id = city,
                                type = model.PenaltyType,
                                FineDate = model.FineDate,
                                Remarks = model.Reason,
                                CreatedBy = currentUserId,
                                UpdatedBy = currentUserId,
                                amount = model.Amount
                            }, transaction);
                        }

                        await transaction.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        public async Task<bool> UpdatePenaltyFineAsync(PenaltyFineModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        if (model.Mode == "E")
                        {
                            int exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_penalty_fine WHERE code=@code AND FineDate=@FineDate AND Remarks=@Remarks AND ID<>@ID", 
                                new { code = model.EmpNo, FineDate = model.FineDate, Remarks = model.Reason, ID = model.ID }, transaction);
                            if (exists > 0) throw new ArgumentException($"Emp_no: {model.EmpNo} has a same entry on the selected date.");

                            string cQ = "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo";
                            string city = await connection.ExecuteScalarAsync<string>(cQ, new { EmpNo = model.EmpNo }, transaction);

                            string uQ = @"UPDATE hr_penalty_fine SET code=@code, city_id=@city_id, flag='E', type=@type, FineDate=@FineDate, Remarks=@Remarks, UpdatedBy=@UpdatedBy, Updated_Date=NOW(), amount=@amount WHERE ID=@ID";
                            await connection.ExecuteAsync(uQ, new { code = model.EmpNo, city_id = city, type = model.PenaltyType, FineDate = model.FineDate, Remarks = model.Reason, UpdatedBy = currentUserId, amount = model.Amount, ID = model.ID }, transaction);
                        }
                        else
                        {
                            // Department wise updating legacy allowed only amount update directly
                            string uQ = @"UPDATE hr_penalty_fine SET amount=@amount, UpdatedBy=@UpdatedBy, Updated_Date=NOW() WHERE ID=@ID";
                            await connection.ExecuteAsync(uQ, new { amount = model.Amount, UpdatedBy = currentUserId, ID = model.ID }, transaction);
                        }

                        await transaction.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        public async Task<bool> DeletePenaltyFineAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                int affected = await connection.ExecuteAsync("DELETE FROM hr_penalty_fine WHERE ID=@ID", new { ID = id });
                return affected > 0;
            }
        }

        public async Task<IEnumerable<BulkPenaltyDeleteModel>> GetBulkPenaltyBatchesAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<BulkPenaltyDeleteModel>();
                await connection.OpenAsync();

                string query = @"SELECT * FROM (
                                   SELECT COUNT(pn.code) AS Records, u.UserName AS CreatedBy, Created_Date as CreatedDate
                                   FROM hr_penalty_fine pn 
                                   INNER JOIN lcs_users u ON u.userID = pn.CreatedBy
                                   WHERE DATE(Created_Date) >= DATE_FORMAT(CURDATE(), '%Y-%m-01') - INTERVAL 2 MONTH
                                   AND pn.Remarks <> 'Carry Forward'
                                   GROUP BY Created_Date, pn.CreatedBy
                                 ) AS xb WHERE xb.Records > 1 ORDER BY xb.CreatedDate DESC";
                                 
                return await connection.QueryAsync<BulkPenaltyDeleteModel>(query);
            }
        }

        public async Task<bool> DeleteBulkPenaltyBatchAsync(string createdBy, string createdDate)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string q = "DELETE FROM hr_penalty_fine WHERE Created_Date=@Date AND (SELECT UserName FROM lcs_users WHERE userID=hr_penalty_fine.CreatedBy)=@By AND Remarks <> 'Carry Forward'";
                int res = await connection.ExecuteAsync(q, new { Date = Convert.ToDateTime(createdDate), By = createdBy });
                return res > 0;
            }
        }

        public async Task<(int successCount, string message)> BulkUploadPenaltyFineAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            if (file == null || file.Length == 0) return (0, "Invalid file.");
            if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) return (0, "File should have .csv extension.");

            int insertedRows = 0;

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;
                
                using (var reader = new StreamReader(stream))
                {
                    using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
                    {
                        if (connection == null) return (0, "DB Error");
                        await connection.OpenAsync();

                        using (var transaction = await connection.BeginTransactionAsync())
                        {
                            try
                            {
                                int row = 0;
                                while (!reader.EndOfStream)
                                {
                                    var line = await reader.ReadLineAsync();
                                    var values = line?.Split(',');
                                    if (values == null || values.Length != 5) throw new ArgumentException("Incorrect CSV format");
                                    
                                    if (row == 0) { row++; continue; } // Skip header

                                    string empNo = string.IsNullOrEmpty(values[0]) ? null : values[0].PadLeft(14, '0');
                                    string fineDate = string.IsNullOrEmpty(values[1]) ? string.Empty : values[1];
                                    string pType = string.IsNullOrEmpty(values[2]) ? null : values[2];
                                    decimal amt = string.IsNullOrEmpty(values[3]) ? 0 : Convert.ToDecimal(values[3]);
                                    string comments = string.IsNullOrEmpty(values[4]) ? null : values[4];

                                    if(string.IsNullOrEmpty(empNo)) continue;

                                    string cQ = "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo";
                                    string city = await connection.ExecuteScalarAsync<string>(cQ, new { EmpNo = empNo }, transaction);
                                    
                                    string newId = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_penalty_fine", "ID", 6);

                                    string insertQuery = @"INSERT INTO hr_penalty_fine (ID, code, city_id, flag, type, FineDate, Remarks, CreatedBy, Created_Date, UpdatedBy, Updated_Date, amount)
                                                           VALUES (@ID, @code, @city_id, 'E', @type, @FineDate, @Remarks, @CreatedBy, NOW(), @UpdatedBy, NOW(), @amount)";

                                    await connection.ExecuteAsync(insertQuery, new {
                                        ID = newId,
                                        code = empNo,
                                        city_id = city,
                                        type = pType,
                                        FineDate = Convert.ToDateTime(fineDate),
                                        Remarks = comments,
                                        CreatedBy = currentUserId,
                                        UpdatedBy = currentUserId,
                                        amount = amt
                                    }, transaction);

                                    insertedRows++;
                                    row++;
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
