using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models.Closing;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class ClosingService : IClosingService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public ClosingService(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IEnumerable<dynamic>> GetZonesByUserAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();

                string query = @"SELECT DISTINCT z.Code as Value, z.FullName as Text 
                                 FROM hr_regionalzones z 
                                 INNER JOIN hr_city c ON c.RZoneCode = z.Code
                                 INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                                 WHERE lul.userid = @UserId";

                return await connection.QueryAsync(query, new { UserId = currentUserId });
            }
        }

        public async Task<IEnumerable<dynamic>> GetCitiesByZoneUserAsync(string zoneCode, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<dynamic>();
                await connection.OpenAsync();

                string query = @"SELECT c.Code as Value, c.FullName as Text 
                                 FROM hr_city c
                                 INNER JOIN lcs_user_location lul ON lul.city_code = c.Code
                                 WHERE c.RZoneCode = @ZoneCode AND lul.userid = @UserId";

                return await connection.QueryAsync(query, new { ZoneCode = zoneCode, UserId = currentUserId });
            }
        }

        public async Task<IEnumerable<CloseProcessModel>> GetAllClosedProcessesAsync(string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return new List<CloseProcessModel>();
                await connection.OpenAsync();

                string query = @"SELECT CONCAT(hc.City, hc.year, hc.Month) as Code, hc.City as CityCode, hc1.FullName as City, hc.year as Year, hc.Month as Month, DATE_FORMAT(hc.Updated_Date, '%Y-%m-%d %T') as UpdatedDateStr  
                                 FROM hr_closeprocesses hc 
                                 INNER JOIN hr_city hc1 ON hc.City = hc1.Code
                                 INNER JOIN lcs_user_location lul ON hc.City = lul.city_code
                                 WHERE lul.USERID = @UserId 
                                 ORDER BY hc.year DESC, hc.Month DESC LIMIT 500";

                var results = await connection.QueryAsync(query, new { UserId = currentUserId });
                
                var list = new List<CloseProcessModel>();
                foreach (var r in results)
                {
                    DateTime? parsedDate = null;
                    string dateStr = r.UpdatedDateStr as string;
                    if (!string.IsNullOrEmpty(dateStr) && dateStr != "0000-00-00 00:00:00")
                    {
                        if (DateTime.TryParse(dateStr, out DateTime tempDate))
                        {
                            parsedDate = tempDate;
                        }
                    }

                    list.Add(new CloseProcessModel
                    {
                        Code = r.Code?.ToString() ?? "",
                        CityCode = r.CityCode?.ToString() ?? "",
                        CityName = r.City?.ToString() ?? "",
                        Year = r.Year != null ? Convert.ToInt32(r.Year) : 0,
                        Month = r.Month != null ? Convert.ToInt32(r.Month) : 0,
                        UpdatedDate = parsedDate
                    });
                }
                
                return list;
            }
        }

        public async Task<bool> AddCloseProcessAsync(CloseProcessModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                int curMonth = DateTime.Now.Month;
                int curYear = DateTime.Now.Year;

                if (model.Year == curYear && model.Month == curMonth)
                {
                    throw new ArgumentException("You can not close current working month.");
                }

                using (var transaction = await connection.BeginTransactionAsync())
                {
                    try
                    {
                        string cityQuery = "SELECT c.`Code` FROM `hr_city` c ";
                        if (!string.IsNullOrEmpty(model.ZoneCode) && model.ZoneCode != "0")
                        {
                            if (!string.IsNullOrEmpty(model.CityCode) && model.CityCode != "00")
                            {
                                cityQuery += " WHERE c.`Code`=@CityCode;";
                            }
                            else
                            {
                                cityQuery += " WHERE c.`ZoneCode`=@ZoneCode;";
                            }
                        }

                        var cities = await connection.QueryAsync<string>(cityQuery, new { CityCode = model.CityCode, ZoneCode = model.ZoneCode }, transaction);

                        if (!cities.Any()) throw new ArgumentException("No cities found for selection.");

                        foreach (var city in cities)
                        {
                            int exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM hr_closeprocesses WHERE city=@City AND Year=@Year AND Month=@Month", 
                                new { City = city, Year = model.Year, Month = model.Month }, transaction);
                            if (exists > 0) throw new ArgumentException($"Record Already Exist for city {city}.");

                            string insertQuery = @"INSERT INTO hr_closeprocesses (City, Year, Month, UserID, Updated_Date) VALUES (@City, @Year, @Month, @UserId, NOW())";
                            await connection.ExecuteAsync(insertQuery, new { City = city, Year = model.Year, Month = model.Month, UserId = currentUserId }, transaction);
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

        public async Task<bool> DeleteCloseProcessAsync(string cityCode, int year, int month)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                int curMonth = DateTime.Now.Month - 1;
                int curYear = DateTime.Now.Year;
                if (curMonth == 0)
                {
                    curMonth = 12;
                    curYear -= 1;
                }

                if (year != curYear || month != curMonth)
                {
                    throw new ArgumentException("You can Delete only Last month record.");
                }

                string deleteQuery = "DELETE FROM hr_closeprocesses WHERE City = @City AND Year = @Year AND Month=@Month";
                int rows = await connection.ExecuteAsync(deleteQuery, new { City = cityCode, Year = year, Month = month });
                return rows > 0;
            }
        }

        public async Task<bool> UnlockSalaryAsync(UnlockSalaryViewModel model)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_deptsalaryprocessstatus WHERE month=@Month AND year=@Year";
                if (!string.IsNullOrEmpty(model.ZoneCode) && model.ZoneCode != "0" && model.ZoneCode != "00")
                {
                    query += " AND cityID IN (SELECT Code FROM hr_city c WHERE c.ZoneCode = @ZoneCode)";
                }

                await connection.ExecuteAsync(query, new { Month = model.Month, Year = model.Year, ZoneCode = model.ZoneCode });
                return true;
            }
        }

        public async Task<bool> CommissionUnlockAsync(CommissionUnlockViewModel model)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                if (model.CommissionType == 0)
                {
                    // Regular Commission
                    await connection.ExecuteAsync("DELETE FROM hr_commissionprocess WHERE month=@Month AND year=@Year AND citycode=@CityCode", 
                        new { Month = model.Month, Year = model.Year, CityCode = model.CityCode });
                }
                else if (model.CommissionType == 1 || model.CommissionType == 2)
                {
                    // OLE Commission or Return COD Commission (Stubbed logic based on complex location ids mapping)
                    // The actual legacy logic involves retrieving LocationIDs mapped against the City Station.
                    string stationQuery = @"SELECT DISTINCT BStationId FROM hr_locationmapping lm 
                                            WHERE lm.GlLocationId IN (SELECT l.LocationID FROM lcs_setup.locations l WHERE l.BILLINGCITYID = (SELECT c.station_id FROM hr_city c WHERE c.Code = @CityCode)) AND BStationId IS NOT NULL";
                    var stationIds = await connection.QueryAsync<string>(stationQuery, new { CityCode = model.CityCode });
                    
                    if (stationIds.Any())
                    {
                        string stations = string.Join(",", stationIds.Select(s => $"'{s}'"));
                        
                        string locQuery = $"SELECT DISTINCT GlLocationId FROM hr_locationmapping WHERE BStationId IN ({stations})";
                        var locIds = await connection.QueryAsync<string>(locQuery);
                        string locations = string.Join(",", locIds);

                        if (model.CommissionType == 1)
                        {
                            await connection.ExecuteAsync($"DELETE FROM hr_olecommission WHERE ComYear = @Year AND ComMonth = @Month AND StationId IN ({stations})", new { Year = model.Year, Month = model.Month });
                            if(!string.IsNullOrEmpty(locations))
                                await connection.ExecuteAsync($"DELETE FROM hr_olecommissionprocess WHERE YEAR = @Year AND MONTH = @Month AND GlLocationId IN ({locations})", new { Year = model.Year, Month = model.Month });
                        }
                        else if (model.CommissionType == 2)
                        {
                            await connection.ExecuteAsync($"DELETE FROM hr_codreturncommission WHERE ComYear = @Year AND ComMonth = @Month AND StationId IN ({stations})", new { Year = model.Year, Month = model.Month });
                            if(!string.IsNullOrEmpty(locations))
                                await connection.ExecuteAsync($"DELETE FROM hr_codreturncommissionprocess WHERE YEAR = @Year AND MONTH = @Month AND GlLocationId IN ({locations})", new { Year = model.Year, Month = model.Month });
                        }
                    }
                }
                return true;
            }
        }
    }
}