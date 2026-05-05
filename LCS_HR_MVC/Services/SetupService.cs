using System.Data;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class SetupService : ISetupService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public SetupService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<CountryModel>> GetAllCountriesAsync()
        {
            var countries = new List<CountryModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return countries;
                await connection.OpenAsync();

                string query = "SELECT CODE, FullName, ShortName FROM hr_country ORDER BY CODE DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        countries.Add(new CountryModel
                        {
                            Code = reader["CODE"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return countries;
        }

        public async Task<bool> IsCountryExistsAsync(string fullName, string shortName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_country WHERE (FullName = @fname OR ShortName = @sname) LIMIT 1"
                    : "SELECT 1 FROM hr_country WHERE (FullName = @fname OR ShortName = @sname) AND CODE <> @code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@fname", fullName);
                    command.Parameters.AddWithValue("@sname", shortName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        private async Task<string> GenerateNewCountryCodeAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return "001";
                await connection.OpenAsync();
                
                string query = "SELECT MAX(CAST(CODE AS UNSIGNED)) FROM hr_country";
                using (var command = new MySqlCommand(query, connection))
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != DBNull.Value && result != null)
                    {
                        int maxId = Convert.ToInt32(result);
                        return (maxId + 1).ToString("D3"); // Usually countries are 3 digits
                    }
                }
                return "001";
            }
        }

        public async Task<bool> AddCountryAsync(CountryModel model, string currentUserId)
        {
            model.Code = await GenerateNewCountryCodeAsync();

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"INSERT INTO hr_country (CODE, FullName, ShortName, createdby, created_date, updatedby, updated_date) 
                                 VALUES (@code, @fname, @sname, @userid, @date, @userid, @date)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", model.Code);
                    command.Parameters.AddWithValue("@fname", model.FullName);
                    command.Parameters.AddWithValue("@sname", model.ShortName);
                    command.Parameters.AddWithValue("@userid", currentUserId);
                    command.Parameters.AddWithValue("@date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateCountryAsync(CountryModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_country SET FullName = @fname, ShortName = @sname, updatedby = @userid, updated_date = @date 
                                 WHERE CODE = @code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", model.Code);
                    command.Parameters.AddWithValue("@fname", model.FullName);
                    command.Parameters.AddWithValue("@sname", model.ShortName);
                    command.Parameters.AddWithValue("@userid", currentUserId);
                    command.Parameters.AddWithValue("@date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteCountryAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_country WHERE CODE = @code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<ProvinceModel>> GetAllProvincesAsync()
        {
            var provinces = new List<ProvinceModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return provinces;
                await connection.OpenAsync();

                string query = @"SELECT p.Code, p.CountryCode, c.FullName as countryname, p.FullName, p.ShortName 
                                 FROM hr_provinces p 
                                 INNER JOIN hr_country c ON p.CountryCode = c.Code 
                                 ORDER BY p.Code DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        provinces.Add(new ProvinceModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            CountryCode = reader["CountryCode"].ToString() ?? string.Empty,
                            CountryName = reader["countryname"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return provinces;
        }

        public async Task<bool> IsProvinceExistsAsync(string countryCode, string fullName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_provinces WHERE CountryCode = @countryCode AND FullName = @fname LIMIT 1"
                    : "SELECT 1 FROM hr_provinces WHERE CountryCode = @countryCode AND FullName = @fname AND CODE <> @code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@countryCode", countryCode);
                    command.Parameters.AddWithValue("@fname", fullName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        private async Task<string> GenerateNewProvinceCodeAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return "001";
                await connection.OpenAsync();
                
                string query = "SELECT MAX(CAST(CODE AS UNSIGNED)) FROM hr_provinces";
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

        public async Task<bool> AddProvinceAsync(ProvinceModel model, string currentUserId)
        {
            model.Code = await GenerateNewProvinceCodeAsync();

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"INSERT INTO hr_provinces (CODE, CountryCode, FullName, ShortName, createdby, createddate, updatedby, updateddate) 
                                 VALUES (@code, @CountryCode, @fname, @sname, @userid, @date, @userid, @date)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", model.Code);
                    command.Parameters.AddWithValue("@CountryCode", model.CountryCode);
                    command.Parameters.AddWithValue("@fname", model.FullName);
                    command.Parameters.AddWithValue("@sname", model.ShortName);
                    command.Parameters.AddWithValue("@userid", currentUserId);
                    command.Parameters.AddWithValue("@date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateProvinceAsync(ProvinceModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_provinces SET FullName = @fname, ShortName = @sname, CountryCode = @CountryCode, 
                                 updatedby = @userid, updateddate = @date 
                                 WHERE CODE = @code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", model.Code);
                    command.Parameters.AddWithValue("@CountryCode", model.CountryCode);
                    command.Parameters.AddWithValue("@fname", model.FullName);
                    command.Parameters.AddWithValue("@sname", model.ShortName);
                    command.Parameters.AddWithValue("@userid", currentUserId);
                    command.Parameters.AddWithValue("@date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteProvinceAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_provinces WHERE CODE = @code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<CityModel>> GetAllCitiesAsync()
        {
            var cities = new List<CityModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return cities;
                await connection.OpenAsync();

                string query = @"
                    SELECT
                        city.Code,
                        prv.FullName province,
                        prv.Code ProvinceCode,
                        coun.FullName country,
                        coun.Code CountryCode,
                        zone.FullName zone,
                        zone.Code ZoneCode,
                        city.FullName,
                        city.ShortName,
                        city.station_id
                    FROM hr_city city
                    INNER JOIN hr_provinces prv ON city.ProvCode = prv.Code
                    INNER JOIN hr_country coun ON city.CountryCode = coun.Code
                    INNER JOIN hr_regionalzones zone ON city.RZoneCode = zone.Code
                    ORDER BY city.Code DESC";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        cities.Add(new CityModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            ProvinceName = reader["province"].ToString() ?? string.Empty,
                            ProvinceCode = reader["ProvinceCode"].ToString() ?? string.Empty,
                            CountryName = reader["country"].ToString() ?? string.Empty,
                            CountryCode = reader["CountryCode"].ToString() ?? string.Empty,
                            ZoneName = reader["zone"].ToString() ?? string.Empty,
                            ZoneCode = reader["ZoneCode"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty,
                            StationId = reader["station_id"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return cities;
        }
        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetZonesAsync()
        {
            var zones = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return zones;
                await connection.OpenAsync();
                
                string query = "SELECT CODE, FullName FROM hr_regionalzones";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        zones.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["CODE"].ToString(),
                            Text = reader["FullName"].ToString()
                        });
                    }
                }
            }
            return zones;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetProvincesByCountryAsync(string countryCode)
        {
            var provinces = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return provinces;
                await connection.OpenAsync();

                string query = "SELECT CODE, FullName FROM hr_provinces WHERE CountryCode=@CountryCode";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CountryCode", countryCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            provinces.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = reader["CODE"].ToString(),
                                Text = reader["FullName"].ToString()
                            });
                        }
                    }
                }
            }
            return provinces;
        }

        public async Task<bool> IsCityExistsAsync(string fullName, string shortName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_city WHERE FullName=@FullName OR ShortName=@ShortName LIMIT 1"
                    : "SELECT 1 FROM hr_city WHERE (FullName=@FullName OR ShortName=@ShortName) AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", fullName);
                    command.Parameters.AddWithValue("@ShortName", shortName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        private async Task<string> GenerateNewCityCodeAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return "001";
                await connection.OpenAsync();
                
                string query = "SELECT MAX(CAST(Code AS UNSIGNED)) FROM hr_city";
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

        public async Task<bool> AddCityAsync(CityModel model, string currentUserId)
        {
            model.Code = await GenerateNewCityCodeAsync();

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"INSERT INTO hr_city (Code, ProvCode, CountryCode, RZoneCode, FullName, ShortName, station_id, branch_id, CreatedBy, Created_Date, UpdatedBy, Updated_Date) 
                                 VALUES (@Code, @ProvCode, @CountryCode, @RZoneCode, @FullName, @ShortName, @station_id, @branch_id, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@ProvCode", model.ProvinceCode);
                    command.Parameters.AddWithValue("@CountryCode", model.CountryCode);
                    command.Parameters.AddWithValue("@RZoneCode", model.ZoneCode);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@station_id", model.StationId);
                    command.Parameters.AddWithValue("@branch_id", model.BranchId);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateCityAsync(CityModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_city 
                                 SET ProvCode = @ProvCode, CountryCode = @CountryCode, RZoneCode = @RZoneCode, 
                                     FullName = @FullName, ShortName = @ShortName, station_id = @station_id, branch_id = @branch_id,
                                     UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                 WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@ProvCode", model.ProvinceCode);
                    command.Parameters.AddWithValue("@CountryCode", model.CountryCode);
                    command.Parameters.AddWithValue("@RZoneCode", model.ZoneCode);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@station_id", model.StationId);
                    command.Parameters.AddWithValue("@branch_id", model.BranchId);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteCityAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_city WHERE Code = @code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<int> GetExtraFixedDaysAsync(string cityId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return 0;
                await connection.OpenAsync();

                string query = "SELECT Value FROM lcs_hr.hr_employee_extrasdays_fixed WHERE City_id = @cityID";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@cityID", cityId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result);
                    }
                    return 0;
                }
            }
        }

        public async Task<IEnumerable<DepartmentModel>> GetAllDepartmentsAsync()
        {
            var depts = new List<DepartmentModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return depts;
                await connection.OpenAsync();

                string query = @"SELECT 
                                    sd.SDID AS SDID,
                                    pd.PDID AS PDID,
                                    pd.PDName AS PdeptName,
                                    sd.FullName AS SdeptName,
                                    sd.Courier_Dept AS CourierDept,
                                    sd.ShortName AS ShortSDname,
                                    c.CompanyID CID,
                                    c.Name AS Company,
                                    b.BUID AS BUID,
                                    b.Name AS Bunit 
                                FROM hr_parentdepartment pd
                                INNER JOIN hr_subdepartment sd ON sd.ParentID = pd.PDID
                                INNER JOIN lcs_setup.company c ON c.CompanyID = pd.CompanyId
                                INNER JOIN lcs_setup.businessunit b ON b.BUID = pd.BUID
                                WHERE sd.Isdeleted = 0";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        depts.Add(new DepartmentModel
                        {
                            SDID = reader["SDID"].ToString() ?? string.Empty,
                            PDID = reader["PDID"].ToString() ?? string.Empty,
                            PdeptName = reader["PdeptName"].ToString() ?? string.Empty,
                            SdeptName = reader["SdeptName"].ToString() ?? string.Empty,
                            CourierDept = reader["CourierDept"].ToString() ?? "N",
                            ShortSDname = reader["ShortSDname"].ToString() ?? string.Empty,
                            CID = reader["CID"].ToString() ?? string.Empty,
                            Company = reader["Company"].ToString() ?? string.Empty,
                            BUID = reader["BUID"].ToString() ?? string.Empty,
                            Bunit = reader["Bunit"].ToString() ?? string.Empty,
                            IsCourierDept = reader["CourierDept"].ToString() == "Y"
                        });
                    }
                }
            }
            return depts;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCompaniesAsync()
        {
            var companies = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return companies;
                await connection.OpenAsync();
                
                string query = "SELECT CompanyID, Name FROM lcs_setup.company WHERE IsActive=1";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        companies.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["CompanyID"].ToString(),
                            Text = reader["Name"].ToString()
                        });
                    }
                }
            }
            return companies;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetBusinessUnitsAsync()
        {
            var bus = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return bus;
                await connection.OpenAsync();
                
                string query = "SELECT BUID, Name FROM lcs_setup.businessunit WHERE IsDeleted=0 ORDER BY BUID DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        bus.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["BUID"].ToString(),
                            Text = reader["Name"].ToString()
                        });
                    }
                }
            }
            return bus;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetParentDepartmentsAsync()
        {
            var parents = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return parents;
                await connection.OpenAsync();
                
                string query = "SELECT PDID, PDName FROM hr_parentdepartment WHERE IsDeleted = 0";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        parents.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["PDID"].ToString(),
                            Text = reader["PDName"].ToString()
                        });
                    }
                }
            }
            return parents;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetParentDepartmentsByIDAsync(int companyId, int buId)
        {
            var parents = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return parents;
                await connection.OpenAsync();

                string query = @"SELECT PDID, PDName
                                 FROM hr_parentdepartment
                                 WHERE IsDeleted = 0
                                   AND (@CompanyId = 0 OR CompanyID = @CompanyId)
                                   AND (@BuId = 0 OR BUID = @BuId)
                                 ORDER BY PDName ASC";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CompanyId", companyId);
                    command.Parameters.AddWithValue("@BuId", buId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            parents.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = reader["PDID"].ToString(),
                                Text = reader["PDName"].ToString()
                            });
                        }
                    }
                }
            }
            return parents;
        }

        public async Task<bool> IsDepartmentExistsAsync(string fullName, string shortName, string companyId, string buId, bool isParent, string parentId, string? excludeSdid = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (isParent && string.IsNullOrEmpty(excludeSdid))
                {
                    string query = "SELECT 1 FROM hr_parentdepartment WHERE (PDName=@FullName OR ShortName=@ShortName) AND CompanyID=@CompanyID AND BUID=@BUID LIMIT 1";
                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@FullName", fullName);
                        command.Parameters.AddWithValue("@ShortName", shortName);
                        command.Parameters.AddWithValue("@CompanyID", companyId);
                        command.Parameters.AddWithValue("@BUID", buId);
                        var result = await command.ExecuteScalarAsync();
                        return result != null;
                    }
                }
                else
                {
                    string query = string.IsNullOrEmpty(excludeSdid)
                        ? "SELECT 1 FROM hr_subdepartment WHERE (FullName=@FullName OR ShortName=@ShortName) AND ParentID=@ParentID LIMIT 1"
                        : "SELECT 1 FROM hr_subdepartment WHERE (FullName=@FullName OR ShortName=@ShortName) AND SDID<>@SDID LIMIT 1";

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@FullName", fullName);
                        command.Parameters.AddWithValue("@ShortName", shortName);
                        if (!string.IsNullOrEmpty(excludeSdid))
                            command.Parameters.AddWithValue("@SDID", excludeSdid);
                        else
                            command.Parameters.AddWithValue("@ParentID", parentId);

                        var result = await command.ExecuteScalarAsync();
                        return result != null;
                    }
                }
            }
        }

        private async Task<string> GenerateNewIdAsync(MySqlConnection connection, MySqlTransaction transaction, string table, string column, int digits)
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

        public async Task<bool> AddDepartmentAsync(DepartmentModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string sdid = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_subdepartment", "SDID", 3);
                        string pdid = model.PDID;

                        if (model.IsParent)
                        {
                            pdid = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_parentdepartment", "PDID", 3);
                            
                            string insertParent = @"INSERT INTO hr_parentdepartment VALUES(@PDID, @FullNAME, @ShortName, @CompanyID, @BUID, @IsDeleted, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                            using (var cmd = new MySqlCommand(insertParent, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@PDID", pdid);
                                cmd.Parameters.AddWithValue("@FullNAME", model.SdeptName);
                                cmd.Parameters.AddWithValue("@ShortName", model.ShortSDname);
                                cmd.Parameters.AddWithValue("@CompanyID", model.CID);
                                cmd.Parameters.AddWithValue("@BUID", model.BUID);
                                cmd.Parameters.AddWithValue("@IsDeleted", false);
                                cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                                cmd.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                                cmd.Parameters.AddWithValue("@UpdatedBy", DBNull.Value);
                                cmd.Parameters.AddWithValue("@Updated_Date", DBNull.Value);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string insertSub = @"INSERT INTO hr_subdepartment VALUES(@SDID, @FullNAME, @ShortName, @PDID, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date, @Courier_Dept, @IsDeleted)";
                        using (var cmd = new MySqlCommand(insertSub, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@SDID", sdid);
                            cmd.Parameters.AddWithValue("@FullNAME", model.SdeptName);
                            cmd.Parameters.AddWithValue("@ShortName", model.ShortSDname);
                            cmd.Parameters.AddWithValue("@PDID", pdid);
                            cmd.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@UpdatedBy", DBNull.Value);
                            cmd.Parameters.AddWithValue("@Updated_Date", DBNull.Value);
                            cmd.Parameters.AddWithValue("@Courier_Dept", model.IsCourierDept ? "Y" : "N");
                            cmd.Parameters.AddWithValue("@IsDeleted", false);
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

        public async Task<bool> UpdateDepartmentAsync(DepartmentModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        if (model.IsParent)
                        {
                            string updateParent = @"UPDATE hr_parentdepartment 
                                                    SET PDName = @FullNAME, ShortName = @ShortName, CompanyID = @CompanyID, BUID = @BUID, 
                                                    UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date
                                                    WHERE PDID = @PDID";
                            using (var cmd = new MySqlCommand(updateParent, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@PDID", model.PDID);
                                cmd.Parameters.AddWithValue("@FullNAME", model.SdeptName);
                                cmd.Parameters.AddWithValue("@ShortName", model.ShortSDname);
                                cmd.Parameters.AddWithValue("@CompanyID", model.CID);
                                cmd.Parameters.AddWithValue("@BUID", model.BUID);
                                cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                                cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string updateSub = @"UPDATE hr_subdepartment 
                                             SET FullName = @FullNAME, ShortName = @ShortName, ParentID = @PDID, 
                                             UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date, Courier_Dept = @Courier_Dept
                                             WHERE SDID = @SDID";
                        using (var cmd = new MySqlCommand(updateSub, connection, transaction as MySqlTransaction))
                        {
                            cmd.Parameters.AddWithValue("@SDID", model.SDID);
                            cmd.Parameters.AddWithValue("@FullNAME", model.SdeptName);
                            cmd.Parameters.AddWithValue("@ShortName", model.ShortSDname);
                            cmd.Parameters.AddWithValue("@PDID", model.PDID);
                            cmd.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            cmd.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            cmd.Parameters.AddWithValue("@Courier_Dept", model.IsCourierDept ? "Y" : "N");
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

        public async Task<bool> DeleteDepartmentAsync(string sdid, string pdid)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "UPDATE hr_subdepartment SET IsDeleted = 1 WHERE SDID = @SDID AND ParentID = @PDID";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SDID", sdid);
                    command.Parameters.AddWithValue("@PDID", pdid);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<DivisionModel>> GetAllDivisionsAsync()
        {
            var divisions = new List<DivisionModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return divisions;
                await connection.OpenAsync();

                string query = "SELECT BUID, Name, ShortName FROM lcs_setup.businessunit ORDER BY BUID DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        divisions.Add(new DivisionModel
                        {
                            BUID = reader["BUID"].ToString() ?? string.Empty,
                            FullName = reader["Name"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return divisions;
        }

        public async Task<bool> IsDivisionExistsAsync(string fullName, string shortName, string? excludeId = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeId)
                    ? "SELECT 1 FROM lcs_setup.businessunit WHERE (Name=@FullName OR ShortName=@ShortName) LIMIT 1"
                    : "SELECT 1 FROM lcs_setup.businessunit WHERE (Name=@FullName OR ShortName=@ShortName) AND BUID<>@BUID LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", fullName);
                    command.Parameters.AddWithValue("@ShortName", shortName);
                    if (!string.IsNullOrEmpty(excludeId))
                    {
                        command.Parameters.AddWithValue("@BUID", excludeId);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddDivisionAsync(DivisionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"INSERT INTO lcs_setup.businessunit 
                                 (Name, ShortName, IsActive, CreatedBy, Created_Date, isDeleted) 
                                 VALUES (@FullName, @ShortName, 0, @CreatedBy, @Created_Date, 0)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateDivisionAsync(DivisionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE lcs_setup.businessunit 
                                 SET Name = @FullName, ShortName = @ShortName, UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                 WHERE BUID = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.BUID);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<JobModel>> GetAllJobsAsync()
        {
            var jobs = new List<JobModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return jobs;
                await connection.OpenAsync();

                string query = @"SELECT J.`Code`, J.`FullName`, J.`ShortName`, J.`Level`, d.`PDID`,
                                        d.`PDName`, sd.`SDID`, sd.`FullName` AS SDName, j.`IsEligible`
                                 FROM `hr_jobs` j
                                 INNER JOIN hr_designationmapping dm ON dm.`DesignationId` = j.`Code`
                                 INNER JOIN `hr_parentdepartment` d ON d.`PDID` = dm.`PDeptId`
                                 INNER JOIN hr_subdepartment sd ON sd.`ParentID` = d.`PDID` AND sd.`SDID` = dm.`SDeptId`
                                 ORDER BY j.Code DESC";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        jobs.Add(new JobModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty,
                            Level = reader["Level"].ToString() ?? string.Empty,
                            ParentDeptId = reader["PDID"].ToString() ?? string.Empty,
                            ParentDeptName = reader["PDName"].ToString() ?? string.Empty,
                            SubDeptId = reader["SDID"].ToString() ?? string.Empty,
                            SubDeptName = reader["SDName"].ToString() ?? string.Empty,
                            IsEligible = reader["IsEligible"] != DBNull.Value && Convert.ToInt32(reader["IsEligible"]) == 1
                        });
                    }
                }
            }
            return jobs;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetSubDepartmentsByParentAsync(string parentId)
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
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
                            items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
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

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAllSubDepartmentsAsync()
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();

                const string query = @"SELECT SDID, FullName
                                       FROM hr_subdepartment
                                       WHERE IsDeleted = 0
                                       ORDER BY FullName";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["SDID"].ToString(),
                            Text = reader["FullName"].ToString()
                        });
                    }
                }
            }
            return items;
        }

        public async Task<bool> IsJobExistsAsync(string fullName, string shortName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_jobs WHERE FullName=@FullName OR ShortName=@ShortName LIMIT 1"
                    : "SELECT 1 FROM hr_jobs WHERE (FullName=@FullName OR ShortName=@ShortName) AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", fullName);
                    command.Parameters.AddWithValue("@ShortName", shortName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddJobAsync(JobModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string code = await GenerateNewIdAsync(connection, transaction as MySqlTransaction, "hr_jobs", "Code", 3);

                        string insertQuery = @"INSERT INTO hr_jobs 
                                               (Code, FullName, ShortName, Level, CreatedBy, Created_Date, UpdatedBy, Updated_Date, IsEligible) 
                                               VALUES (@Code, @FullName, @ShortName, @Level, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date, @IsEligible)";
                        using (var command = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            command.Parameters.AddWithValue("@FullName", model.FullName);
                            command.Parameters.AddWithValue("@ShortName", model.ShortName);
                            command.Parameters.AddWithValue("@Level", model.Level);
                            command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            command.Parameters.AddWithValue("@IsEligible", model.IsEligible ? 1 : 0);
                            await command.ExecuteNonQueryAsync();
                        }

                        string mappingQuery = "INSERT INTO hr_designationmapping (PDeptId, SDeptId, DesignationId) VALUES (@PDeptId, @SDeptId, @Code)";
                        using (var command = new MySqlCommand(mappingQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@PDeptId", model.ParentDeptId);
                            command.Parameters.AddWithValue("@SDeptId", model.SubDeptId);
                            command.Parameters.AddWithValue("@Code", code);
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

        public async Task<bool> UpdateJobAsync(JobModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string updateQuery = @"UPDATE hr_jobs 
                                               SET FullName = @FullName, ShortName = @ShortName, Level = @Level, 
                                                   UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date, IsEligible = @IsEligible 
                                               WHERE Code = @Code";
                        using (var command = new MySqlCommand(updateQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", model.Code);
                            command.Parameters.AddWithValue("@FullName", model.FullName);
                            command.Parameters.AddWithValue("@ShortName", model.ShortName);
                            command.Parameters.AddWithValue("@Level", model.Level);
                            command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                            command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                            command.Parameters.AddWithValue("@IsEligible", model.IsEligible ? 1 : 0);
                            await command.ExecuteNonQueryAsync();
                        }

                        string mappingQuery = "UPDATE hr_designationmapping SET PDeptId = @PDeptId, SDeptId = @SDeptId WHERE DesignationId = @Code";
                        using (var command = new MySqlCommand(mappingQuery, connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@PDeptId", model.ParentDeptId);
                            command.Parameters.AddWithValue("@SDeptId", model.SubDeptId);
                            command.Parameters.AddWithValue("@Code", model.Code);
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

        public async Task<bool> DeleteJobAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        using (var command = new MySqlCommand("DELETE FROM hr_jobs WHERE Code = @Code", connection, transaction as MySqlTransaction))
                        {
                            command.Parameters.AddWithValue("@Code", code);
                            await command.ExecuteNonQueryAsync();
                        }

                        using (var command = new MySqlCommand("DELETE FROM hr_designationmapping WHERE DesignationId = @Code", connection, transaction as MySqlTransaction))
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
                        return false;
                    }
                }
            }
        }

        public async Task<IEnumerable<EmployeeTypeModel>> GetAllEmployeeTypesAsync()
        {
            var types = new List<EmployeeTypeModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return types;
                await connection.OpenAsync();

                string query = "SELECT Code, FullName, ShortName FROM hr_employeetype ORDER BY Code DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        types.Add(new EmployeeTypeModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return types;
        }

        public async Task<bool> IsEmployeeTypeExistsAsync(string fullName, string shortName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_employeetype WHERE (FullName=@FullName OR ShortName=@ShortName) LIMIT 1"
                    : "SELECT 1 FROM hr_employeetype WHERE (FullName=@FullName OR ShortName=@ShortName) AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", fullName);
                    command.Parameters.AddWithValue("@ShortName", shortName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddEmployeeTypeAsync(EmployeeTypeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_employeetype", "Code", 3);

                string query = @"INSERT INTO hr_employeetype (Code, FullName, ShortName, IsActive, CreatedBy, Created_Date, UpdatedBy, Updated_Date) 
                                 VALUES (@Code, @FullName, @ShortName, b'0', @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateEmployeeTypeAsync(EmployeeTypeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_employeetype 
                                 SET FullName = @FullName, ShortName = @ShortName, UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                 WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteEmployeeTypeAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_employeetype WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<RegionalZoneModel>> GetAllRegionalZonesAsync()
        {
            var zones = new List<RegionalZoneModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return zones;
                await connection.OpenAsync();

                string query = "SELECT Code, FullName, ShortName FROM hr_regionalzones ORDER BY Code DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        zones.Add(new RegionalZoneModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return zones;
        }

        public async Task<bool> IsRegionalZoneExistsAsync(string fullName, string shortName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_regionalzones WHERE (FullName=@FullName OR ShortName=@ShortName) LIMIT 1"
                    : "SELECT 1 FROM hr_regionalzones WHERE (FullName=@FullName OR ShortName=@ShortName) AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", fullName);
                    command.Parameters.AddWithValue("@ShortName", shortName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddRegionalZoneAsync(RegionalZoneModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_regionalzones", "Code", 3);

                string query = @"INSERT INTO hr_regionalzones (Code, FullName, ShortName, CreatedBy, Created_Date, UpdatedBy, Updated_Date) 
                                 VALUES (@Code, @FullName, @ShortName, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateRegionalZoneAsync(RegionalZoneModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_regionalzones 
                                 SET FullName = @FullName, ShortName = @ShortName, UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                 WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteRegionalZoneAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_regionalzones WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<SalaryBankModel>> GetAllSalaryBanksAsync(string currentUserId)
        {
            var banks = new List<SalaryBankModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return banks;
                await connection.OpenAsync();

                string query = @"SELECT hs.Code, hs.city_id, hc.FullName cityname, hs.bank_desc, hs.bank_glcode, hs.name, hs.address
                                 FROM hr_salarybanks hs 
                                 INNER JOIN hr_city hc ON hs.city_id = hc.Code 
                                 INNER JOIN lcs_user_location lul ON hc.Code = lul.city_code 
                                 WHERE lul.userid=@UserId ORDER BY hs.Code DESC";
                
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            banks.Add(new SalaryBankModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                CityId = reader["city_id"].ToString() ?? string.Empty,
                                CityName = reader["cityname"].ToString() ?? string.Empty,
                                BankDesc = reader["bank_desc"].ToString() ?? string.Empty,
                                BankGlCode = reader["bank_glcode"].ToString() ?? string.Empty,
                                Name = reader["name"].ToString() ?? string.Empty,
                                Address = reader["address"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return banks;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCitiesByUserAsync(string currentUserId)
        {
            var cities = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return cities;
                await connection.OpenAsync();

                string query = @"SELECT c.Code, c.FullName 
                                 FROM hr_city c 
                                 INNER JOIN lcs_user_location ul ON c.Code = ul.city_code 
                                 WHERE ul.userid = @UserId AND c.CountryCode = '001' ORDER BY c.FullName";
                
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            cities.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = reader["Code"].ToString(),
                                Text = reader["FullName"].ToString()
                            });
                        }
                    }
                }
            }
            return cities;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetBanksByCityAsync(string cityCode)
        {
            var banks = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return banks;
                await connection.OpenAsync();

                string query = "SELECT gcoa.GLcode, gcoa.DESCRIPTION FROM hr_city hc INNER JOIN lcs_gl.gl_chart_of_acc gcoa ON hc.station_id=gcoa.REMARKS2 WHERE hc.Code=@city";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@city", cityCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            banks.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = reader["GLcode"].ToString(),
                                Text = reader["DESCRIPTION"].ToString()
                            });
                        }
                    }
                }
            }
            return banks;
        }

        public async Task<bool> IsSalaryBankExistsAsync(string cityId, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_salarybanks WHERE city_id=@city_id LIMIT 1"
                    : "SELECT 1 FROM hr_salarybanks WHERE city_id=@city_id AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@city_id", cityId);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddSalaryBankAsync(SalaryBankModel model, string currentUserId, string bankDesc)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_salarybanks", "Code", 3);

                string query = @"INSERT INTO hr_salarybanks (Code, city_id, bank_glcode, bank_desc, bankname, bankaddress, CreatedBy, Created_Date, UpdatedBy, Updated_Date) 
                                 VALUES (@Code, @city_id, @bank_glcode, @bank_desc, @bankname, @bankaddress, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@city_id", model.CityId);
                    command.Parameters.AddWithValue("@bank_glcode", model.BankGlCode);
                    command.Parameters.AddWithValue("@bank_desc", bankDesc);
                    command.Parameters.AddWithValue("@bankname", model.Name);
                    command.Parameters.AddWithValue("@bankaddress", model.Address);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateSalaryBankAsync(SalaryBankModel model, string currentUserId, string bankDesc)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_salarybanks 
                                 SET city_id=@city_id, bank_glcode=@bank_glcode, bank_desc=@bank_desc, name=@bankname, address=@bankaddress, UpdatedBy=@UpdatedBy, updateddate=@Updated_Date 
                                 WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@city_id", model.CityId);
                    command.Parameters.AddWithValue("@bank_glcode", model.BankGlCode);
                    command.Parameters.AddWithValue("@bank_desc", bankDesc);
                    command.Parameters.AddWithValue("@bankname", model.Name);
                    command.Parameters.AddWithValue("@bankaddress", model.Address);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteSalaryBankAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_salarybanks WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<LoanTypeModel>> GetAllLoanTypesAsync()
        {
            var types = new List<LoanTypeModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return types;
                await connection.OpenAsync();

                string query = "SELECT Code, FullName, ShortName, Comments FROM hr_loantypes ORDER BY Code DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        types.Add(new LoanTypeModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty,
                            Comments = reader["Comments"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return types;
        }

        public async Task<bool> IsLoanTypeExistsAsync(string fullName, string shortName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_loantypes WHERE (FullName=@FullName OR ShortName=@ShortName) LIMIT 1"
                    : "SELECT 1 FROM hr_loantypes WHERE (FullName=@FullName OR ShortName=@ShortName) AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@FullName", fullName);
                    command.Parameters.AddWithValue("@ShortName", shortName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddLoanTypeAsync(LoanTypeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_loantypes", "Code", 3);

                string query = @"INSERT INTO hr_loantypes (Code, FullName, ShortName, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date) 
                                 VALUES (@Code, @FullName, @ShortName, @Comments, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateLoanTypeAsync(LoanTypeModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_loantypes 
                                 SET FullName = @FullName, ShortName = @ShortName, Comments=@Comments, UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                 WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteLoanTypeAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_loantypes WHERE Code = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<ShiftModel>> GetAllShiftsAsync()
        {
            var shifts = new List<ShiftModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return shifts;
                await connection.OpenAsync();

                string query = @"
                    SELECT 
                      CODE, NAME,
                      TIME_FORMAT(Start_Time, '%H:%i') Start_Time,
                      TIME_FORMAT(End_Time, '%H:%i') End_Time,
                      TIME_FORMAT(Grace_Time_IN, '%H:%i') Grace_Time_IN,
                      TIME_FORMAT(Grace_Time_OUT, '%H:%i') Grace_Time_OUT,
                      TIME_FORMAT(Begin_IN, '%H:%i') Begin_IN,
                      TIME_FORMAT(End_IN, '%H:%i') End_IN,
                      TIME_FORMAT(Begin_OUT, '%H:%i') Begin_OUT,
                      TIME_FORMAT(End_OUT, '%H:%i') End_OUT,
                      TIME_FORMAT(OT_Start_Time, '%H:%i') OT_Start_Time,
                      NightShift 
                    FROM hr_shiftdetails WHERE active='Y' ORDER BY CODE DESC";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        shifts.Add(new ShiftModel
                        {
                            Code = reader["CODE"].ToString() ?? string.Empty,
                            Name = reader["NAME"].ToString() ?? string.Empty,
                            StartTime = reader["Start_Time"].ToString() ?? string.Empty,
                            EndTime = reader["End_Time"].ToString() ?? string.Empty,
                            GraceTimeIn = reader["Grace_Time_IN"].ToString() ?? string.Empty,
                            GraceTimeOut = reader["Grace_Time_OUT"].ToString() ?? string.Empty,
                            BeginIn = reader["Begin_IN"].ToString() ?? string.Empty,
                            EndIn = reader["End_IN"].ToString() ?? string.Empty,
                            BeginOut = reader["Begin_OUT"].ToString() ?? string.Empty,
                            EndOut = reader["End_OUT"].ToString() ?? string.Empty,
                            OverTime = reader["OT_Start_Time"] == DBNull.Value ? "00:00" : reader["OT_Start_Time"].ToString()!,
                            NightShift = reader["NightShift"].ToString() == "Y"
                        });
                    }
                }
            }
            return shifts;
        }

        public async Task<bool> IsShiftExistsAsync(string name, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_shiftdetails WHERE NAME=@Name LIMIT 1"
                    : "SELECT 1 FROM hr_shiftdetails WHERE NAME=@Name AND CODE<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Name", name);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddShiftAsync(ShiftModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_shiftdetails", "CODE", 3);

                var start = TimeSpan.Parse(model.StartTime);
                var end = TimeSpan.Parse(model.EndTime);
                var diff = end - start;
                decimal totalHours = diff.Hours; // Replicating old bug/logic decimal.Parse(TotalHours.Hours.ToString())

                string query = @"INSERT INTO hr_shiftdetails 
                                 (CODE, NAME, Start_Time, End_Time, Grace_Time_IN, Grace_Time_OUT, Begin_IN, End_IN, Begin_OUT, End_OUT, OT_Start_Time, active, NightShift, CreatedBy, Created_Date, UpdatedBy, Updated_Date, TotalHours) 
                                 VALUES (@Code, @Name, @Start_Time, @End_Time, @Grace_Time_IN, @Grace_Time_OUT, @Begin_IN, @End_IN, @Begin_OUT, @End_OUT, @OT_Start_Time, 'Y', @NightShift, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date, @TotalHours)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Name", model.Name);
                    command.Parameters.AddWithValue("@Start_Time", model.StartTime);
                    command.Parameters.AddWithValue("@End_Time", model.EndTime);
                    command.Parameters.AddWithValue("@Grace_Time_IN", model.GraceTimeIn);
                    command.Parameters.AddWithValue("@Grace_Time_OUT", model.GraceTimeOut);
                    command.Parameters.AddWithValue("@Begin_IN", model.BeginIn);
                    command.Parameters.AddWithValue("@End_IN", model.EndIn);
                    command.Parameters.AddWithValue("@Begin_OUT", model.BeginOut);
                    command.Parameters.AddWithValue("@End_OUT", model.EndOut);
                    command.Parameters.AddWithValue("@OT_Start_Time", "00:00");
                    command.Parameters.AddWithValue("@NightShift", model.NightShift ? "Y" : "N");
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@TotalHours", totalHours);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateShiftAsync(ShiftModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_shiftdetails 
                                 SET NAME = @Name, Start_Time = @Start_Time, End_Time = @End_Time, Grace_Time_IN = @Grace_Time_IN, Grace_Time_OUT = @Grace_Time_OUT, Begin_IN = @Begin_IN, End_IN = @End_IN, Begin_OUT = @Begin_OUT, End_OUT = @End_OUT, OT_Start_Time = @OT_Start_Time, NightShift = @NightShift, UpdatedBy = @UpdatedBy, Updated_Date = @Updated_Date 
                                 WHERE CODE = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Name", model.Name);
                    command.Parameters.AddWithValue("@Start_Time", model.StartTime);
                    command.Parameters.AddWithValue("@End_Time", model.EndTime);
                    command.Parameters.AddWithValue("@Grace_Time_IN", model.GraceTimeIn);
                    command.Parameters.AddWithValue("@Grace_Time_OUT", model.GraceTimeOut);
                    command.Parameters.AddWithValue("@Begin_IN", model.BeginIn);
                    command.Parameters.AddWithValue("@End_IN", model.EndIn);
                    command.Parameters.AddWithValue("@Begin_OUT", model.BeginOut);
                    command.Parameters.AddWithValue("@End_OUT", model.EndOut);
                    command.Parameters.AddWithValue("@OT_Start_Time", string.IsNullOrEmpty(model.OverTime) ? "00:00" : model.OverTime);
                    command.Parameters.AddWithValue("@NightShift", model.NightShift ? "Y" : "N");
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteShiftAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_shiftdetails WHERE CODE = @Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<AllowanceDeductionModel>> GetAllAllowanceDeductionsAsync()
        {
            var data = new List<AllowanceDeductionModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT ad.ID, c.ID as code_ID, c.Code AS Code_Type, ad.Description, u.Name AS Created_By
                                 FROM lcs_hr.hrms_allownces_deduction_code ad 
                                 INNER JOIN lcs_hr.hrms_allownce_code_type c ON c.ID=ad.Code_ID
                                 INNER JOIN lcs_hr.lcs_users u ON u.userID=ad.Created_By 
                                 ORDER BY ad.ID DESC;";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new AllowanceDeductionModel
                        {
                            ID = reader["ID"].ToString() ?? string.Empty,
                            CodeID = reader["code_ID"].ToString() ?? string.Empty,
                            CodeType = reader["Code_Type"].ToString() ?? string.Empty,
                            Description = reader["Description"].ToString() ?? string.Empty,
                            CreatedBy = reader["Created_By"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAllowanceCodeTypesAsync()
        {
            var types = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return types;
                await connection.OpenAsync();

                string query = "SELECT c.ID, c.Code FROM lcs_hr.hrms_allownce_code_type c WHERE c.IDeleted=b'0';";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        types.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["ID"].ToString(),
                            Text = reader["Code"].ToString()
                        });
                    }
                }
            }
            return types;
        }

        public async Task<bool> AddAllowanceDeductionAsync(AllowanceDeductionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"INSERT INTO lcs_hr.hrms_allownces_deduction_code 
                                 (Code_ID, Code_Type, Description, Created_By, Created_Date)
                                 VALUES (@Code_ID, @Code_Type, @Description, @Created_By, @Created_Date);";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code_ID", model.CodeID);
                    command.Parameters.AddWithValue("@Code_Type", model.CodeType);
                    command.Parameters.AddWithValue("@Description", model.Description);
                    command.Parameters.AddWithValue("@Created_By", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateAllowanceDeductionAsync(AllowanceDeductionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE lcs_hr.hrms_allownces_deduction_code 
                                 SET Code_ID=@Code_ID, Code_Type=@Code_Type, Description=@Description, Updated_By=@Updated_By, Updated_Date=@Updated_Date
                                 WHERE ID=@ID;";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ID", model.ID);
                    command.Parameters.AddWithValue("@Code_ID", model.CodeID);
                    command.Parameters.AddWithValue("@Code_Type", model.CodeType);
                    command.Parameters.AddWithValue("@Description", model.Description);
                    command.Parameters.AddWithValue("@Updated_By", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteAllowanceDeductionAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM lcs_hr.hrms_allownces_deduction_code WHERE ID=@ID;";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ID", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<AllowanceDeductionDetailModel>> GetAllAllowanceDeductionDetailsAsync()
        {
            var data = new List<AllowanceDeductionDetailModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT 
                                    ad.ID,
                                    c.Description AS `Type`,
                                    ad.AD_Code AS ADCode,
                                    ad.FullName AS FullName,
                                    u.Name AS Created_By
                                 FROM lcs_hr.hrms_allownces_dedcution_detail ad 
                                 INNER JOIN lcs_hr.hrms_allownces_deduction_code c ON c.ID=ad.Type_ID
                                 LEFT JOIN lcs_hr.lcs_users u ON u.userID=ad.Created_By 
                                 ORDER BY ad.ID DESC;";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new AllowanceDeductionDetailModel
                        {
                            ID = reader["ID"].ToString() ?? string.Empty,
                            TypeName = reader["Type"].ToString() ?? string.Empty,
                            ADCode = reader["ADCode"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            CreatedBy = reader["Created_By"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAllowanceTypesAsync()
        {
            var types = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return types;
                await connection.OpenAsync();

                string query = "SELECT c.ID, c.Description FROM lcs_hr.hrms_allownces_deduction_code c ORDER BY ID DESC;";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        types.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["ID"].ToString(),
                            Text = reader["Description"].ToString()
                        });
                    }
                }
            }
            return types;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCommissionPolicyRatesAsync()
        {
            var rates = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return rates;
                await connection.OpenAsync();

                string query = "SELECT p.Rate, p.Type FROM lcs_hr.hr_commissionpolicy p;";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        rates.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["Rate"].ToString(),
                            Text = reader["Type"].ToString()
                        });
                    }
                }
            }
            return rates;
        }

        public async Task<AllowanceDeductionDetailModel?> GetAllowanceDeductionDetailByIdAsync(string id)
        {
            AllowanceDeductionDetailModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = "SELECT * FROM lcs_hr.hrms_allownces_dedcution_detail WHERE ID=@ID;";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ID", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new AllowanceDeductionDetailModel
                            {
                                ID = reader["ID"].ToString() ?? string.Empty,
                                TypeID = reader["Type_ID"].ToString() ?? string.Empty,
                                ADCode = reader["AD_Code"].ToString() ?? string.Empty,
                                FullName = reader["FullName"].ToString() ?? string.Empty,
                                TaxFlag = reader["Tax_Flag"] != DBNull.Value && Convert.ToBoolean(reader["Tax_Flag"]),
                                OverTimeFlag = reader["Over_Time_Flag"] != DBNull.Value && Convert.ToBoolean(reader["Over_Time_Flag"]),
                                ExcludeAbsent = reader["Exclude_Absent"] != DBNull.Value && Convert.ToBoolean(reader["Exclude_Absent"]),
                                PaySlipVisible = reader["PaySlip_Visiable"] != DBNull.Value && Convert.ToBoolean(reader["PaySlip_Visiable"]),
                                IsActive = reader["Is_Active"] != DBNull.Value && Convert.ToBoolean(reader["Is_Active"]),
                                PaymentMode = reader["PaymentMode"].ToString() ?? string.Empty,
                                RateID = reader["RateID"].ToString() ?? string.Empty,
                                Comments = reader["Comments"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddAllowanceDeductionDetailAsync(AllowanceDeductionDetailModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"INSERT INTO lcs_hr.hrms_allownces_dedcution_detail (
                                    Type_ID, AD_Code, FullName, Tax_Flag, PaymentMode, Over_Time_Flag,
                                    Exclude_Absent, PaySlip_Visiable, RateID, Comments, Created_By, Created_Date, Is_Active
                                 ) VALUES (
                                    @Type_ID, @AD_Code, @FullName, @Tax_Flag, @PaymentMode, @Over_Time_Flag,
                                    @Exclude_Absent, @PaySlip_Visiable, @RateID, @Comments, @Created_By, @Created_Date, @Is_Active
                                 );";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Type_ID", model.TypeID);
                    command.Parameters.AddWithValue("@AD_Code", model.ADCode);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@Tax_Flag", model.TaxFlag);
                    command.Parameters.AddWithValue("@PaymentMode", model.PaymentMode);
                    command.Parameters.AddWithValue("@Over_Time_Flag", model.OverTimeFlag);
                    command.Parameters.AddWithValue("@Exclude_Absent", model.ExcludeAbsent);
                    command.Parameters.AddWithValue("@PaySlip_Visiable", model.PaySlipVisible);
                    command.Parameters.AddWithValue("@RateID", string.IsNullOrEmpty(model.RateID) || model.RateID == "0" ? (object)DBNull.Value : model.RateID);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@Created_By", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@Is_Active", model.IsActive);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateAllowanceDeductionDetailAsync(AllowanceDeductionDetailModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE lcs_hr.hrms_allownces_dedcution_detail SET
                                    Type_ID=@Type_ID, AD_Code=@AD_Code, FullName=@FullName, Tax_Flag=@Tax_Flag, 
                                    PaymentMode=@PaymentMode, Over_Time_Flag=@Over_Time_Flag, Exclude_Absent=@Exclude_Absent,
                                    PaySlip_Visiable=@PaySlip_Visiable, RateID=@RateID, Comments=@Comments, 
                                    Updated_By=@Updated_By, Updated_Date=@Updated_Date, Is_Active=@Is_Active
                                 WHERE ID=@ID;";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ID", model.ID);
                    command.Parameters.AddWithValue("@Type_ID", model.TypeID);
                    command.Parameters.AddWithValue("@AD_Code", model.ADCode);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@Tax_Flag", model.TaxFlag);
                    command.Parameters.AddWithValue("@PaymentMode", model.PaymentMode);
                    command.Parameters.AddWithValue("@Over_Time_Flag", model.OverTimeFlag);
                    command.Parameters.AddWithValue("@Exclude_Absent", model.ExcludeAbsent);
                    command.Parameters.AddWithValue("@PaySlip_Visiable", model.PaySlipVisible);
                    command.Parameters.AddWithValue("@RateID", string.IsNullOrEmpty(model.RateID) || model.RateID == "0" ? (object)DBNull.Value : model.RateID);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@Updated_By", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@Is_Active", model.IsActive);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteAllowanceDeductionDetailAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM lcs_hr.hrms_allownces_dedcution_detail WHERE ID=@ID;";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ID", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<GradeAllowanceModel>> GetAllGradeAllowancesAsync()
        {
            var data = new List<GradeAllowanceModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT CODE, FullName, ShortName, CASE TYPE WHEN 'A' THEN 'Allowance' ELSE 'Deduction' END TYPE, Pct_Amount, Fix_Amount 
                                 FROM hr_allow_ded_details ORDER BY Code DESC";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new GradeAllowanceModel
                        {
                            Code = reader["CODE"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty,
                            Type = reader["TYPE"].ToString() ?? string.Empty,
                            PctAmount = reader["Pct_Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Pct_Amount"]) : 0,
                            FixAmount = reader["Fix_Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Fix_Amount"]) : 0
                        });
                    }
                }
            }
            return data;
        }

        public async Task<GradeAllowanceModel?> GetGradeAllowanceByCodeAsync(string code)
        {
            GradeAllowanceModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = "SELECT CODE, FullName, ShortName, TYPE, Pct_Amount, Fix_Amount, exclude_absent, EmpWise_Flag, Comments, GlCode FROM hr_allow_ded_details WHERE CODE=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new GradeAllowanceModel
                            {
                                Code = reader["CODE"].ToString() ?? string.Empty,
                                FullName = reader["FullName"].ToString() ?? string.Empty,
                                ShortName = reader["ShortName"].ToString() ?? string.Empty,
                                Type = reader["TYPE"].ToString() ?? string.Empty,
                                PctAmount = reader["Pct_Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Pct_Amount"]) : 0,
                                FixAmount = reader["Fix_Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Fix_Amount"]) : 0,
                                ExcludeAbsent = reader["exclude_absent"].ToString() ?? "N",
                                ApplyTo = reader["EmpWise_Flag"].ToString() ?? "NIL",
                                Comments = reader["Comments"].ToString() ?? string.Empty,
                                GlCode = reader["GlCode"].ToString() ?? string.Empty
                            };

                            if (model.ApplyTo != "All" && model.ApplyTo != "NIL")
                            {
                                model.DepartmentCode = model.ApplyTo;
                                model.ApplyTo = "Department";
                            }
                        }
                    }
                }

                if (model != null && model.ApplyTo == "Department" && !string.IsNullOrEmpty(model.DepartmentCode))
                {
                    string deptQuery = "SELECT FullName FROM hr_department WHERE CODE=@Code";
                    using (var cmd = new MySqlCommand(deptQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Code", model.DepartmentCode);
                        var deptName = await cmd.ExecuteScalarAsync();
                        if (deptName != null)
                        {
                            model.DepartmentDescription = deptName.ToString();
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> IsGradeAllowanceExistsAsync(string type, string fullName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_allow_ded_details WHERE TYPE=@Type AND FullName=@FullName LIMIT 1"
                    : "SELECT 1 FROM hr_allow_ded_details WHERE TYPE=@Type AND FullName=@FullName AND CODE<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Type", type);
                    command.Parameters.AddWithValue("@FullName", fullName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddGradeAllowanceAsync(GradeAllowanceModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_allow_ded_details", "CODE", 3);

                string flag = model.ApplyTo;
                if (flag == "Department") flag = model.DepartmentCode ?? "NIL";

                string query = @"INSERT INTO hr_allow_ded_details 
                                 (CODE, FullName, ShortName, TYPE, Pct_Amount, Fix_Amount, exclude_absent, EmpWise_Flag, Comments, createdby, createddate, updatedby, updateddate, GlCode) 
                                 VALUES (@Code, @FullName, @ShortName, @Type, @PctAmount, @FixAmount, @ExcludeAbsent, @ApplyTo, @Comments, @UserId, @Date, @UserId, @Date, @GlCode)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@Type", model.Type);
                    command.Parameters.AddWithValue("@PctAmount", model.PctAmount);
                    command.Parameters.AddWithValue("@FixAmount", model.FixAmount);
                    command.Parameters.AddWithValue("@ExcludeAbsent", model.ExcludeAbsent);
                    command.Parameters.AddWithValue("@ApplyTo", flag);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);
                    command.Parameters.AddWithValue("@GlCode", model.GlCode);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateGradeAllowanceAsync(GradeAllowanceModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string flag = model.ApplyTo;
                if (flag == "Department") flag = model.DepartmentCode ?? "NIL";

                string query = @"UPDATE hr_allow_ded_details 
                                 SET FullName=@FullName, ShortName=@ShortName, TYPE=@Type, Pct_Amount=@PctAmount, Fix_Amount=@FixAmount, 
                                     exclude_absent=@ExcludeAbsent, EmpWise_Flag=@ApplyTo, Comments=@Comments, updatedby=@UserId, updated_date=@Date, GlCode=@GlCode 
                                 WHERE CODE=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@Type", model.Type);
                    command.Parameters.AddWithValue("@PctAmount", model.PctAmount);
                    command.Parameters.AddWithValue("@FixAmount", model.FixAmount);
                    command.Parameters.AddWithValue("@ExcludeAbsent", model.ExcludeAbsent);
                    command.Parameters.AddWithValue("@ApplyTo", flag);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);
                    command.Parameters.AddWithValue("@GlCode", model.GlCode);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteGradeAllowanceAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_allow_ded_details WHERE CODE=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<dynamic>> SearchDepartmentsAsync(string term)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = "SELECT FullName, CODE FROM hr_department WHERE FullName LIKE @term ORDER BY FullName LIMIT 20";
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
                                value = reader["CODE"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }

        public async Task<string?> GetGlCodeByShortNameAsync(string shortName)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = "SELECT GlCode FROM hr_allow_ded_details WHERE ShortName=@ShortName LIMIT 1";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ShortName", shortName);
                    var result = await command.ExecuteScalarAsync();
                    return result?.ToString();
                }
            }
        }

        public async Task<IEnumerable<CompanyAssetModel>> GetAllCompanyAssetsAsync()
        {
            var data = new List<CompanyAssetModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT
                                    comAsset.Code, comAsset.Name, ass_Stru.Description, comAsset.Type,
                                    comAsset.Prop1, comAsset.Prop2, comAsset.Prop3, comAsset.Prop4, comAsset.Prop5,
                                    comAsset.Prop6, comAsset.Prop7, comAsset.Prop8, comAsset.Prop9, comAsset.Prop10
                                 FROM hr_companyassets comAsset
                                 INNER JOIN hr_assetstructure ass_Stru ON comAsset.Type = ass_Stru.Code
                                 ORDER BY comAsset.Code DESC";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new CompanyAssetModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            Name = reader["Name"].ToString() ?? string.Empty,
                            Description = reader["Description"].ToString() ?? string.Empty,
                            Type = reader["Type"].ToString() ?? string.Empty,
                            Prop1 = reader["Prop1"].ToString(),
                            Prop2 = reader["Prop2"].ToString(),
                            Prop3 = reader["Prop3"].ToString(),
                            Prop4 = reader["Prop4"].ToString(),
                            Prop5 = reader["Prop5"].ToString(),
                            Prop6 = reader["Prop6"].ToString(),
                            Prop7 = reader["Prop7"].ToString(),
                            Prop8 = reader["Prop8"].ToString(),
                            Prop9 = reader["Prop9"].ToString(),
                            Prop10 = reader["Prop10"].ToString()
                        });
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetAssetTypesAsync()
        {
            var types = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return types;
                await connection.OpenAsync();

                string query = "SELECT Code, Description FROM hr_assetstructure";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        types.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                        {
                            Value = reader["Code"].ToString(),
                            Text = reader["Description"].ToString()
                        });
                    }
                }
            }
            return types;
        }

        public async Task<List<string>> GetAssetStructureLabelsAsync(string typeCode)
        {
            var labels = new List<string>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return labels;
                await connection.OpenAsync();

                string query = @"SELECT Prop1, Prop2, Prop3, Prop4, Prop5, Prop6, Prop7, Prop8, Prop9, Prop10 
                                 FROM hr_assetstructure WHERE Code=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", typeCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            for (int i = 1; i <= 10; i++)
                            {
                                string colName = "Prop" + i;
                                string label = reader[colName].ToString() ?? "";
                                if (!string.IsNullOrEmpty(label))
                                {
                                    labels.Add(label);
                                }
                            }
                        }
                    }
                }
            }
            return labels;
        }

        public async Task<bool> IsCompanyAssetExistsAsync(string name, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_companyassets WHERE Name=@Name LIMIT 1"
                    : "SELECT 1 FROM hr_companyassets WHERE Name=@Name AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Name", name);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddCompanyAssetAsync(CompanyAssetModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_companyassets", "Code", 3);

                string query = @"INSERT INTO hr_companyassets (Code, Name, Type, Prop1, Prop2, Prop3, Prop4, Prop5, Prop6, Prop7, Prop8, Prop9, Prop10, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @Name, @Type, @Prop1, @Prop2, @Prop3, @Prop4, @Prop5, @Prop6, @Prop7, @Prop8, @Prop9, @Prop10, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Name", model.Name);
                    command.Parameters.AddWithValue("@Type", model.Type);
                    command.Parameters.AddWithValue("@Prop1", string.IsNullOrEmpty(model.Prop1) ? DBNull.Value : model.Prop1);
                    command.Parameters.AddWithValue("@Prop2", string.IsNullOrEmpty(model.Prop2) ? DBNull.Value : model.Prop2);
                    command.Parameters.AddWithValue("@Prop3", string.IsNullOrEmpty(model.Prop3) ? DBNull.Value : model.Prop3);
                    command.Parameters.AddWithValue("@Prop4", string.IsNullOrEmpty(model.Prop4) ? DBNull.Value : model.Prop4);
                    command.Parameters.AddWithValue("@Prop5", string.IsNullOrEmpty(model.Prop5) ? DBNull.Value : model.Prop5);
                    command.Parameters.AddWithValue("@Prop6", string.IsNullOrEmpty(model.Prop6) ? DBNull.Value : model.Prop6);
                    command.Parameters.AddWithValue("@Prop7", string.IsNullOrEmpty(model.Prop7) ? DBNull.Value : model.Prop7);
                    command.Parameters.AddWithValue("@Prop8", string.IsNullOrEmpty(model.Prop8) ? DBNull.Value : model.Prop8);
                    command.Parameters.AddWithValue("@Prop9", string.IsNullOrEmpty(model.Prop9) ? DBNull.Value : model.Prop9);
                    command.Parameters.AddWithValue("@Prop10", string.IsNullOrEmpty(model.Prop10) ? DBNull.Value : model.Prop10);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateCompanyAssetAsync(CompanyAssetModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_companyassets 
                                 SET Name=@Name, Type=@Type, Prop1=@Prop1, Prop2=@Prop2, Prop3=@Prop3, Prop4=@Prop4, Prop5=@Prop5, Prop6=@Prop6, Prop7=@Prop7, Prop8=@Prop8, Prop9=@Prop9, Prop10=@Prop10, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                 WHERE Code=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Name", model.Name);
                    command.Parameters.AddWithValue("@Type", model.Type);
                    command.Parameters.AddWithValue("@Prop1", string.IsNullOrEmpty(model.Prop1) ? DBNull.Value : model.Prop1);
                    command.Parameters.AddWithValue("@Prop2", string.IsNullOrEmpty(model.Prop2) ? DBNull.Value : model.Prop2);
                    command.Parameters.AddWithValue("@Prop3", string.IsNullOrEmpty(model.Prop3) ? DBNull.Value : model.Prop3);
                    command.Parameters.AddWithValue("@Prop4", string.IsNullOrEmpty(model.Prop4) ? DBNull.Value : model.Prop4);
                    command.Parameters.AddWithValue("@Prop5", string.IsNullOrEmpty(model.Prop5) ? DBNull.Value : model.Prop5);
                    command.Parameters.AddWithValue("@Prop6", string.IsNullOrEmpty(model.Prop6) ? DBNull.Value : model.Prop6);
                    command.Parameters.AddWithValue("@Prop7", string.IsNullOrEmpty(model.Prop7) ? DBNull.Value : model.Prop7);
                    command.Parameters.AddWithValue("@Prop8", string.IsNullOrEmpty(model.Prop8) ? DBNull.Value : model.Prop8);
                    command.Parameters.AddWithValue("@Prop9", string.IsNullOrEmpty(model.Prop9) ? DBNull.Value : model.Prop9);
                    command.Parameters.AddWithValue("@Prop10", string.IsNullOrEmpty(model.Prop10) ? DBNull.Value : model.Prop10);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteCompanyAssetAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_companyassets WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<AssetStructureModel>> GetAllAssetStructuresAsync()
        {
            var data = new List<AssetStructureModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT Code, Description, Prop1, Prop2, Prop3, Prop4, Prop5, Prop6, Prop7, Prop8, Prop9, Prop10 FROM hr_assetstructure ORDER BY Code DESC";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new AssetStructureModel
                        {
                            Code = reader["Code"].ToString() ?? string.Empty,
                            Description = reader["Description"].ToString() ?? string.Empty,
                            Prop1 = reader["Prop1"].ToString(),
                            Prop2 = reader["Prop2"].ToString(),
                            Prop3 = reader["Prop3"].ToString(),
                            Prop4 = reader["Prop4"].ToString(),
                            Prop5 = reader["Prop5"].ToString(),
                            Prop6 = reader["Prop6"].ToString(),
                            Prop7 = reader["Prop7"].ToString(),
                            Prop8 = reader["Prop8"].ToString(),
                            Prop9 = reader["Prop9"].ToString(),
                            Prop10 = reader["Prop10"].ToString()
                        });
                    }
                }
            }
            return data;
        }

        public async Task<bool> IsAssetStructureExistsAsync(string description, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_assetstructure WHERE Description=@Description LIMIT 1"
                    : "SELECT 1 FROM hr_assetstructure WHERE Description=@Description AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Description", description);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddAssetStructureAsync(AssetStructureModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_assetstructure", "Code", 3);

                string query = @"INSERT INTO hr_assetstructure (Code, Description, Prop1, Prop2, Prop3, Prop4, Prop5, Prop6, Prop7, Prop8, Prop9, Prop10, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @Description, @Prop1, @Prop2, @Prop3, @Prop4, @Prop5, @Prop6, @Prop7, @Prop8, @Prop9, @Prop10, @CreatedBy, @Created_Date, @UpdatedBy, @Updated_Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Description", model.Description);
                    command.Parameters.AddWithValue("@Prop1", string.IsNullOrEmpty(model.Prop1) ? DBNull.Value : model.Prop1);
                    command.Parameters.AddWithValue("@Prop2", string.IsNullOrEmpty(model.Prop2) ? DBNull.Value : model.Prop2);
                    command.Parameters.AddWithValue("@Prop3", string.IsNullOrEmpty(model.Prop3) ? DBNull.Value : model.Prop3);
                    command.Parameters.AddWithValue("@Prop4", string.IsNullOrEmpty(model.Prop4) ? DBNull.Value : model.Prop4);
                    command.Parameters.AddWithValue("@Prop5", string.IsNullOrEmpty(model.Prop5) ? DBNull.Value : model.Prop5);
                    command.Parameters.AddWithValue("@Prop6", string.IsNullOrEmpty(model.Prop6) ? DBNull.Value : model.Prop6);
                    command.Parameters.AddWithValue("@Prop7", string.IsNullOrEmpty(model.Prop7) ? DBNull.Value : model.Prop7);
                    command.Parameters.AddWithValue("@Prop8", string.IsNullOrEmpty(model.Prop8) ? DBNull.Value : model.Prop8);
                    command.Parameters.AddWithValue("@Prop9", string.IsNullOrEmpty(model.Prop9) ? DBNull.Value : model.Prop9);
                    command.Parameters.AddWithValue("@Prop10", string.IsNullOrEmpty(model.Prop10) ? DBNull.Value : model.Prop10);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Created_Date", DateTime.Now);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateAssetStructureAsync(AssetStructureModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_assetstructure 
                                 SET Description=@Description, Prop1=@Prop1, Prop2=@Prop2, Prop3=@Prop3, Prop4=@Prop4, Prop5=@Prop5, Prop6=@Prop6, Prop7=@Prop7, Prop8=@Prop8, Prop9=@Prop9, Prop10=@Prop10, UpdatedBy=@UpdatedBy, Updated_Date=@Updated_Date 
                                 WHERE Code=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Description", model.Description);
                    command.Parameters.AddWithValue("@Prop1", string.IsNullOrEmpty(model.Prop1) ? DBNull.Value : model.Prop1);
                    command.Parameters.AddWithValue("@Prop2", string.IsNullOrEmpty(model.Prop2) ? DBNull.Value : model.Prop2);
                    command.Parameters.AddWithValue("@Prop3", string.IsNullOrEmpty(model.Prop3) ? DBNull.Value : model.Prop3);
                    command.Parameters.AddWithValue("@Prop4", string.IsNullOrEmpty(model.Prop4) ? DBNull.Value : model.Prop4);
                    command.Parameters.AddWithValue("@Prop5", string.IsNullOrEmpty(model.Prop5) ? DBNull.Value : model.Prop5);
                    command.Parameters.AddWithValue("@Prop6", string.IsNullOrEmpty(model.Prop6) ? DBNull.Value : model.Prop6);
                    command.Parameters.AddWithValue("@Prop7", string.IsNullOrEmpty(model.Prop7) ? DBNull.Value : model.Prop7);
                    command.Parameters.AddWithValue("@Prop8", string.IsNullOrEmpty(model.Prop8) ? DBNull.Value : model.Prop8);
                    command.Parameters.AddWithValue("@Prop9", string.IsNullOrEmpty(model.Prop9) ? DBNull.Value : model.Prop9);
                    command.Parameters.AddWithValue("@Prop10", string.IsNullOrEmpty(model.Prop10) ? DBNull.Value : model.Prop10);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);
                    command.Parameters.AddWithValue("@Updated_Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteAssetStructureAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_assetstructure WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<AttendanceRuleModel>> GetAllAttendanceRulesAsync()
        {
            var data = new List<AttendanceRuleModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT RuleCode, LeaveName, MinUnit, Unit, Comments FROM hr_attendancerules ORDER BY RuleCode DESC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new AttendanceRuleModel
                        {
                            Code = reader["RuleCode"].ToString() ?? string.Empty,
                            LeaveName = reader["LeaveName"].ToString() ?? string.Empty,
                            MinUnit = reader["MinUnit"] != DBNull.Value ? Convert.ToDecimal(reader["MinUnit"]) : 0,
                            Unit = reader["Unit"] != DBNull.Value ? Convert.ToDecimal(reader["Unit"]) : 0,
                            Comments = reader["Comments"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return data;
        }

        public async Task<bool> IsAttendanceRuleExistsAsync(string leaveName, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_attendancerules WHERE LeaveName=@LeaveName LIMIT 1"
                    : "SELECT 1 FROM hr_attendancerules WHERE LeaveName=@LeaveName AND RuleCode<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LeaveName", leaveName);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddAttendanceRuleAsync(AttendanceRuleModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_attendancerules", "RuleCode", 3);

                string query = @"INSERT INTO hr_attendancerules (RuleCode, LeaveName, MinUnit, Unit, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @LeaveName, @MinUnit, @Unit, @Comments, @UserId, @Date, @UserId, @Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@LeaveName", model.LeaveName);
                    command.Parameters.AddWithValue("@MinUnit", model.MinUnit);
                    command.Parameters.AddWithValue("@Unit", model.Unit);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateAttendanceRuleAsync(AttendanceRuleModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_attendancerules 
                                 SET LeaveName=@LeaveName, MinUnit=@MinUnit, Unit=@Unit, Comments=@Comments, UpdatedBy=@UserId, Updated_Date=@Date 
                                 WHERE RuleCode=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@LeaveName", model.LeaveName);
                    command.Parameters.AddWithValue("@MinUnit", model.MinUnit);
                    command.Parameters.AddWithValue("@Unit", model.Unit);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteAttendanceRuleAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_attendancerules WHERE RuleCode=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<CommissionRateModel>> GetAllCommissionRatesAsync(string currentUserId)
        {
            var data = new List<CommissionRateModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hcr.Code, hcr.Citycode, hc.FullName as city, hcr.DOM_Cash, hcr.DOM_Credit, hcr.LCL_Cash, hcr.LCL_Credit, hcr.LCL_DLD, hcr.PMCL, hcr.INTL, hcr.Porter, hcr.COD 
                                 FROM hr_comm_rates hcr 
                                 INNER JOIN hr_city hc ON hcr.Citycode = hc.Code 
                                 INNER JOIN lcs_user_location lul ON hcr.Citycode = lul.city_code 
                                 WHERE lul.USERID = @UserId 
                                 ORDER BY hcr.Code DESC";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new CommissionRateModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                Citycode = reader["Citycode"].ToString() ?? string.Empty,
                                CityName = reader["city"].ToString() ?? string.Empty,
                                DOM_Cash = reader["DOM_Cash"] != DBNull.Value ? Convert.ToDecimal(reader["DOM_Cash"]) : 0,
                                DOM_Credit = reader["DOM_Credit"] != DBNull.Value ? Convert.ToDecimal(reader["DOM_Credit"]) : 0,
                                LCL_Cash = reader["LCL_Cash"] != DBNull.Value ? Convert.ToDecimal(reader["LCL_Cash"]) : 0,
                                LCL_Credit = reader["LCL_Credit"] != DBNull.Value ? Convert.ToDecimal(reader["LCL_Credit"]) : 0,
                                LCL_DLD = reader["LCL_DLD"] != DBNull.Value ? Convert.ToDecimal(reader["LCL_DLD"]) : 0,
                                PMCL = reader["PMCL"] != DBNull.Value ? Convert.ToDecimal(reader["PMCL"]) : 0,
                                INTL = reader["INTL"] != DBNull.Value ? Convert.ToDecimal(reader["INTL"]) : 0,
                                Porter = reader["Porter"] != DBNull.Value ? Convert.ToDecimal(reader["Porter"]) : 0,
                                COD = reader["COD"] != DBNull.Value ? Convert.ToDecimal(reader["COD"]) : 0
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<bool> IsCommissionRateExistsAsync(string cityCode, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM hr_comm_rates WHERE Citycode=@Citycode LIMIT 1"
                    : "SELECT 1 FROM hr_comm_rates WHERE Citycode=@Citycode AND Code<>@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Citycode", cityCode);
                    if (!string.IsNullOrEmpty(excludeCode))
                    {
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    }

                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        public async Task<bool> AddCommissionRateAsync(CommissionRateModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_comm_rates", "Code", 3);

                string query = @"INSERT INTO hr_comm_rates 
                                 (Code, Citycode, DOM_Cash, DOM_Credit, LCL_Cash, LCL_Credit, LCL_DLD, PMCL, INTL, Porter, COD, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @Citycode, @DOM_Cash, @DOM_Credit, @LCL_Cash, @LCL_Credit, @LCL_DLD, @PMCL, @INTL, @Porter, @COD, @UserId, @Date, @UserId, @Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Citycode", model.Citycode);
                    command.Parameters.AddWithValue("@DOM_Cash", model.DOM_Cash);
                    command.Parameters.AddWithValue("@DOM_Credit", model.DOM_Credit);
                    command.Parameters.AddWithValue("@LCL_Cash", model.LCL_Cash);
                    command.Parameters.AddWithValue("@LCL_Credit", model.LCL_Credit);
                    command.Parameters.AddWithValue("@LCL_DLD", model.LCL_DLD);
                    command.Parameters.AddWithValue("@PMCL", model.PMCL);
                    command.Parameters.AddWithValue("@INTL", model.INTL);
                    command.Parameters.AddWithValue("@Porter", model.Porter);
                    command.Parameters.AddWithValue("@COD", model.COD);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateCommissionRateAsync(CommissionRateModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_comm_rates 
                                 SET DOM_Cash=@DOM_Cash, DOM_Credit=@DOM_Credit, LCL_Cash=@LCL_Cash, LCL_Credit=@LCL_Credit, LCL_DLD=@LCL_DLD, PMCL=@PMCL, INTL=@INTL, Porter=@Porter, COD=@COD, UpdatedBy=@UserId, Updated_Date=@Date 
                                 WHERE Code=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@DOM_Cash", model.DOM_Cash);
                    command.Parameters.AddWithValue("@DOM_Credit", model.DOM_Credit);
                    command.Parameters.AddWithValue("@LCL_Cash", model.LCL_Cash);
                    command.Parameters.AddWithValue("@LCL_Credit", model.LCL_Credit);
                    command.Parameters.AddWithValue("@LCL_DLD", model.LCL_DLD);
                    command.Parameters.AddWithValue("@PMCL", model.PMCL);
                    command.Parameters.AddWithValue("@INTL", model.INTL);
                    command.Parameters.AddWithValue("@Porter", model.Porter);
                    command.Parameters.AddWithValue("@COD", model.COD);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteCommissionRateAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Fixing legacy bug, it should delete from hr_comm_rates
                string query = "DELETE FROM hr_comm_rates WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<CommissionEligibilityListModel>> GetAllCommissionEligibilitiesAsync()
        {
            var data = new List<CommissionEligibilityListModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT eli.`Emp_no`, p.`NAME` AS emp_Name, po.`Type`, IF(eli.`IsEligible`=1, 'Yes', 'No') AS IsEligible
                                 FROM `hr_empcommissioneligibility` eli
                                 INNER JOIN `hr_commissionpolicy` po ON po.`RateID`=eli.`CommissionId`
                                 INNER JOIN `hr_employeepersonaldetail` p ON p.`EMP_NO` = eli.`Emp_no`
                                 ORDER BY DATE(eli.`CreatedDate`) DESC LIMIT 1000";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new CommissionEligibilityListModel
                        {
                            EmpNo = reader["Emp_no"].ToString() ?? string.Empty,
                            EmpName = reader["emp_Name"].ToString() ?? string.Empty,
                            Type = reader["Type"].ToString() ?? string.Empty,
                            IsEligible = reader["IsEligible"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return data;
        }

        public async Task<CommissionEligibilityModel?> GetCommissionEligibilityByEmpNoAsync(string empNo)
        {
            CommissionEligibilityModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT el.Emp_no, p.`NAME`, el.CommissionId, el.`IsEligible`
                                 FROM `hr_empcommissioneligibility` el 
                                 INNER JOIN `hr_employeepersonaldetail` p ON p.Emp_no = el.Emp_no  
                                 WHERE el.Emp_no = @EmpNo";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmpNo", empNo);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            if (model == null)
                            {
                                model = new CommissionEligibilityModel
                                {
                                    EmpNo = reader["Emp_no"].ToString() ?? string.Empty,
                                    EmployeeDescription = reader["NAME"].ToString() ?? string.Empty
                                };
                            }

                            int comID = Convert.ToInt32(reader["CommissionId"]);
                            bool isEligible = Convert.ToBoolean(reader["IsEligible"]);

                            switch (comID)
                            {
                                case 2: model.OLE_Dispatch_Proper = isEligible; break;
                                case 3: model.OLE_Transit_Dispatch = isEligible; break;
                                case 4: model.OLE_Delivery_OPS = isEligible; break;
                            }
                        }
                    }
                }
            }
            return model;
        }

        public async Task<IEnumerable<dynamic>> SearchActiveEmployeesAsync(string term)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = "SELECT EMP_NO, NAME FROM hr_employeepersonaldetail WHERE NAME LIKE @term AND EMP_STATUS <> 'I' ORDER BY NAME LIMIT 20";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@term", $"%{term}%");
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(new
                            {
                                label = $"{reader["EMP_NO"]} - {reader["NAME"]}",
                                value = reader["EMP_NO"].ToString(),
                                desc = reader["NAME"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }

        public async Task<bool> SaveCommissionEligibilityAsync(CommissionEligibilityModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validate Employee
                string empQuery = "SELECT 1 FROM hr_employeepersonaldetail WHERE EMP_NO=@EMP_NO AND NAME=@NAME AND EMP_STATUS <> 'I'";
                using (var empCmd = new MySqlCommand(empQuery, connection))
                {
                    empCmd.Parameters.AddWithValue("@EMP_NO", model.EmpNo);
                    empCmd.Parameters.AddWithValue("@NAME", model.EmployeeDescription);
                    var exists = await empCmd.ExecuteScalarAsync();
                    if (exists == null)
                    {
                        throw new ArgumentException("Employee does not exist or is inactive.");
                    }
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string queryTemplate = @"INSERT INTO `hr_empcommissioneligibility` 
                                                (Emp_no, CommissionId, IsEligible, CreatedBy, CreatedDate) 
                                                VALUES (@Emp_no, @CommissionId, @IsEligible, @UserId, NOW()) 
                                                ON DUPLICATE KEY UPDATE `IsEligible` = @IsEligible, updatedBy = @UserId, UpdatedDate = NOW();";

                        var list = new[]
                        {
                            new { Id = 2, Eligible = model.OLE_Dispatch_Proper },
                            new { Id = 3, Eligible = model.OLE_Transit_Dispatch },
                            new { Id = 4, Eligible = model.OLE_Delivery_OPS }
                        };

                        foreach (var item in list)
                        {
                            using (var command = new MySqlCommand(queryTemplate, connection, transaction as MySqlTransaction))
                            {
                                command.Parameters.AddWithValue("@Emp_no", model.EmpNo);
                                command.Parameters.AddWithValue("@CommissionId", item.Id);
                                command.Parameters.AddWithValue("@IsEligible", item.Eligible ? 1 : 0);
                                command.Parameters.AddWithValue("@UserId", currentUserId);
                                await command.ExecuteNonQueryAsync();
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

        public async Task<IEnumerable<LeaveStructureModel>> GetAllLeaveStructuresAsync()
        {
            var data = new List<LeaveStructureModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT CODE, FullName, ShortName, Total_Leaves, Comments FROM hr_leavestructure ORDER BY CODE DESC";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        data.Add(new LeaveStructureModel
                        {
                            Code = reader["CODE"].ToString() ?? string.Empty,
                            FullName = reader["FullName"].ToString() ?? string.Empty,
                            ShortName = reader["ShortName"].ToString() ?? string.Empty,
                            TotalLeaves = reader["Total_Leaves"] != DBNull.Value ? Convert.ToInt32(reader["Total_Leaves"]) : 0,
                            Comments = reader["Comments"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return data;
        }

        public async Task<bool> AddLeaveStructureAsync(LeaveStructureModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_leavestructure", "Code", 3);

                string query = @"INSERT INTO hr_leavestructure (Code, FullName, ShortName, FromDate, ToDate, Total_Leaves, Comments, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @FullName, @ShortName, @FromDate, @ToDate, @Total_Leaves, @Comments, @UserId, @Date, @UserId, @Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@FromDate", DBNull.Value);
                    command.Parameters.AddWithValue("@ToDate", DBNull.Value);
                    command.Parameters.AddWithValue("@Total_Leaves", model.TotalLeaves);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateLeaveStructureAsync(LeaveStructureModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = @"UPDATE hr_leavestructure 
                                 SET FullName=@FullName, ShortName=@ShortName, Total_Leaves=@Total_Leaves, Comments=@Comments, UpdatedBy=@UserId, Updated_Date=@Date 
                                 WHERE Code=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@FullName", model.FullName);
                    command.Parameters.AddWithValue("@ShortName", model.ShortName);
                    command.Parameters.AddWithValue("@Total_Leaves", model.TotalLeaves);
                    command.Parameters.AddWithValue("@Comments", model.Comments);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteLeaveStructureAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_leavestructure WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<DepartmentStrengthModel>> GetDepartmentStrengthsByCityAsync(string cityCode)
        {
            var data = new List<DepartmentStrengthModel>();
            if (string.IsNullOrEmpty(cityCode) || cityCode == "00") return data;

            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT hds.CityID, hd.`PDID`, hd.`PDName` AS PDept, sb.SDID, sb.FullName AS SubDept, IFNULL(hds.value,0) Strength  
                                 FROM hr_department_strength hds  
                                 INNER JOIN hr_parentdepartment hd ON hd.`PDID`= hds.DeptID
                                 INNER JOIN hr_subdepartment sb ON sb.SDID = hds.SubDeptID
                                 WHERE hds.CityID = @CityID 
                                 ORDER BY hd.PDName, sb.FullName ASC";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@CityID", cityCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new DepartmentStrengthModel
                            {
                                CityID = reader["CityID"].ToString() ?? string.Empty,
                                PDID = reader["PDID"].ToString() ?? string.Empty,
                                PDeptName = reader["PDept"].ToString() ?? string.Empty,
                                SDID = reader["SDID"].ToString() ?? string.Empty,
                                SubDeptName = reader["SubDept"].ToString() ?? string.Empty,
                                Strength = Convert.ToInt32(reader["Strength"])
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<bool> UpdateDepartmentStrengthAsync(string cityId, string pdid, string sdid, int strength, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string execQuery = @"DELETE FROM hr_department_strength WHERE CityID=@CityID AND DeptID=@PDID AND SubDeptID=@SDID;
                                     INSERT INTO hr_department_strength VALUES(@CityID, @PDID, @SDID, @Strength, @UserId, NOW());";

                using (var command = new MySqlCommand(execQuery, connection))
                {
                    command.Parameters.AddWithValue("@CityID", cityId);
                    command.Parameters.AddWithValue("@PDID", pdid);
                    command.Parameters.AddWithValue("@SDID", sdid);
                    command.Parameters.AddWithValue("@Strength", strength);
                    command.Parameters.AddWithValue("@UserId", currentUserId);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<IEnumerable<GazettedHolidayModel>> GetAllGazettedHolidaysAsync()
        {
            var data = new List<GazettedHolidayModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT g.CODE, DATE_FORMAT(g.FromDate,'%Y-%m-%d') AS FromDate, DATE_FORMAT(g.ToDate,'%Y-%m-%d') AS ToDate, g.days, g.Reason, 
                                 CASE g.holiday_flag WHEN 'All' THEN 'All' ELSE (SELECT fullname FROM hr_city h WHERE h.code=g.holiday_flag LIMIT 1) END location, 
                                 g.holiday_flag 
                                 FROM hr_gazetted_holidays g ORDER BY g.CODE desc";

                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var model = new GazettedHolidayModel
                        {
                            Code = reader["CODE"].ToString() ?? string.Empty,
                            FromDate = reader["FromDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["FromDate"]),
                            ToDate = reader["ToDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["ToDate"]),
                            Days = reader["days"] != DBNull.Value ? Convert.ToInt32(reader["days"]) : 0,
                            Reason = reader["Reason"].ToString() ?? string.Empty,
                            LocationID = reader["holiday_flag"].ToString() ?? string.Empty,
                            DisplayLocation = reader["location"].ToString() ?? string.Empty,
                            IsAllLocations = reader["holiday_flag"].ToString() == "All"
                        };
                        
                        if (!model.IsAllLocations)
                        {
                            model.LocationDescription = model.DisplayLocation;
                        }

                        data.Add(model);
                    }
                }
            }
            return data;
        }

        public async Task<bool> AddGazettedHolidayAsync(GazettedHolidayModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string code = await GenerateNewIdAsync(connection, null, "hr_gazetted_holidays", "Code", 3);
                string flag = model.IsAllLocations ? "All" : model.LocationID;

                // Calculate days (excluding Sundays as per legacy logic)
                int holidays = 0;
                if (model.FromDate.HasValue && model.ToDate.HasValue)
                {
                    int noOfDays = (model.ToDate.Value - model.FromDate.Value).Days + 1;
                    for (int i = 0; i < noOfDays; i++)
                    {
                        if (model.FromDate.Value.AddDays(i).DayOfWeek != DayOfWeek.Sunday)
                        {
                            holidays++;
                        }
                    }
                }

                string query = @"INSERT INTO hr_gazetted_holidays (Code, Year, Month, FromDate, ToDate, days, Reason, holiday_flag, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                 VALUES (@Code, @Year, @Month, @FromDate, @ToDate, @days, @Reason, @flag, @UserId, @Date, @UserId, @Date)";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Year", model.FromDate?.Year);
                    command.Parameters.AddWithValue("@Month", model.FromDate?.Month);
                    command.Parameters.AddWithValue("@FromDate", model.FromDate);
                    command.Parameters.AddWithValue("@ToDate", model.ToDate);
                    command.Parameters.AddWithValue("@days", holidays);
                    command.Parameters.AddWithValue("@Reason", model.Reason);
                    command.Parameters.AddWithValue("@flag", flag);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateGazettedHolidayAsync(GazettedHolidayModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string flag = model.IsAllLocations ? "All" : model.LocationID;

                // Calculate days (excluding Sundays as per legacy logic)
                int holidays = 0;
                if (model.FromDate.HasValue && model.ToDate.HasValue)
                {
                    int noOfDays = (model.ToDate.Value - model.FromDate.Value).Days + 1;
                    for (int i = 0; i < noOfDays; i++)
                    {
                        if (model.FromDate.Value.AddDays(i).DayOfWeek != DayOfWeek.Sunday)
                        {
                            holidays++;
                        }
                    }
                }

                string query = @"UPDATE hr_gazetted_holidays 
                                 SET Year=@Year, Month=@Month, FromDate=@FromDate, ToDate=@ToDate, days=@days, Reason=@Reason, Holiday_flag=@flag, UpdatedBy=@UserId, Updated_Date=@Date 
                                 WHERE Code=@Code";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Year", model.FromDate?.Year);
                    command.Parameters.AddWithValue("@Month", model.FromDate?.Month);
                    command.Parameters.AddWithValue("@FromDate", model.FromDate);
                    command.Parameters.AddWithValue("@ToDate", model.ToDate);
                    command.Parameters.AddWithValue("@days", holidays);
                    command.Parameters.AddWithValue("@Reason", model.Reason);
                    command.Parameters.AddWithValue("@flag", flag);
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    command.Parameters.AddWithValue("@Date", DateTime.Now);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteGazettedHolidayAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_gazetted_holidays WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<int> InsertEmpHierarchyAsync(List<HRHierarchyModel> empHierarchyInfo, string currentUserId)
        {
            if (empHierarchyInfo == null || !empHierarchyInfo.Any()) return 0;

            int affectedRows = 0;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return 0;
                await connection.OpenAsync();

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string deleteEmpIDS = empHierarchyInfo.First().DeleteHirerchyEmpIDS;
                        if (!string.IsNullOrEmpty(deleteEmpIDS))
                        {
                            deleteEmpIDS = deleteEmpIDS.TrimEnd(',');
                            // Safely construct delete statement using FIND_IN_SET
                            string deleteQuery = $"DELETE FROM definehierarchy WHERE FIND_IN_SET(`Emp_no`, @DeleteIds) > 0 AND `ReportToEmpNo` = @ReportTo";
                            using (var cmd = new MySqlCommand(deleteQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@DeleteIds", deleteEmpIDS);
                                cmd.Parameters.AddWithValue("@ReportTo", empHierarchyInfo.First().ReportTo);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string insertQuery = @"
                            INSERT INTO definehierarchy(Emp_no, email, Cell, ReportToEmpNo, CreatedBy, CreatedDate)  
                            VALUES(@empNO, @email, @cell, @reportTo, @createby, @createdDate) 
                            ON DUPLICATE KEY UPDATE email=@email, Cell=@cell, ReportToEmpNo=@reportTo, ModifiedBy=@createby, ModifiedDate=@createdDate; 
                            
                            UPDATE hr_employeepersonaldetail SET CELL_CONTACT_1 = @cell, EMAIL_ADD = @email WHERE Emp_no = @empNO;";

                        foreach (var item in empHierarchyInfo)
                        {
                            using (var cmd = new MySqlCommand(insertQuery, connection, transaction as MySqlTransaction))
                            {
                                cmd.Parameters.AddWithValue("@empNO", item.EmpNo);
                                cmd.Parameters.AddWithValue("@email", item.Email);
                                cmd.Parameters.AddWithValue("@cell", item.CellNo);
                                cmd.Parameters.AddWithValue("@reportTo", item.ReportTo);
                                cmd.Parameters.AddWithValue("@createby", currentUserId);
                                cmd.Parameters.AddWithValue("@createdDate", DateTime.Now);
                                affectedRows += await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        return 0;
                    }
                }
            }
            return affectedRows;
        }

        public async Task<IEnumerable<dynamic>> GetEmployeesBySubDepartmentAsync(string deptId, string subDeptId)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = @"SELECT e.EMP_NO, e.NAME, IFNULL(e.EMAIL_ADD, '') AS Email, IFNULL(e.CELL_CONTACT_1, '') AS Cell 
                                 FROM hr_employeepersonaldetail e 
                                 INNER JOIN hr_employeedepartmentdetails deptDet ON e.EMP_NO = deptDet.Emp_No 
                                 WHERE deptDet.ToDate IS NULL AND e.EMP_STATUS <> 'I' 
                                 AND deptDet.DeptCode = @DeptId AND deptDet.SubDeptCode = @SubDeptId";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@DeptId", deptId);
                    command.Parameters.AddWithValue("@SubDeptId", subDeptId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(new
                            {
                                EmpNo = reader["EMP_NO"].ToString(),
                                empName = reader["NAME"].ToString(),
                                Email = reader["Email"].ToString(),
                                Cell = reader["Cell"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }

        public async Task<IEnumerable<dynamic>> GetReportedEmployeesAsync(string reportToId)
        {
            var results = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return results;
                await connection.OpenAsync();

                string query = @"SELECT dh.Emp_no, e.NAME, IFNULL(dh.email, '') AS email, IFNULL(dh.Cell, '') AS Cell 
                                 FROM definehierarchy dh 
                                 INNER JOIN hr_employeepersonaldetail e ON e.EMP_NO = dh.Emp_no 
                                 WHERE dh.ReportToEmpNo = @ReportToId";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@ReportToId", reportToId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(new
                            {
                                EmpNo = reader["Emp_no"].ToString(),
                                empName = reader["NAME"].ToString(),
                                Email = reader["email"].ToString(),
                                Cell = reader["Cell"].ToString()
                            });
                        }
                    }
                }
            }
            return results;
        }

        // ─── GL Locations ────────────────────────────────────────────────────────

        public async Task<IEnumerable<GLLocationModel>> GetAllGLLocationsAsync()
        {
            var list = new List<GLLocationModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return list;
                await connection.OpenAsync();
                string query = "SELECT loc_code, DESCR FROM gl_location ORDER BY loc_code ASC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new GLLocationModel
                        {
                            Code = reader["loc_code"].ToString() ?? string.Empty,
                            Description = reader["DESCR"].ToString() ?? string.Empty
                        });
                    }
                }
            }
            return list;
        }

        public async Task<bool> IsGLLocationExistsAsync(string description, string? excludeCode = null)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = string.IsNullOrEmpty(excludeCode)
                    ? "SELECT 1 FROM gl_location WHERE DESCR=@Descr LIMIT 1"
                    : "SELECT 1 FROM gl_location WHERE DESCR=@Descr AND loc_code<>@Code LIMIT 1";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Descr", description);
                    if (!string.IsNullOrEmpty(excludeCode))
                        command.Parameters.AddWithValue("@Code", excludeCode);
                    var result = await command.ExecuteScalarAsync();
                    return result != null;
                }
            }
        }

        private async Task<string> GenerateNewGLLocationCodeAsync()
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return "001";
                await connection.OpenAsync();
                string query = "SELECT MAX(CAST(loc_code AS UNSIGNED)) FROM gl_location";
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

        public async Task<bool> AddGLLocationAsync(GLLocationModel model, string currentUserId)
        {
            model.Code = await GenerateNewGLLocationCodeAsync();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = "INSERT INTO gl_location (loc_code, DESCR, createdby, createddate, updatedby, updateddate) VALUES (@loc_code, @DESCR, @createdby, @createddate, @updatedby, @updateddate)";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@loc_code", model.Code);
                    command.Parameters.AddWithValue("@DESCR", model.Description);
                    command.Parameters.AddWithValue("@createdby", currentUserId);
                    command.Parameters.AddWithValue("@createddate", DateTime.Now);
                    command.Parameters.AddWithValue("@updatedby", currentUserId);
                    command.Parameters.AddWithValue("@updateddate", DateTime.Now);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateGLLocationAsync(GLLocationModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = "UPDATE gl_location SET DESCR=@DESCR, updatedby=@updatedby, updateddate=@updateddate WHERE loc_code=@loc_code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@loc_code", model.Code);
                    command.Parameters.AddWithValue("@DESCR", model.Description);
                    command.Parameters.AddWithValue("@updatedby", currentUserId);
                    command.Parameters.AddWithValue("@updateddate", DateTime.Now);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteGLLocationAsync(string code)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                // Check references before deleting
                string checkQuery = "SELECT 1 FROM hr_salaryprocessed_hdr WHERE loc_code=@code LIMIT 1";
                using (var checkCmd = new MySqlCommand(checkQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@code", code);
                    var refResult = await checkCmd.ExecuteScalarAsync();
                    if (refResult != null) return false; // has references
                }
                string query = "DELETE FROM gl_location WHERE loc_code=@code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@code", code);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        // ─── Assign Multiple Locations ──────────────────────────────────────────

        public async Task<IEnumerable<SetupLocationModel>> GetSetupLocationsAsync()
        {
            var list = new List<SetupLocationModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return list;
                await connection.OpenAsync();
                string query = @"SELECT
                    loc.LocationID,
                    CONCAT(loc.LocationName,' (',lt.ShortName,')') AS LocationName,
                    city.CityName,
                    zone.`Name` AS ZoneName,
                    coun.Name AS CountryName
                FROM lcs_Setup.locations loc
                INNER JOIN lcs_Setup.citytest city ON loc.CityID = city.CityID AND city.IsActive = 1
                INNER JOIN lcs_Setup.`zone` zone ON zone.ZoneID = city.ZoneID AND zone.IsActive = 1
                INNER JOIN lcs_Setup.country coun ON city.CountryID = coun.CountryID AND coun.IsActive = 1
                INNER JOIN lcs_setup.location_type lt ON lt.ID = loc.LocationTypeID AND lt.IsDeleted = 0
                WHERE loc.LocationTypeID NOT IN(1, 2)
                ORDER BY zone.`Name`";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.CommandTimeout = 120;
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            list.Add(new SetupLocationModel
                            {
                                LocationId = Convert.ToInt32(reader["LocationID"]),
                                LocationName = reader["LocationName"].ToString() ?? string.Empty,
                                CityName = reader["CityName"].ToString() ?? string.Empty,
                                ZoneName = reader["ZoneName"].ToString() ?? string.Empty,
                                CountryName = reader["CountryName"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
            }
            return list;
        }

        public async Task<List<int>> GetAssignedLocationsByEmpAsync(string empNo)
        {
            empNo = empNo.PadLeft(14, '0');
            var ids = new List<int>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return ids;
                await connection.OpenAsync();
                string query = "SELECT GROUP_CONCAT(LocationId) AS LocationIds FROM allot_multiple_locations WHERE Emp_no=@empNo AND IsDeleted=0";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@empNo", empNo);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value && !string.IsNullOrEmpty(result.ToString()))
                    {
                        ids = result.ToString()!.Split(',').Select(int.Parse).ToList();
                    }
                }
            }
            return ids;
        }

        public async Task<bool> SaveAssignedLocationsAsync(string empNo, List<int> locIds, string currentUserId)
        {
            empNo = empNo.PadLeft(14, '0');
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        // Upsert each selected location
                        string upsertQuery = @"INSERT INTO lcs_hr.allot_multiple_locations(Emp_no, LocationId, CreatedBy, CreatedDate)
                            VALUES(@EmpNo, @locId, @createby, NOW())
                            ON DUPLICATE KEY UPDATE Emp_no=@EmpNo, LocationId=@locId, IsDeleted=0, UpdateBy=@createby, Updateddate=NOW()";
                        foreach (var locId in locIds)
                        {
                            using (var cmd = new MySqlCommand(upsertQuery, connection, (MySqlTransaction)transaction))
                            {
                                cmd.Parameters.AddWithValue("@EmpNo", empNo);
                                cmd.Parameters.AddWithValue("@locId", locId);
                                cmd.Parameters.AddWithValue("@createby", currentUserId);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        // Soft-delete locations that were previously assigned but now removed
                        var currentAssigned = await GetAssignedLocationsByEmpAsync(empNo.TrimStart('0').PadLeft(14, '0'));
                        var toDelete = currentAssigned.Where(id => !locIds.Contains(id)).ToList();
                        if (toDelete.Count > 0)
                        {
                            string deleteQuery = "UPDATE lcs_hr.allot_multiple_locations SET IsDeleted=1 WHERE Emp_no=@EmpNo AND LocationId=@locId";
                            foreach (var locId in toDelete)
                            {
                                using (var cmd = new MySqlCommand(deleteQuery, connection, (MySqlTransaction)transaction))
                                {
                                    cmd.Parameters.AddWithValue("@EmpNo", empNo);
                                    cmd.Parameters.AddWithValue("@locId", locId);
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

        // ─── Location Coordinate Update ─────────────────────────────────────────

        public async Task<IEnumerable<LocationCoordinateModel>> GetLocationsWithCoordinatesAsync()
        {
            var list = new List<LocationCoordinateModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return list;
                await connection.OpenAsync();
                string query = @"SELECT z.fullname AS ZoneName, z.code AS ZCode,
                    ct.CityID AS CityCode,
                    c.FullName AS CityName,
                    l.LocationName, l.LandLine, l.PostalCode,
                    l.LATITUDE, l.LONGITUDE, l.LocationCode, l.LocationID
                FROM hr_city c
                INNER JOIN lcs_Setup.citytest ct ON ct.cityid = c.station_id
                INNER JOIN hr_regionalzones z ON z.code = c.rzonecode
                INNER JOIN lcs_Setup.locations l ON l.CityID = ct.cityid
                WHERE l.LocationTypeID NOT IN (1,2)
                ORDER BY z.Fullname, c.FullName, l.LocationName ASC";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new LocationCoordinateModel
                        {
                            ZoneName = reader["ZoneName"].ToString() ?? string.Empty,
                            ZoneCode = reader["ZCode"].ToString() ?? string.Empty,
                            CityCode = reader["CityCode"].ToString() ?? string.Empty,
                            CityName = reader["CityName"].ToString() ?? string.Empty,
                            LocationName = reader["LocationName"].ToString() ?? string.Empty,
                            LandLine = reader["LandLine"].ToString() ?? string.Empty,
                            PostalCode = reader["PostalCode"].ToString() ?? string.Empty,
                            Latitude = reader["LATITUDE"].ToString() ?? string.Empty,
                            Longitude = reader["LONGITUDE"].ToString() ?? string.Empty,
                            LocationCode = reader["LocationCode"].ToString() ?? string.Empty,
                            LocationId = Convert.ToInt32(reader["LocationID"])
                        });
                    }
                }
            }
            return list;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCitiesByZoneAsync(string zoneCode)
        {
            var cities = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return cities;
                await connection.OpenAsync();
                string query = @"SELECT ct.cityid AS CODE, c.FullName
                    FROM hr_city c
                    INNER JOIN lcs_Setup.citytest ct ON ct.cityid = c.station_id
                    INNER JOIN hr_regionalzones z ON z.code = c.rzonecode
                    WHERE c.RZoneCode = @zoneCode
                    ORDER BY c.FullName";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@zoneCode", zoneCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            cities.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = reader["CODE"].ToString(),
                                Text = reader["FullName"].ToString()
                            });
                        }
                    }
                }
            }
            return cities;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetLocationsByCityAsync(string cityId)
        {
            var locations = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return locations;
                await connection.OpenAsync();
                string query = @"SELECT l.locationid AS CODE, l.LocationName AS FullName
                    FROM lcs_setup.locations l
                    WHERE l.LocationTypeID NOT IN (1,2) AND l.CityID = @cityId
                    ORDER BY l.LocationName";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@cityId", cityId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            locations.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                            {
                                Value = reader["CODE"].ToString(),
                                Text = reader["FullName"].ToString()
                            });
                        }
                    }
                }
            }
            return locations;
        }

        public async Task<bool> UpdateLocationCoordinatesAsync(int locationId, string cityId, string latitude, string longitude, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();
                string query = @"UPDATE lcs_setup.locations l
                    SET l.LONGITUDE = @LONGITUDE, l.LATITUDE = @LATITUDE
                    WHERE l.locationid = @locationID AND l.CityID = @CityID";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@LONGITUDE", longitude);
                    command.Parameters.AddWithValue("@LATITUDE", latitude);
                    command.Parameters.AddWithValue("@locationID", locationId);
                    command.Parameters.AddWithValue("@CityID", cityId);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<(int generated, string message)> GenerateEmpGlCodesAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return (0, "Database connection failed.");
                await connection.OpenAsync();

                // Find employees without a GL code
                string findQuery = "SELECT EMP_NO, NAME FROM hr_employeepersonaldetail WHERE (emp_glcode IS NULL OR LENGTH(emp_glcode) = 0)";
                var employees = new List<(string EmpNo, string Name)>();
                using (var cmd = new MySqlCommand(findQuery, connection))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        employees.Add((reader["EMP_NO"].ToString()!, reader["NAME"].ToString()!));
                    }
                }

                if (employees.Count == 0)
                    return (0, "No employees found without a GL code.");

                // Get next subsidiary account number from lcs_gl
                string maxQuery = "SELECT IFNULL(MAX(CAST(GLSUBSIDARY_A_C AS UNSIGNED)), 0) + 1 FROM lcs_gl.gl_chart_of_acc WHERE GLNATURE_OF_A_C='5' AND GLBROAD_CATEGORY='02' AND GLCONTROL_A_C='01' AND GLSUB_CONTROL_A_C='0008'";
                int nextId;
                using (var cmd = new MySqlCommand(maxQuery, connection))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    nextId = Convert.ToInt32(result);
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string insertGlQuery = @"INSERT INTO lcs_gl.gl_chart_of_acc
                            (GLNATURE_OF_A_C, GLBROAD_CATEGORY, GLCONTROL_A_C, GLSUB_CONTROL_A_C, GLSUBSIDARY_A_C, GLcode, DESCRIPTION, OLD_GL_CODE, COST_TYPE, REMARKS1, REMARKS2, createdby, createddate, updatedby, updateddate)
                            VALUES ('5','02','01','0008',@SubCode,@GLcode,@Description,NULL,'P',@EmpNo,NULL,@UserId,@Date,@UserId,@Date)";

                        string updateEmpQuery = "UPDATE hr_employeepersonaldetail SET emp_glcode=@glcode WHERE EMP_NO=@empNo";

                        foreach (var (empNo, name) in employees)
                        {
                            string subCode = nextId.ToString("00000");
                            string fullGlCode = "5" + "02" + "01" + "0008" + subCode;

                            using (var insertCmd = new MySqlCommand(insertGlQuery, connection, transaction as MySqlTransaction))
                            {
                                insertCmd.Parameters.AddWithValue("@SubCode", subCode);
                                insertCmd.Parameters.AddWithValue("@GLcode", fullGlCode);
                                insertCmd.Parameters.AddWithValue("@Description", name + " - " + empNo);
                                insertCmd.Parameters.AddWithValue("@EmpNo", empNo);
                                insertCmd.Parameters.AddWithValue("@UserId", currentUserId);
                                insertCmd.Parameters.AddWithValue("@Date", DateTime.Now);
                                await insertCmd.ExecuteNonQueryAsync();
                            }

                            using (var updateCmd = new MySqlCommand(updateEmpQuery, connection, transaction as MySqlTransaction))
                            {
                                updateCmd.Parameters.AddWithValue("@glcode", fullGlCode);
                                updateCmd.Parameters.AddWithValue("@empNo", empNo);
                                await updateCmd.ExecuteNonQueryAsync();
                            }

                            nextId++;
                        }

                        await transaction.CommitAsync();
                        return (employees.Count, $"GL codes generated successfully for {employees.Count} employee(s).");
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
        }

        #region Employee Personal Detail Lookups

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCountriesSelectAsync()
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                string query = "SELECT Code, FullName FROM hr_country ORDER BY FullName";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = reader["Code"].ToString(), Text = reader["FullName"].ToString() });
                }
            }
            return items;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetCitiesByCountryAsync(string countryCode)
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                string query = "SELECT Code, FullName FROM hr_city WHERE CountryCode=@cc ORDER BY FullName";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@cc", countryCode);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = reader["Code"].ToString(), Text = reader["FullName"].ToString() });
                    }
                }
            }
            return items;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetEmployeeTypesSelectAsync()
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                string query = "SELECT Code, FullName FROM hr_employeetype WHERE IsDeleted=0 ORDER BY FullName";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = reader["Code"].ToString(), Text = reader["FullName"].ToString() });
                }
            }
            return items;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetDivisionsSelectAsync()
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                string query = "SELECT BUID, Name FROM lcs_setup.businessunit WHERE IsDeleted=0 ORDER BY Name";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = reader["BUID"].ToString(), Text = reader["Name"].ToString() });
                }
            }
            return items;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetJobTypesAsync()
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                string query = "SELECT ID, Name FROM hr_Jobtype WHERE IsDeleted=0 ORDER BY Name";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = reader["ID"].ToString(), Text = reader["Name"].ToString() });
                }
            }
            return items;
        }

        public async Task<IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>> GetThirdPartiesAsync()
        {
            var items = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return items;
                await connection.OpenAsync();
                string query = "SELECT TPId, TPName FROM thirdparty WHERE IsDeleted=0 ORDER BY TPName";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        items.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = reader["TPId"].ToString(), Text = reader["TPName"].ToString() });
                }
            }
            return items;
        }

        #endregion
    }
}
