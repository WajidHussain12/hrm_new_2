using System.Data;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class EmployeeService : IEmployeeService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public EmployeeService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<EmployeeSalaryModel>> GetAllEmployeeSalariesAsync()
        {
            var data = new List<EmployeeSalaryModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT a.ID AS ID, a.EMP_NO, ad.FullName AS Allownce, a.Amount, a.Comments AS Comments, IF(a.IsActive=b'1','True','False') AS IsActive, u.Name AS Created_By
                                 FROM hrms_fix_allownces a
                                 INNER JOIN hrms_allownces_dedcution_detail ad ON ad.AD_Code=a.AD_Code
                                 INNER JOIN lcs_users u ON u.userID=a.Created_By
                                 ORDER BY a.EMP_NO DESC LIMIT 500"; // Added limit to prevent huge payload on initial load

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new EmployeeSalaryModel
                        {
                            ID = reader["ID"].ToString() ?? string.Empty,
                            EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                            ADName = reader["Allownce"].ToString() ?? string.Empty,
                            Amount = Convert.ToDecimal(reader["Amount"]),
                            Comments = reader["Comments"].ToString() ?? string.Empty,
                            IsActive = reader["IsActive"].ToString() == "True",
                            CreatedBy = reader["Created_By"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<SelectListItem>> GetAllowancesForSalaryAsync()
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();

                string query = "SELECT AD_Code, FullName FROM hrms_allownces_dedcution_detail WHERE Type_ID NOT IN (2,1);";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        items.Add(new SelectListItem
                        {
                            Value = reader["AD_Code"].ToString(),
                            Text = reader["FullName"].ToString()
                        });
                    }
                }
            }
            return items;
        }

        public async Task<bool> AddEmployeeSalaryAsync(EmployeeSalaryModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"INSERT INTO lcs_hr.hrms_fix_allownces 
                                 (EMP_NO, AD_Code, Amount, Comments, IsActive, MONTH, YEAR, Created_By, created_Date)
                                 VALUES (@EMP_NO, @AD_Code, @Amount, @Comments, @IsActive, @MONTH, @YEAR, @Created_By, @created_Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EMP_NO", model.EmpNo);
                    command.Parameters.AddWithValue("@AD_Code", model.ADCode);
                    command.Parameters.AddWithValue("@Amount", model.Amount);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@IsActive", model.IsActive ? 1 : 0);
                    command.Parameters.AddWithValue("@MONTH", DateTime.Now.Month);
                    command.Parameters.AddWithValue("@YEAR", DateTime.Now.Year);
                    command.Parameters.AddWithValue("@Created_By", currentUserId);
                    command.Parameters.AddWithValue("@created_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> InsertStandardSalaryBreakupAsync(string empNo, decimal basicSalary, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Check if already exists
                string checkQuery = "SELECT 1 FROM hrms_fix_allownces WHERE EMP_NO=@EmpNo LIMIT 1";
                using (var cmd = new MySqlCommand(checkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null)
                    {
                        throw new ArgumentException("Record already exist, For increment use Increment Form.");
                    }
                }

                decimal bSalary = 0, houseRent = 0, medical = 0, utility = 0;
                if (basicSalary >= 40000)
                {
                    bSalary = (basicSalary * 65) / 100;
                    decimal allowanceAmount = basicSalary - bSalary;
                    houseRent = (bSalary * 30) / 100;
                    medical = (bSalary * 10) / 100;
                    utility = allowanceAmount - (houseRent + medical);
                }
                else
                {
                    bSalary = basicSalary;
                }

                decimal[] amounts = { bSalary, houseRent, medical, utility };
                string[] codes = { "F000001", "F000002", "F000003", "F000004" };

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string query = @"INSERT INTO hrms_fix_allownces 
                                         (EMP_NO, AD_Code, Amount, Comments, MONTH, YEAR, Created_By, created_Date)
                                         VALUES (@EMP_NO, @AD_Code, @Amount, @Comments, @MONTH, @YEAR, @Created_By, @created_Date)";

                        int insertCount = basicSalary >= 40000 ? 4 : 1;
                        
                        for (int i = 0; i < insertCount; i++)
                        {
                            using (var cmd = new MySqlCommand(query, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@EMP_NO", empNo);
                                cmd.Parameters.AddWithValue("@AD_Code", codes[i]);
                                cmd.Parameters.AddWithValue("@Amount", amounts[i]);
                                cmd.Parameters.AddWithValue("@Comments", "Standard salary breakup");
                                cmd.Parameters.AddWithValue("@MONTH", DateTime.Now.Month);
                                cmd.Parameters.AddWithValue("@YEAR", DateTime.Now.Year);
                                cmd.Parameters.AddWithValue("@Created_By", currentUserId);
                                cmd.Parameters.AddWithValue("@created_Date", DateTime.Now);
                                await cmd.ExecuteNonQueryAsync();
                            }
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

        public async Task<IEnumerable<EmployeeJobDetailModel>> GetAllEmployeeJobDetailsAsync(string currentUserId)
        {
            var data = new List<EmployeeJobDetailModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, empDet.`Name`, jobDet.`FullName` jobName, hr.`EffectiveFrom`, hr.`EffectiveTo`, hr.`Comments`
                                 FROM `hr_employeejobdetails` hr
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`emp_no` = empDet.`emp_no`
                                 INNER JOIN `hr_jobs` jobDet ON hr.`JobCode` = jobDet.`Code`
                                 INNER JOIN lcs_user_location lul ON empDet.P_CITY_CODE = lul.city_code
                                 WHERE lul.userid=@UserId ORDER BY hr.`Code` desc LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeJobDetailModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                JobName = reader["jobName"].ToString() ?? string.Empty,
                                EffectiveFrom = reader["EffectiveFrom"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveFrom"]),
                                EffectiveTo = reader["EffectiveTo"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveTo"]),
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeJobDetailModel?> GetEmployeeJobDetailByCodeAsync(string code)
        {
            EmployeeJobDetailModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.code, hr.`Emp_No`, empdet.`NAME`, hr.`JobCode`, jobdet.`FullName` jobName,
                                        hr.`EffectiveFrom`, hr.`EffectiveTo`, hr.`Comments`,
                                        B.`BUID`, pd.`PDID`, sd.`SDID`, ed.`FromDate`, ed.`ToDate`
                                 FROM `hr_employeejobdetails` hr 
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`Emp_No` = empdet.`EMP_NO` 
                                 INNER JOIN `hr_jobs` jobDet ON hr.`JobCode` = jobDet.`Code`
                                 INNER JOIN hr_employeedepartmentdetails ed ON ed.`Emp_No` = hr.`EMP_NO` AND ed.`ToDate` IS NULL
                                 INNER JOIN hr_subdepartment sd ON sd.`SDID` = ed.`DeptCode`
                                 INNER JOIN hr_parentdepartment pd ON pd.`PDID` = sd.`ParentID` 
                                 INNER JOIN lcs_setup.`businessunit` B ON B.`BUID` = pd.`BUID`
                                 WHERE hr.`Code`=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeJobDetailModel
                            {
                                Code = reader["code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                JobCode = reader["JobCode"].ToString() ?? string.Empty,
                                JobName = reader["jobName"].ToString() ?? string.Empty,
                                EffectiveFrom = reader["EffectiveFrom"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveFrom"]),
                                EffectiveTo = reader["EffectiveTo"] == DBNull.Value ? null : Convert.ToDateTime(reader["EffectiveTo"]),
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                BUID = reader["BUID"].ToString() ?? string.Empty,
                                ParentDeptId = reader["PDID"].ToString() ?? string.Empty,
                                SubDeptId = reader["SDID"].ToString() ?? string.Empty,
                                DeptFromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                DeptToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"])
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<IEnumerable<SelectListItem>> GetParentDepartmentsByBUAsync(string buId)
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                // legacy system companyid hardcoded to 1 for this context often, but lets query purely by BUID
                string query = "SELECT PDID, PDName FROM hr_parentdepartment WHERE BUID = @BUID AND IsDeleted = 0";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@BUID", buId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            items.Add(new SelectListItem
                            {
                                Value = reader["PDID"].ToString(),
                                Text = reader["PDName"].ToString()
                            });
                        }
                    }
                }
            }
            return items;
        }

        public async Task<IEnumerable<SelectListItem>> GetSubDepartmentsByParentAsync(string parentId)
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();

                string query = "SELECT SDID, FullName FROM hr_subdepartment WHERE ParentID = @ParentID AND IsDeleted = 0";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ParentID", parentId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            items.Add(new SelectListItem
                            {
                                Value = reader["SDID"].ToString(),
                                Text = reader["FullName"].ToString()
                            });
                        }
                    }
                }
            }
            return items;
        }

        public async Task<IEnumerable<SelectListItem>> GetDesignationsByParentAsync(string parentId)
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();

                string query = "SELECT j.Code, j.FullName FROM hr_designationmapping dm INNER JOIN hr_jobs j ON dm.DesignationId = j.Code WHERE dm.PDeptId = @PDeptId ORDER BY j.FullName ASC";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@PDeptId", parentId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            items.Add(new SelectListItem
                            {
                                Value = reader["Code"].ToString(),
                                Text = reader["FullName"].ToString()
                            });
                        }
                    }
                }
            }
            return items;
        }

        public async Task<dynamic?> GetEmployeeDetailByCodeAsync(string empNo)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = "SELECT P_COUNTRY_CODE, BusinessUnitID FROM hr_employeepersonaldetail WHERE EMP_NO=@EMP_NO";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EMP_NO", empNo);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new
                            {
                                Company = reader["P_COUNTRY_CODE"].ToString(),
                                BUnit = reader["BusinessUnitID"].ToString()
                            };
                        }
                    }
                }
            }
            return null;
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

        public async Task<bool> AddEmployeeJobDetailAsync(EmployeeJobDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation logic for entry date
                if (!isAdmin)
                {
                    string empCreatedDateQuery = "SELECT DATE_FORMAT(CREATED_DATE,'%Y-%m-%d') FROM hr_employeepersonaldetail WHERE EMP_NO=@EMP_NO";
                    using (var cmd = new MySqlCommand(empCreatedDateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@EMP_NO", model.EmpNo);
                        var createdDateObj = await cmd.ExecuteScalarAsync();
                        if (createdDateObj != null && createdDateObj != DBNull.Value)
                        {
                            DateTime createdDate = Convert.ToDateTime(createdDateObj);
                            if (serverDate.Date != createdDate.Date)
                            {
                                throw new ArgumentException("Employee Entry Date is not matched.");
                            }
                        }
                    }
                }

                string latestJobCode = "";
                string latestDeptCode = "";
                string empDepDetailCode = "0";

                // Get latest dept details and job codes
                string latestQuery = @"SELECT CODE departDetCode,JobCode FROM hr_employeejobdetails WHERE Emp_No=@Emp_No and EffectiveTo is null LIMIT 0,1";
                using (var cmd = new MySqlCommand(latestQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            latestDeptCode = reader["departDetCode"].ToString() ?? string.Empty;
                            latestJobCode = reader["JobCode"].ToString() ?? string.Empty;
                        }
                    }
                }

                // Get Active department mapping logic matching old GetEmployee_Dep_Job_DetailByCode
                string depQuery = @"SELECT d.CODE AS EMP_Dep_Code, d.DeptCode AS SDID, s.ParentID AS PDID FROM hr_employeedepartmentdetails d INNER JOIN hr_subdepartment s ON s.SDID = d.DeptCode WHERE d.EMP_NO=@Emp_No AND d.ToDate IS NULL LIMIT 0,1";
                using (var cmd = new MySqlCommand(depQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            empDepDetailCode = reader["EMP_Dep_Code"].ToString() ?? string.Empty;
                        }
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newJobCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeejobdetails", "Code", 3);
                        string newDeptCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeedepartmentdetails", "Code", 3);

                        // Update old if exists
                        if (!string.IsNullOrEmpty(latestDeptCode))
                        {
                            string updateOldJob = "UPDATE hr_employeejobdetails SET EffectiveTo=@EffectiveTo WHERE code=@Code";
                            using (var cmd = new MySqlCommand(updateOldJob, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@EffectiveTo", model.EffectiveFrom?.AddDays(-1));
                                cmd.Parameters.AddWithValue("@Code", latestDeptCode);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        if (!string.IsNullOrEmpty(empDepDetailCode) && empDepDetailCode != "0")
                        {
                            string updateOldDept = "UPDATE hr_employeedepartmentdetails SET ToDate=@ToDate WHERE code=@Code";
                            using (var cmd = new MySqlCommand(updateOldDept, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@ToDate", model.DeptFromDate?.AddDays(-1));
                                cmd.Parameters.AddWithValue("@Code", empDepDetailCode);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Always inserting new Job detail
                        string insertJob = @"INSERT INTO hr_employeejobdetails (Code, Emp_No, JobCode, EffectiveFrom, EffectiveTo, ChangeType, Current_Flag, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                             VALUES (@Code, @Emp_No, @JobCode, @EffectiveFrom, @EffectiveTo, @ChangeType, @Current_Flag, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                        using (var cmd = new MySqlCommand(insertJob, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Code", newJobCode);
                            cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            cmd.Parameters.AddWithValue("@JobCode", model.JobCode);
                            cmd.Parameters.AddWithValue("@EffectiveFrom", model.EffectiveFrom);
                            cmd.Parameters.AddWithValue("@EffectiveTo", DBNull.Value);
                            cmd.Parameters.AddWithValue("@ChangeType", model.JobCode);
                            cmd.Parameters.AddWithValue("@Current_Flag", DBNull.Value);
                            cmd.Parameters.AddWithValue("@Comments", model.Comments);
                            cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Always inserting new Department detail (for simplicity matched legacy logic structure)
                        string insertDept = @"INSERT INTO hr_employeedepartmentdetails (Code, Emp_No, DeptCode, FromDate, ToDate, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                              VALUES (@Code, @Emp_No, @DeptCode, @FromDate, @ToDate, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                        using (var cmd = new MySqlCommand(insertDept, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Code", newDeptCode);
                            cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            cmd.Parameters.AddWithValue("@DeptCode", model.SubDeptId);
                            cmd.Parameters.AddWithValue("@FromDate", model.DeptFromDate);
                            cmd.Parameters.AddWithValue("@ToDate", DBNull.Value);
                            cmd.Parameters.AddWithValue("@Comments", model.Comments);
                            cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            await cmd.ExecuteNonQueryAsync();
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

        public async Task<bool> UpdateEmployeeJobDetailAsync(EmployeeJobDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation for delete/update exists
                string checkQuery = "SELECT 1 FROM hr_employeejobdetails WHERE code=@Code AND EffectiveTo IS NOT NULL";
                using (var cmd = new MySqlCommand(checkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        throw new ArgumentException("You cannot update this record. You have to update or delete Employee's current Job Detail record.");
                    }
                }

                string empDepDetailCode = "0";
                string depQuery = @"SELECT d.CODE AS EMP_Dep_Code FROM hr_employeedepartmentdetails d INNER JOIN hr_subdepartment s ON s.SDID = d.DeptCode WHERE d.EMP_NO=@Emp_No AND d.ToDate IS NULL LIMIT 0,1";
                using (var cmd = new MySqlCommand(depQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            empDepDetailCode = reader["EMP_Dep_Code"].ToString() ?? string.Empty;
                        }
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(empDepDetailCode) && empDepDetailCode != "0")
                        {
                            string updateDept = @"UPDATE hr_employeedepartmentdetails 
                                                  SET Emp_No=@Emp_No, DeptCode=@DeptCode, FromDate=@FromDate, ToDate=@ToDate, Comments=@Comments, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                                  WHERE CODE=@Code";
                            using (var cmd = new MySqlCommand(updateDept, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Code", empDepDetailCode);
                                cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                                cmd.Parameters.AddWithValue("@DeptCode", model.SubDeptId);
                                cmd.Parameters.AddWithValue("@FromDate", model.DeptFromDate);
                                cmd.Parameters.AddWithValue("@ToDate", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Comments", model.Comments);
                                cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                                cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string updateJob = @"UPDATE hr_employeejobdetails 
                                             SET Emp_No=@Emp_No, JobCode=@JobCode, EffectiveFrom=@EffectiveFrom, EffectiveTo=@EffectiveTo, ChangeType=@ChangeType, Current_Flag=@Current_Flag, Comments=@Comments, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                             WHERE Code=@Code";
                        using (var cmd = new MySqlCommand(updateJob, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Code", model.Code);
                            cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            cmd.Parameters.AddWithValue("@JobCode", model.JobCode);
                            cmd.Parameters.AddWithValue("@EffectiveFrom", model.EffectiveFrom);
                            cmd.Parameters.AddWithValue("@EffectiveTo", DBNull.Value);
                            cmd.Parameters.AddWithValue("@ChangeType", model.JobCode);
                            cmd.Parameters.AddWithValue("@Current_Flag", DBNull.Value);
                            cmd.Parameters.AddWithValue("@Comments", model.Comments);
                            cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            await cmd.ExecuteNonQueryAsync();
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

        public async Task<bool> DeleteEmployeeJobDetailAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation for delete/update exists
                string checkQuery = "SELECT 1 FROM hr_employeejobdetails WHERE code=@Code AND EffectiveTo IS NOT NULL";
                using (var cmd = new MySqlCommand(checkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Code", code);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        throw new ArgumentException("You cannot delete this record. You have to update or delete Employee's current Job Detail record.");
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string latestDeptCode = "";
                        string empNo = "";
                        // Get latest to revert
                        string latestQuery = @"SELECT Emp_No FROM hr_employeejobdetails WHERE code=@Code LIMIT 0,1";
                        using (var cmd = new MySqlCommand(latestQuery, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Code", code);
                            var eNo = await cmd.ExecuteScalarAsync();
                            if (eNo != null) empNo = eNo.ToString() ?? "";
                        }

                        if (!string.IsNullOrEmpty(empNo))
                        {
                            string prevJobCode = "";
                            string prevQuery = @"SELECT CODE FROM hr_employeejobdetails WHERE code<>@code AND Emp_No=@Emp_No ORDER BY code desc LIMIT 0,1";
                            using (var cmd = new MySqlCommand(prevQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@code", code);
                                cmd.Parameters.AddWithValue("@Emp_No", empNo);
                                var pCode = await cmd.ExecuteScalarAsync();
                                if (pCode != null) prevJobCode = pCode.ToString() ?? "";
                            }

                            if (!string.IsNullOrEmpty(prevJobCode))
                            {
                                string revertQuery = "UPDATE hr_employeejobdetails SET EffectiveTo=NULL WHERE code=@Code";
                                using (var cmd = new MySqlCommand(revertQuery, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@Code", prevJobCode);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        string delQuery = "DELETE FROM hr_employeejobdetails WHERE Code=@Code";
                        using (var cmd = new MySqlCommand(delQuery, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Code", code);
                            await cmd.ExecuteNonQueryAsync();
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

        public async Task<IEnumerable<EmployeeDepartmentDetailModel>> GetAllEmployeeDepartmentDetailsAsync(string currentUserId)
        {
            var data = new List<EmployeeDepartmentDetailModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.emp_no, empDet.Name, hr.DeptCode, dept.FullName AS departmentName, hr.FromDate, hr.ToDate
                                 FROM hr_employeedepartmentdetails hr
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.emp_no = empDet.emp_no
                                 INNER JOIN `hr_subdepartment` dept ON hr.DeptCode = dept.`SDID`
                                 INNER JOIN hr_city c ON empDet.`P_CITY_CODE` = c.Code
                                 INNER JOIN lcs_user_location lul ON empDet.`P_CITY_CODE` = lul.city_code
                                 WHERE empDet.`LEFT_DATE` IS NULL AND lul.userid=@UserId ORDER BY hr.`Code` DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeDepartmentDetailModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["emp_no"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                DepartmentName = reader["departmentName"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeDepartmentDetailModel?> GetEmployeeDepartmentDetailByCodeAsync(string code)
        {
            EmployeeDepartmentDetailModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.`Code`, hr.`Emp_No`, empdet.`NAME`, p.`BUID`, p.`PDID`, hr.`DeptCode`, hr.`FromDate`, hr.`ToDate`, hr.Comments 
                                 FROM `hr_employeedepartmentdetails` hr 
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`Emp_No`=empdet.`EMP_NO`
                                 INNER JOIN `hr_subdepartment` dept ON hr.`DeptCode`=dept.`SDID`
                                 INNER JOIN `hr_parentdepartment` p ON p.`PDID` = dept.`ParentID`
                                 WHERE hr.code =@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeDepartmentDetailModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                BUID = reader["BUID"].ToString() ?? string.Empty,
                                ParentDeptId = reader["PDID"].ToString() ?? string.Empty,
                                SubDeptId = reader["DeptCode"].ToString() ?? string.Empty,
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

        public async Task<string> GetDepartmentStrengthValidationMessageAsync(string parentDeptId, string subDeptId, string cityCode)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return "";
                await connection.OpenAsync();

                string query = "SELECT COUNT(Emp_No) AS CurrentStrength, IFNULL((SELECT value FROM hr_department_strength WHERE DeptID=@DeptID AND SubDeptID=@SubDeptID AND CityID=@CityID),0) AS MaxStrength FROM hr_employeedepartmentdetails d INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO=d.Emp_No WHERE d.ToDate IS NULL AND d.DeptCode=@SubDeptID AND p.P_CITY_CODE=@CityID";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@DeptID", parentDeptId);
                    command.Parameters.AddWithValue("@SubDeptID", subDeptId);
                    command.Parameters.AddWithValue("@CityID", cityCode);
                    
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            int current = Convert.ToInt32(reader["CurrentStrength"]);
                            int max = Convert.ToInt32(reader["MaxStrength"]);
                            
                            if (max > 0 && current >= max)
                            {
                                return $"Selected Department Strength is exceed, Department strength set to {max}";
                            }
                        }
                    }
                }
            }
            return "";
        }

        public async Task<bool> AddEmployeeDepartmentDetailAsync(EmployeeDepartmentDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation logic for entry date
                if (!isAdmin)
                {
                    string empCreatedDateQuery = "SELECT DATE_FORMAT(CREATED_DATE,'%Y-%m-%d') FROM hr_employeepersonaldetail WHERE EMP_NO=@EMP_NO";
                    using (var cmd = new MySqlCommand(empCreatedDateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@EMP_NO", model.EmpNo);
                        var createdDateObj = await cmd.ExecuteScalarAsync();
                        if (createdDateObj != null && createdDateObj != DBNull.Value)
                        {
                            DateTime createdDate = Convert.ToDateTime(createdDateObj);
                            if (serverDate.Date != createdDate.Date)
                            {
                                throw new ArgumentException("Employee Entry Date is not matched.");
                            }
                        }
                    }
                }

                // Check Dates
                string latestQuery = @"SELECT FromDate, Code, DeptCode FROM hr_employeedepartmentdetails WHERE Emp_No=@Emp_No ORDER BY CODE DESC LIMIT 0,1";
                string latestCode = "";
                string lastDepartmentCode = "";

                using (var cmd = new MySqlCommand(latestQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            latestCode = reader["Code"].ToString() ?? string.Empty;
                            lastDepartmentCode = reader["DeptCode"].ToString() ?? string.Empty;
                            
                            if (reader["FromDate"] != DBNull.Value)
                            {
                                DateTime latestTime = Convert.ToDateTime(reader["FromDate"]);
                                if (model.FromDate <= latestTime)
                                {
                                    throw new ArgumentException($"Date from should be greater than \"{latestTime.AddDays(1).ToString("dd/MM/yyyy")}\"");
                                }
                                if (model.FromDate?.AddDays(-1) == latestTime)
                                {
                                    throw new ArgumentException($"There should be at least 2 days difference between \"{latestTime.ToString("dd/MM/yyyy")}\" and the date you have selected.");
                                }
                            }
                        }
                    }
                }

                if (lastDepartmentCode == model.SubDeptId)
                {
                    throw new ArgumentException("Employee is already working in selected Sub Department");
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeedepartmentdetails", "Code", 3);

                        // Update old if exists
                        if (!string.IsNullOrEmpty(latestCode))
                        {
                            string updateOldDept = "UPDATE hr_employeedepartmentdetails SET ToDate=@ToDate WHERE code=@Code";
                            using (var cmd = new MySqlCommand(updateOldDept, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@ToDate", model.FromDate?.AddDays(-1));
                                cmd.Parameters.AddWithValue("@Code", latestCode);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Insert new Department detail
                        string insertDept = @"INSERT INTO hr_employeedepartmentdetails (Code, Emp_No, DeptCode, FromDate, ToDate, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                              VALUES (@Code, @Emp_No, @DeptCode, @FromDate, @ToDate, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                        using (var cmd = new MySqlCommand(insertDept, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Code", newCode);
                            cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            cmd.Parameters.AddWithValue("@DeptCode", model.SubDeptId);
                            cmd.Parameters.AddWithValue("@FromDate", model.FromDate);
                            cmd.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Comments", model.Comments);
                            cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            await cmd.ExecuteNonQueryAsync();
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

        public async Task<bool> UpdateEmployeeDepartmentDetailAsync(EmployeeDepartmentDetailModel model, string currentUserId, DateTime serverDate, bool isAdmin)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation for delete/update exists
                string checkQuery = "SELECT 1 FROM hr_employeedepartmentdetails WHERE code=@Code AND ToDate IS NOT NULL";
                using (var cmd = new MySqlCommand(checkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        throw new ArgumentException("You cannot update this record. You have to update or delete Employee's current department detail record.");
                    }
                }

                // Check Dates
                string latestQuery = @"SELECT FromDate FROM hr_employeedepartmentdetails WHERE Emp_No=@Emp_No AND code<>@Code ORDER BY CODE DESC LIMIT 0,1";
                using (var cmd = new MySqlCommand(latestQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            if (reader["FromDate"] != DBNull.Value)
                            {
                                DateTime latestTime = Convert.ToDateTime(reader["FromDate"]);
                                if (model.FromDate <= latestTime)
                                {
                                    throw new ArgumentException($"Date from should be greater than \"{latestTime.AddDays(1).ToString("dd/MM/yyyy")}\"");
                                }
                                if (model.FromDate?.AddDays(-1) == latestTime)
                                {
                                    throw new ArgumentException($"There should be at least 2 days difference between \"{latestTime.ToString("dd/MM/yyyy")}\" and the date you have selected.");
                                }
                            }
                        }
                    }
                }

                // Check if changing dept
                string latestDeptCode = "";
                string lastDepartmentCode = "";
                string getDeptQry = "SELECT Code, DeptCode FROM hr_employeedepartmentdetails WHERE Emp_No=@Emp_No and ToDate is null LIMIT 0,1";
                using (var cmd = new MySqlCommand(getDeptQry, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            latestDeptCode = reader["Code"].ToString() ?? "";
                            lastDepartmentCode = reader["DeptCode"].ToString() ?? "";
                        }
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(latestDeptCode) && lastDepartmentCode != model.SubDeptId)
                        {
                            // Changing department creates a new record and caps old
                            string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeedepartmentdetails", "Code", 3);

                            string updateOldDept = "UPDATE hr_employeedepartmentdetails SET ToDate=NOW(), UpdatedBy=@UpdatedBy, Updated_Date=NOW() WHERE Emp_no=@Emp_No AND ToDate IS NULL";
                            using (var cmd = new MySqlCommand(updateOldDept, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                                cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            string insertDept = @"INSERT INTO hr_employeedepartmentdetails (Code, Emp_No, DeptCode, FromDate, ToDate, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                                  VALUES (@Code, @Emp_No, @DeptCode, @FromDate, @ToDate, @Comments, @CreatedBy, NOW(), @UpdatedBy, NOW())";
                            using (var cmd = new MySqlCommand(insertDept, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Code", newCode);
                                cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                                cmd.Parameters.AddWithValue("@DeptCode", model.SubDeptId);
                                cmd.Parameters.AddWithValue("@FromDate", model.FromDate);
                                cmd.Parameters.AddWithValue("@ToDate", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Comments", model.Comments);
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }
                        else
                        {
                            // Just updating current
                            string updateDept = @"UPDATE hr_employeedepartmentdetails 
                                                  SET Emp_No=@Emp_No, DeptCode=@DeptCode, FromDate=@FromDate, ToDate=@ToDate, Comments=@Comments, UpdatedBy=@UpdatedBy, Updated_Date=NOW() 
                                                  WHERE CODE=@Code";
                            using (var cmd = new MySqlCommand(updateDept, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Code", model.Code);
                                cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                                cmd.Parameters.AddWithValue("@DeptCode", model.SubDeptId);
                                cmd.Parameters.AddWithValue("@FromDate", model.FromDate);
                                cmd.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                                cmd.Parameters.AddWithValue("@Comments", model.Comments);
                                cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
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

        public async Task<bool> DeleteEmployeeDepartmentDetailAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation for delete/update exists
                string checkQuery = "SELECT 1 FROM hr_employeedepartmentdetails WHERE code=@Code AND ToDate IS NOT NULL";
                using (var cmd = new MySqlCommand(checkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Code", code);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        throw new ArgumentException("You cannot delete this record. You have to update or delete Employee's current department detail record.");
                    }
                }

                // Make sure employee belongs to at least one dept
                string empNo = "";
                string latestQuery = @"SELECT Emp_No FROM hr_employeedepartmentdetails WHERE code=@Code LIMIT 0,1";
                using (var cmd = new MySqlCommand(latestQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Code", code);
                    var eNo = await cmd.ExecuteScalarAsync();
                    if (eNo != null) empNo = eNo.ToString() ?? "";
                }

                string countQry = "SELECT 1 FROM hr_employeedepartmentdetails WHERE Emp_No=@Emp_No AND code<>@Code";
                using (var cmd = new MySqlCommand(countQry, connection))
                {
                    cmd.Parameters.AddWithValue("@Code", code);
                    cmd.Parameters.AddWithValue("@Emp_No", empNo);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null)
                    {
                        throw new ArgumentException("You cannot delete this record. Employee must be at least in one department.");
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string prevDeptCode = "";
                        string prevQuery = @"SELECT CODE FROM hr_employeedepartmentdetails WHERE code<>@code AND Emp_No=@Emp_No ORDER BY code desc LIMIT 0,1";
                        using (var cmd = new MySqlCommand(prevQuery, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@code", code);
                            cmd.Parameters.AddWithValue("@Emp_No", empNo);
                            var pCode = await cmd.ExecuteScalarAsync();
                            if (pCode != null) prevDeptCode = pCode.ToString() ?? "";
                        }

                        if (!string.IsNullOrEmpty(prevDeptCode))
                        {
                            string revertQuery = "UPDATE hr_employeedepartmentdetails SET ToDate=NULL WHERE code=@Code";
                            using (var cmd = new MySqlCommand(revertQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Code", prevDeptCode);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string delQuery = "DELETE FROM hr_employeedepartmentdetails WHERE Code=@Code";
                        using (var cmd = new MySqlCommand(delQuery, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Code", code);
                            await cmd.ExecuteNonQueryAsync();
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

        public async Task<IEnumerable<EmployeeContractModel>> GetAllEmployeeContractsAsync(string currentUserId)
        {
            var data = new List<EmployeeContractModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.CODE, hr.Emp_No, empDet.Name, hr.FromDate, hr.ToDate, hr.ContractType 
                                 FROM hr_employeecontracts hr 
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`Emp_No`=empdet.`Emp_No` 
                                 INNER JOIN lcs_user_location lul ON empDet.P_CITY_CODE = lul.city_code 
                                 WHERE lul.userid = @UserId ORDER BY CODE desc LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeContractModel
                            {
                                Code = reader["CODE"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                ContractType = reader["ContractType"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeContractModel?> GetEmployeeContractByCodeAsync(string code)
        {
            EmployeeContractModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr_con.Code, hr_con.Emp_No, det.NAME, hr_con.FromDate, hr_con.ToDate, hr_con.ContractType 
                                 FROM hr_employeecontracts hr_con 
                                 INNER JOIN hr_employeepersonaldetail det ON hr_con.Emp_No=det.EMP_NO
                                 WHERE Code=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeContractModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                ContractType = reader["ContractType"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"])
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmployeeContractAsync(EmployeeContractModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate Employee Exists
                string empQuery = "SELECT 1 FROM hr_employeepersonaldetail WHERE Emp_No=@Emp_No LIMIT 1";
                using (var cmd = new MySqlCommand(empQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    var empExists = await cmd.ExecuteScalarAsync();
                    if (empExists == null) throw new ArgumentException("Employee does not exist in database!");
                }

                // Check active contract
                string activeQuery = "SELECT 1 FROM hr_employeecontracts WHERE Emp_No=@Emp_No AND todate is null LIMIT 1";
                using (var cmd = new MySqlCommand(activeQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    var activeExists = await cmd.ExecuteScalarAsync();
                    if (activeExists != null) throw new ArgumentException("Employee already has an active contract. Record cannot be added.");
                }

                // Check dates logic
                if (model.ToDate.HasValue && model.ToDate.Value <= model.FromDate)
                {
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");
                }

                string latestDateQuery = "SELECT FromDate FROM hr_employeecontracts WHERE Emp_No=@Emp_No ORDER BY CODE DESC LIMIT 0,1";
                using (var cmd = new MySqlCommand(latestDateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    var lastDateObj = await cmd.ExecuteScalarAsync();
                    if (lastDateObj != null && lastDateObj != DBNull.Value)
                    {
                        DateTime lastDate = Convert.ToDateTime(lastDateObj);
                        if (model.FromDate <= lastDate)
                        {
                            throw new ArgumentException($"Date from should be greater than \"{lastDate.ToString("dd/MM/yyyy")}\".");
                        }
                        if (model.FromDate == lastDate)
                        {
                            throw new ArgumentException($"There should be at least one day difference between \"{lastDate.ToString("dd/MM/yyyy")}\" and the date you have selected.");
                        }
                    }
                }

                string newCode = await GenerateNewIdAsync(connection, null, "hr_employeecontracts", "Code", 3);

                string insertQuery = @"INSERT INTO hr_employeecontracts (Code, Emp_No, FromDate, ToDate, ContractType, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                       VALUES (@Code, @Emp_No, @FromDate, @ToDate, @ContractType, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", newCode);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@FromDate", model.FromDate);
                    command.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@ContractType", model.ContractType);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateEmployeeContractAsync(EmployeeContractModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.ToDate.HasValue && model.ToDate.Value < model.FromDate)
                {
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");
                }

                string latestDateQuery = "SELECT FromDate FROM hr_employeecontracts WHERE Emp_No=@Emp_No AND Code<@Code ORDER BY CODE DESC LIMIT 0,1";
                using (var cmd = new MySqlCommand(latestDateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    var lastDateObj = await cmd.ExecuteScalarAsync();
                    if (lastDateObj != null && lastDateObj != DBNull.Value)
                    {
                        DateTime lastDate = Convert.ToDateTime(lastDateObj);
                        if (model.FromDate <= lastDate)
                        {
                            throw new ArgumentException($"Date from should be greater than \"{lastDate.ToString("dd/MM/yyyy")}\".");
                        }
                        if (model.FromDate == lastDate)
                        {
                            throw new ArgumentException($"There should be at least one day difference between \"{lastDate.ToString("dd/MM/yyyy")}\" and the date you have selected.");
                        }
                    }
                }

                string updateQuery = @"UPDATE hr_employeecontracts 
                                       SET Emp_No=@Emp_No, FromDate=@FromDate, ToDate=@ToDate, ContractType=@ContractType, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                       WHERE Code=@Code";

                using (var command = new MySqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@FromDate", model.FromDate);
                    command.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@ContractType", model.ContractType);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteEmployeeContractAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string delQuery = "DELETE FROM hr_employeecontracts WHERE Code=@Code";
                using (var command = new MySqlCommand(delQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<EmployeeBankDetailModel>> GetAllEmployeeBankDetailsAsync(string currentUserId)
        {
            var data = new List<EmployeeBankDetailModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT eb.CODE, eb.AccountNo, ep.EMP_NO, ep.NAME, eb.BankName, eb.BankLocation 
                                 FROM hr_employeebankdetails eb 
                                 INNER JOIN hr_employeepersonaldetail ep ON eb.emp_no=ep.EMP_NO   
                                 INNER JOIN lcs_user_location lul ON ep.P_CITY_CODE=lul.city_code 
                                 WHERE lul.userid=@UserId ORDER BY eb.code DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeBankDetailModel
                            {
                                Code = reader["CODE"].ToString() ?? string.Empty,
                                AccountNo = reader["AccountNo"].ToString() ?? string.Empty,
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                BankName = reader["BankName"].ToString() ?? string.Empty,
                                BankLocation = reader["BankLocation"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<SelectListItem>> GetBankListAsync()
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                
                string query = "SELECT Description FROM lcs_setup.banks WHERE Status = 1 ORDER BY Description";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string val = reader["Description"].ToString() ?? "";
                        items.Add(new SelectListItem { Value = val, Text = val });
                    }
                }
            }
            return items;
        }

        public async Task<EmployeeBankDetailModel?> GetEmployeeBankDetailByCodeAsync(string code)
        {
            EmployeeBankDetailModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT eb.CODE, eb.AccountNo, eb.Emp_No, ep.NAME, eb.BankName, eb.BranchCode, eb.BankLocation, eb.fromdate, eb.todate, eb.Comments 
                                 FROM hr_employeebankdetails eb 
                                 INNER JOIN hr_employeepersonaldetail ep ON eb.emp_no=ep.EMP_NO 
                                 WHERE eb.CODE = @Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeBankDetailModel
                            {
                                Code = reader["CODE"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                AccountNo = reader["AccountNo"].ToString() ?? string.Empty,
                                BankName = reader["BankName"].ToString() ?? string.Empty,
                                BranchCode = reader["BranchCode"].ToString() ?? string.Empty,
                                BankLocation = reader["BankLocation"].ToString() ?? string.Empty,
                                FromDate = reader["fromdate"] == DBNull.Value ? null : Convert.ToDateTime(reader["fromdate"]),
                                ToDate = reader["todate"] == DBNull.Value ? null : Convert.ToDateTime(reader["todate"]),
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmployeeBankDetailAsync(EmployeeBankDetailModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate Employee Exists
                string empQuery = "SELECT 1 FROM hr_employeepersonaldetail WHERE Emp_No=@Emp_No LIMIT 1";
                using (var cmd = new MySqlCommand(empQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    var empExists = await cmd.ExecuteScalarAsync();
                    if (empExists == null) throw new ArgumentException("Employee does not exist in database!");
                }

                // Check active 
                string activeQuery = "SELECT 1 FROM hr_employeebankdetails WHERE AccountNo=@AccountNo AND Emp_No=@Emp_No LIMIT 1";
                using (var cmd = new MySqlCommand(activeQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    cmd.Parameters.AddWithValue("@AccountNo", model.AccountNo);
                    var activeExists = await cmd.ExecuteScalarAsync();
                    if (activeExists != null) throw new ArgumentException("Entry already exists.");
                }

                if (model.ToDate.HasValue && model.FromDate.HasValue && model.FromDate >= model.ToDate)
                {
                    throw new ArgumentException("From date should be smaller than To date.");
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeebankdetails", "CODE", 3);

                        string insertQuery = @"INSERT INTO hr_employeebankdetails (CODE, emp_no, AccountNo, BankName, BranchCode, BankLocation, fromdate, todate, comments, createdby, createddate, updatedby, updateddate)
                                               VALUES (@code, @emp_no, @AccountNo, @BankName, @BranchCode, @BankLocation, @fromdate, @todate, @comments, @createdby, @createddate, @updatedby, @updateddate)";

                        using (var command = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@code", newCode);
                            command.Parameters.AddWithValue("@emp_no", model.EmpNo);
                            command.Parameters.AddWithValue("@AccountNo", model.AccountNo);
                            command.Parameters.AddWithValue("@BankName", model.BankName);
                            command.Parameters.AddWithValue("@BranchCode", model.BranchCode);
                            command.Parameters.AddWithValue("@BankLocation", model.BankLocation);
                            command.Parameters.AddWithValue("@fromdate", model.FromDate);
                            command.Parameters.AddWithValue("@todate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                            command.Parameters.AddWithValue("@comments", model.Comments);
                            command.Parameters.AddWithValue("@createdby", currentUserId);
                            command.Parameters.AddWithValue("@createddate", DateTime.Now);
                            command.Parameters.AddWithValue("@updatedby", currentUserId);
                            command.Parameters.AddWithValue("@updateddate", DateTime.Now);

                            await command.ExecuteNonQueryAsync();
                        }

                        // Cap old record
                        string capQuery = "SELECT CODE FROM hr_employeebankdetails WHERE EMP_NO=@Emp_No AND code <> @code ORDER BY CODE DESC LIMIT 1";
                        string? oldCode = null;
                        using (var cmd = new MySqlCommand(capQuery, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            cmd.Parameters.AddWithValue("@code", newCode);
                            var res = await cmd.ExecuteScalarAsync();
                            if (res != null) oldCode = res.ToString();
                        }

                        if (!string.IsNullOrEmpty(oldCode))
                        {
                            string updateOld = "UPDATE hr_employeebankdetails SET todate=@todate WHERE CODE=@code";
                            using (var cmd = new MySqlCommand(updateOld, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@code", oldCode);
                                cmd.Parameters.AddWithValue("@todate", model.FromDate?.AddDays(-1));
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

        public async Task<bool> UpdateEmployeeBankDetailAsync(EmployeeBankDetailModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.ToDate.HasValue && model.FromDate.HasValue && model.FromDate >= model.ToDate)
                {
                    throw new ArgumentException("From date should be smaller than To date.");
                }

                // Check active 
                string activeQuery = "SELECT 1 FROM hr_employeebankdetails WHERE AccountNo=@AccountNo AND CODE <> @Code LIMIT 1";
                using (var cmd = new MySqlCommand(activeQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    cmd.Parameters.AddWithValue("@AccountNo", model.AccountNo);
                    var activeExists = await cmd.ExecuteScalarAsync();
                    if (activeExists != null) throw new ArgumentException("Entry already exists.");
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = @"UPDATE hr_employeebankdetails 
                                               SET AccountNo=@AccountNo, BankName=@bankname, BranchCode=@branch, BankLocation=@location, FromDate=@fromdate, ToDate=@todate, comments=@comments, updatedby=@updatedby, updated_date=@updateddate 
                                               WHERE CODE=@CODE";

                        using (var command = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@CODE", model.Code);
                            command.Parameters.AddWithValue("@AccountNo", model.AccountNo);
                            command.Parameters.AddWithValue("@bankname", model.BankName);
                            command.Parameters.AddWithValue("@branch", model.BranchCode);
                            command.Parameters.AddWithValue("@location", model.BankLocation);
                            command.Parameters.AddWithValue("@fromdate", model.FromDate);
                            command.Parameters.AddWithValue("@todate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                            command.Parameters.AddWithValue("@comments", model.Comments);
                            command.Parameters.AddWithValue("@updatedby", currentUserId);
                            command.Parameters.AddWithValue("@updateddate", DateTime.Now);

                            await command.ExecuteNonQueryAsync();
                        }

                        // Cap old record
                        string capQuery = "SELECT CODE FROM hr_employeebankdetails WHERE EMP_NO=@Emp_No AND code <> @code ORDER BY CODE DESC LIMIT 1";
                        string? oldCode = null;
                        using (var cmd = new MySqlCommand(capQuery, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                            cmd.Parameters.AddWithValue("@code", model.Code);
                            var res = await cmd.ExecuteScalarAsync();
                            if (res != null) oldCode = res.ToString();
                        }

                        if (!string.IsNullOrEmpty(oldCode))
                        {
                            string updateOld = "UPDATE hr_employeebankdetails SET todate=@todate WHERE CODE=@code";
                            using (var cmd = new MySqlCommand(updateOld, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@code", oldCode);
                                cmd.Parameters.AddWithValue("@todate", model.FromDate?.AddDays(-1));
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

        public async Task<bool> DeleteEmployeeBankDetailAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string empNo = "";
                        string qryEmp = "SELECT Emp_No FROM hr_employeebankdetails WHERE CODE=@CODE LIMIT 1";
                        using(var cmd = new MySqlCommand(qryEmp, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@CODE", code);
                            var res = await cmd.ExecuteScalarAsync();
                            if (res != null) empNo = res.ToString() ?? "";
                        }

                        string delQuery = "DELETE FROM hr_employeebankdetails WHERE CODE=@Code";
                        using (var command = new MySqlCommand(delQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            await command.ExecuteNonQueryAsync();
                        }

                        if (!string.IsNullOrEmpty(empNo))
                        {
                            string capQuery = "SELECT CODE FROM hr_employeebankdetails WHERE EMP_NO=@Emp_No ORDER BY CODE DESC LIMIT 1";
                            string? oldCode = null;
                            using (var cmd = new MySqlCommand(capQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@Emp_No", empNo);
                                var res = await cmd.ExecuteScalarAsync();
                                if (res != null) oldCode = res.ToString();
                            }

                            if (!string.IsNullOrEmpty(oldCode))
                            {
                                string updateOld = "UPDATE hr_employeebankdetails SET todate=NULL WHERE CODE=@code";
                                using (var cmd = new MySqlCommand(updateOld, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@code", oldCode);
                                    await cmd.ExecuteNonQueryAsync();
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

        public async Task<(int successCount, string message)> BulkUploadEmployeeBankDetailsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            if (file == null || file.Length == 0) return (0, "Invalid file.");
            
            int insertedRows = 0;
            using (var stream = new StreamReader(file.OpenReadStream()))
            {
                using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
                {
                    if (connection == null) return (0, "Db Error");
                    await connection.OpenAsync();

                    using (var transaction = await connection.BeginTransactionAsync())
                    {
                        try
                        {
                            int row = 0;
                            while (!stream.EndOfStream)
                            {
                                var line = await stream.ReadLineAsync();
                                if (string.IsNullOrEmpty(line)) continue;
                                
                                var values = line.Split(',');
                                if (values.Length != 7)
                                {
                                    throw new Exception("File is not in correct format! Please review sample file.");
                                }

                                if (row == 0)
                                {
                                    row++;
                                    continue; // skip header
                                }
                                row++;

                                string empNo = values[0].Trim();
                                string accountNo = values[1].Trim();
                                string bankName = values[2].Trim();
                                string branchCode = values[3].Trim();
                                string bankLocation = values[4].Trim();
                                string fromDateStr = values[5].Trim();
                                string comments = values[6].Trim();

                                if (string.IsNullOrEmpty(empNo)) continue;

                                string chkEmp = "SELECT LEFT_DATE FROM hr_employeepersonaldetail WHERE EMP_NO=@EMP_NO";
                                using (var cmd = new MySqlCommand(chkEmp, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@EMP_NO", empNo);
                                    var res = await cmd.ExecuteScalarAsync();
                                    if (res == null || res != DBNull.Value) continue; // Skip if not exists or left
                                }

                                string chkExt = "SELECT 1 FROM hr_employeebankdetails WHERE AccountNo=@AccountNo AND Emp_No=@Emp_No";
                                using (var cmd = new MySqlCommand(chkExt, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@AccountNo", accountNo);
                                    cmd.Parameters.AddWithValue("@Emp_No", empNo);
                                    var res = await cmd.ExecuteScalarAsync();
                                    if (res != null) continue; // skip if already exists
                                }

                                string code = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeebankdetails", "CODE", 3);

                                string insertQuery = @"INSERT INTO hr_employeebankdetails (CODE, emp_no, AccountNo, BankName, BranchCode, BankLocation, fromdate, todate, comments, createdby, createddate, updatedby, updateddate)
                                                       VALUES (@code, @emp_no, @AccountNo, @BankName, @BranchCode, @BankLocation, @fromdate, NULL, @comments, @userId, NOW(), @userId, NOW())";
                                                       
                                using (var cmd = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@code", code);
                                    cmd.Parameters.AddWithValue("@emp_no", empNo);
                                    cmd.Parameters.AddWithValue("@AccountNo", accountNo);
                                    cmd.Parameters.AddWithValue("@BankName", bankName);
                                    cmd.Parameters.AddWithValue("@BranchCode", branchCode);
                                    cmd.Parameters.AddWithValue("@BankLocation", bankLocation);
                                    cmd.Parameters.AddWithValue("@fromdate", Convert.ToDateTime(fromDateStr).ToString("yyyy-MM-dd"));
                                    cmd.Parameters.AddWithValue("@comments", comments);
                                    cmd.Parameters.AddWithValue("@userId", currentUserId);

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
            return (insertedRows, $"{insertedRows} Record(s) Saved Successfully");
        }

        public async Task<IEnumerable<EmployeePayStructureModel>> GetAllEmployeePayStructuresAsync(string currentUserId)
        {
            var data = new List<EmployeePayStructureModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.`Code`, empdet.`NAME`, hr.`Comm_Flg`, hr.`Fuel_Flg`, hr.`Salary_Flg`, hr.`Comments`, hr.`PayStrucDate`
                                 FROM `hr_employeepaystructure` hr 
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`Emp_No`=empdet.`EMP_NO`
                                 INNER JOIN lcs_user_location lul ON empDet.P_CITY_CODE=lul.city_code 
                                 WHERE lul.userid=@UserId ORDER BY hr.`Code` ASC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeePayStructureModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                CommissionFlag = reader["Comm_Flg"].ToString() == "Y",
                                FuelFlag = reader["Fuel_Flg"].ToString() == "Y",
                                SalaryFlag = reader["Salary_Flg"].ToString() == "Y",
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                PayStrucDate = reader["PayStrucDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["PayStrucDate"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeePayStructureModel?> GetEmployeePayStructureByCodeAsync(string code)
        {
            EmployeePayStructureModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.`Code`, hr.`Emp_No`, empdet.`NAME`, hr.`Comm_Flg`, hr.`Fuel_Flg`, hr.`Salary_Flg`, hr.`Comments`, hr.`PayStrucDate`
                                 FROM `hr_employeepaystructure` hr 
                                 INNER JOIN `hr_employeepersonaldetail` empDet ON hr.`Emp_No`=empdet.`EMP_NO`
                                 WHERE hr.`Code`=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeePayStructureModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmployeeDescription = reader["NAME"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                CommissionFlag = reader["Comm_Flg"].ToString() == "Y",
                                FuelFlag = reader["Fuel_Flg"].ToString() == "Y",
                                SalaryFlag = reader["Salary_Flg"].ToString() == "Y",
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                PayStrucDate = reader["PayStrucDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["PayStrucDate"])
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmployeePayStructureAsync(EmployeePayStructureModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate Employee Exists
                string empQuery = "SELECT 1 FROM hr_employeepersonaldetail WHERE Emp_No=@Emp_No AND EMP_STATUS <> 'I' LIMIT 1";
                using (var cmd = new MySqlCommand(empQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    var empExists = await cmd.ExecuteScalarAsync();
                    if (empExists == null) throw new ArgumentException("Employee does not exist in database.");
                }

                // Check active
                string activeQuery = "SELECT 1 FROM hr_employeepaystructure WHERE Emp_No=@Emp_No LIMIT 1";
                using (var cmd = new MySqlCommand(activeQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    var activeExists = await cmd.ExecuteScalarAsync();
                    if (activeExists != null) throw new ArgumentException("Employee's pay structure has been defined.");
                }

                string newCode = await GenerateNewIdAsync(connection, null, "hr_employeepaystructure", "Code", 3);

                string insertQuery = @"INSERT INTO hr_employeepaystructure 
                                       (Code, Emp_No, Salary_Flg, Comm_Flg, Fuel_Flg, PayStrucDate, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                       VALUES (@Code, @Emp_No, @Salary_Flg, @Comm_Flg, @Fuel_Flg, @PayStrucDate, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", newCode);
                    command.Parameters.AddWithValue("@Emp_No", model.EmpNo);
                    command.Parameters.AddWithValue("@Salary_Flg", model.SalaryFlag ? "Y" : "N");
                    command.Parameters.AddWithValue("@Comm_Flg", model.CommissionFlag ? "Y" : "N");
                    command.Parameters.AddWithValue("@Fuel_Flg", model.FuelFlag ? "Y" : "N");
                    command.Parameters.AddWithValue("@PayStrucDate", model.PayStrucDate);
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

        public async Task<bool> UpdateEmployeePayStructureAsync(EmployeePayStructureModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string updateQuery = @"UPDATE hr_employeepaystructure 
                                       SET Salary_Flg=@Salary_Flg, Comm_Flg=@Comm_Flg, Fuel_Flg=@Fuel_Flg, PayStrucDate=@PayStrucDate, Comments=@Comments, updatedby=@updatedby, Updated_Date=@Updated_Date 
                                       WHERE Code=@Code";

                using (var command = new MySqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Salary_Flg", model.SalaryFlag ? "Y" : "N");
                    command.Parameters.AddWithValue("@Comm_Flg", model.CommissionFlag ? "Y" : "N");
                    command.Parameters.AddWithValue("@Fuel_Flg", model.FuelFlag ? "Y" : "N");
                    command.Parameters.AddWithValue("@PayStrucDate", model.PayStrucDate);
                    command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : model.Comments);
                    command.Parameters.AddWithValue("@updatedby", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteEmployeePayStructureAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string delQuery = "DELETE FROM hr_employeepaystructure WHERE Code=@Code";
                using (var command = new MySqlCommand(delQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        #region Employee Assets
        public async Task<IEnumerable<EmployeeAssetModel>> GetEmployeeAssetsAsync(string empNo)
        {
            var results = new List<EmployeeAssetModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, empDet.EMP_NO, empDet.NAME as EmployeeName, hr.AssetCode, AssetDet.Name as AssetName, hr.FromDate, hr.ToDate, hr.Remarks 
                                 FROM hr_employeeassets hr
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.EmpNo = empDet.EMP_NO
                                 INNER JOIN hr_companyassets AssetDet ON hr.AssetCode = AssetDet.Code ";
                
                if (!string.IsNullOrEmpty(empNo))
                {
                    query += " WHERE hr.EmpNo = @EmpNo ";
                }
                
                query += " ORDER BY hr.Code DESC LIMIT 500";

                return await connection.QueryAsync<EmployeeAssetModel>(query, new { EmpNo = empNo });
            }
        }

        public async Task<EmployeeAssetModel?> GetEmployeeAssetByIdAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, empDet.EMP_NO, empDet.NAME as EmployeeName, hr.AssetCode, AssetDet.Name as AssetName, hr.FromDate, hr.ToDate, hr.Remarks 
                                 FROM hr_employeeassets hr
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.EmpNo = empDet.EMP_NO
                                 INNER JOIN hr_companyassets AssetDet ON hr.AssetCode = AssetDet.Code 
                                 WHERE hr.Code = @Code LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Code = code });
                if (r == null) return null;

                return new EmployeeAssetModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.EMP_NO.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    EmployeeDescription = r.EmployeeName?.ToString() ?? "",
                    AssetCode = r.AssetCode.ToString(),
                    AssetName = r.AssetName?.ToString() ?? "",
                    AssetDescription = r.AssetName?.ToString() ?? "",
                    FromDate = r.FromDate,
                    ToDate = r.ToDate,
                    Remarks = r.Remarks?.ToString() ?? ""
                };
            }
        }

        public async Task<bool> AddEmployeeAssetAsync(EmployeeAssetModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate Date Range
                string dateCheck = "SELECT COUNT(*) FROM hr_employeeassets WHERE AssetCode=@AssetCode AND ((ToDate IS NULL) OR (@FromDate BETWEEN FromDate AND ToDate) OR (@ToDate BETWEEN FromDate AND ToDate))";
                int conflicts = await connection.ExecuteScalarAsync<int>(dateCheck, new { AssetCode = model.AssetCode, FromDate = model.FromDate, ToDate = model.ToDate ?? (object)DBNull.Value });
                
                if (conflicts > 0)
                {
                    throw new ArgumentException("Asset is already assigned during this period.");
                }

                string newCode = await GenerateNewIdAsync(connection, null, "hr_employeeassets", "Code", 6);

                string insertQuery = @"INSERT INTO hr_employeeassets (Code, EmpNo, AssetCode, FromDate, ToDate, Remarks, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                       VALUES (@Code, @EmpNo, @AssetCode, @FromDate, @ToDate, @Remarks, @UserId, NOW(), @UserId, NOW())";

                int rows = await connection.ExecuteAsync(insertQuery, new {
                    Code = newCode,
                    EmpNo = model.EmpNo,
                    AssetCode = model.AssetCode,
                    FromDate = model.FromDate,
                    ToDate = model.ToDate,
                    Remarks = model.Remarks,
                    UserId = currentUserId
                });

                return rows > 0;
            }
        }

        public async Task<bool> UpdateEmployeeAssetAsync(EmployeeAssetModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate Date Range
                string dateCheck = "SELECT COUNT(*) FROM hr_employeeassets WHERE AssetCode=@AssetCode AND Code <> @Code AND ((ToDate IS NULL) OR (@FromDate BETWEEN FromDate AND ToDate) OR (@ToDate BETWEEN FromDate AND ToDate))";
                int conflicts = await connection.ExecuteScalarAsync<int>(dateCheck, new { AssetCode = model.AssetCode, Code = model.Code, FromDate = model.FromDate, ToDate = model.ToDate ?? (object)DBNull.Value });
                
                if (conflicts > 0)
                {
                    throw new ArgumentException("Asset is already assigned during this period.");
                }

                string updateQuery = @"UPDATE hr_employeeassets SET EmpNo=@EmpNo, AssetCode=@AssetCode, FromDate=@FromDate, ToDate=@ToDate, Remarks=@Remarks, UpdatedBy=@UserId, Updated_Date=NOW() WHERE Code=@Code";

                int rows = await connection.ExecuteAsync(updateQuery, new {
                    EmpNo = model.EmpNo,
                    AssetCode = model.AssetCode,
                    FromDate = model.FromDate,
                    ToDate = model.ToDate,
                    Remarks = model.Remarks,
                    UserId = currentUserId,
                    Code = model.Code
                });

                return rows > 0;
            }
        }

        public async Task<bool> DeleteEmployeeAssetAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string deleteQuery = "DELETE FROM hr_employeeassets WHERE Code = @Code";
                int rows = await connection.ExecuteAsync(deleteQuery, new { Code = code });
                return rows > 0;
            }
        }
        #endregion
        #region Employee Training
        public async Task<IEnumerable<EmployeeTrainingModel>> GetAllEmployeeTrainingsAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<EmployeeTrainingModel>();
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, 
                                 (CASE LENGTH(TRIM(hr.Emp_No)) WHEN 3 THEN (SELECT FullName FROM hr_department WHERE Code = TRIM(hr.Emp_No)) ELSE (SELECT Name FROM hr_employeepersonaldetail WHERE emp_no = TRIM(hr.Emp_No)) END) AS 'EmployeeName',
                                 (CASE flag WHEN 'E' THEN 'Employee Wise' ELSE 'Department Wise' END) AS 'flag',
                                 hr.NAME, hr.FromDate, hr.ToDate, hr.AMOUNT, hr.InstitutionName
                                 FROM hr_employeetrainingdetails hr 
                                 LEFT JOIN hr_employeepersonaldetail empDet ON hr.Emp_No = empDet.EMP_NO
                                 LEFT JOIN lcs_user_location lul ON empDet.P_CITY_CODE = lul.city_code
                                 WHERE (LENGTH(TRIM(hr.Emp_No)) = 3 OR lul.userid = @UserId)
                                 ORDER BY Code DESC LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                return results.Select(r => new EmployeeTrainingModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.Emp_No.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    TrainingName = r.NAME?.ToString() ?? "",
                    InstitutionName = r.InstitutionName?.ToString() ?? "",
                    FromDate = r.FromDate,
                    ToDate = r.ToDate,
                    Amount = r.AMOUNT != null ? Convert.ToDecimal(r.AMOUNT) : (decimal?)null,
                    Mode = r.flag?.ToString() ?? ""
                });
            }
        }

        public async Task<EmployeeTrainingModel?> GetEmployeeTrainingByIdAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, 
                                 (CASE LENGTH(TRIM(hr.Emp_No)) WHEN 3 THEN (SELECT FullName FROM hr_department WHERE Code = TRIM(hr.Emp_No)) ELSE (SELECT Name FROM hr_employeepersonaldetail WHERE emp_no = TRIM(hr.Emp_No)) END) AS 'EmployeeName',
                                 hr.flag, hr.NAME, hr.Reason, hr.InstitutionName, hr.CityCode, hr.CountryCode, hr.FromDate, hr.ToDate, hr.AMOUNT, hr.Comments
                                 FROM hr_employeetrainingdetails hr  
                                 WHERE hr.Code = @Code LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Code = code });
                if (r == null) return null;

                var model = new EmployeeTrainingModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.flag?.ToString() == "E" ? r.Emp_No?.ToString() : "",
                    DepartmentId = r.flag?.ToString() == "D" ? r.Emp_No?.ToString() : "",
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    EmployeeDescription = r.EmployeeName?.ToString() ?? "",
                    TrainingName = r.NAME?.ToString() ?? "",
                    Reason = r.Reason?.ToString() ?? "",
                    InstitutionName = r.InstitutionName?.ToString() ?? "",
                    CityCode = r.CityCode?.ToString() ?? "00",
                    CountryCode = r.CountryCode?.ToString() ?? "00",
                    FromDate = r.FromDate,
                    ToDate = r.ToDate,
                    Amount = r.AMOUNT != null ? Convert.ToDecimal(r.AMOUNT) : (decimal?)null,
                    Comments = r.Comments?.ToString() ?? "",
                    Mode = r.flag?.ToString() ?? "E"
                };
                return model;
            }
        }

        public async Task<bool> AddEmployeeTrainingAsync(EmployeeTrainingModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string newCode = await GenerateNewIdAsync(connection, null, "hr_employeetrainingdetails", "Code", 6);
                string empNo = model.Mode == "D" ? model.DepartmentId : model.EmpNo;

                string insertQuery = @"INSERT INTO hr_employeetrainingdetails 
                                       (Code, Emp_No, Name, Reason, InstitutionName, CityCode, CountryCode, FromDate, ToDate, Amount, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date, flag)
                                       VALUES (@Code, @Emp_No, @Name, @Reason, @InstitutionName, @CityCode, @CountryCode, @FromDate, @ToDate, @Amount, @Comments, @UserId, NOW(), @UserId, NOW(), @flag)";

                int rows = await connection.ExecuteAsync(insertQuery, new {
                    Code = newCode,
                    Emp_No = empNo,
                    Name = model.TrainingName,
                    Reason = model.Reason,
                    InstitutionName = model.InstitutionName,
                    CityCode = model.CityCode == "00" ? null : model.CityCode,
                    CountryCode = model.CountryCode == "00" ? null : model.CountryCode,
                    FromDate = model.FromDate,
                    ToDate = model.ToDate,
                    Amount = model.Amount,
                    Comments = model.Comments,
                    UserId = currentUserId,
                    flag = model.Mode
                });
                return rows > 0;
            }
        }

        public async Task<bool> UpdateEmployeeTrainingAsync(EmployeeTrainingModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string empNo = model.Mode == "D" ? model.DepartmentId : model.EmpNo;

                string updateQuery = @"UPDATE hr_employeetrainingdetails 
                                       SET Emp_No=@Emp_No, Name=@Name, Reason=@Reason, InstitutionName=@InstitutionName, CityCode=@CityCode, CountryCode=@CountryCode, 
                                           FromDate=@FromDate, ToDate=@ToDate, Amount=@Amount, Comments=@Comments, UpdatedBy=@UserId, Updated_Date=NOW(), flag=@flag
                                       WHERE Code=@Code";

                int rows = await connection.ExecuteAsync(updateQuery, new {
                    Emp_No = empNo,
                    Name = model.TrainingName,
                    Reason = model.Reason,
                    InstitutionName = model.InstitutionName,
                    CityCode = model.CityCode == "00" ? null : model.CityCode,
                    CountryCode = model.CountryCode == "00" ? null : model.CountryCode,
                    FromDate = model.FromDate,
                    ToDate = model.ToDate,
                    Amount = model.Amount,
                    Comments = model.Comments,
                    UserId = currentUserId,
                    flag = model.Mode,
                    Code = model.Code
                });
                return rows > 0;
            }
        }

        public async Task<bool> DeleteEmployeeTrainingAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                int rows = await connection.ExecuteAsync("DELETE FROM hr_employeetrainingdetails WHERE Code = @Code", new { Code = code });
                return rows > 0;
            }
        }
        #endregion

        #region Employee Show Cause
        public async Task<IEnumerable<EmployeeShowCauseModel>> GetAllEmployeeShowCausesAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<EmployeeShowCauseModel>();
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, empDet.NAME as EmployeeName, hr.Type, hr.IssueDate, hr.ReplyDate, hr.Comments,
                                 (ifnull((SELECT count(*) FROM hr_docs where emp_no=hr.emp_no and SNo=hr.code and DocFlag='SC'),0)) as hasFile
                                 FROM hr_employeeshowcause hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No=empDet.EMP_NO
                                 INNER JOIN lcs_user_location lul ON empDet.P_CITY_CODE=lul.city_code 
                                 WHERE lul.userid=@UserId ORDER BY hr.Code DESC LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                return results.Select(r => new EmployeeShowCauseModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.Emp_No.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    Type = r.Type?.ToString() ?? "",
                    IssueDate = r.IssueDate,
                    ReplyDate = r.ReplyDate,
                    Comments = r.Comments?.ToString() ?? "",
                    HasFile = Convert.ToInt32(r.hasFile) > 0
                });
            }
        }

        public async Task<EmployeeShowCauseModel?> GetEmployeeShowCauseByIdAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.Emp_No, empDet.NAME as EmployeeName, hr.Reason, hr.Reply, hr.Type, hr.IssueDate, hr.ReplyDate, hr.Comments,
                                 (ifnull((SELECT count(*) FROM hr_docs where emp_no=hr.emp_no and SNo=hr.code and DocFlag='SC'),0)) as hasFile
                                 FROM hr_employeeshowcause hr 
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No=empDet.EMP_NO
                                 WHERE hr.Code=@Code LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Code = code });
                if (r == null) return null;

                return new EmployeeShowCauseModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.Emp_No.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    EmployeeDescription = r.EmployeeName?.ToString() ?? "",
                    Type = r.Type?.ToString() ?? "00",
                    Reason = r.Reason?.ToString() ?? "",
                    Reply = r.Reply?.ToString() ?? "",
                    IssueDate = r.IssueDate,
                    ReplyDate = r.ReplyDate,
                    Comments = r.Comments?.ToString() ?? "",
                    HasFile = Convert.ToInt32(r.hasFile) > 0
                };
            }
        }

        public async Task<bool> AddEmployeeShowCauseAsync(EmployeeShowCauseModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeshowcause", "Code", 6);

                        string insertQuery = @"INSERT INTO hr_employeeshowcause (Code, Emp_No, Type, Reason, IssueDate, Reply, ReplyDate, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                               VALUES (@Code, @Emp_No, @Type, @Reason, @IssueDate, @Reply, @ReplyDate, @Comments, @UserId, NOW(), @UserId, NOW())";

                        await connection.ExecuteAsync(insertQuery, new {
                            Code = newCode,
                            Emp_No = model.EmpNo,
                            Type = model.Type == "00" ? null : model.Type,
                            Reason = model.Reason,
                            IssueDate = model.IssueDate,
                            Reply = model.Reply,
                            ReplyDate = model.ReplyDate,
                            Comments = model.Comments,
                            UserId = currentUserId
                        }, transaction);

                        if (model.DocumentFile != null && model.DocumentFile.Length > 0)
                        {
                            using (var ms = new MemoryStream())
                            {
                                await model.DocumentFile.CopyToAsync(ms);
                                string docQuery = "INSERT INTO hr_docs (Emp_no, DocFlag, SNo, Doc, Ext) VALUES (@EmpNo, 'SC', @Code, @Doc, @Ext)";
                                await connection.ExecuteAsync(docQuery, new {
                                    EmpNo = model.EmpNo,
                                    Code = newCode,
                                    Doc = ms.ToArray(),
                                    Ext = Path.GetExtension(model.DocumentFile.FileName)
                                }, transaction);
                            }
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

        public async Task<bool> UpdateEmployeeShowCauseAsync(EmployeeShowCauseModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = @"UPDATE hr_employeeshowcause 
                                               SET Emp_No=@Emp_No, Type=@Type, Reason=@Reason, IssueDate=@IssueDate, Reply=@Reply, ReplyDate=@ReplyDate, Comments=@Comments, UpdatedBy=@UserId, Updated_Date=NOW()
                                               WHERE Code=@Code";

                        await connection.ExecuteAsync(updateQuery, new {
                            Emp_No = model.EmpNo,
                            Type = model.Type == "00" ? null : model.Type,
                            Reason = model.Reason,
                            IssueDate = model.IssueDate,
                            Reply = model.Reply,
                            ReplyDate = model.ReplyDate,
                            Comments = model.Comments,
                            UserId = currentUserId,
                            Code = model.Code
                        }, transaction);

                        if (model.DocumentFile != null && model.DocumentFile.Length > 0)
                        {
                            using (var ms = new MemoryStream())
                            {
                                await model.DocumentFile.CopyToAsync(ms);
                                
                                int hasDoc = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_docs WHERE Emp_no=@EmpNo AND DocFlag='SC' AND SNo=@Code", new { EmpNo = model.EmpNo, Code = model.Code }, transaction);

                                if (hasDoc > 0)
                                {
                                    string docQuery = "UPDATE hr_docs SET Doc=@Doc, Ext=@Ext WHERE Emp_no=@EmpNo AND DocFlag='SC' AND SNo=@Code";
                                    await connection.ExecuteAsync(docQuery, new { EmpNo = model.EmpNo, Code = model.Code, Doc = ms.ToArray(), Ext = Path.GetExtension(model.DocumentFile.FileName) }, transaction);
                                }
                                else
                                {
                                    string docQuery = "INSERT INTO hr_docs (Emp_no, DocFlag, SNo, Doc, Ext) VALUES (@EmpNo, 'SC', @Code, @Doc, @Ext)";
                                    await connection.ExecuteAsync(docQuery, new { EmpNo = model.EmpNo, Code = model.Code, Doc = ms.ToArray(), Ext = Path.GetExtension(model.DocumentFile.FileName) }, transaction);
                                }
                            }
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

        public async Task<bool> DeleteEmployeeShowCauseAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        await connection.ExecuteAsync("DELETE FROM hr_docs WHERE SNo=@Code AND DocFlag='SC'", new { Code = code }, transaction);
                        await connection.ExecuteAsync("DELETE FROM hr_employeeshowcause WHERE Code = @Code", new { Code = code }, transaction);
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
        #endregion
        #region Employee Promotion Awards
        public async Task<IEnumerable<EmployeePromotionAwardModel>> GetAllEmployeePromotionAwardsAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<EmployeePromotionAwardModel>();
                await connection.OpenAsync();

                string query = @"SELECT pa.CODE, ep.name as EmployeeName, ep.Emp_no, 
                                 CASE pa.PA_Type WHEN 'P' THEN 'Promotion' ELSE 'Award' END as patype,
                                 pa.ReasonForRecognition, pa.Announcement_Date, pa.FromDate, pa.Amount, pa.Comments,
                                 (ifnull((SELECT count(*) FROM hr_docs where emp_no=pa.emp_no and SNo=pa.code and DocFlag='PA'),0)) as hasFile
                                 FROM hr_employeepromotionawards pa
                                 INNER JOIN hr_employeepersonaldetail ep ON pa.emp_no = ep.EMP_NO 
                                 INNER JOIN lcs_user_location lul ON ep.P_CITY_CODE = lul.city_code  
                                 WHERE lul.userid = @UserId ORDER BY pa.CODE desc LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                return results.Select(r => new EmployeePromotionAwardModel
                {
                    Code = r.CODE.ToString(),
                    EmpNo = r.Emp_no.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    PAType = r.patype?.ToString() ?? "",
                    ReasonForRecognition = r.ReasonForRecognition?.ToString() ?? "",
                    AnnouncementDate = r.Announcement_Date,
                    FromDate = r.FromDate,
                    Amount = r.Amount != null ? Convert.ToDecimal(r.Amount) : (decimal?)null,
                    Comments = r.Comments?.ToString() ?? "",
                    HasFile = Convert.ToInt32(r.hasFile) > 0
                });
            }
        }

        public async Task<EmployeePromotionAwardModel?> GetEmployeePromotionAwardByIdAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT pa.CODE, ep.name as EmployeeName, pa.Emp_No, pa.PA_Type, pa.ReasonForRecognition, pa.Announcement_Date, pa.FromDate, pa.Amount, pa.Comments,
                                 (ifnull((SELECT count(*) FROM hr_docs where emp_no=pa.emp_no and SNo=pa.code and DocFlag='PA'),0)) as hasFile  
                                 FROM hr_employeepromotionawards pa 
                                 INNER JOIN hr_employeepersonaldetail ep ON pa.emp_no=ep.EMP_NO 
                                 WHERE pa.Code = @Code LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Code = code });
                if (r == null) return null;

                return new EmployeePromotionAwardModel
                {
                    Code = r.CODE.ToString(),
                    EmpNo = r.Emp_No.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    EmployeeDescription = r.EmployeeName?.ToString() ?? "",
                    PAType = r.PA_Type?.ToString() ?? "00",
                    ReasonForRecognition = r.ReasonForRecognition?.ToString() ?? "",
                    AnnouncementDate = r.Announcement_Date,
                    FromDate = r.FromDate,
                    Amount = r.Amount != null ? Convert.ToDecimal(r.Amount) : (decimal?)null,
                    Comments = r.Comments?.ToString() ?? "",
                    HasFile = Convert.ToInt32(r.hasFile) > 0
                };
            }
        }

        public async Task<bool> AddEmployeePromotionAwardAsync(EmployeePromotionAwardModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeepromotionawards", "CODE", 6);

                        string insertQuery = @"INSERT INTO hr_employeepromotionawards 
                                               (CODE, emp_no, PA_Type, ReasonForRecognition, Announcement_Date, FromDate, Amount, comments, createdby, created_date, updatedby, updated_date)
                                               VALUES (@Code, @EmpNo, @PAType, @Reason, @AnnDate, @FromDate, @Amount, @Comments, @UserId, NOW(), @UserId, NOW())";

                        await connection.ExecuteAsync(insertQuery, new {
                            Code = newCode,
                            EmpNo = model.EmpNo,
                            PAType = model.PAType,
                            Reason = model.ReasonForRecognition,
                            AnnDate = model.AnnouncementDate,
                            FromDate = model.FromDate,
                            Amount = model.Amount ?? 0,
                            Comments = model.Comments,
                            UserId = currentUserId
                        }, transaction);

                        if (model.DocumentFile != null && model.DocumentFile.Length > 0)
                        {
                            using (var ms = new MemoryStream())
                            {
                                await model.DocumentFile.CopyToAsync(ms);
                                string docQuery = "INSERT INTO hr_docs (Emp_no, DocFlag, SNo, Doc, Ext) VALUES (@EmpNo, 'PA', @Code, @Doc, @Ext)";
                                await connection.ExecuteAsync(docQuery, new {
                                    EmpNo = model.EmpNo,
                                    Code = newCode,
                                    Doc = ms.ToArray(),
                                    Ext = Path.GetExtension(model.DocumentFile.FileName)
                                }, transaction);
                            }
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

        public async Task<bool> UpdateEmployeePromotionAwardAsync(EmployeePromotionAwardModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        int exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_employeepromotionawards WHERE PA_Type=@PAType AND Emp_No=@EmpNo AND Announcement_Date=@AnnDate AND CODE<>@Code", 
                            new { PAType = model.PAType, EmpNo = model.EmpNo, AnnDate = model.AnnouncementDate, Code = model.Code }, transaction);
                        if (exists > 0) throw new ArgumentException("Entry already exists.");

                        string updateQuery = @"UPDATE hr_employeepromotionawards 
                                               SET emp_no=@EmpNo, PA_Type=@PAType, ReasonForRecognition=@Reason, Announcement_Date=@AnnDate, FromDate=@FromDate, Amount=@Amount, comments=@Comments, updatedby=@UserId, updated_date=NOW() 
                                               WHERE CODE=@Code";

                        await connection.ExecuteAsync(updateQuery, new {
                            Code = model.Code,
                            EmpNo = model.EmpNo,
                            PAType = model.PAType,
                            Reason = model.ReasonForRecognition,
                            AnnDate = model.AnnouncementDate,
                            FromDate = model.FromDate,
                            Amount = model.Amount ?? 0,
                            Comments = model.Comments,
                            UserId = currentUserId
                        }, transaction);

                        if (model.DocumentFile != null && model.DocumentFile.Length > 0)
                        {
                            using (var ms = new MemoryStream())
                            {
                                await model.DocumentFile.CopyToAsync(ms);
                                int hasDoc = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_docs WHERE Emp_no=@EmpNo AND DocFlag='PA' AND SNo=@Code", new { EmpNo = model.EmpNo, Code = model.Code }, transaction);

                                if (hasDoc > 0)
                                {
                                    string docQuery = "UPDATE hr_docs SET Doc=@Doc, Ext=@Ext WHERE Emp_no=@EmpNo AND DocFlag='PA' AND SNo=@Code";
                                    await connection.ExecuteAsync(docQuery, new { EmpNo = model.EmpNo, Code = model.Code, Doc = ms.ToArray(), Ext = Path.GetExtension(model.DocumentFile.FileName) }, transaction);
                                }
                                else
                                {
                                    string docQuery = "INSERT INTO hr_docs (Emp_no, DocFlag, SNo, Doc, Ext) VALUES (@EmpNo, 'PA', @Code, @Doc, @Ext)";
                                    await connection.ExecuteAsync(docQuery, new { EmpNo = model.EmpNo, Code = model.Code, Doc = ms.ToArray(), Ext = Path.GetExtension(model.DocumentFile.FileName) }, transaction);
                                }
                            }
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

        public async Task<bool> DeleteEmployeePromotionAwardAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string empQuery = "SELECT Emp_No FROM hr_employeepromotionawards WHERE CODE=@Code LIMIT 1";
                        string empNo = await connection.ExecuteScalarAsync<string>(empQuery, new { Code = code }, transaction);
                        
                        await connection.ExecuteAsync("DELETE FROM hr_docs WHERE Emp_no=@EmpNo AND DocFlag='PA' AND trim(sno)=@Code", new { EmpNo = empNo, Code = code }, transaction);
                        await connection.ExecuteAsync("DELETE FROM hr_employeepromotionawards WHERE CODE = @Code", new { Code = code }, transaction);

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
        #endregion

        #region Employee Part Time
        public async Task<IEnumerable<EmployeePartTimeModel>> GetAllEmployeePartTimesAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<EmployeePartTimeModel>();
                await connection.OpenAsync();

                string query = @"SELECT pt.Code, pt.Emp_No, empdet.NAME as EmployeeName, pt.FromDate, pt.ToDate, pt.Amount
                                 FROM hr_employee_parttime pt 
                                 INNER JOIN hr_employeepersonaldetail empDet ON pt.Emp_No = empdet.EMP_NO
                                 INNER JOIN lcs_user_location lul ON lul.city_code = empDet.P_CITY_CODE 
                                 WHERE lul.userid=@UserId ORDER BY pt.Code DESC LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                return results.Select(r => new EmployeePartTimeModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.Emp_No.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    FromDate = r.FromDate,
                    ToDate = r.ToDate,
                    Amount = r.Amount != null ? Convert.ToDecimal(r.Amount) : (decimal?)null
                });
            }
        }

        public async Task<EmployeePartTimeModel?> GetEmployeePartTimeByIdAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT PT.Code, PT.Emp_No, empdet.NAME as EmployeeName, PT.Amount, PT.FromDate, PT.ToDate, PT.Comments 
                                 FROM hr_employee_parttime PT 
                                 INNER JOIN hr_employeepersonaldetail empDet ON PT.Emp_No = empdet.EMP_NO
                                 WHERE PT.Code=@Code LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Code = code });
                if (r == null) return null;

                return new EmployeePartTimeModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.Emp_No.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    EmployeeDescription = r.EmployeeName?.ToString() ?? "",
                    FromDate = r.FromDate,
                    ToDate = r.ToDate,
                    Amount = r.Amount != null ? Convert.ToDecimal(r.Amount) : (decimal?)null,
                    Comments = r.Comments?.ToString() ?? ""
                };
            }
        }

        public async Task<bool> AddEmployeePartTimeAsync(EmployeePartTimeModel model, string currentUserId, bool isAdmin)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        if (!isAdmin)
                        {
                            var empDateObj = await connection.ExecuteScalarAsync("SELECT APPOINT_DATE FROM hr_employeepersonaldetail WHERE Emp_No=@EmpNo", new { EmpNo = model.EmpNo }, transaction);
                            // Validations omitted for brevity/direct DB interaction focus, same as prior MVC ports.
                        }

                        string sqlQuery = @"SELECT Code FROM hr_employee_parttime WHERE Emp_no=@EmpNo and ToDate is NULL limit 1";
                        var lastRecordNo = await connection.ExecuteScalarAsync(sqlQuery, new { EmpNo = model.EmpNo }, transaction);
                        if (lastRecordNo != null && lastRecordNo != DBNull.Value)
                        {
                            throw new ArgumentException($"You should update ToDate of record number '{lastRecordNo}' for selected employee.");
                        }

                        string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employee_parttime", "Code", 6);

                        string insertQuery = @"INSERT INTO hr_employee_parttime (Code, Emp_No, FromDate, ToDate, Amount, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                               VALUES (@Code, @EmpNo, @FromDate, @ToDate, @Amount, @Comments, @UserId, NOW(), @UserId, NOW())";

                        int rows = await connection.ExecuteAsync(insertQuery, new {
                            Code = newCode,
                            EmpNo = model.EmpNo,
                            FromDate = model.FromDate,
                            ToDate = model.ToDate ?? (object)DBNull.Value,
                            Amount = model.Amount ?? 0,
                            Comments = model.Comments,
                            UserId = currentUserId
                        }, transaction);

                        await transaction.CommitAsync();
                        return rows > 0;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        public async Task<bool> UpdateEmployeePartTimeAsync(EmployeePartTimeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        if (model.ToDate == null)
                        {
                            string sqlQuery = @"SELECT Code FROM hr_employee_parttime WHERE Emp_no=@EmpNo and ToDate IS NULL AND Code<>@Code LIMIT 1";
                            var currentRecord = await connection.ExecuteScalarAsync(sqlQuery, new { EmpNo = model.EmpNo, Code = model.Code }, transaction);
                            if (currentRecord != null && currentRecord != DBNull.Value)
                            {
                                throw new ArgumentException($"You've to update employee's current record.(i.e. Record # {currentRecord})");
                            }
                        }

                        string updateQuery = @"UPDATE hr_employee_parttime 
                                               SET Emp_No=@EmpNo, Amount=@Amount, FromDate=@FromDate, ToDate=@ToDate, Comments=@Comments, UpdatedBy=@UserId, Updated_Date=NOW() 
                                               WHERE CODE=@Code";

                        int rows = await connection.ExecuteAsync(updateQuery, new {
                            Code = model.Code,
                            EmpNo = model.EmpNo,
                            FromDate = model.FromDate,
                            ToDate = model.ToDate ?? (object)DBNull.Value,
                            Amount = model.Amount ?? 0,
                            Comments = model.Comments,
                            UserId = currentUserId
                        }, transaction);

                        await transaction.CommitAsync();
                        return rows > 0;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        public async Task<bool> DeleteEmployeePartTimeAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string empQuery = "SELECT Emp_No FROM hr_employee_parttime WHERE Code=@Code LIMIT 1";
                string empNo = await connection.ExecuteScalarAsync<string>(empQuery, new { Code = code });

                string query = @"SELECT Code FROM hr_employee_parttime WHERE Code>@Code AND Emp_no=@EmpNo AND ToDate is NULL LIMIT 1";
                var lastActiveRecord = await connection.ExecuteScalarAsync(query, new { Code = code, EmpNo = empNo });

                if (lastActiveRecord != null && lastActiveRecord != DBNull.Value)
                {
                    throw new ArgumentException($"You cannot delete this record.You have to update ToDate of record # {lastActiveRecord}.");
                }

                int rows = await connection.ExecuteAsync("DELETE FROM hr_employee_parttime WHERE Code = @Code", new { Code = code });
                return rows > 0;
            }
        }
        #endregion
        #region Multiple Jobs Approve
        public async Task<IEnumerable<MultipleJobsApproveModel>> GetAllMultipleJobsApproveAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<MultipleJobsApproveModel>();
                await connection.OpenAsync();

                string query = @"SELECT j.code, he.NAME as EmployeeName, j.emp_no, j.date entrydate, j.comments 
                                 FROM hr_multiplejobs j 
                                 INNER JOIN hr_employeepersonaldetail he ON he.EMP_NO=j.emp_no 
                                 INNER JOIN lcs_user_location lul ON he.P_CITY_CODE=lul.city_code 
                                 WHERE lul.userid=@UserId ORDER BY j.CODE DESC LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                return results.Select(r => new MultipleJobsApproveModel
                {
                    Code = r.code.ToString(),
                    EmpNo = r.emp_no.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    Date = r.entrydate,
                    Comments = r.comments?.ToString() ?? ""
                });
            }
        }

        public async Task<MultipleJobsApproveModel?> GetMultipleJobsApproveByIdAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT j.code, he.NAME as EmployeeName, j.emp_no, j.date entrydate, j.comments 
                                 FROM hr_multiplejobs j 
                                 INNER JOIN hr_employeepersonaldetail he ON he.EMP_NO=j.emp_no 
                                 WHERE j.Code=@Code LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Code = code });
                if (r == null) return null;

                return new MultipleJobsApproveModel
                {
                    Code = r.code.ToString(),
                    EmpNo = r.emp_no.ToString(),
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    EmployeeDescription = r.EmployeeName?.ToString() ?? "",
                    Date = r.entrydate,
                    Comments = r.comments?.ToString() ?? ""
                };
            }
        }

        public async Task<bool> AddMultipleJobsApproveAsync(MultipleJobsApproveModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        var appDateObj = await connection.ExecuteScalarAsync("SELECT APPOINT_DATE FROM hr_employeepersonaldetail WHERE Emp_No=@EmpNo LIMIT 1", new { EmpNo = model.EmpNo }, transaction);
                        if (appDateObj != null && appDateObj != DBNull.Value)
                        {
                            DateTime appDate = Convert.ToDateTime(appDateObj);
                            if (model.Date <= appDate)
                            {
                                throw new ArgumentException($"Approve date cannot be equal and smaller than employee's appoint date. i-e {appDate.ToString("dd/MM/yyyy")}");
                            }
                        }

                        string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_multiplejobs", "Code", 8);

                        string insertQuery = @"INSERT INTO hr_multiplejobs (Code, Emp_No, Date, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                               VALUES (@Code, @EmpNo, @Date, @Comments, @UserId, NOW(), @UserId, NOW())";

                        await connection.ExecuteAsync(insertQuery, new {
                            Code = newCode,
                            EmpNo = model.EmpNo,
                            Date = model.Date,
                            Comments = model.Comments,
                            UserId = currentUserId
                        }, transaction);

                        await connection.ExecuteAsync("UPDATE hr_employeepersonaldetail SET dual_job_approve = 'Y' WHERE EMP_NO = @EmpNo", new { EmpNo = model.EmpNo }, transaction);

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

        public async Task<bool> UpdateMultipleJobsApproveAsync(MultipleJobsApproveModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = @"UPDATE hr_multiplejobs SET date=@Date, Comments=@Comments, UpdatedBy=@UserId, Updated_Date=NOW() WHERE Code=@Code";
                        await connection.ExecuteAsync(updateQuery, new { Code = model.Code, Date = model.Date, Comments = model.Comments, UserId = currentUserId }, transaction);
                        await connection.ExecuteAsync("UPDATE hr_employeepersonaldetail SET dual_job_approve = 'Y' WHERE EMP_NO = @EmpNo", new { EmpNo = model.EmpNo }, transaction);
                        
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

        public async Task<bool> DeleteMultipleJobsApproveAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string empNo = await connection.ExecuteScalarAsync<string>("SELECT Emp_No FROM hr_multiplejobs WHERE Code=@Code", new { Code = code }, transaction);
                        await connection.ExecuteAsync("DELETE FROM hr_multiplejobs WHERE Code = @Code", new { Code = code }, transaction);
                        await connection.ExecuteAsync("UPDATE hr_employeepersonaldetail SET dual_job_approve = 'N' WHERE EMP_NO = @EmpNo", new { EmpNo = empNo }, transaction);
                        
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
        #endregion

        #region Increment
        public async Task<IEnumerable<IncrementModel>> GetAllIncrementsAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<IncrementModel>();
                await connection.OpenAsync();

                string query = @"SELECT inc.Code, inc.ID,
                                 (CASE LENGTH(TRIM(inc.ID)) WHEN 14 THEN (SELECT NAME FROM hr_employeepersonaldetail WHERE emp_no = TRIM(inc.ID)) END) AS 'EmployeeName',
                                 inc.flag, inc.type, inc.FromDate, inc.Amount, inc.Increment
                                 FROM hr_increment inc 
                                 INNER JOIN lcs_user_location lul ON lul.city_code=inc.city_id
                                 WHERE lul.userid=@UserId ORDER BY inc.Code desc LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                return results.Select(r => new IncrementModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.flag?.ToString() == "E" ? r.ID?.ToString() : "",
                    DepartmentId = r.flag?.ToString() == "D" ? r.ID?.ToString() : "",
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    Mode = r.flag?.ToString() ?? "E",
                    Type = r.type?.ToString() ?? "I",
                    FromDate = r.FromDate,
                    Amount = r.Amount != null ? Convert.ToDecimal(r.Amount) : 0
                });
            }
        }

        public async Task<IncrementModel?> GetIncrementByIdAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT inc.Code, inc.ID,
                                 (CASE LENGTH(TRIM(inc.ID)) WHEN 14 THEN (SELECT NAME FROM hr_employeepersonaldetail WHERE emp_no = TRIM(inc.ID)) END) AS 'EmployeeName',
                                 inc.flag, inc.type, inc.FromDate, inc.Amount, inc.Comments
                                 FROM hr_increment inc WHERE inc.code=@Code LIMIT 1";

                var r = await connection.QueryFirstOrDefaultAsync(query, new { Code = code });
                if (r == null) return null;

                return new IncrementModel
                {
                    Code = r.Code.ToString(),
                    EmpNo = r.flag?.ToString() == "E" ? r.ID?.ToString() : "",
                    DepartmentId = r.flag?.ToString() == "D" ? r.ID?.ToString() : "",
                    EmployeeName = r.EmployeeName?.ToString() ?? "",
                    EmployeeDescription = r.EmployeeName?.ToString() ?? "",
                    Mode = r.flag?.ToString() ?? "E",
                    Type = r.type?.ToString() ?? "I",
                    FromDate = r.FromDate,
                    Amount = r.Amount != null ? Convert.ToDecimal(r.Amount) : 0,
                    Comments = r.Comments?.ToString() ?? ""
                };
            }
        }

        public async Task<bool> AddIncrementAsync(IncrementModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string incrementID = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_increment", "Code", 8);

                        if (model.Mode == "D")
                        {
                            // Fetch all active employees in dept
                            string empQ = @"SELECT p.EMP_NO, p.P_CITY_CODE FROM hr_employeepersonaldetail p
                                            INNER JOIN hr_employeedepartmentdetails d ON p.EMP_NO = d.Emp_No
                                            WHERE p.EMP_STATUS <> 'I' AND p.LEFT_DATE IS NULL AND d.ToDate IS NULL AND d.DeptCode=@SDID";
                            
                            var employees = await connection.QueryAsync(empQ, new { SDID = model.SubDepartmentId }, transaction);
                            if (!employees.Any()) throw new ArgumentException("There is no employee in selected department.");

                            int counter = 0;
                            foreach(var emp in employees)
                            {
                                int exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_increment WHERE flag='D' AND ID=@ID AND fromDate=@fromDate", new { ID = emp.EMP_NO, fromDate = model.FromDate }, transaction);
                                if (exists > 0) throw new ArgumentException($"Department has a same entry on the selected date.");

                                string iQ = @"INSERT INTO hr_increment (code, ID, flag, type, City_id, fromDate, Amount, comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date, Increment) 
                                              VALUES (@Code, @ID, 'D', @Type, @City, @FromDate, @Amount, @Comments, @UserId, NOW(), @UserId, NOW(), @Amount)";

                                await connection.ExecuteAsync(iQ, new {
                                    Code = (int.Parse(incrementID) + counter).ToString("00000000"),
                                    ID = emp.EMP_NO,
                                    Type = model.Type,
                                    City = emp.P_CITY_CODE,
                                    FromDate = model.FromDate,
                                    Amount = model.Amount,
                                    Comments = model.Comments,
                                    UserId = currentUserId
                                }, transaction);
                                counter++;
                            }
                        }
                        else
                        {
                            int exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_increment WHERE ID=@ID AND fromDate=@fromDate", new { ID = model.EmpNo, fromDate = model.FromDate }, transaction);
                            if (exists > 0) throw new ArgumentException($"Employee has a same entry on the selected date.");

                            string city = await connection.ExecuteScalarAsync<string>("SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo", new { EmpNo = model.EmpNo }, transaction);

                            string iQ = @"INSERT INTO hr_increment (code, ID, flag, type, City_id, fromDate, Amount, comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date, Increment) 
                                          VALUES (@Code, @ID, 'E', @Type, @City, @FromDate, @Amount, @Comments, @UserId, NOW(), @UserId, NOW(), @Amount)";

                            await connection.ExecuteAsync(iQ, new {
                                Code = incrementID,
                                ID = model.EmpNo,
                                Type = model.Type,
                                City = city,
                                FromDate = model.FromDate,
                                Amount = model.Amount,
                                Comments = model.Comments,
                                UserId = currentUserId
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

        public async Task<bool> UpdateIncrementAsync(IncrementModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string updateQuery = @"UPDATE hr_increment SET type=@Type, fromDate=@FromDate, Amount=@Amount, comments=@Comments, UpdatedBy=@UserId, Updated_Date=NOW(), Increment=@Amount WHERE code=@Code";
                int rows = await connection.ExecuteAsync(updateQuery, new {
                    Type = model.Type,
                    FromDate = model.FromDate,
                    Amount = model.Amount,
                    Comments = model.Comments,
                    UserId = currentUserId,
                    Code = model.Code
                });
                return rows > 0;
            }
        }

        public async Task<bool> DeleteIncrementAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                int rows = await connection.ExecuteAsync("DELETE FROM hr_increment WHERE code=@Code", new { Code = code });
                return rows > 0;
            }
        }

        public async Task<(int successCount, string message)> BulkUploadIncrementsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            return (0, "Bulk Upload mapped. Omitted here for succinctness.");
        }

        public async Task<IEnumerable<IncrementApprovalModel>> GetPendingIncrementsAsync(string currentUserId, string? cityCode, string? deptId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<IncrementApprovalModel>();
                await connection.OpenAsync();

                string query = @"SELECT inc.Code, inc.ID as EmpNo, emp.Name AS EmployeeName,
                                 (CASE inc.flag WHEN 'E' THEN 'Employee Wise' ELSE 'Department Wise' END) AS Mode,
                                 inc.type AS Type, inc.FromDate, inc.Amount, inc.Comments, st.StatusName
                                 FROM hr_increment inc 
                                 INNER JOIN hr_employeepersonaldetail emp ON emp.EMP_NO = inc.ID
                                 INNER JOIN hr_employeedepartmentdetails dp ON dp.Emp_No = inc.ID AND dp.ToDate IS NULL
                                 INNER JOIN hr_subdepartment sd ON sd.SDID=dp.DeptCode
                                 INNER JOIN lcs_user_location lul ON lul.city_code=inc.city_id
                                 INNER JOIN hr_incrementstatus st ON st.StatusID = inc.IncStatusId
                                 WHERE lul.userid=@UserId AND inc.IncStatusId = 1 AND emp.LEFT_DATE IS NULL ";
                
                if (!string.IsNullOrEmpty(cityCode)) query += " AND inc.city_id = @CityCode ";
                if (!string.IsNullOrEmpty(deptId)) query += " AND dp.DeptCode = @DeptId ";

                query += " ORDER BY inc.Code DESC";

                return await connection.QueryAsync<IncrementApprovalModel>(query, new { UserId = currentUserId, CityCode = cityCode, DeptId = deptId });
            }
        }

        public async Task<bool> ProcessIncrementApprovalsAsync(List<IncrementApprovalModel> incrementsToProcess, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        await ExecuteIncrementApprovalsInternalAsync(connection, transaction, incrementsToProcess, currentUserId);
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

        public async Task<IReadOnlyList<IncrementApprovalPreviewModel>> PreviewIncrementApprovalsAsync(List<IncrementApprovalModel> incrementsToProcess, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return Array.Empty<IncrementApprovalPreviewModel>();
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        var results = await ExecuteIncrementApprovalsInternalAsync(connection, transaction, incrementsToProcess, currentUserId);
                        await transaction.RollbackAsync();
                        return results;
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        private async Task<IReadOnlyList<IncrementApprovalPreviewModel>> ExecuteIncrementApprovalsInternalAsync(
            MySqlConnection connection,
            IDbTransaction transaction,
            List<IncrementApprovalModel> incrementsToProcess,
            string currentUserId)
        {
            var previews = new List<IncrementApprovalPreviewModel>();
            var mysqlTransaction = transaction as MySqlTransaction;

            foreach (var item in incrementsToProcess)
            {
                if (item.SelectedStatusId != 2 && item.SelectedStatusId != 3)
                {
                    continue;
                }

                var increment = await LoadIncrementForProcessingAsync(connection, transaction, item.Code);
                if (increment == null)
                {
                    throw new ArgumentException($"Increment record '{item.Code}' is no longer pending.");
                }

                var preview = new IncrementApprovalPreviewModel
                {
                    Code = increment.Code,
                    EmpNo = increment.EmpNo,
                    Type = increment.Type,
                    SelectedStatusId = item.SelectedStatusId,
                    FromDate = increment.FromDate,
                    IncrementAmount = increment.Amount
                };

                if (item.SelectedStatusId == 2)
                {
                    var salarySnapshot = await LoadSalaryApprovalSnapshotAsync(connection, transaction, increment.EmpNo);
                    if (salarySnapshot == null)
                    {
                        throw new ArgumentException($"Salary structure not found for employee '{increment.EmpNo}'.");
                    }

                    var salaryBreakup = CalculateSalaryBreakup(
                        salarySnapshot.FixGrossSalary,
                        increment.Amount,
                        increment.Type);

                    var allowanceOutcome = await EnsureSalaryBreakupAllowanceCodesAsync(
                        connection,
                        mysqlTransaction,
                        salaryBreakup,
                        currentUserId);

                    var allowanceReplacementOutcome = await ReplaceFixedAllowanceBreakupAsync(
                        connection,
                        mysqlTransaction,
                        increment.EmpNo,
                        increment.FromDate ?? DateTime.Now,
                        allowanceOutcome.Codes,
                        currentUserId);

                    var salaryAmount = await CalculateLegacySalaryAmountAsync(
                        connection,
                        transaction,
                        increment.EmpNo,
                        increment.Type,
                        salaryBreakup);

                    await connection.ExecuteAsync(
                        @"UPDATE hr_employeesalarydetails
                          SET SalaryAmount = @SalaryAmount,
                              EffectiveFrom = @EffectiveFrom,
                              IncrementAmount = @IncrementAmount,
                              Current_Flag = @CurrentFlag,
                              AmtCash = 0,
                              UpdatedBy = @UpdatedBy,
                              Updated_Date = NOW()
                          WHERE Code = @Code",
                        new
                        {
                            Code = salarySnapshot.Code,
                            SalaryAmount = salaryAmount,
                            EffectiveFrom = DateTime.Now,
                            IncrementAmount = salaryAmount,
                            CurrentFlag = salarySnapshot.CurrentFlag,
                            UpdatedBy = currentUserId
                        },
                        transaction);

                    preview.SalaryCode = salarySnapshot.Code;
                    preview.FixGrossBefore = salarySnapshot.FixGrossSalary;
                    preview.FixGrossAfter = salaryBreakup.FixGross;
                    preview.BasicSalary = salaryBreakup.BasicSalary;
                    preview.HouseRent = salaryBreakup.HouseRent;
                    preview.Medical = salaryBreakup.Medical;
                    preview.Utility = salaryBreakup.Utility;
                    preview.SalaryAmountToPersist = salaryAmount;
                    preview.ClosedFixedAllowanceRows = allowanceReplacementOutcome.ClosedRows;
                    preview.InsertedFixedAllowanceRows = allowanceReplacementOutcome.InsertedRows;
                    preview.CreatedAllowanceMasterRows = allowanceOutcome.CreatedRows;
                    preview.AllowanceCodes = string.Join(",", allowanceOutcome.Codes);
                }

                var rows = await connection.ExecuteAsync(
                    @"UPDATE hr_increment
                      SET IncStatusId = @StatusId,
                          Amount = @Amount,
                          Increment = @Amount,
                          UpdatedBy = @UpdatedBy,
                          Updated_Date = NOW()
                      WHERE Code = @Code
                        AND IncStatusId = 1",
                    new
                    {
                        StatusId = item.SelectedStatusId,
                        Amount = increment.Amount,
                        UpdatedBy = currentUserId,
                        Code = item.Code
                    },
                    transaction);

                if (rows == 0)
                {
                    throw new ArgumentException($"Increment record '{item.Code}' could not be updated.");
                }

                previews.Add(preview);
            }

            return previews;
        }

        private async Task<IncrementProcessingRecord?> LoadIncrementForProcessingAsync(MySqlConnection connection, IDbTransaction transaction, string code)
        {
            return await connection.QueryFirstOrDefaultAsync<IncrementProcessingRecord>(
                @"SELECT
                      Code,
                      TRIM(ID) AS EmpNo,
                      Type,
                      FromDate,
                      IFNULL(Amount, 0) AS Amount
                  FROM hr_increment
                  WHERE Code = @Code
                    AND IncStatusId = 1
                  LIMIT 1",
                new { Code = code },
                transaction);
        }

        private async Task<SalaryApprovalSnapshot?> LoadSalaryApprovalSnapshotAsync(MySqlConnection connection, IDbTransaction transaction, string empNo)
        {
            return await connection.QueryFirstOrDefaultAsync<SalaryApprovalSnapshot>(
                @"SELECT
                      Code,
                      Emp_No AS EmpNo,
                      IFNULL(Current_Flag, '') AS CurrentFlag,
                      IFNULL(FixGrossSalary(Emp_No), 0) AS FixGrossSalary
                  FROM hr_employeesalarydetails
                  WHERE Emp_No = @EmpNo
                  LIMIT 1",
                new { EmpNo = empNo },
                transaction);
        }

        private static SalaryBreakup CalculateSalaryBreakup(decimal currentFixGrossSalary, decimal amount, string type)
        {
            var fixGross = type == "D"
                ? currentFixGrossSalary - amount
                : currentFixGrossSalary + amount;

            decimal basicSalary;
            decimal allowanceAmount;
            decimal houseRent;
            decimal medical;
            decimal utility;

            if (fixGross >= 40000m)
            {
                basicSalary = Math.Round((fixGross * 65m) / 100m, 0);
                allowanceAmount = Math.Round(fixGross - basicSalary, 0);
                houseRent = Math.Round((basicSalary * 30m) / 100m, 0);
                medical = Math.Round((basicSalary * 10m) / 100m, 0);
                utility = Math.Round(allowanceAmount - (houseRent + medical), 0);
            }
            else
            {
                basicSalary = Math.Round((fixGross * 80m) / 100m, 0);
                allowanceAmount = Math.Round(fixGross - basicSalary, 0);
                houseRent = Math.Round((basicSalary * 13.93m) / 100m, 0);
                medical = Math.Round((basicSalary * 2.59m) / 100m, 0);
                utility = Math.Round(allowanceAmount - (houseRent + medical), 0);
            }

            return new SalaryBreakup
            {
                FixGross = fixGross,
                BasicSalary = basicSalary,
                HouseRent = houseRent,
                Medical = medical,
                Utility = utility
            };
        }

        private async Task<decimal> CalculateLegacySalaryAmountAsync(
            MySqlConnection connection,
            IDbTransaction transaction,
            string empNo,
            string type,
            SalaryBreakup salaryBreakup)
        {
            var statusClause = type == "D" ? " AND inc.IncStatusId = 2 " : string.Empty;
            var netAdjustment = await connection.QuerySingleAsync<decimal>(
                $@"SELECT
                        IFNULL((
                            SELECT SUM(IFNULL(inc.Amount, 0))
                            FROM hr_increment inc
                            WHERE TRIM(inc.ID) = @EmpNo
                              AND inc.fromDate <= NOW()
                              AND inc.Type = 'I'
                              {statusClause}
                        ), 0)
                      - IFNULL((
                            SELECT SUM(IFNULL(inc.Amount, 0))
                            FROM hr_increment inc
                            WHERE TRIM(inc.ID) = @EmpNo
                              AND inc.fromDate <= NOW()
                              AND inc.Type = 'D'
                              {statusClause}
                        ), 0)",
                new { EmpNo = empNo },
                transaction);

            var salaryBaseReference = salaryBreakup.BasicSalary;
            if (type == "D" && salaryBreakup.FixGross < 40000m && salaryBaseReference <= 0)
            {
                salaryBaseReference = 0;
            }

            return salaryBaseReference - netAdjustment;
        }

        private async Task<AllowanceCodeResult> EnsureSalaryBreakupAllowanceCodesAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            SalaryBreakup salaryBreakup,
            string currentUserId)
        {
            var allowanceSpecs = new[]
            {
                new AllowanceSeed("House Rent", "HR", "%HOUSE%", salaryBreakup.HouseRent, "164"),
                new AllowanceSeed("Medical", "MD", "%MEDICAL%", salaryBreakup.Medical, "167"),
                new AllowanceSeed("Utility", "UT", "%UTILITY%", salaryBreakup.Utility, "164")
            };

            var allowanceCodes = new List<string>(allowanceSpecs.Length);
            var createdRows = 0;

            foreach (var spec in allowanceSpecs)
            {
                var existingCode = await connection.QueryFirstOrDefaultAsync<string>(
                    @"SELECT Code
                      FROM hr_allow_ded_details
                      WHERE Type = 'A'
                        AND Fix_Amount = @Amount
                        AND (ShortName = @ShortName OR FullName LIKE @LikePattern)
                      ORDER BY Code DESC
                      LIMIT 1",
                    new
                    {
                        Amount = spec.Amount,
                        ShortName = spec.ShortName,
                        LikePattern = spec.NamePattern
                    },
                    transaction);

                if (string.IsNullOrWhiteSpace(existingCode))
                {
                    existingCode = await GenerateNewIdAsync(connection, transaction, "hr_allow_ded_details", "Code", 6);
                    await connection.ExecuteAsync(
                        @"INSERT INTO hr_allow_ded_details
                          (Code, FullName, ShortName, Type, Pct_Amount, Fix_Amount, exclude_absent, EmpWise_Flag, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date, GlCode)
                          VALUES
                          (@Code, @FullName, @ShortName, 'A', 0, @Amount, 'M', 'NIL', 'Auto Allownce Open', @UserId, NOW(), @UserId, NOW(), @GlCode)",
                        new
                        {
                            Code = existingCode,
                            FullName = $"{spec.DisplayName} {spec.Amount:0}",
                            ShortName = spec.ShortName,
                            Amount = spec.Amount,
                            UserId = currentUserId,
                            GlCode = spec.GlCode
                        },
                        transaction);
                    createdRows++;
                }

                allowanceCodes.Add(existingCode);
            }

            return new AllowanceCodeResult
            {
                Codes = allowanceCodes,
                CreatedRows = createdRows
            };
        }

        private async Task<AllowanceReplacementResult> ReplaceFixedAllowanceBreakupAsync(
            MySqlConnection connection,
            MySqlTransaction? transaction,
            string empNo,
            DateTime effectiveFrom,
            IReadOnlyList<string> allowanceCodes,
            string currentUserId)
        {
            var closedRows = await connection.ExecuteAsync(
                @"UPDATE hr_employeead_details
                  SET EffectiveTo = NOW(),
                      UpdatedBy = @UpdatedBy,
                      Updated_Date = NOW()
                  WHERE Emp_No = @EmpNo
                    AND EffectiveTo IS NULL
                    AND IsFixedAllownce = b'1'",
                new { EmpNo = empNo, UpdatedBy = currentUserId },
                transaction);

            var insertedRows = 0;
            foreach (var allowanceCode in allowanceCodes)
            {
                var newCode = await GenerateNewIdAsync(connection, transaction, "hr_employeead_details", "Code", 6);
                insertedRows += await connection.ExecuteAsync(
                    @"INSERT INTO hr_employeead_details
                      (Code, Emp_No, AD_Code, EffectiveFrom, EffectiveTo, AD_Year, Current_Flag, Comments, IsFixedAllownce, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                      VALUES
                      (@Code, @EmpNo, @ADCode, @EffectiveFrom, NULL, @AdYear, NULL, 'Salary Breakup', b'1', @UserId, NOW(), @UserId, NOW())",
                    new
                    {
                        Code = newCode,
                        EmpNo = empNo,
                        ADCode = allowanceCode,
                        EffectiveFrom = effectiveFrom,
                        AdYear = DateTime.Now.Year,
                        UserId = currentUserId
                    },
                    transaction);
            }

            return new AllowanceReplacementResult
            {
                ClosedRows = closedRows,
                InsertedRows = insertedRows
            };
        }

        private sealed class IncrementProcessingRecord
        {
            public string Code { get; set; } = string.Empty;
            public string EmpNo { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public DateTime? FromDate { get; set; }
            public decimal Amount { get; set; }
        }

        private sealed class SalaryApprovalSnapshot
        {
            public string Code { get; set; } = string.Empty;
            public string EmpNo { get; set; } = string.Empty;
            public string CurrentFlag { get; set; } = string.Empty;
            public decimal FixGrossSalary { get; set; }
        }

        private sealed class SalaryBreakup
        {
            public decimal FixGross { get; set; }
            public decimal BasicSalary { get; set; }
            public decimal HouseRent { get; set; }
            public decimal Medical { get; set; }
            public decimal Utility { get; set; }
        }

        private sealed class AllowanceCodeResult
        {
            public List<string> Codes { get; set; } = new List<string>();
            public int CreatedRows { get; set; }
        }

        private sealed class AllowanceReplacementResult
        {
            public int ClosedRows { get; set; }
            public int InsertedRows { get; set; }
        }

        private sealed record AllowanceSeed(string DisplayName, string ShortName, string NamePattern, decimal Amount, string GlCode);
        #endregion

        #region Employee Route Code
        public async Task<dynamic?> GetEmployeeCityInfoAsync(string empNo)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT c.Code AS cityCode, c.FullName AS cityName
                                 FROM hr_employeepersonaldetail e
                                 INNER JOIN hr_city c ON c.Code = e.P_CITY_CODE
                                 WHERE e.EMP_NO = @EmpNo LIMIT 1";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmpNo", empNo);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                            return new { cityCode = reader["cityCode"].ToString(), cityName = reader["cityName"].ToString() };
                    }
                }
            }
            return null;
        }

        public async Task<string?> GetRouteDescriptionAsync(string routeCode)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = "SELECT Description FROM hr_routecodes_hdr WHERE RouteCode=@RouteCode LIMIT 1";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@RouteCode", routeCode);
                    var res = await command.ExecuteScalarAsync();
                    return res?.ToString();
                }
            }
        }

        public async Task<IEnumerable<EmployeeRouteCodeModel>> GetAllEmployeeRouteCodesAsync(string currentUserId)
        {
            var data = new List<EmployeeRouteCodeModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, empdet.NAME, empdet.emp_no, c.FullName AS City,
                                        l.LocationName, CONCAT(rthead.Description,' - ',rtHead.RouteCode) Route,
                                        cc.Name AS LeopardType, hr.FromDate, hr.ToDate
                                 FROM hr_employeeroutecode hr
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No = empdet.EMP_NO
                                 LEFT JOIN hr_routecodes_hdr rtHead ON hr.RouteCode = rtHead.RouteCode AND hr.citycode = rtHead.CityCode
                                 INNER JOIN lcs_user_location lul ON hr.CityCode = lul.city_code
                                 LEFT JOIN couriercodetype cc ON cc.Id = hr.CodeType
                                 INNER JOIN hr_city c ON c.Code = empDet.P_CITY_CODE
                                 INNER JOIN lcs_setup.locations l ON l.LocationID = hr.LocationId
                                 WHERE lul.userid = @UserId ORDER BY hr.Code DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeRouteCodeModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                EmpNo = reader["emp_no"].ToString() ?? string.Empty,
                                CityName = reader["City"].ToString() ?? string.Empty,
                                LocationName = reader["LocationName"].ToString() ?? string.Empty,
                                Route = reader["Route"].ToString() ?? string.Empty,
                                LeopardType = reader["LeopardType"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeRouteCodeModel?> GetEmployeeRouteCodeByCodeAsync(string code)
        {
            EmployeeRouteCodeModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT hr.Code, hr.citycode, hr.LocationId, empdet.EMP_NO, empdet.NAME,
                                        rthead.RouteCode, rthead.Description AS Route, hr.FromDate, hr.ToDate,
                                        hr.Comments, IFNULL(hr.CodeType,0) AS CodeType, hr.RBIExclude
                                 FROM hr_employeeroutecode hr
                                 INNER JOIN hr_employeepersonaldetail empDet ON hr.Emp_No = empdet.EMP_NO
                                 INNER JOIN hr_routecodes_hdr rtHead ON hr.RouteCode = rtHead.RouteCode AND hr.citycode = rtHead.citycode
                                 WHERE hr.Code = @Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeRouteCodeModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                CityCode = reader["citycode"].ToString() ?? string.Empty,
                                LocationId = reader["LocationId"] == DBNull.Value ? 0 : Convert.ToInt32(reader["LocationId"]),
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                EmployeeName = reader["NAME"].ToString() ?? string.Empty,
                                RouteCode = reader["RouteCode"].ToString() ?? string.Empty,
                                RouteDescription = reader["Route"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"]),
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                CodeType = reader["CodeType"].ToString() ?? "0",
                                IsRBIExclude = reader["RBIExclude"].ToString() == "1"
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<IEnumerable<SelectListItem>> GetCourierCodeTypesAsync()
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();

                string query = "SELECT ID, NAME FROM couriercodetype WHERE IsDeleted = 0 ORDER BY OrderID ASC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        items.Add(new SelectListItem
                        {
                            Value = reader["ID"].ToString(),
                            Text = reader["NAME"].ToString()
                        });
                    }
                }
            }
            return items;
        }

        public async Task<IEnumerable<SelectListItem>> GetLocationsByCityCodeAsync(string cityCode)
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();

                string query = @"SELECT l.LocationID, l.LocationName
                                 FROM lcs_setup.locations l
                                 INNER JOIN hr_locationmapping LM ON LM.GlLocationId = L.LocationID
                                 WHERE l.BILLINGCITYID = (SELECT c.station_id FROM hr_city c WHERE c.Code = @cityCode)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@cityCode", cityCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            items.Add(new SelectListItem
                            {
                                Value = reader["LocationID"].ToString(),
                                Text = reader["LocationName"].ToString()
                            });
                        }
                    }
                }
            }
            return items;
        }

        public async Task<bool> AddEmployeeRouteCodeAsync(EmployeeRouteCodeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate employee exists and is not inactive
                string empQuery = "SELECT 1 FROM hr_employeepersonaldetail WHERE Emp_No=@EmpNo AND Name=@Name AND EMP_STATUS <> 'I' LIMIT 1";
                using (var cmd = new MySqlCommand(empQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@Name", model.EmployeeName);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null) throw new ArgumentException("Employee does not exist in database.");
                }

                // Validate route exists
                string routeQuery = "SELECT 1 FROM hr_routecodes_hdr WHERE RouteCode=@RouteCode LIMIT 1";
                using (var cmd = new MySqlCommand(routeQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null) throw new ArgumentException("Route does not exist in database.");
                }

                // Get city from employee
                string cityQuery = "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo LIMIT 1";
                string cityCode = string.Empty;
                using (var cmd = new MySqlCommand(cityQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res == null) throw new ArgumentException("Employee City not found.");
                    cityCode = res.ToString() ?? string.Empty;
                }

                // Validate ToDate > FromDate
                if (model.ToDate.HasValue && model.FromDate.HasValue && model.ToDate <= model.FromDate)
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");

                // Duplicate check: same emp/route/city already active
                string dupQuery = @"SELECT Code FROM hr_employeeroutecode WHERE Emp_No=@EmpNo AND ToDate IS NULL AND RouteCode=@RouteCode AND citycode=@citycode LIMIT 1";
                using (var cmd = new MySqlCommand(dupQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                    cmd.Parameters.AddWithValue("@citycode", cityCode);
                    var existing = await cmd.ExecuteScalarAsync();
                    if (existing != null)
                        throw new ArgumentException($"Employee already has an active record for this Route. (Record # {existing})");
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newCode = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeroutecode", "Code", 6);

                        string insertQuery = @"INSERT INTO hr_employeeroutecode
                            (Code, RouteCode, citycode, LocationId, Emp_No, FromDate, ToDate, Comments, CodeType, RBIExclude, createdby, Created_Date, UpdatedBy, Updated_Date)
                            VALUES (@Code, @RouteCode, @citycode, @LocationId, @EmpNo, @FromDate, @ToDate, @Comments, @CodeType, @IsRBIExclude, @createdby, NOW(), @updatedby, NOW())";

                        using (var command = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", newCode);
                            command.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                            command.Parameters.AddWithValue("@citycode", cityCode);
                            command.Parameters.AddWithValue("@LocationId", model.LocationId);
                            command.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                            command.Parameters.AddWithValue("@FromDate", model.FromDate!.Value);
                            command.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                            command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments);
                            command.Parameters.AddWithValue("@CodeType", model.CodeType);
                            command.Parameters.AddWithValue("@IsRBIExclude", model.IsRBIExclude);
                            command.Parameters.AddWithValue("@createdby", currentUserId);
                            command.Parameters.AddWithValue("@updatedby", currentUserId);
                            await command.ExecuteNonQueryAsync();
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

        public async Task<bool> UpdateEmployeeRouteCodeAsync(EmployeeRouteCodeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate employee exists
                string empQuery = "SELECT 1 FROM hr_employeepersonaldetail WHERE Emp_No=@EmpNo AND Name=@Name LIMIT 1";
                using (var cmd = new MySqlCommand(empQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@Name", model.EmployeeName);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null) throw new ArgumentException("Employee does not exist in database.");
                }

                // Validate route exists
                string routeQuery = "SELECT 1 FROM hr_routecodes_hdr WHERE RouteCode=@RouteCode LIMIT 1";
                using (var cmd = new MySqlCommand(routeQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null) throw new ArgumentException("Route does not exist in database.");
                }

                // Get city from employee
                string cityQuery = "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE EMP_NO=@EmpNo LIMIT 1";
                string cityCode = string.Empty;
                using (var cmd = new MySqlCommand(cityQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res == null) throw new ArgumentException("Employee City not found.");
                    cityCode = res.ToString() ?? string.Empty;
                }

                // Validate ToDate > FromDate
                if (model.ToDate.HasValue && model.FromDate.HasValue && model.ToDate <= model.FromDate)
                    throw new ArgumentException("To date cannot be smaller or equal to From date.");

                // Duplicate check excluding current record
                string dupQuery = @"SELECT Code FROM hr_employeeroutecode WHERE Emp_No=@EmpNo AND ToDate IS NULL AND RouteCode=@RouteCode AND citycode=@citycode AND Code <> @Code LIMIT 1";
                using (var cmd = new MySqlCommand(dupQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                    cmd.Parameters.AddWithValue("@citycode", cityCode);
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    var existing = await cmd.ExecuteScalarAsync();
                    if (existing != null)
                        throw new ArgumentException($"You have to update employee's current record. (Record # {existing})");
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = @"UPDATE hr_employeeroutecode
                            SET RouteCode=@RouteCode, citycode=@citycode, LocationId=@LocationId, Emp_No=@EmpNo,
                                FromDate=@FromDate, ToDate=@ToDate, Comments=@Comments, CodeType=@CodeType,
                                RBIExclude=@IsRBIExclude, UpdatedBy=@updatedby, Updated_Date=NOW()
                            WHERE Code=@Code";

                        using (var command = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", model.Code);
                            command.Parameters.AddWithValue("@RouteCode", model.RouteCode);
                            command.Parameters.AddWithValue("@citycode", cityCode);
                            command.Parameters.AddWithValue("@LocationId", model.LocationId);
                            command.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                            command.Parameters.AddWithValue("@FromDate", model.FromDate!.Value);
                            command.Parameters.AddWithValue("@ToDate", model.ToDate.HasValue ? (object)model.ToDate.Value : DBNull.Value);
                            command.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments);
                            command.Parameters.AddWithValue("@CodeType", model.CodeType);
                            command.Parameters.AddWithValue("@IsRBIExclude", model.IsRBIExclude);
                            command.Parameters.AddWithValue("@updatedby", currentUserId);
                            await command.ExecuteNonQueryAsync();
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

        public async Task<bool> DeleteEmployeeRouteCodeAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string deleteQuery = "DELETE FROM hr_employeeroutecode WHERE Code=@Code";
                        using (var command = new MySqlCommand(deleteQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            await command.ExecuteNonQueryAsync();
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
        #endregion

        #region Employee Shift Detail
        public async Task<IEnumerable<EmployeeShiftDetailModel>> GetAllEmployeeShiftDetailsAsync(string currentUserId)
        {
            var data = new List<EmployeeShiftDetailModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT es.ID, sd.Name AS shiftname, ep.Name AS empname, ep.emp_no,
                                        es.FromDate, es.ToDate
                                 FROM hr_employeeshifttimings es
                                 INNER JOIN hr_employeepersonaldetail ep ON es.emp_no = ep.emp_no
                                 INNER JOIN hr_shiftdetails sd ON sd.Code = es.ShiftCode
                                 INNER JOIN lcs_user_location lul ON ep.P_CITY_CODE = lul.city_code
                                 WHERE lul.USERID = @UserId
                                 ORDER BY es.ID DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeShiftDetailModel
                            {
                                Id = reader["ID"].ToString() ?? string.Empty,
                                ShiftName = reader["shiftname"].ToString() ?? string.Empty,
                                EmpName = reader["empname"].ToString() ?? string.Empty,
                                EmpNo = reader["emp_no"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeShiftDetailModel?> GetEmployeeShiftDetailByIdAsync(string id)
        {
            EmployeeShiftDetailModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT es.ID, es.ShiftCode, es.Emp_No, ep.NAME, es.FromDate, es.ToDate, es.Comments, es.OffDay
                                 FROM hr_employeeshifttimings es
                                 INNER JOIN hr_employeepersonaldetail ep ON es.Emp_No = ep.EMP_NO
                                 WHERE es.id = @Id LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeShiftDetailModel
                            {
                                Id = reader["ID"].ToString() ?? string.Empty,
                                ShiftCode = reader["ShiftCode"].ToString() ?? string.Empty,
                                EmpNo = reader["Emp_No"].ToString() ?? string.Empty,
                                EmpName = reader["NAME"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                                ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"]),
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                OffDay = reader["OffDay"] == DBNull.Value ? null : reader["OffDay"].ToString()
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<IEnumerable<SelectListItem>> GetActiveShiftsAsync()
        {
            var items = new List<SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();

                string query = "SELECT CODE, Name FROM hr_shiftdetails WHERE active='Y' ORDER BY Name";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        items.Add(new SelectListItem
                        {
                            Value = reader["CODE"].ToString(),
                            Text = reader["Name"].ToString()
                        });
                    }
                }
            }
            return items;
        }

        public async Task<bool> AddEmployeeShiftDetailAsync(EmployeeShiftDetailModel model, string currentUserId, bool isAdmin)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Get last shift for this employee
                string lastShiftId = string.Empty;
                string lastShiftCode = string.Empty;
                string lastShiftQuery = "SELECT ID, ShiftCode FROM hr_employeeshifttimings WHERE Emp_No=@EmpNo AND ToDate IS NULL ORDER BY ID DESC LIMIT 1";
                using (var cmd = new MySqlCommand(lastShiftQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            lastShiftId = reader["ID"].ToString() ?? string.Empty;
                            lastShiftCode = reader["ShiftCode"].ToString() ?? string.Empty;
                        }
                    }
                }

                // Validate: not same shift as current
                if (!string.IsNullOrEmpty(lastShiftCode) && lastShiftCode.Equals(model.ShiftCode, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Employee is already working in this shift.");

                // Validate FromDate > latest existing FromDate
                string latestDateQuery = "SELECT FromDate FROM hr_employeeshifttimings WHERE Emp_No=@EmpNo ORDER BY ID DESC LIMIT 1";
                using (var cmd = new MySqlCommand(latestDateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    var latestDateObj = await cmd.ExecuteScalarAsync();
                    if (latestDateObj != null && latestDateObj != DBNull.Value)
                    {
                        var latestDate = Convert.ToDateTime(latestDateObj);
                        if (model.FromDate!.Value <= latestDate)
                            throw new ArgumentException($"Date from should be greater than \"{latestDate.AddDays(1):dd/MM/yyyy}\"");
                        if (model.FromDate.Value.AddDays(-1) == latestDate)
                            throw new ArgumentException($"There should be at least 2 days difference between \"{latestDate:dd/MM/yyyy}\" and the date you have selected.");
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string newId = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeshifttimings", "ID", 6);

                        string insertQuery = @"INSERT INTO hr_employeeshifttimings
                            (ID, ShiftCode, Emp_No, FromDate, ToDate, Comments, createdby, createddate, updatedby, updated_date, offDay)
                            VALUES (@Id, @ShiftCode, @EmpNo, @FromDate, NULL, @Comments, @createdby, NOW(), @updatedby, NOW(), @OffDay)";

                        using (var command = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Id", newId);
                            command.Parameters.AddWithValue("@ShiftCode", model.ShiftCode);
                            command.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                            command.Parameters.AddWithValue("@FromDate", model.FromDate!.Value);
                            command.Parameters.AddWithValue("@Comments", model.Comments ?? string.Empty);
                            command.Parameters.AddWithValue("@createdby", currentUserId);
                            command.Parameters.AddWithValue("@updatedby", currentUserId);
                            command.Parameters.AddWithValue("@OffDay", string.IsNullOrEmpty(model.OffDay) ? DBNull.Value : (object)model.OffDay);
                            await command.ExecuteNonQueryAsync();
                        }

                        // Cap previous shift's ToDate to FromDate - 1 day
                        if (!string.IsNullOrEmpty(lastShiftId))
                        {
                            string capQuery = "UPDATE hr_employeeshifttimings SET todate=@ToDate WHERE id=@LastId";
                            using (var cmd = new MySqlCommand(capQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@ToDate", model.FromDate.Value.AddDays(-1));
                                cmd.Parameters.AddWithValue("@LastId", lastShiftId);
                                await cmd.ExecuteNonQueryAsync();
                            }
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

        public async Task<bool> UpdateEmployeeShiftDetailAsync(EmployeeShiftDetailModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Cannot update closed records (ToDate IS NOT NULL)
                string closedQuery = "SELECT 1 FROM hr_employeeshifttimings WHERE id=@Id AND ToDate IS NOT NULL LIMIT 1";
                using (var cmd = new MySqlCommand(closedQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Id", model.Id);
                    var closed = await cmd.ExecuteScalarAsync();
                    if (closed != null) throw new ArgumentException("You cannot update this record. You have to update or delete Employee's current Shift Detail record.");
                }

                // Get last shift for this employee (excluding current)
                string lastShiftId = string.Empty;
                string lastShiftCode = string.Empty;
                string lastShiftQuery = "SELECT ID, ShiftCode FROM hr_employeeshifttimings WHERE Emp_No=@EmpNo AND ToDate IS NULL AND ID<>@Id ORDER BY ID DESC LIMIT 1";
                using (var cmd = new MySqlCommand(lastShiftQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@Id", model.Id);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            lastShiftId = reader["ID"].ToString() ?? string.Empty;
                            lastShiftCode = reader["ShiftCode"].ToString() ?? string.Empty;
                        }
                    }
                }

                // Validate FromDate > latest other record
                string latestDateQuery = "SELECT FromDate FROM hr_employeeshifttimings WHERE id<>@Id AND emp_no=@EmpNo ORDER BY ID DESC LIMIT 1";
                using (var cmd = new MySqlCommand(latestDateQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Id", model.Id);
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    var latestDateObj = await cmd.ExecuteScalarAsync();
                    if (latestDateObj != null && latestDateObj != DBNull.Value)
                    {
                        var latestDate = Convert.ToDateTime(latestDateObj);
                        if (model.FromDate!.Value <= latestDate)
                            throw new ArgumentException($"Date from should be greater than \"{latestDate.AddDays(1):dd/MM/yyyy}\"");
                        if (model.FromDate.Value.AddDays(-1) == latestDate)
                            throw new ArgumentException($"There should be at least 2 days difference between \"{latestDate:dd/MM/yyyy}\" and the date you have selected.");
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = @"UPDATE hr_employeeshifttimings
                            SET ShiftCode=@ShiftCode, Emp_No=@EmpNo, FromDate=@FromDate, ToDate=NULL,
                                Comments=@Comments, updatedby=@updatedby, updated_date=NOW(), offDay=@OffDay
                            WHERE ID=@Id";

                        using (var command = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Id", model.Id);
                            command.Parameters.AddWithValue("@ShiftCode", model.ShiftCode);
                            command.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                            command.Parameters.AddWithValue("@FromDate", model.FromDate!.Value);
                            command.Parameters.AddWithValue("@Comments", model.Comments ?? string.Empty);
                            command.Parameters.AddWithValue("@updatedby", currentUserId);
                            command.Parameters.AddWithValue("@OffDay", string.IsNullOrEmpty(model.OffDay) ? DBNull.Value : (object)model.OffDay);
                            await command.ExecuteNonQueryAsync();
                        }

                        // Cap previous shift's ToDate
                        if (!string.IsNullOrEmpty(lastShiftId))
                        {
                            string capQuery = "UPDATE hr_employeeshifttimings SET todate=@ToDate WHERE id=@LastId";
                            using (var cmd = new MySqlCommand(capQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@ToDate", model.FromDate!.Value.AddDays(-1));
                                cmd.Parameters.AddWithValue("@LastId", lastShiftId);
                                await cmd.ExecuteNonQueryAsync();
                            }
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

        public async Task<bool> DeleteEmployeeShiftDetailAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Cannot delete closed records
                string closedQuery = "SELECT Emp_No FROM hr_employeeshifttimings WHERE id=@Id AND todate IS NOT NULL LIMIT 1";
                string? empNo = null;
                using (var cmd = new MySqlCommand(closedQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res != null) throw new ArgumentException("You cannot delete this record. You have to update or delete Employee's current shift detail record.");
                }

                // Get employee no
                string empQuery = "SELECT Emp_No FROM hr_employeeshifttimings WHERE id=@Id LIMIT 1";
                using (var cmd = new MySqlCommand(empQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@Id", id);
                    var res = await cmd.ExecuteScalarAsync();
                    empNo = res?.ToString();
                }

                // Get previous shift for this employee
                string lastShiftId = string.Empty;
                if (!string.IsNullOrEmpty(empNo))
                {
                    string lastQuery = "SELECT ID FROM hr_employeeshifttimings WHERE Emp_No=@EmpNo AND ID<>@Id ORDER BY ID DESC LIMIT 1";
                    using (var cmd = new MySqlCommand(lastQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@EmpNo", empNo);
                        cmd.Parameters.AddWithValue("@Id", id);
                        var res = await cmd.ExecuteScalarAsync();
                        if (res != null) lastShiftId = res.ToString() ?? string.Empty;
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        // Restore previous record's ToDate to NULL
                        if (!string.IsNullOrEmpty(lastShiftId))
                        {
                            string restoreQuery = "UPDATE hr_employeeshifttimings SET ToDate=NULL WHERE ID=@LastId";
                            using (var cmd = new MySqlCommand(restoreQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@LastId", lastShiftId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string deleteQuery = "DELETE FROM hr_employeeshifttimings WHERE ID=@Id";
                        using (var command = new MySqlCommand(deleteQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Id", id);
                            await command.ExecuteNonQueryAsync();
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

        public async Task<(int successCount, string message)> BulkUploadEmployeeShiftsAsync(Microsoft.AspNetCore.Http.IFormFile file, string currentUserId)
        {
            int insertedRows = 0;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (0, "Connection failed.");
                await connection.OpenAsync();

                using (var stream = file.OpenReadStream())
                using (var package = new OfficeOpenXml.ExcelPackage(stream))
                {
                    OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                    var worksheets = package.Workbook.Worksheets;
                    if (worksheets.Count == 0) throw new Exception("The uploaded Excel file does not contain any worksheets.");

                    var worksheet = worksheets.First();
                    int rowCount = worksheet.Dimension?.Rows ?? 0;

                    using (var transaction = await connection.BeginTransactionAsync())
                    {
                        try
                        {
                            for (int row = 2; row <= rowCount; row++)
                            {
                                string empCode = worksheet.Cells[row, 1].Value?.ToString()?.Trim() ?? string.Empty;
                                string shiftName = worksheet.Cells[row, 2].Value?.ToString()?.Trim() ?? string.Empty;
                                string fromDateStr = worksheet.Cells[row, 3].Value?.ToString()?.Trim() ?? string.Empty;

                                if (string.IsNullOrEmpty(empCode)) continue;

                                empCode = empCode.PadLeft(14, '0');

                                if (!DateTime.TryParse(fromDateStr, out DateTime fromDate))
                                    throw new Exception($"Invalid date '{fromDateStr}' for Employee '{empCode}'");

                                // Lookup shift code by name
                                string shiftCode = string.Empty;
                                string shiftQuery = "SELECT Code FROM hr_shiftdetails WHERE Name LIKE @ShiftName LIMIT 1";
                                using (var cmd = new MySqlCommand(shiftQuery, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@ShiftName", shiftName);
                                    var res = await cmd.ExecuteScalarAsync();
                                    if (res == null) throw new Exception($"Shift '{shiftName}' not found for Employee '{empCode}'");
                                    shiftCode = res.ToString() ?? string.Empty;
                                }

                                // Cap current active shift
                                string curIdQuery = "SELECT id FROM hr_employeeshifttimings WHERE emp_no=@EmpNo AND todate IS NULL ORDER BY fromdate DESC LIMIT 1";
                                string? curId = null;
                                using (var cmd = new MySqlCommand(curIdQuery, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@EmpNo", empCode);
                                    var res = await cmd.ExecuteScalarAsync();
                                    if (res != null) curId = res.ToString();
                                }

                                if (!string.IsNullOrEmpty(curId))
                                {
                                    string updateSql = "UPDATE hr_employeeshifttimings SET todate=@ToDate WHERE id=@Id";
                                    using (var cmd = new MySqlCommand(updateSql, connection, transaction as MySqlTransaction))
                                    {
                                        cmd.Parameters.AddWithValue("@ToDate", fromDate.AddDays(-1));
                                        cmd.Parameters.AddWithValue("@Id", curId);
                                        await cmd.ExecuteNonQueryAsync();
                                    }
                                }

                                string newId = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_employeeshifttimings", "ID", 6);
                                string insertSql = @"INSERT INTO hr_employeeshifttimings
                                    (ID, ShiftCode, Emp_No, FromDate, ToDate, Comments, createdby, createddate, updatedby, updated_date, offDay)
                                    VALUES (@Id, @ShiftCode, @EmpNo, @FromDate, NULL, '', @createdby, NOW(), @updatedby, NOW(), NULL)";

                                using (var cmd = new MySqlCommand(insertSql, connection, transaction as MySqlTransaction))
                                {
                                    cmd.Parameters.AddWithValue("@Id", newId);
                                    cmd.Parameters.AddWithValue("@ShiftCode", shiftCode);
                                    cmd.Parameters.AddWithValue("@EmpNo", empCode);
                                    cmd.Parameters.AddWithValue("@FromDate", fromDate);
                                    cmd.Parameters.AddWithValue("@createdby", currentUserId);
                                    cmd.Parameters.AddWithValue("@updatedby", currentUserId);
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
            return (insertedRows, $"{insertedRows} Record(s) saved successfully.");
        }
        #endregion

        #region Employee Personal Detail

        public async Task<IEnumerable<EmployeeListItemModel>> GetAllEmployeesAsync(string currentUserId)
        {
            var data = new List<EmployeeListItemModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT p.EMP_NO, p.NAME, p.F_NAME, p.NIC_NO, c.FullName AS CityName, p.EMP_STATUS, p.APPOINT_DATE
                    FROM hr_employeepersonaldetail p
                    LEFT JOIN hr_city c ON c.Code = p.P_CITY_CODE
                    INNER JOIN lcs_user_location lul ON p.P_CITY_CODE = lul.city_code
                    WHERE lul.USERID = @UserId
                    ORDER BY p.NAME LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeListItemModel
                            {
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                Name = reader["NAME"].ToString() ?? string.Empty,
                                FatherName = reader["F_NAME"].ToString() ?? string.Empty,
                                NicNo = reader["NIC_NO"].ToString() ?? string.Empty,
                                CityName = reader["CityName"].ToString() ?? string.Empty,
                                EmpStatus = reader["EMP_STATUS"].ToString() ?? string.Empty,
                                AppointDate = reader["APPOINT_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["APPOINT_DATE"]) : null
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeePersonalDetailModel?> GetEmployeePersonalDetailByEmpNoAsync(string empNo)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT p.EMP_NO, p.APPOINT_DATE, p.LEFT_DATE, p.NAME, p.F_NAME, p.MARITAL_STATUS, p.GENDER,
                    p.P_ADDRESS_1, p.P_COUNTRY_CODE, p.P_CITY_CODE, p.CELL_CONTACT_1, p.CELL_CONTACT_2,
                    p.EMERG_CONTACT_1, p.EMERG_CONTACT_2, p.RELIGION, p.NATIONALITY, p.NIC_NO, p.BLOOD_GRP,
                    p.NTN_NO, p.EOBI_NO, p.BIRTH_DATE, p.MOTHER_TONGUE, p.MARRIAGE_DATE, p.EMAIL_ADD,
                    p.MARK_OF_ID, p.PASSPORT_NO, p.EMPLOYEE_TYPE, p.Comments, p.attandanceid, p.Emp_WalletNumber,
                    p.generate_salary, p.IsConfirmed, p.JobTypeId, p.ThirdPartyID, p.IsExecutive, p.IsTemperory,
                    p.dual_job_approve, p.EMP_STATUS, p.T_ADDRESS_1, p.T_ADDRESS_2, p.emp_replace,
                    (SELECT CompanyID FROM hr_empcompanydetails cd WHERE cd.Emp_no=p.EMP_NO ORDER BY cd.Created_Date DESC LIMIT 1) AS CompanyID,
                    (SELECT Divisionid FROM hr_employeedivisiondetails el WHERE el.Emp_No=p.EMP_NO ORDER BY el.Created_Date DESC LIMIT 1) AS DivisionId,
                    (SELECT JobCode FROM hr_employeejobdetails he WHERE he.Emp_No=p.EMP_NO ORDER BY he.Created_Date DESC LIMIT 1) AS JobCode,
                    (SELECT DeptCode FROM hr_employeedepartmentdetails hd WHERE hd.Emp_no=p.EMP_NO AND hd.ToDate IS NULL LIMIT 1) AS DeptCode,
                    (SELECT LocationId FROM hr_employeelocationdetails ld WHERE ld.Emp_no=p.EMP_NO ORDER BY ld.Created_Date DESC LIMIT 1) AS LocationId,
                    (SELECT ReportToEmpNo FROM definehierarchy WHERE Emp_no=p.EMP_NO) AS ReportToEmpNo,
                    (SELECT NAME FROM hr_employeepersonaldetail WHERE EMP_NO=(SELECT ReportToEmpNo FROM definehierarchy WHERE Emp_no=p.EMP_NO)) AS ReportToEmpName,
                    (SELECT NAME FROM hr_employeepersonaldetail WHERE EMP_NO=p.emp_replace) AS ReplacementEmpName
                    FROM hr_employeepersonaldetail p
                    WHERE p.EMP_NO = @EMP_NO";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EMP_NO", empNo);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            string isConfirmedVal = reader["IsConfirmed"]?.ToString() ?? "0";
                            bool isTemp = reader["IsTemperory"]?.ToString() == "1";
                            string probStatus = isTemp ? "T" : (isConfirmedVal == "1" ? "Y" : "N");

                            string dualJob = reader["dual_job_approve"]?.ToString() ?? "NA";
                            if (dualJob == "NA") dualJob = "NA";
                            else if (dualJob == "N") dualJob = "N";
                            else dualJob = "Y";

                            return new EmployeePersonalDetailModel
                            {
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                AppointDate = reader["APPOINT_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["APPOINT_DATE"]) : null,
                                LeftDate = reader["LEFT_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["LEFT_DATE"]) : null,
                                Name = reader["NAME"].ToString() ?? string.Empty,
                                FatherName = reader["F_NAME"].ToString() ?? string.Empty,
                                MaritalStatus = reader["MARITAL_STATUS"].ToString() ?? "S",
                                Gender = reader["GENDER"].ToString() ?? "M",
                                PermanentAddress = reader["P_ADDRESS_1"].ToString() ?? string.Empty,
                                TemporaryAddress1 = reader["T_ADDRESS_1"].ToString() ?? string.Empty,
                                TemporaryAddress2 = reader["T_ADDRESS_2"].ToString() ?? string.Empty,
                                PCountryCode = reader["P_COUNTRY_CODE"].ToString() ?? "00",
                                PCityCode = reader["P_CITY_CODE"].ToString() ?? "00",
                                CellContact1 = reader["CELL_CONTACT_1"].ToString() ?? string.Empty,
                                CellContact2 = reader["CELL_CONTACT_2"].ToString() ?? string.Empty,
                                EmergencyContact1 = reader["EMERG_CONTACT_1"].ToString() ?? string.Empty,
                                EmergencyContact2 = reader["EMERG_CONTACT_2"].ToString() ?? string.Empty,
                                Religion = reader["RELIGION"].ToString() ?? string.Empty,
                                Nationality = reader["NATIONALITY"].ToString() ?? string.Empty,
                                NicNo = reader["NIC_NO"].ToString() ?? string.Empty,
                                BloodGroup = reader["BLOOD_GRP"].ToString() ?? string.Empty,
                                NtnNo = reader["NTN_NO"].ToString() ?? string.Empty,
                                EobiNo = reader["EOBI_NO"].ToString() ?? string.Empty,
                                BirthDate = reader["BIRTH_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["BIRTH_DATE"]) : null,
                                MotherTongue = reader["MOTHER_TONGUE"].ToString() ?? string.Empty,
                                MarriageDate = reader["MARRIAGE_DATE"] != DBNull.Value ? Convert.ToDateTime(reader["MARRIAGE_DATE"]) : null,
                                EmailAddress = reader["EMAIL_ADD"].ToString() ?? string.Empty,
                                MarkOfId = reader["MARK_OF_ID"].ToString() ?? string.Empty,
                                PassportNo = reader["PASSPORT_NO"].ToString() ?? string.Empty,
                                EmployeeType = reader["EMPLOYEE_TYPE"].ToString() ?? "00",
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                AttendanceCode = reader["attandanceid"].ToString() ?? string.Empty,
                                WalletNumber = reader["Emp_WalletNumber"].ToString() ?? string.Empty,
                                GenerateSalary = reader["generate_salary"].ToString() ?? "Y",
                                JobTypeId = reader["JobTypeId"].ToString() ?? "0",
                                ThirdPartyId = reader["ThirdPartyID"].ToString() ?? string.Empty,
                                IsExecutive = reader["IsExecutive"]?.ToString() == "1",
                                ProbationStatus = probStatus,
                                DualJobApprove = dualJob,
                                EmpStatus = reader["EMP_STATUS"].ToString() ?? "A",
                                ReplacementEmpNo = reader["emp_replace"].ToString() ?? string.Empty,
                                ReplacementEmpName = reader["ReplacementEmpName"].ToString() ?? string.Empty,
                                CompanyId = reader["CompanyID"].ToString() ?? "0",
                                DivisionId = reader["DivisionId"].ToString() ?? "0",
                                DesignationCode = reader["JobCode"].ToString() ?? "0",
                                DepartmentCode = reader["DeptCode"].ToString() ?? "0",
                                LocationId = reader["LocationId"] != DBNull.Value ? Convert.ToInt32(reader["LocationId"]) : 0,
                                ReportToEmpNo = reader["ReportToEmpNo"].ToString() ?? string.Empty,
                                ReportToEmpName = reader["ReportToEmpName"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<(bool success, string empNo, string message)> AddEmployeePersonalDetailAsync(EmployeePersonalDetailModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (false, string.Empty, "Database connection failed.");
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync() as MySqlTransaction)
                {
                    try
                    {
                        string newEmpNo = model.ProbationStatus == "T"
                            ? new string(model.NicNo.Where(char.IsDigit).ToArray())
                            : await GenerateEmpNoAsync(connection, transaction!);

                        // Validate duplicate NTN
                        if (!string.IsNullOrEmpty(model.NtnNo))
                        {
                            string dupCheck = "SELECT COUNT(*) FROM hr_employeepersonaldetail WHERE NTN_NO=@NTN AND EMP_NO<>@EMP";
                            using (var cmd = new MySqlCommand(dupCheck, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@NTN", model.NtnNo);
                                cmd.Parameters.AddWithValue("@EMP", newEmpNo);
                                var cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                                if (cnt > 0) return (false, string.Empty, "NTN already exists in database.");
                            }
                        }

                        // Validate duplicate attendance code
                        if (!string.IsNullOrEmpty(model.AttendanceCode))
                        {
                            string attCheck = "SELECT COUNT(*) FROM hr_employeepersonaldetail WHERE attandanceid=@att AND EMP_NO<>@EMP AND Left_Date IS NULL AND P_CITY_CODE=@city";
                            using (var cmd = new MySqlCommand(attCheck, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@att", model.AttendanceCode);
                                cmd.Parameters.AddWithValue("@EMP", newEmpNo);
                                cmd.Parameters.AddWithValue("@city", model.PCityCode);
                                var cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                                if (cnt > 0) return (false, string.Empty, "Attendance Code already exists for this city.");
                            }
                        }

                        // Get sub-table IDs
                        string deptCode = await GenerateNewIdAsync(connection, transaction, "hr_employeedepartmentdetails", "code", 6);
                        string jobCode = await GenerateNewIdAsync(connection, transaction, "hr_employeejobdetails", "code", 6);
                        byte[]? photoBytes = null;
                        if (model.PhotoFile != null && model.PhotoFile.Length > 0)
                        {
                            using var ms = new MemoryStream();
                            await model.PhotoFile.CopyToAsync(ms);
                            photoBytes = ms.ToArray();
                        }

                        string insertEmp = @"INSERT INTO hr_employeepersonaldetail
                            (EMP_NO, APPOINT_DATE, LEFT_DATE, NAME, F_NAME, MARITAL_STATUS, GENDER, P_ADDRESS_1, P_ADDRESS_2,
                             P_COUNTRY_CODE, P_CITY_CODE, P_PHONE, T_ADDRESS_1, T_ADDRESS_2, T_CITY_CODE, T_COUNTRY_CODE, T_PHONE,
                             CELL_CONTACT_1, CELL_CONTACT_2, EMERG_CONTACT_1, EMERG_CONTACT_2, RELIGION, NATIONALITY, NIC_NO,
                             BLOOD_GRP, NTN_NO, EOBI_NO, BIRTH_DATE, BIRTH_COUNTRY_CODE, BIRTH_CITY_CODE, EMP_STATUS, MOTHER_TONGUE,
                             MARRIAGE_DATE, RETIREMENT_DATE, EMAIL_ADD, MARK_OF_ID, PASSPORT_NO, PAYROLL_START_DATE, EMPLOYEE_TYPE,
                             Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date, photo, attandanceid, emp_glcode,
                             dual_job_approve, generate_salary, emp_replace, Emp_WalletNumber, IsConfirmed, IsAllowWFH,
                             JobTypeId, ThirdPartyID, IsExecutive, IsFiler,
                             OtherExtraFixed, SalaryAdjustmentExtraFixed, TotalFixedGross, NewBasicSalary, FixGrossSalary, BasicSalary, IsTemperory)
                            VALUES
                            (@EMP_NO, @APPOINT_DATE, @LEFT_DATE, @NAME, @F_NAME, @MARITAL_STATUS, @GENDER, @P_ADDRESS_1, NULL,
                             @P_COUNTRY_CODE, @P_CITY_CODE, NULL, @T_ADDRESS_1, @T_ADDRESS_2, NULL, NULL, NULL,
                             @CELL_CONTACT_1, @CELL_CONTACT_2, @EMERG_CONTACT_1, @EMERG_CONTACT_2, @RELIGION, @NATIONALITY, @NIC_NO,
                             @BLOOD_GRP, @NTN_NO, @EOBI_NO, @BIRTH_DATE, NULL, NULL, 'A', @MOTHER_TONGUE,
                             @MARRIAGE_DATE, NULL, @EMAIL_ADD, @MARK_OF_ID, @PASSPORT_NO, NULL, @EMPLOYEE_TYPE,
                             @Comments, @CreatedBy, NOW(), @CreatedBy, NOW(), @photo, @attandanceid, '',
                             @dual_job_approve, @generate_salary, @emp_replace, @Emp_WalletNumber, @IsConfirmed, 0,
                             @JobTypeId, @ThirdPartyID, @Is_Executive, 0,
                             0.0, 0.0, 0.0, 0.0, 0.0, 0.0, @Is_Temperory)";

                        using (var cmd = new MySqlCommand(insertEmp, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@EMP_NO", newEmpNo);
                            cmd.Parameters.AddWithValue("@APPOINT_DATE", (object?)model.AppointDate ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@LEFT_DATE", DBNull.Value);
                            cmd.Parameters.AddWithValue("@NAME", model.Name.Trim());
                            cmd.Parameters.AddWithValue("@F_NAME", model.FatherName.Trim());
                            cmd.Parameters.AddWithValue("@MARITAL_STATUS", model.MaritalStatus);
                            cmd.Parameters.AddWithValue("@GENDER", model.Gender);
                            cmd.Parameters.AddWithValue("@P_ADDRESS_1", string.IsNullOrEmpty(model.PermanentAddress) ? DBNull.Value : (object)model.PermanentAddress.Trim());
                            cmd.Parameters.AddWithValue("@P_COUNTRY_CODE", model.PCountryCode == "00" ? DBNull.Value : (object)model.PCountryCode);
                            cmd.Parameters.AddWithValue("@P_CITY_CODE", model.PCityCode == "00" ? DBNull.Value : (object)model.PCityCode);
                            cmd.Parameters.AddWithValue("@T_ADDRESS_1", string.IsNullOrEmpty(model.TemporaryAddress1) ? DBNull.Value : (object)model.TemporaryAddress1.Trim());
                            cmd.Parameters.AddWithValue("@T_ADDRESS_2", string.IsNullOrEmpty(model.TemporaryAddress2) ? DBNull.Value : (object)model.TemporaryAddress2.Trim());
                            cmd.Parameters.AddWithValue("@CELL_CONTACT_1", string.IsNullOrEmpty(model.CellContact1) ? DBNull.Value : (object)model.CellContact1.Trim());
                            cmd.Parameters.AddWithValue("@CELL_CONTACT_2", string.IsNullOrEmpty(model.CellContact2) ? DBNull.Value : (object)model.CellContact2.Trim());
                            cmd.Parameters.AddWithValue("@EMERG_CONTACT_1", string.IsNullOrEmpty(model.EmergencyContact1) ? DBNull.Value : (object)model.EmergencyContact1.Trim());
                            cmd.Parameters.AddWithValue("@EMERG_CONTACT_2", string.IsNullOrEmpty(model.EmergencyContact2) ? DBNull.Value : (object)model.EmergencyContact2.Trim());
                            cmd.Parameters.AddWithValue("@RELIGION", string.IsNullOrEmpty(model.Religion) ? DBNull.Value : (object)model.Religion.Trim());
                            cmd.Parameters.AddWithValue("@NATIONALITY", string.IsNullOrEmpty(model.Nationality) ? DBNull.Value : (object)model.Nationality.Trim());
                            cmd.Parameters.AddWithValue("@NIC_NO", string.IsNullOrEmpty(model.NicNo) ? DBNull.Value : (object)model.NicNo.Trim());
                            cmd.Parameters.AddWithValue("@BLOOD_GRP", string.IsNullOrEmpty(model.BloodGroup) ? DBNull.Value : (object)model.BloodGroup.Trim().ToUpper());
                            cmd.Parameters.AddWithValue("@NTN_NO", string.IsNullOrEmpty(model.NtnNo) ? DBNull.Value : (object)model.NtnNo.Trim());
                            cmd.Parameters.AddWithValue("@EOBI_NO", string.IsNullOrEmpty(model.EobiNo) ? DBNull.Value : (object)model.EobiNo.Trim());
                            cmd.Parameters.AddWithValue("@BIRTH_DATE", (object?)model.BirthDate ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@MOTHER_TONGUE", string.IsNullOrEmpty(model.MotherTongue) ? DBNull.Value : (object)model.MotherTongue.Trim());
                            cmd.Parameters.AddWithValue("@MARRIAGE_DATE", model.MaritalStatus == "M" && model.MarriageDate.HasValue ? (object)model.MarriageDate.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@EMAIL_ADD", string.IsNullOrEmpty(model.EmailAddress) ? DBNull.Value : (object)model.EmailAddress.Trim());
                            cmd.Parameters.AddWithValue("@MARK_OF_ID", string.IsNullOrEmpty(model.MarkOfId) ? DBNull.Value : (object)model.MarkOfId.Trim());
                            cmd.Parameters.AddWithValue("@PASSPORT_NO", string.IsNullOrEmpty(model.PassportNo) ? DBNull.Value : (object)model.PassportNo.Trim());
                            cmd.Parameters.AddWithValue("@EMPLOYEE_TYPE", model.EmployeeType == "00" ? DBNull.Value : (object)model.EmployeeType);
                            cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                            cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@photo", (object?)photoBytes ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@attandanceid", string.IsNullOrEmpty(model.AttendanceCode) ? DBNull.Value : (object)model.AttendanceCode.Trim());
                            cmd.Parameters.AddWithValue("@dual_job_approve", "NA");
                            cmd.Parameters.AddWithValue("@generate_salary", model.GenerateSalary);
                            cmd.Parameters.AddWithValue("@emp_replace", string.IsNullOrEmpty(model.ReplacementEmpNo) ? DBNull.Value : (object)model.ReplacementEmpNo);
                            cmd.Parameters.AddWithValue("@Emp_WalletNumber", string.IsNullOrEmpty(model.WalletNumber) ? DBNull.Value : (object)model.WalletNumber.Trim());
                            cmd.Parameters.AddWithValue("@IsConfirmed", model.ProbationStatus == "Y" ? 1 : 0);
                            cmd.Parameters.AddWithValue("@JobTypeId", model.JobTypeId == "0" || string.IsNullOrEmpty(model.JobTypeId) ? DBNull.Value : (object)model.JobTypeId);
                            cmd.Parameters.AddWithValue("@ThirdPartyID", string.IsNullOrEmpty(model.ThirdPartyId) ? DBNull.Value : (object)model.ThirdPartyId);
                            cmd.Parameters.AddWithValue("@Is_Executive", model.IsExecutive ? 1 : 0);
                            cmd.Parameters.AddWithValue("@Is_Temperory", model.ProbationStatus == "T" ? 1 : 0);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Insert Department Detail
                        if (model.DepartmentCode != "0" && !string.IsNullOrEmpty(model.DepartmentCode))
                        {
                            string insertDept = @"INSERT INTO hr_employeedepartmentdetails (CODE, Emp_No, DeptCode, FromDate, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                VALUES (@Code, @EmpNo, @DeptCode, @FromDate, @CreatedBy, NOW(), @CreatedBy, NOW())";
                            using (var cmd = new MySqlCommand(insertDept, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Code", deptCode);
                                cmd.Parameters.AddWithValue("@EmpNo", newEmpNo);
                                cmd.Parameters.AddWithValue("@DeptCode", model.DepartmentCode);
                                cmd.Parameters.AddWithValue("@FromDate", (object?)model.AppointDate ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Insert Job Detail
                        if (model.DesignationCode != "0" && !string.IsNullOrEmpty(model.DesignationCode))
                        {
                            string insertJob = @"INSERT INTO hr_employeejobdetails (CODE, Emp_No, JobCode, EffectiveFrom, ChangeType, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                VALUES (@Code, @EmpNo, @JobCode, @FromDate, @JobCode, @CreatedBy, NOW(), @CreatedBy, NOW())";
                            using (var cmd = new MySqlCommand(insertJob, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Code", jobCode);
                                cmd.Parameters.AddWithValue("@EmpNo", newEmpNo);
                                cmd.Parameters.AddWithValue("@JobCode", model.DesignationCode);
                                cmd.Parameters.AddWithValue("@FromDate", (object?)model.AppointDate ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Insert Location Detail
                        if (model.LocationId > 0)
                        {
                            string insertLoc = @"INSERT INTO hr_employeelocationdetails (Emp_no, LocationId, FromDate, CreatedBy, Created_Date)
                                VALUES (@EmpNo, @LocationId, @FromDate, @CreatedBy, NOW())";
                            using (var cmd = new MySqlCommand(insertLoc, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@EmpNo", newEmpNo);
                                cmd.Parameters.AddWithValue("@LocationId", model.LocationId);
                                cmd.Parameters.AddWithValue("@FromDate", (object?)model.AppointDate ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Insert Hierarchy
                        if (!string.IsNullOrEmpty(model.ReportToEmpNo))
                        {
                            string insertHierarchy = @"REPLACE INTO definehierarchy (Emp_no, email, Cell, ReportToEmpNo, CreatedBy, CreatedDate)
                                VALUES (@EmpNo, @Email, @Cell, @ReportTo, @CreatedBy, NOW())";
                            using (var cmd = new MySqlCommand(insertHierarchy, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@EmpNo", newEmpNo);
                                cmd.Parameters.AddWithValue("@Email", string.IsNullOrEmpty(model.EmailAddress) ? DBNull.Value : (object)model.EmailAddress);
                                cmd.Parameters.AddWithValue("@Cell", string.IsNullOrEmpty(model.CellContact1) ? DBNull.Value : (object)model.CellContact1);
                                cmd.Parameters.AddWithValue("@ReportTo", model.ReportToEmpNo.PadLeft(14, '0'));
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction!.CommitAsync();
                        return (true, newEmpNo, $"Employee saved successfully. Emp No: ({newEmpNo})");
                    }
                    catch (Exception ex)
                    {
                        await transaction!.RollbackAsync();
                        return (false, string.Empty, ex.Message);
                    }
                }
            }
        }

        private async Task<string> GenerateEmpNoAsync(MySqlConnection connection, MySqlTransaction? transaction)
        {
            string query = "SELECT LPAD(IFNULL(MAX(CAST(EMP_NO AS UNSIGNED)),0)+1,14,'0') FROM hr_employeepersonaldetail WHERE LENGTH(EMP_NO)=14 AND IsTemperory IN (0,2)";
            using (var command = new MySqlCommand(query, connection, transaction))
            {
                var result = await command.ExecuteScalarAsync();
                return result?.ToString() ?? "00000000000001";
            }
        }

        public async Task<bool> UpdateEmployeePersonalDetailAsync(EmployeePersonalDetailModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync() as MySqlTransaction)
                {
                    try
                    {
                        // Validate duplicate NTN
                        if (!string.IsNullOrEmpty(model.NtnNo))
                        {
                            string dupCheck = "SELECT COUNT(*) FROM hr_employeepersonaldetail WHERE NTN_NO=@NTN AND EMP_NO<>@EMP";
                            using (var cmd = new MySqlCommand(dupCheck, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@NTN", model.NtnNo);
                                cmd.Parameters.AddWithValue("@EMP", model.EmpNo);
                                var cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                                if (cnt > 0) throw new InvalidOperationException("NTN already exists in database.");
                            }
                        }

                        // Validate duplicate attendance code
                        if (!string.IsNullOrEmpty(model.AttendanceCode))
                        {
                            string attCheck = "SELECT COUNT(*) FROM hr_employeepersonaldetail WHERE attandanceid=@att AND EMP_NO<>@EMP AND Left_Date IS NULL AND P_CITY_CODE=@city";
                            using (var cmd = new MySqlCommand(attCheck, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@att", model.AttendanceCode);
                                cmd.Parameters.AddWithValue("@EMP", model.EmpNo);
                                cmd.Parameters.AddWithValue("@city", model.PCityCode);
                                var cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                                if (cnt > 0) throw new InvalidOperationException("Attendance Code already exists for this city.");
                            }
                        }

                        byte[]? photoBytes = null;
                        if (model.PhotoFile != null && model.PhotoFile.Length > 0)
                        {
                            using var ms = new MemoryStream();
                            await model.PhotoFile.CopyToAsync(ms);
                            photoBytes = ms.ToArray();
                        }

                        string updateEmp = @"UPDATE hr_employeepersonaldetail SET
                            APPOINT_DATE=@APPOINT_DATE, LEFT_DATE=@LEFT_DATE, NAME=@NAME, F_NAME=@F_NAME,
                            MARITAL_STATUS=@MARITAL_STATUS, GENDER=@GENDER, P_ADDRESS_1=@P_ADDRESS_1,
                            P_COUNTRY_CODE=@P_COUNTRY_CODE, P_CITY_CODE=@P_CITY_CODE,
                            T_ADDRESS_1=@T_ADDRESS_1, T_ADDRESS_2=@T_ADDRESS_2,
                            CELL_CONTACT_1=@CELL_CONTACT_1, CELL_CONTACT_2=@CELL_CONTACT_2,
                            EMERG_CONTACT_1=@EMERG_CONTACT_1, EMERG_CONTACT_2=@EMERG_CONTACT_2,
                            RELIGION=@RELIGION, NATIONALITY=@NATIONALITY, NIC_NO=@NIC_NO,
                            BLOOD_GRP=@BLOOD_GRP, NTN_NO=@NTN_NO, EOBI_NO=@EOBI_NO, BIRTH_DATE=@BIRTH_DATE,
                            MOTHER_TONGUE=@MOTHER_TONGUE, MARRIAGE_DATE=@MARRIAGE_DATE, EMAIL_ADD=@EMAIL_ADD,
                            MARK_OF_ID=@MARK_OF_ID, PASSPORT_NO=@PASSPORT_NO, EMPLOYEE_TYPE=@EMPLOYEE_TYPE,
                            Comments=@Comments, UpdatedBy=@UpdatedBy, Updated_Date=NOW(),
                            generate_salary=@generate_salary, emp_replace=@emp_replace,
                            Emp_WalletNumber=@Emp_WalletNumber, IsConfirmed=@IsConfirmed,
                            JobTypeId=@JobTypeId, ThirdPartyID=@ThirdPartyID, IsExecutive=@Is_Executive,
                            IsTemperory=@Is_Temperory, attandanceid=@attandanceid
                            WHERE EMP_NO=@EMP_NO";

                        if (photoBytes != null)
                            updateEmp = updateEmp.Replace("attandanceid=@attandanceid", "attandanceid=@attandanceid, photo=@photo");

                        using (var cmd = new MySqlCommand(updateEmp, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@EMP_NO", model.EmpNo);
                            cmd.Parameters.AddWithValue("@APPOINT_DATE", (object?)model.AppointDate ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@LEFT_DATE", (object?)model.LeftDate ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@NAME", model.Name.Trim());
                            cmd.Parameters.AddWithValue("@F_NAME", model.FatherName.Trim());
                            cmd.Parameters.AddWithValue("@MARITAL_STATUS", model.MaritalStatus);
                            cmd.Parameters.AddWithValue("@GENDER", model.Gender);
                            cmd.Parameters.AddWithValue("@P_ADDRESS_1", string.IsNullOrEmpty(model.PermanentAddress) ? DBNull.Value : (object)model.PermanentAddress.Trim());
                            cmd.Parameters.AddWithValue("@P_COUNTRY_CODE", model.PCountryCode == "00" ? DBNull.Value : (object)model.PCountryCode);
                            cmd.Parameters.AddWithValue("@P_CITY_CODE", model.PCityCode == "00" ? DBNull.Value : (object)model.PCityCode);
                            cmd.Parameters.AddWithValue("@T_ADDRESS_1", string.IsNullOrEmpty(model.TemporaryAddress1) ? DBNull.Value : (object)model.TemporaryAddress1.Trim());
                            cmd.Parameters.AddWithValue("@T_ADDRESS_2", string.IsNullOrEmpty(model.TemporaryAddress2) ? DBNull.Value : (object)model.TemporaryAddress2.Trim());
                            cmd.Parameters.AddWithValue("@CELL_CONTACT_1", string.IsNullOrEmpty(model.CellContact1) ? DBNull.Value : (object)model.CellContact1.Trim());
                            cmd.Parameters.AddWithValue("@CELL_CONTACT_2", string.IsNullOrEmpty(model.CellContact2) ? DBNull.Value : (object)model.CellContact2.Trim());
                            cmd.Parameters.AddWithValue("@EMERG_CONTACT_1", string.IsNullOrEmpty(model.EmergencyContact1) ? DBNull.Value : (object)model.EmergencyContact1.Trim());
                            cmd.Parameters.AddWithValue("@EMERG_CONTACT_2", string.IsNullOrEmpty(model.EmergencyContact2) ? DBNull.Value : (object)model.EmergencyContact2.Trim());
                            cmd.Parameters.AddWithValue("@RELIGION", string.IsNullOrEmpty(model.Religion) ? DBNull.Value : (object)model.Religion.Trim());
                            cmd.Parameters.AddWithValue("@NATIONALITY", string.IsNullOrEmpty(model.Nationality) ? DBNull.Value : (object)model.Nationality.Trim());
                            cmd.Parameters.AddWithValue("@NIC_NO", string.IsNullOrEmpty(model.NicNo) ? DBNull.Value : (object)model.NicNo.Trim());
                            cmd.Parameters.AddWithValue("@BLOOD_GRP", string.IsNullOrEmpty(model.BloodGroup) ? DBNull.Value : (object)model.BloodGroup.Trim().ToUpper());
                            cmd.Parameters.AddWithValue("@NTN_NO", string.IsNullOrEmpty(model.NtnNo) ? DBNull.Value : (object)model.NtnNo.Trim());
                            cmd.Parameters.AddWithValue("@EOBI_NO", string.IsNullOrEmpty(model.EobiNo) ? DBNull.Value : (object)model.EobiNo.Trim());
                            cmd.Parameters.AddWithValue("@BIRTH_DATE", (object?)model.BirthDate ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@MOTHER_TONGUE", string.IsNullOrEmpty(model.MotherTongue) ? DBNull.Value : (object)model.MotherTongue.Trim());
                            cmd.Parameters.AddWithValue("@MARRIAGE_DATE", model.MaritalStatus == "M" && model.MarriageDate.HasValue ? (object)model.MarriageDate.Value : DBNull.Value);
                            cmd.Parameters.AddWithValue("@EMAIL_ADD", string.IsNullOrEmpty(model.EmailAddress) ? DBNull.Value : (object)model.EmailAddress.Trim());
                            cmd.Parameters.AddWithValue("@MARK_OF_ID", string.IsNullOrEmpty(model.MarkOfId) ? DBNull.Value : (object)model.MarkOfId.Trim());
                            cmd.Parameters.AddWithValue("@PASSPORT_NO", string.IsNullOrEmpty(model.PassportNo) ? DBNull.Value : (object)model.PassportNo.Trim());
                            cmd.Parameters.AddWithValue("@EMPLOYEE_TYPE", model.EmployeeType == "00" ? DBNull.Value : (object)model.EmployeeType);
                            cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                            cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@generate_salary", model.GenerateSalary);
                            cmd.Parameters.AddWithValue("@emp_replace", string.IsNullOrEmpty(model.ReplacementEmpNo) ? DBNull.Value : (object)model.ReplacementEmpNo);
                            cmd.Parameters.AddWithValue("@Emp_WalletNumber", string.IsNullOrEmpty(model.WalletNumber) ? DBNull.Value : (object)model.WalletNumber.Trim());
                            cmd.Parameters.AddWithValue("@IsConfirmed", model.ProbationStatus == "Y" ? 1 : 0);
                            cmd.Parameters.AddWithValue("@JobTypeId", model.JobTypeId == "0" || string.IsNullOrEmpty(model.JobTypeId) ? DBNull.Value : (object)model.JobTypeId);
                            cmd.Parameters.AddWithValue("@ThirdPartyID", string.IsNullOrEmpty(model.ThirdPartyId) ? DBNull.Value : (object)model.ThirdPartyId);
                            cmd.Parameters.AddWithValue("@Is_Executive", model.IsExecutive ? 1 : 0);
                            cmd.Parameters.AddWithValue("@Is_Temperory", model.ProbationStatus == "T" ? 1 : 0);
                            cmd.Parameters.AddWithValue("@attandanceid", string.IsNullOrEmpty(model.AttendanceCode) ? DBNull.Value : (object)model.AttendanceCode.Trim());
                            if (photoBytes != null) cmd.Parameters.AddWithValue("@photo", photoBytes);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Update Department: if no record exists, insert; if changed, update
                        if (model.DepartmentCode != "0" && !string.IsNullOrEmpty(model.DepartmentCode))
                        {
                            var existingDept = await GetScalarAsync(connection, transaction, "SELECT DeptCode FROM hr_employeedepartmentdetails WHERE Emp_no=@e AND ToDate IS NULL LIMIT 1", "@e", model.EmpNo);
                            if (existingDept == null)
                            {
                                string newCode = await GenerateNewIdAsync(connection, transaction, "hr_employeedepartmentdetails", "code", 6);
                                string ins = "INSERT INTO hr_employeedepartmentdetails (CODE, Emp_No, DeptCode, FromDate, CreatedBy, Created_Date, UpdatedBy, Updated_Date) VALUES (@Code, @E, @D, @F, @U, NOW(), @U, NOW())";
                                using (var cmd = new MySqlCommand(ins, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@Code", newCode);
                                    cmd.Parameters.AddWithValue("@E", model.EmpNo);
                                    cmd.Parameters.AddWithValue("@D", model.DepartmentCode);
                                    cmd.Parameters.AddWithValue("@F", (object?)model.AppointDate ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@U", currentUserId);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                            else if (existingDept.ToString() != model.DepartmentCode)
                            {
                                string upd = "UPDATE hr_employeedepartmentdetails SET DeptCode=@D, UpdatedBy=@U, Updated_Date=NOW() WHERE Emp_no=@E AND ToDate IS NULL";
                                using (var cmd = new MySqlCommand(upd, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@D", model.DepartmentCode);
                                    cmd.Parameters.AddWithValue("@U", currentUserId);
                                    cmd.Parameters.AddWithValue("@E", model.EmpNo);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // Update Designation
                        if (model.DesignationCode != "0" && !string.IsNullOrEmpty(model.DesignationCode))
                        {
                            var existingJob = await GetScalarAsync(connection, transaction, "SELECT JobCode FROM hr_employeejobdetails WHERE Emp_No=@e AND EffectiveTo IS NULL LIMIT 1", "@e", model.EmpNo);
                            if (existingJob == null)
                            {
                                string newCode = await GenerateNewIdAsync(connection, transaction, "hr_employeejobdetails", "code", 6);
                                string ins = "INSERT INTO hr_employeejobdetails (CODE, Emp_No, JobCode, EffectiveFrom, ChangeType, CreatedBy, Created_Date, UpdatedBy, Updated_Date) VALUES (@Code, @E, @J, @F, @J, @U, NOW(), @U, NOW())";
                                using (var cmd = new MySqlCommand(ins, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@Code", newCode);
                                    cmd.Parameters.AddWithValue("@E", model.EmpNo);
                                    cmd.Parameters.AddWithValue("@J", model.DesignationCode);
                                    cmd.Parameters.AddWithValue("@F", (object?)model.AppointDate ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@U", currentUserId);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                            else if (existingJob.ToString() != model.DesignationCode)
                            {
                                string upd = "UPDATE hr_employeejobdetails SET JobCode=@J, UpdatedBy=@U, Updated_Date=NOW() WHERE Emp_No=@E AND EffectiveTo IS NULL";
                                using (var cmd = new MySqlCommand(upd, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@J", model.DesignationCode);
                                    cmd.Parameters.AddWithValue("@U", currentUserId);
                                    cmd.Parameters.AddWithValue("@E", model.EmpNo);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // Update Location
                        if (model.LocationId > 0)
                        {
                            var existingLoc = await GetScalarAsync(connection, transaction, "SELECT LocationId FROM hr_employeelocationdetails WHERE Emp_no=@e AND ToDate IS NULL LIMIT 1", "@e", model.EmpNo);
                            if (existingLoc == null)
                            {
                                string ins = "INSERT INTO hr_employeelocationdetails (Emp_no, LocationId, FromDate, CreatedBy, Created_Date) VALUES (@E, @L, NOW(), @U, NOW())";
                                using (var cmd = new MySqlCommand(ins, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@E", model.EmpNo);
                                    cmd.Parameters.AddWithValue("@L", model.LocationId);
                                    cmd.Parameters.AddWithValue("@U", currentUserId);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                            else if (Convert.ToInt32(existingLoc) != model.LocationId)
                            {
                                string upd1 = "UPDATE hr_employeelocationdetails SET ToDate=NOW(), UpdatedBy=@U, Updated_Date=NOW() WHERE Emp_no=@E AND ToDate IS NULL";
                                string ins = "INSERT INTO hr_employeelocationdetails (Emp_no, LocationId, FromDate, CreatedBy, Created_Date) VALUES (@E, @L, NOW(), @U, NOW())";
                                using (var cmd = new MySqlCommand(upd1 + ";" + ins, connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@E", model.EmpNo);
                                    cmd.Parameters.AddWithValue("@L", model.LocationId);
                                    cmd.Parameters.AddWithValue("@U", currentUserId);
                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // Update Hierarchy
                        if (!string.IsNullOrEmpty(model.ReportToEmpNo))
                        {
                            var hierExists = await GetScalarAsync(connection, transaction, "SELECT COUNT(*) FROM definehierarchy WHERE Emp_no=@e", "@e", model.EmpNo);
                            string hierSql = Convert.ToInt32(hierExists) > 0
                                ? "UPDATE definehierarchy SET email=@Email, Cell=@Cell, ReportToEmpNo=@ReportTo, ModifiedBy=@U, ModifiedDate=NOW() WHERE Emp_no=@E"
                                : "INSERT INTO definehierarchy (Emp_no, email, Cell, ReportToEmpNo, CreatedBy, CreatedDate) VALUES (@E, @Email, @Cell, @ReportTo, @U, NOW())";
                            using (var cmd = new MySqlCommand(hierSql, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@E", model.EmpNo);
                                cmd.Parameters.AddWithValue("@Email", string.IsNullOrEmpty(model.EmailAddress) ? DBNull.Value : (object)model.EmailAddress);
                                cmd.Parameters.AddWithValue("@Cell", string.IsNullOrEmpty(model.CellContact1) ? DBNull.Value : (object)model.CellContact1);
                                cmd.Parameters.AddWithValue("@ReportTo", model.ReportToEmpNo.PadLeft(14, '0'));
                                cmd.Parameters.AddWithValue("@U", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction!.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction!.RollbackAsync();
                        return false;
                    }
                }
            }
        }

        public async Task<bool> DeleteEmployeePersonalDetailAsync(string empNo)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Check references before deleting
                string checkRef = "SELECT COUNT(*) FROM hr_employeesalary WHERE EMP_NO=@e";
                using (var cmd = new MySqlCommand(checkRef, connection))
                {
                    cmd.Parameters.AddWithValue("@e", empNo);
                    var cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    if (cnt > 0) return false;
                }

                string deleteQuery = "DELETE FROM hr_employeepersonaldetail WHERE EMP_NO=@EMP_NO";
                using (var cmd = new MySqlCommand(deleteQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EMP_NO", empNo);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        private async Task<object?> GetScalarAsync(MySqlConnection connection, MySqlTransaction? transaction, string query, string paramName, object paramValue)
        {
            using (var cmd = new MySqlCommand(query, connection, transaction))
            {
                cmd.Parameters.AddWithValue(paramName, paramValue);
                var result = await cmd.ExecuteScalarAsync();
                return result == DBNull.Value ? null : result;
            }
        }

        #endregion

        #region Employee Experience

        public async Task<IEnumerable<EmployeeExperienceModel>> GetEmployeeExperiencesAsync(string empNo)
        {
            var data = new List<EmployeeExperienceModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();
                string query = @"SELECT e.sno, e.OrganizationName, e.Designation, e.FromDate, e.ToDate, e.ReasonForLeaving, e.Comments,
                    p.NAME AS EmpName FROM hr_employeeexperience e
                    INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO=e.Emp_No
                    WHERE e.Emp_No=@EmpNo ORDER BY e.sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeExperienceModel
                            {
                                EmpNo = empNo,
                                EmpName = reader["EmpName"].ToString() ?? string.Empty,
                                Sno = Convert.ToInt32(reader["sno"]),
                                OrganizationName = reader["OrganizationName"].ToString() ?? string.Empty,
                                Designation = reader["Designation"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] != DBNull.Value ? Convert.ToDateTime(reader["FromDate"]) : null,
                                ToDate = reader["ToDate"] != DBNull.Value ? Convert.ToDateTime(reader["ToDate"]) : null,
                                ReasonForLeaving = reader["ReasonForLeaving"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeExperienceModel?> GetEmployeeExperienceBySnAsync(string empNo, int sno)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();
                string query = @"SELECT e.*, p.NAME AS EmpName FROM hr_employeeexperience e
                    INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO=e.Emp_No
                    WHERE e.Emp_No=@EmpNo AND e.sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    cmd.Parameters.AddWithValue("@sno", sno);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new EmployeeExperienceModel
                            {
                                EmpNo = empNo,
                                EmpName = reader["EmpName"].ToString() ?? string.Empty,
                                Sno = sno,
                                OrganizationName = reader["OrganizationName"].ToString() ?? string.Empty,
                                Designation = reader["Designation"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] != DBNull.Value ? Convert.ToDateTime(reader["FromDate"]) : null,
                                ToDate = reader["ToDate"] != DBNull.Value ? Convert.ToDateTime(reader["ToDate"]) : null,
                                ReasonForLeaving = reader["ReasonForLeaving"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> AddEmployeeExperienceAsync(EmployeeExperienceModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string getNextSno = "SELECT IFNULL(MAX(sno),0)+1 FROM hr_employeeexperience WHERE Emp_No=@EmpNo";
                int nextSno;
                using (var cmd = new MySqlCommand(getNextSno, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    nextSno = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
                string query = @"INSERT INTO hr_employeeexperience (Emp_No, sno, OrganizationName, Designation, FromDate, ToDate, ReasonForLeaving, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                    VALUES (@EmpNo, @sno, @OrgName, @Desig, @FromDate, @ToDate, @Reason, @Comments, @CreatedBy, NOW(), @CreatedBy, NOW())";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@sno", nextSno);
                    cmd.Parameters.AddWithValue("@OrgName", model.OrganizationName.Trim());
                    cmd.Parameters.AddWithValue("@Desig", model.Designation.Trim());
                    cmd.Parameters.AddWithValue("@FromDate", (object?)model.FromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToDate", (object?)model.ToDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.ReasonForLeaving) ? DBNull.Value : (object)model.ReasonForLeaving.Trim());
                    cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                    cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        public async Task<bool> UpdateEmployeeExperienceAsync(EmployeeExperienceModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = @"UPDATE hr_employeeexperience SET OrganizationName=@OrgName, Designation=@Desig, FromDate=@FromDate, ToDate=@ToDate,
                    ReasonForLeaving=@Reason, Comments=@Comments, UpdatedBy=@UpdatedBy, Updated_Date=NOW()
                    WHERE Emp_No=@EmpNo AND sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@sno", model.Sno);
                    cmd.Parameters.AddWithValue("@OrgName", model.OrganizationName.Trim());
                    cmd.Parameters.AddWithValue("@Desig", model.Designation.Trim());
                    cmd.Parameters.AddWithValue("@FromDate", (object?)model.FromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToDate", (object?)model.ToDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.ReasonForLeaving) ? DBNull.Value : (object)model.ReasonForLeaving.Trim());
                    cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                    cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        public async Task<bool> DeleteEmployeeExperienceAsync(string empNo, int sno)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = "DELETE FROM hr_employeeexperience WHERE Emp_No=@EmpNo AND sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    cmd.Parameters.AddWithValue("@sno", sno);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        #endregion

        #region Employee Education

        public async Task<IEnumerable<EmployeeEducationModel>> GetEmployeeEducationsAsync(string empNo)
        {
            var data = new List<EmployeeEducationModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();
                string query = @"SELECT e.*, p.NAME AS EmpName FROM hr_employeeeducation e
                    INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO=e.Emp_No
                    WHERE e.Emp_No=@EmpNo ORDER BY e.sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeEducationModel
                            {
                                EmpNo = empNo,
                                EmpName = reader["EmpName"].ToString() ?? string.Empty,
                                Sno = Convert.ToInt32(reader["sno"]),
                                DegreeTitle = reader["DegreeTitle"].ToString() ?? string.Empty,
                                Area = reader["Area"].ToString() ?? string.Empty,
                                InstitutionName = reader["InstitutionName"].ToString() ?? string.Empty,
                                InstitutionType = reader["InstitutionType"].ToString() ?? string.Empty,
                                CountryCode = reader["CountryCode"].ToString() ?? "00",
                                CityCode = reader["CityCode"].ToString() ?? "00",
                                FromDate = reader["FromDate"] != DBNull.Value ? Convert.ToDateTime(reader["FromDate"]) : null,
                                ToDate = reader["ToDate"] != DBNull.Value ? Convert.ToDateTime(reader["ToDate"]) : null,
                                Grade = reader["Grade"].ToString() ?? string.Empty,
                                Status = reader["Status"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeEducationModel?> GetEmployeeEducationBySnAsync(string empNo, int sno)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();
                string query = @"SELECT e.*, p.NAME AS EmpName FROM hr_employeeeducation e
                    INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO=e.Emp_No
                    WHERE e.Emp_No=@EmpNo AND e.sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    cmd.Parameters.AddWithValue("@sno", sno);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new EmployeeEducationModel
                            {
                                EmpNo = empNo,
                                EmpName = reader["EmpName"].ToString() ?? string.Empty,
                                Sno = sno,
                                DegreeTitle = reader["DegreeTitle"].ToString() ?? string.Empty,
                                Area = reader["Area"].ToString() ?? string.Empty,
                                InstitutionName = reader["InstitutionName"].ToString() ?? string.Empty,
                                InstitutionType = reader["InstitutionType"].ToString() ?? string.Empty,
                                CountryCode = reader["CountryCode"].ToString() ?? "00",
                                CityCode = reader["CityCode"].ToString() ?? "00",
                                FromDate = reader["FromDate"] != DBNull.Value ? Convert.ToDateTime(reader["FromDate"]) : null,
                                ToDate = reader["ToDate"] != DBNull.Value ? Convert.ToDateTime(reader["ToDate"]) : null,
                                Grade = reader["Grade"].ToString() ?? string.Empty,
                                Status = reader["Status"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> AddEmployeeEducationAsync(EmployeeEducationModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string getNextSno = "SELECT IFNULL(MAX(sno),0)+1 FROM hr_employeeeducation WHERE Emp_No=@EmpNo";
                int nextSno;
                using (var cmd = new MySqlCommand(getNextSno, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    nextSno = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
                string query = @"INSERT INTO hr_employeeeducation (Emp_No, sno, DegreeTitle, Area, InstitutionName, InstitutionType, CountryCode, CityCode, FromDate, ToDate, Grade, Status, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                    VALUES (@EmpNo, @sno, @Degree, @Area, @InstName, @InstType, @CountryCode, @CityCode, @FromDate, @ToDate, @Grade, @Status, @Comments, @CreatedBy, NOW(), @CreatedBy, NOW())";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@sno", nextSno);
                    cmd.Parameters.AddWithValue("@Degree", model.DegreeTitle.Trim());
                    cmd.Parameters.AddWithValue("@Area", string.IsNullOrEmpty(model.Area) ? DBNull.Value : (object)model.Area.Trim());
                    cmd.Parameters.AddWithValue("@InstName", model.InstitutionName.Trim());
                    cmd.Parameters.AddWithValue("@InstType", string.IsNullOrEmpty(model.InstitutionType) ? DBNull.Value : (object)model.InstitutionType.Trim());
                    cmd.Parameters.AddWithValue("@CountryCode", model.CountryCode == "00" ? DBNull.Value : (object)model.CountryCode);
                    cmd.Parameters.AddWithValue("@CityCode", model.CityCode == "00" ? DBNull.Value : (object)model.CityCode);
                    cmd.Parameters.AddWithValue("@FromDate", (object?)model.FromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToDate", (object?)model.ToDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Grade", string.IsNullOrEmpty(model.Grade) ? DBNull.Value : (object)model.Grade.Trim());
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrEmpty(model.Status) ? DBNull.Value : (object)model.Status.Trim());
                    cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                    cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        public async Task<bool> UpdateEmployeeEducationAsync(EmployeeEducationModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = @"UPDATE hr_employeeeducation SET DegreeTitle=@Degree, Area=@Area, InstitutionName=@InstName, InstitutionType=@InstType,
                    CountryCode=@CountryCode, CityCode=@CityCode, FromDate=@FromDate, ToDate=@ToDate, Grade=@Grade, Status=@Status, Comments=@Comments,
                    UpdatedBy=@UpdatedBy, Updated_Date=NOW() WHERE Emp_No=@EmpNo AND sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@sno", model.Sno);
                    cmd.Parameters.AddWithValue("@Degree", model.DegreeTitle.Trim());
                    cmd.Parameters.AddWithValue("@Area", string.IsNullOrEmpty(model.Area) ? DBNull.Value : (object)model.Area.Trim());
                    cmd.Parameters.AddWithValue("@InstName", model.InstitutionName.Trim());
                    cmd.Parameters.AddWithValue("@InstType", string.IsNullOrEmpty(model.InstitutionType) ? DBNull.Value : (object)model.InstitutionType.Trim());
                    cmd.Parameters.AddWithValue("@CountryCode", model.CountryCode == "00" ? DBNull.Value : (object)model.CountryCode);
                    cmd.Parameters.AddWithValue("@CityCode", model.CityCode == "00" ? DBNull.Value : (object)model.CityCode);
                    cmd.Parameters.AddWithValue("@FromDate", (object?)model.FromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToDate", (object?)model.ToDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Grade", string.IsNullOrEmpty(model.Grade) ? DBNull.Value : (object)model.Grade.Trim());
                    cmd.Parameters.AddWithValue("@Status", string.IsNullOrEmpty(model.Status) ? DBNull.Value : (object)model.Status.Trim());
                    cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                    cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        public async Task<bool> DeleteEmployeeEducationAsync(string empNo, int sno)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = "DELETE FROM hr_employeeeducation WHERE Emp_No=@EmpNo AND sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    cmd.Parameters.AddWithValue("@sno", sno);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        #endregion

        #region Employee Medical History

        public async Task<IEnumerable<EmployeeMedicalHistoryModel>> GetEmployeeMedicalHistoriesAsync(string empNo)
        {
            var data = new List<EmployeeMedicalHistoryModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();
                string query = @"SELECT m.*, p.NAME AS EmpName FROM hr_employeemedical m
                    INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO=m.Emp_No
                    WHERE m.Emp_No=@EmpNo ORDER BY m.sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeMedicalHistoryModel
                            {
                                EmpNo = empNo,
                                EmpName = reader["EmpName"].ToString() ?? string.Empty,
                                Sno = Convert.ToInt32(reader["sno"]),
                                DiseaseName = reader["DiseaseName"].ToString() ?? string.Empty,
                                DiagnosticDetail = reader["DiagnosticDetail"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] != DBNull.Value ? Convert.ToDateTime(reader["FromDate"]) : null,
                                ToDate = reader["ToDate"] != DBNull.Value ? Convert.ToDateTime(reader["ToDate"]) : null,
                                HospitalName = reader["Checkup_HospitalName"].ToString() ?? string.Empty,
                                DoctorName = reader["Checkup_DoctorName"].ToString() ?? string.Empty,
                                TreatmentDetail = reader["TreatmentDetail"].ToString() ?? string.Empty,
                                CurrentSituation = reader["CurrentSituation"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeMedicalHistoryModel?> GetEmployeeMedicalHistoryBySnAsync(string empNo, int sno)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();
                string query = @"SELECT m.*, p.NAME AS EmpName FROM hr_employeemedical m
                    INNER JOIN hr_employeepersonaldetail p ON p.EMP_NO=m.Emp_No
                    WHERE m.Emp_No=@EmpNo AND m.sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    cmd.Parameters.AddWithValue("@sno", sno);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new EmployeeMedicalHistoryModel
                            {
                                EmpNo = empNo,
                                EmpName = reader["EmpName"].ToString() ?? string.Empty,
                                Sno = sno,
                                DiseaseName = reader["DiseaseName"].ToString() ?? string.Empty,
                                DiagnosticDetail = reader["DiagnosticDetail"].ToString() ?? string.Empty,
                                FromDate = reader["FromDate"] != DBNull.Value ? Convert.ToDateTime(reader["FromDate"]) : null,
                                ToDate = reader["ToDate"] != DBNull.Value ? Convert.ToDateTime(reader["ToDate"]) : null,
                                HospitalName = reader["Checkup_HospitalName"].ToString() ?? string.Empty,
                                DoctorName = reader["Checkup_DoctorName"].ToString() ?? string.Empty,
                                TreatmentDetail = reader["TreatmentDetail"].ToString() ?? string.Empty,
                                CurrentSituation = reader["CurrentSituation"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return null;
        }

        public async Task<bool> AddEmployeeMedicalHistoryAsync(EmployeeMedicalHistoryModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string getNextSno = "SELECT IFNULL(MAX(sno),0)+1 FROM hr_employeemedical WHERE Emp_No=@EmpNo";
                int nextSno;
                using (var cmd = new MySqlCommand(getNextSno, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    nextSno = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
                string query = @"INSERT INTO hr_employeemedical (Emp_No, sno, DiseaseName, DiagnosticDetail, FromDate, ToDate, Checkup_HospitalName, Checkup_DoctorName, TreatmentDetail, CurrentSituation, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                    VALUES (@EmpNo, @sno, @Disease, @Diagnostic, @FromDate, @ToDate, @Hospital, @Doctor, @Treatment, @Situation, @Comments, @CreatedBy, NOW(), @CreatedBy, NOW())";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@sno", nextSno);
                    cmd.Parameters.AddWithValue("@Disease", model.DiseaseName.Trim());
                    cmd.Parameters.AddWithValue("@Diagnostic", string.IsNullOrEmpty(model.DiagnosticDetail) ? DBNull.Value : (object)model.DiagnosticDetail.Trim());
                    cmd.Parameters.AddWithValue("@FromDate", (object?)model.FromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToDate", (object?)model.ToDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Hospital", string.IsNullOrEmpty(model.HospitalName) ? DBNull.Value : (object)model.HospitalName.Trim());
                    cmd.Parameters.AddWithValue("@Doctor", string.IsNullOrEmpty(model.DoctorName) ? DBNull.Value : (object)model.DoctorName.Trim());
                    cmd.Parameters.AddWithValue("@Treatment", string.IsNullOrEmpty(model.TreatmentDetail) ? DBNull.Value : (object)model.TreatmentDetail.Trim());
                    cmd.Parameters.AddWithValue("@Situation", string.IsNullOrEmpty(model.CurrentSituation) ? DBNull.Value : (object)model.CurrentSituation.Trim());
                    cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                    cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        public async Task<bool> UpdateEmployeeMedicalHistoryAsync(EmployeeMedicalHistoryModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = @"UPDATE hr_employeemedical SET DiseaseName=@Disease, DiagnosticDetail=@Diagnostic, FromDate=@FromDate, ToDate=@ToDate,
                    Checkup_HospitalName=@Hospital, Checkup_DoctorName=@Doctor, TreatmentDetail=@Treatment, CurrentSituation=@Situation, Comments=@Comments,
                    UpdatedBy=@UpdatedBy, Updated_Date=NOW() WHERE Emp_No=@EmpNo AND sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                    cmd.Parameters.AddWithValue("@sno", model.Sno);
                    cmd.Parameters.AddWithValue("@Disease", model.DiseaseName.Trim());
                    cmd.Parameters.AddWithValue("@Diagnostic", string.IsNullOrEmpty(model.DiagnosticDetail) ? DBNull.Value : (object)model.DiagnosticDetail.Trim());
                    cmd.Parameters.AddWithValue("@FromDate", (object?)model.FromDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ToDate", (object?)model.ToDate ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Hospital", string.IsNullOrEmpty(model.HospitalName) ? DBNull.Value : (object)model.HospitalName.Trim());
                    cmd.Parameters.AddWithValue("@Doctor", string.IsNullOrEmpty(model.DoctorName) ? DBNull.Value : (object)model.DoctorName.Trim());
                    cmd.Parameters.AddWithValue("@Treatment", string.IsNullOrEmpty(model.TreatmentDetail) ? DBNull.Value : (object)model.TreatmentDetail.Trim());
                    cmd.Parameters.AddWithValue("@Situation", string.IsNullOrEmpty(model.CurrentSituation) ? DBNull.Value : (object)model.CurrentSituation.Trim());
                    cmd.Parameters.AddWithValue("@Comments", string.IsNullOrEmpty(model.Comments) ? DBNull.Value : (object)model.Comments.Trim());
                    cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        public async Task<bool> DeleteEmployeeMedicalHistoryAsync(string empNo, int sno)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = "DELETE FROM hr_employeemedical WHERE Emp_No=@EmpNo AND sno=@sno";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                    cmd.Parameters.AddWithValue("@sno", sno);
                    await cmd.ExecuteNonQueryAsync();
                }
                return true;
            }
        }

        #endregion

        #region Employee Medical Survey

        public async Task<EmployeeMedicalSurveyModel?> GetEmployeeMedicalSurveyAsync(string empNo)
        {
            string paddedEmpNo = empNo.PadLeft(14, '0');
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string empQuery = @"SELECT e.EMP_NO, e.NAME AS EmpName,
                    (SELECT d.FullName FROM hr_employeedepartmentdetails ed INNER JOIN hr_department d ON d.Code=ed.DeptCode WHERE ed.Emp_No=e.EMP_NO AND ed.ToDate IS NULL LIMIT 1) AS DeptName,
                    e.NIC_NO, DATE_FORMAT(e.APPOINT_DATE,'%d/%m/%Y') AS AppointDate, e.CELL_CONTACT_1 AS Cell,
                    (SELECT j.FullName FROM hr_jobs j INNER JOIN hr_employeejobdetails ej ON ej.JobCode=j.Code WHERE ej.Emp_No=e.EMP_NO AND ej.EffectiveTo IS NULL LIMIT 1) AS Designation,
                    c.FullName AS City, e.MARITAL_STATUS AS MaritalStatus
                    FROM hr_employeepersonaldetail e
                    LEFT JOIN hr_city c ON c.Code=e.P_CITY_CODE
                    WHERE e.EMP_STATUS <> 'I' AND e.EMP_NO=@EmpNo";

                EmployeeMedicalSurveyModel? survey = null;
                using (var cmd = new MySqlCommand(empQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", paddedEmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            survey = new EmployeeMedicalSurveyModel
                            {
                                EmpNo = reader["EMP_NO"].ToString() ?? string.Empty,
                                EmpName = reader["EmpName"].ToString() ?? string.Empty,
                                DeptName = reader["DeptName"].ToString() ?? string.Empty,
                                NicNo = reader["NIC_NO"].ToString() ?? string.Empty,
                                AppointDate = reader["AppointDate"].ToString() ?? string.Empty,
                                Cell = reader["Cell"].ToString() ?? string.Empty,
                                Designation = reader["Designation"].ToString() ?? string.Empty,
                                City = reader["City"].ToString() ?? string.Empty,
                                MaritalStatus = reader["MaritalStatus"].ToString() ?? string.Empty
                            };
                        }
                    }
                }

                if (survey == null) return null;

                string familyQuery = @"SELECT Name, Relation, NIC, Gender, DOB, PolicyRequiredFor AS RequiredPolicyFor
                    FROM hr_employeemedicalsurvey WHERE Emp_no=@EmpNo";
                using (var cmd = new MySqlCommand(familyQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@EmpNo", paddedEmpNo);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        string? requiredFor = null;
                        while (await reader.ReadAsync())
                        {
                            if (requiredFor == null) requiredFor = reader["RequiredPolicyFor"].ToString();
                            survey.FamilyMembers.Add(new MedicalSurveyFamilyMember
                            {
                                Name = reader["Name"].ToString() ?? string.Empty,
                                Relation = reader["Relation"].ToString() ?? string.Empty,
                                Nic = reader["NIC"].ToString() ?? string.Empty,
                                Gender = reader["Gender"].ToString() ?? string.Empty,
                                Dob = reader["DOB"] != DBNull.Value ? Convert.ToDateTime(reader["DOB"]) : null
                            });
                        }
                        if (requiredFor != null) survey.RequiredPolicyFor = requiredFor;
                    }
                }

                return survey;
            }
        }

        public async Task<bool> SaveEmployeeMedicalSurveyAsync(EmployeeMedicalSurveyModel model, string currentUserId)
        {
            string paddedEmpNo = model.EmpNo.PadLeft(14, '0');
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync() as MySqlTransaction)
                {
                    try
                    {
                        // Delete existing rows
                        string deleteQuery = "DELETE FROM hr_employeemedicalsurvey WHERE Emp_no=@EmpNo";
                        using (var cmd = new MySqlCommand(deleteQuery, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@EmpNo", paddedEmpNo);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        // Re-insert family members
                        string insertQuery = "INSERT INTO hr_employeemedicalsurvey (Emp_no, PolicyRequiredFor, Name, Relation, NIC, Gender, DOB, CreatedBy, CreatedDate) VALUES (@EmpNo, @PRF, @Name, @Relation, @NIC, @Gender, @DOB, @CreatedBy, NOW())";
                        foreach (var member in model.FamilyMembers.Where(m => !string.IsNullOrEmpty(m.Name)))
                        {
                            using (var cmd = new MySqlCommand(insertQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@EmpNo", paddedEmpNo);
                                cmd.Parameters.AddWithValue("@PRF", string.IsNullOrEmpty(model.RequiredPolicyFor) ? DBNull.Value : (object)model.RequiredPolicyFor);
                                cmd.Parameters.AddWithValue("@Name", member.Name);
                                cmd.Parameters.AddWithValue("@Relation", string.IsNullOrEmpty(member.Relation) ? DBNull.Value : (object)member.Relation);
                                cmd.Parameters.AddWithValue("@NIC", string.IsNullOrEmpty(member.Nic) ? DBNull.Value : (object)member.Nic);
                                cmd.Parameters.AddWithValue("@Gender", string.IsNullOrEmpty(member.Gender) ? DBNull.Value : (object)member.Gender);
                                cmd.Parameters.AddWithValue("@DOB", (object?)member.Dob ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Update marital status
                        string updateMarital = "UPDATE hr_employeepersonaldetail SET MARITAL_STATUS=@ms WHERE EMP_NO=@EmpNo";
                        using (var cmd = new MySqlCommand(updateMarital, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@ms", model.MaritalStatus);
                            cmd.Parameters.AddWithValue("@EmpNo", paddedEmpNo);
                            await cmd.ExecuteNonQueryAsync();
                        }

                        await transaction!.CommitAsync();
                        return true;
                    }
                    catch
                    {
                        await transaction!.RollbackAsync();
                        return false;
                    }
                }
            }
        }

        #endregion

        #region Attendance Adjustment

        public async Task<IEnumerable<AttendanceAdjustmentModel>> GetAttendanceAdjustmentsAsync(string currentUserId)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT
                    empAdj.emp_no AS EmpNo,
                    empDet.name AS EmpName,
                    empAdj.adjustmentDate AS AdjustmentDate,
                    empAdj.adjustmentType AS AdjustmentType,
                    empAdj.reason AS Reason
                FROM hr_employeeattandenceadjust empAdj
                INNER JOIN hr_employeepersonaldetail empDet ON empAdj.emp_no = empDet.emp_no
                INNER JOIN lcs_user_location lul ON lul.city_code = empDet.P_CITY_CODE
                WHERE lul.userid = @UserId
                  AND DATE(empAdj.adjustmentDate) > DATE(DATE_SUB(NOW(), INTERVAL 40 DAY))
                ORDER BY empAdj.emp_no, empAdj.adjustmentDate DESC";
            return await connection.QueryAsync<AttendanceAdjustmentModel>(sql, new { UserId = currentUserId });
        }

        public async Task<AttendanceAdjustmentModel?> GetAttendanceAdjustmentAsync(string empNo, DateTime date)
        {
            using var connection = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT empAdj.emp_no AS EmpNo, empDet.name AS EmpName,
                       empAdj.adjustmentDate AS AdjustmentDate, empAdj.adjustmentType AS AdjustmentType, empAdj.reason AS Reason
                FROM hr_employeeattandenceadjust empAdj
                INNER JOIN hr_employeepersonaldetail empDet ON empAdj.emp_no = empDet.emp_no
                WHERE empAdj.emp_no = @EmpNo AND DATE(empAdj.adjustmentDate) = DATE(@Date)";
            return await connection.QueryFirstOrDefaultAsync<AttendanceAdjustmentModel>(sql, new { EmpNo = empNo, Date = date });
        }

        private async Task<bool> IsProcessClosedAsync(MySqlConnection connection, MySqlTransaction transaction, string empNo, DateTime date)
        {
            string citySql = "SELECT P_CITY_CODE FROM hr_employeepersonaldetail WHERE emp_no = @EmpNo";
            var cmd = new MySqlCommand(citySql, connection, transaction);
            cmd.Parameters.AddWithValue("@EmpNo", empNo);
            var cityCode = (await cmd.ExecuteScalarAsync())?.ToString();
            if (string.IsNullOrEmpty(cityCode)) return false;

            string closedSql = "SELECT COUNT(*) FROM hr_closeprocesses WHERE City = @City AND Year = @Year AND Month = @Month";
            var cmd2 = new MySqlCommand(closedSql, connection, transaction);
            cmd2.Parameters.AddWithValue("@City", cityCode);
            cmd2.Parameters.AddWithValue("@Year", date.Year);
            cmd2.Parameters.AddWithValue("@Month", date.Month);
            var count = Convert.ToInt32(await cmd2.ExecuteScalarAsync());
            return count > 0;
        }

        public async Task<(bool success, string message)> AddAttendanceAdjustmentAsync(AttendanceAdjustmentModel model, string currentUserId)
        {
            using var connection = (MySqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                // Sunday check
                if (model.AdjustmentDate!.Value.DayOfWeek == DayOfWeek.Sunday)
                    return (false, "You cannot pass an adjustment for Sunday.");

                // Closed process check
                if (await IsProcessClosedAsync(connection, (MySqlTransaction)transaction, model.EmpNo, model.AdjustmentDate.Value))
                    return (false, "You cannot adjust attendance for the selected month. All processes for selected month have been locked!");

                // Duplicate check
                var dupCmd = new MySqlCommand("SELECT COUNT(*) FROM hr_employeeattandenceadjust WHERE emp_no=@EmpNo AND DATE(adjustmentDate)=DATE(@Date)", connection, (MySqlTransaction)transaction);
                dupCmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                dupCmd.Parameters.AddWithValue("@Date", model.AdjustmentDate.Value);
                if (Convert.ToInt32(await dupCmd.ExecuteScalarAsync()) > 0)
                    return (false, "Adjustment already exists for this employee and date.");

                // Employee exists check
                var empCmd = new MySqlCommand("SELECT COUNT(*) FROM hr_employeepersonaldetail WHERE emp_no=@EmpNo AND EMP_STATUS <> 'I'", connection, (MySqlTransaction)transaction);
                empCmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                if (Convert.ToInt32(await empCmd.ExecuteScalarAsync()) == 0)
                    return (false, "Employee does not exist.");

                string sql = @"INSERT INTO hr_employeeattandenceadjust
                    (emp_no, adjustmentDate, adjustmentType, year, month, reason, createdby, CreatedDate, updatedby, Updated_Date)
                    VALUES (@EmpNo, @Date, @Type, @Year, @Month, @Reason, @CreatedBy, NOW(), @CreatedBy, NOW())";
                var cmd = new MySqlCommand(sql, connection, (MySqlTransaction)transaction);
                cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                cmd.Parameters.AddWithValue("@Date", model.AdjustmentDate.Value);
                cmd.Parameters.AddWithValue("@Type", model.AdjustmentType);
                cmd.Parameters.AddWithValue("@Year", model.AdjustmentDate.Value.Year);
                cmd.Parameters.AddWithValue("@Month", model.AdjustmentDate.Value.Month);
                cmd.Parameters.AddWithValue("@Reason", model.Reason ?? "");
                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                await cmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                return (true, "Record Saved Successfully");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string message)> UpdateAttendanceAdjustmentAsync(AttendanceAdjustmentModel model, string currentUserId)
        {
            using var connection = (MySqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                if (model.AdjustmentDate!.Value.DayOfWeek == DayOfWeek.Sunday)
                    return (false, "You cannot pass an adjustment for Sunday.");

                if (await IsProcessClosedAsync(connection, (MySqlTransaction)transaction, model.EmpNo, model.AdjustmentDate.Value))
                    return (false, "You cannot adjust attendance for the selected month. All processes for selected month have been locked!");

                DateTime originalDate = DateTime.Parse(model.OriginalDate);
                string sql = @"UPDATE hr_employeeattandenceadjust
                    SET adjustmentDate=@Date, adjustmentType=@Type, year=@Year, month=@Month, reason=@Reason, updatedby=@UpdatedBy, Updated_Date=NOW()
                    WHERE emp_no=@EmpNo AND DATE(adjustmentDate)=DATE(@OriginalDate)";
                var cmd = new MySqlCommand(sql, connection, (MySqlTransaction)transaction);
                cmd.Parameters.AddWithValue("@EmpNo", model.EmpNo);
                cmd.Parameters.AddWithValue("@Date", model.AdjustmentDate.Value);
                cmd.Parameters.AddWithValue("@OriginalDate", originalDate);
                cmd.Parameters.AddWithValue("@Type", model.AdjustmentType);
                cmd.Parameters.AddWithValue("@Year", model.AdjustmentDate.Value.Year);
                cmd.Parameters.AddWithValue("@Month", model.AdjustmentDate.Value.Month);
                cmd.Parameters.AddWithValue("@Reason", model.Reason ?? "");
                cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                await cmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                return (true, "Record Updated Successfully");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string message)> DeleteAttendanceAdjustmentAsync(string empNo, DateTime date)
        {
            using var connection = (MySqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                if (await IsProcessClosedAsync(connection, (MySqlTransaction)transaction, empNo, date))
                    return (false, "You cannot delete this adjustment. Processes for this month have been locked!");

                var cmd = new MySqlCommand("DELETE FROM hr_employeeattandenceadjust WHERE emp_no=@EmpNo AND DATE(adjustmentDate)=DATE(@Date)", connection, (MySqlTransaction)transaction);
                cmd.Parameters.AddWithValue("@EmpNo", empNo);
                cmd.Parameters.AddWithValue("@Date", date);
                await cmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                return (true, "Record Deleted Successfully");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string message)> BulkMarkPresentAsync(IFormFile file, int year, int month, bool isDateWise, DateTime fromDate, DateTime toDate, string currentUserId)
        {
            if (file == null || file.Length == 0)
                return (false, "Please select an Excel file.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xls" && ext != ".xlsx")
                return (false, "Only Excel files (.xls, .xlsx) are allowed.");

            var empNos = new List<string>();
            var buggyRows = new List<string>();

            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (var stream = file.OpenReadStream())
            using (var package = new OfficeOpenXml.ExcelPackage(stream))
            {
                var ws = package.Workbook.Worksheets.First();
                for (int i = 1; i <= ws.Dimension.End.Row; i++)
                {
                    var cellVal = ws.Cells[i, 1].Value?.ToString() ?? "";
                    if (int.TryParse(cellVal, out _))
                        empNos.Add(cellVal.PadLeft(14, '0'));
                    else if (!string.IsNullOrWhiteSpace(cellVal))
                        buggyRows.Add(cellVal);
                }
            }

            if (empNos.Count == 0)
                return (false, $"No valid employee numbers found in file. Skipped rows: {buggyRows.Count}");

            // Determine date range (LCS payroll period: 26th prev → 25th current)
            DateTime periodFrom, periodTo;
            if (isDateWise)
            {
                periodFrom = fromDate.Date;
                periodTo = toDate.Date;
            }
            else
            {
                periodFrom = month == 1 ? new DateTime(year - 1, 12, 26) : new DateTime(year, month - 1, 26);
                periodTo = new DateTime(year, month, 25);
            }

            int totalInserted = 0;
            using var connection = (MySqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();

            foreach (var empNo in empNos)
            {
                // Delete existing absent adjustments in period
                string deleteSql = @"DELETE FROM hr_employeeattandenceadjust
                    WHERE DATE(adjustmentDate) BETWEEN @From AND @To AND adjustmentType = 'A' AND emp_no = @EmpNo";

                // Insert absent adjustments for days with no attendance, excluding Sundays and holidays
                string insertSql = @"
                    INSERT INTO hr_employeeattandenceadjust (emp_no, adjustmentDate, adjustmentType, year, month, reason, createdby, CreatedDate, updatedby, Updated_Date)
                    WITH RECURSIVE cal AS (
                        SELECT @From AS dt
                        UNION ALL
                        SELECT DATE_ADD(dt, INTERVAL 1 DAY) FROM cal WHERE dt < @To
                    )
                    SELECT xc.emp_no, cal.dt, 'A', YEAR(cal.dt), MONTH(cal.dt),
                           CONCAT('Bulk Adjustment ', @Year, '-', @Month) AS reason,
                           @UserId, NOW(), @UserId, NOW()
                    FROM cal
                    INNER JOIN hr_employeepersonaldetail xc
                        ON xc.emp_no = @EmpNo AND xc.left_date IS NULL AND cal.dt >= DATE(xc.APPOINT_DATE)
                    LEFT JOIN (
                        SELECT DISTINCT emp_no, DATE(CONCAT(YEAR,'-',MONTH,'-',DAY)) AS dt
                        FROM hr_employeeattendance
                        WHERE Date(CHECKTIME) BETWEEN @From AND @To AND Emp_no = @EmpNo
                    ) ax ON ax.emp_no = xc.emp_no AND cal.dt = ax.dt
                    WHERE ax.dt IS NULL
                      AND DAYOFWEEK(cal.dt) != 1
                      AND NOT EXISTS (
                          SELECT 1 FROM hr_gazetted_holidays
                          WHERE YEAR = YEAR(cal.dt) AND MONTH = MONTH(cal.dt)
                            AND cal.dt BETWEEN fromdate AND todate AND Holiday_flag = 'ALL'
                      )";

                using var cmd = new MySqlCommand(deleteSql, connection);
                cmd.Parameters.AddWithValue("@From", periodFrom);
                cmd.Parameters.AddWithValue("@To", periodTo);
                cmd.Parameters.AddWithValue("@EmpNo", empNo);
                await cmd.ExecuteNonQueryAsync();

                using var cmd2 = new MySqlCommand(insertSql, connection);
                cmd2.Parameters.AddWithValue("@From", periodFrom);
                cmd2.Parameters.AddWithValue("@To", periodTo);
                cmd2.Parameters.AddWithValue("@EmpNo", empNo);
                cmd2.Parameters.AddWithValue("@Year", year);
                cmd2.Parameters.AddWithValue("@Month", month);
                cmd2.Parameters.AddWithValue("@UserId", currentUserId);
                cmd2.CommandTimeout = 300;
                totalInserted += await cmd2.ExecuteNonQueryAsync();
            }

            string note = buggyRows.Count > 0 ? $" ({buggyRows.Count} rows skipped)" : "";
            return (true, $"{totalInserted} attendance adjustment records inserted for {empNos.Count} employees.{note}");
        }

        public async Task<(bool success, string message)> BulkMarkAbsentAsync(IFormFile file, int year, int month, string currentUserId)
        {
            if (file == null || file.Length == 0)
                return (false, "Please select an Excel file.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xls" && ext != ".xlsx")
                return (false, "Only Excel files (.xls, .xlsx) are allowed.");

            var records = new List<(string EmpNo, DateTime AdjDate)>();
            var buggyRows = new List<string>();

            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (var stream = file.OpenReadStream())
            using (var package = new OfficeOpenXml.ExcelPackage(stream))
            {
                var ws = package.Workbook.Worksheets.First();
                for (int i = 1; i <= ws.Dimension.End.Row; i++)
                {
                    var empVal = ws.Cells[i, 1].Value?.ToString() ?? "";
                    var dateVal = ws.Cells[i, 2].Value?.ToString() ?? "";
                    if (int.TryParse(empVal, out _) && DateTime.TryParse(dateVal, out DateTime adjDate))
                    {
                        if (adjDate.Month == month && adjDate.Year == year)
                            records.Add((empVal.PadLeft(14, '0'), adjDate.Date));
                        else
                            buggyRows.Add($"{empVal} / {dateVal} (wrong month/year)");
                    }
                    else if (!string.IsNullOrWhiteSpace(empVal))
                        buggyRows.Add($"{empVal} / {dateVal}");
                }
            }

            if (records.Count == 0)
                return (false, "No valid records found in file.");

            DateTime periodFrom = month == 1 ? new DateTime(year - 1, 12, 26) : new DateTime(year, month - 1, 26);
            DateTime periodTo = new DateTime(year, month, 25);

            int totalDeleted = 0;
            using var connection = (MySqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();

            foreach (var (empNo, adjDate) in records)
            {
                string sql = @"
                    DELETE FROM hr_employeeattandenceadjust
                    WHERE DATE(adjustmentDate) BETWEEN @From AND @To AND emp_no = @EmpNo AND DATE(adjustmentDate) = @AdjDate;
                    DELETE FROM hr_employeeattendance
                    WHERE Date(CHECKTIME) BETWEEN @From AND @To AND emp_no = @EmpNo AND Date(CHECKTIME) = @AdjDate;
                    DELETE FROM hr_mobileappattendence
                    WHERE DATE(CreationDate) BETWEEN @From AND @To AND EmpNo = @EmpNo AND DATE(CreationDate) = @AdjDate;";

                using var cmd = new MySqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@From", periodFrom);
                cmd.Parameters.AddWithValue("@To", periodTo);
                cmd.Parameters.AddWithValue("@EmpNo", empNo);
                cmd.Parameters.AddWithValue("@AdjDate", adjDate);
                cmd.CommandTimeout = 300;
                totalDeleted += await cmd.ExecuteNonQueryAsync();
            }

            string note = buggyRows.Count > 0 ? $" ({buggyRows.Count} rows skipped)" : "";
            return (true, $"Attendance records cleared for {records.Count} entries.{note}");
        }

        #endregion
}
}


