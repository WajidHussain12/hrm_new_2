using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public class CommissionService : ICommissionService
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public CommissionService(IDbConnectionFactory connectionFactory)
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

        public async Task<IEnumerable<dynamic>> SearchRoutesAsync(string term, string cityCode)
        {
            var data = new List<dynamic>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = "SELECT RouteCode, Description FROM hr_routecodes_hdr WHERE CityCode=@CityCode AND Description LIKE @term ORDER BY Description LIMIT 20";
                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@CityCode", cityCode);
                    cmd.Parameters.AddWithValue("@term", $"%{term}%");
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new { label = reader["Description"].ToString(), value = reader["RouteCode"].ToString() });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<IEnumerable<EmployeeCommissionModel>> GetAllEmployeeCommissionsAsync(string currentUserId)
        {
            var data = new List<EmployeeCommissionModel>();
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return data;
                await connection.OpenAsync();

                string query = @"SELECT he.Code, he.Month, he.Year, he.cour_id, he1.Name, CASE he.CommType WHEN 'L' THEN 'Local' ELSE 'Domestic' END CommType, he.Quantity, he.rate, he.Amount 
                                 FROM hr_employeecommissiondetails he
                                 INNER JOIN hr_employeepersonaldetail he1 ON he.Emp_No = he1.EMP_NO 
                                 INNER JOIN lcs_user_location lul ON lul.city_code= he1.P_CITY_CODE 
                                 WHERE lul.userid=@UserId ORDER BY he.Code DESC LIMIT 500";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@UserId", currentUserId);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            data.Add(new EmployeeCommissionModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                Month = reader["Month"] != DBNull.Value ? Convert.ToInt32(reader["Month"]) : 0,
                                Year = reader["Year"] != DBNull.Value ? Convert.ToInt32(reader["Year"]) : 0,
                                RouteCode = reader["cour_id"].ToString() ?? string.Empty,
                                EmployeeName = reader["Name"].ToString() ?? string.Empty,
                                CommType = reader["CommType"].ToString() ?? string.Empty,
                                Quantity = reader["Quantity"] != DBNull.Value ? Convert.ToDecimal(reader["Quantity"]) : 0,
                                Rate = reader["rate"] != DBNull.Value ? Convert.ToDecimal(reader["rate"]) : 0,
                                Amount = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0
                            });
                        }
                    }
                }
            }
            return data;
        }

        public async Task<EmployeeCommissionModel?> GetEmployeeCommissionByIdAsync(string id)
        {
            EmployeeCommissionModel? model = null;
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return null;
                await connection.OpenAsync();

                string query = @"SELECT he.Code, he.citycode, he.cour_id, hrh.Description, he.Month, he.Year, he.CommType, he.Quantity, he.rate, he.Amount, he.Reason 
                                 FROM hr_employeecommissiondetails he
                                 INNER JOIN hr_routecodes_hdr hrh ON he.citycode = hrh.CityCode AND hrh.RouteCode=he.cour_id  
                                 WHERE he.Code=@Code LIMIT 1";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            model = new EmployeeCommissionModel
                            {
                                Code = reader["Code"].ToString() ?? string.Empty,
                                CityCode = reader["citycode"].ToString() ?? string.Empty,
                                RouteCode = reader["cour_id"].ToString() ?? string.Empty,
                                RouteDescription = reader["Description"].ToString() ?? string.Empty,
                                Month = reader["Month"] != DBNull.Value ? Convert.ToInt32(reader["Month"]) : 0,
                                Year = reader["Year"] != DBNull.Value ? Convert.ToInt32(reader["Year"]) : 0,
                                CommType = reader["CommType"].ToString() ?? string.Empty,
                                Quantity = reader["Quantity"] != DBNull.Value ? Convert.ToDecimal(reader["Quantity"]) : 0,
                                Rate = reader["rate"] != DBNull.Value ? Convert.ToDecimal(reader["rate"]) : 0,
                                Amount = reader["Amount"] != DBNull.Value ? Convert.ToDecimal(reader["Amount"]) : 0,
                                Reason = reader["Reason"].ToString() ?? string.Empty
                            };
                        }
                    }
                }
            }
            return model;
        }

        public async Task<bool> AddEmployeeCommissionAsync(EmployeeCommissionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation
                string chkQuery = "SELECT 1 FROM hr_employeecommissiondetails WHERE citycode=@citycode AND cour_id=@cour_id AND Year=@year AND Month=@month AND CommType=@type and rate=@rate";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@citycode", model.CityCode);
                    cmd.Parameters.AddWithValue("@cour_id", model.RouteCode);
                    cmd.Parameters.AddWithValue("@year", model.Year);
                    cmd.Parameters.AddWithValue("@month", model.Month);
                    cmd.Parameters.AddWithValue("@type", model.CommType);
                    cmd.Parameters.AddWithValue("@rate", model.Rate);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null) throw new ArgumentException("Record is already exists.");
                }

                // Get Emp No
                string emp_no = "";
                using (var cmd = new MySqlCommand("SELECT he.Emp_No FROM hr_employeeroutecode he WHERE he.citycode=@citycode AND he.RouteCode=@route AND he.ToDate IS NULL LIMIT 1", connection))
                {
                    cmd.Parameters.AddWithValue("@citycode", model.CityCode);
                    cmd.Parameters.AddWithValue("@route", model.RouteCode);
                    var empObj = await cmd.ExecuteScalarAsync();
                    if (empObj == null || string.IsNullOrEmpty(empObj.ToString()))
                        throw new ArgumentException("This Route Code is not assign to any employee.");
                    emp_no = empObj.ToString()!;
                }

                decimal amount = model.Quantity * model.Rate;
                string code = await GenerateNewIdAsync(connection, null, "hr_employeecommissiondetails", "Code", 3);

                string insertQuery = @"INSERT INTO hr_employeecommissiondetails
                                       (Code, Year, Month, Emp_No, citycode, cour_id, CommType, Quantity, rate, Amount, Reason, status, CreatedBy, Created_Date, UpdatedBy, Updated_Date)
                                       VALUES (@Code, @Year, @Month, @Emp_No, @citycode, @cour_id, @CommType, @Quantity, @rate, @Amount, @Reason, 'P', @CreatedBy, NOW(), @UpdatedBy, NOW())";

                using (var command = new MySqlCommand(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    command.Parameters.AddWithValue("@Year", model.Year);
                    command.Parameters.AddWithValue("@Month", model.Month);
                    command.Parameters.AddWithValue("@Emp_No", emp_no);
                    command.Parameters.AddWithValue("@citycode", model.CityCode);
                    command.Parameters.AddWithValue("@cour_id", model.RouteCode);
                    command.Parameters.AddWithValue("@CommType", model.CommType);
                    command.Parameters.AddWithValue("@Quantity", model.Quantity);
                    command.Parameters.AddWithValue("@rate", model.Rate);
                    command.Parameters.AddWithValue("@Amount", amount);
                    command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                    command.Parameters.AddWithValue("@CreatedBy", currentUserId);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> UpdateEmployeeCommissionAsync(EmployeeCommissionModel model, string currentUserId)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                // Validation
                string chkQuery = "SELECT 1 FROM hr_employeecommissiondetails WHERE citycode=@citycode AND cour_id=@cour_id AND Year=@year AND Month=@month AND CommType=@type and rate=@rate AND Code<>@Code";
                using (var cmd = new MySqlCommand(chkQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@citycode", model.CityCode);
                    cmd.Parameters.AddWithValue("@cour_id", model.RouteCode);
                    cmd.Parameters.AddWithValue("@year", model.Year);
                    cmd.Parameters.AddWithValue("@month", model.Month);
                    cmd.Parameters.AddWithValue("@type", model.CommType);
                    cmd.Parameters.AddWithValue("@rate", model.Rate);
                    cmd.Parameters.AddWithValue("@Code", model.Code);
                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists != null) throw new ArgumentException("Record is already exists.");
                }

                // Get Emp No
                string emp_no = "";
                using (var cmd = new MySqlCommand("SELECT he.Emp_No FROM hr_employeeroutecode he WHERE he.citycode=@citycode AND he.RouteCode=@route AND he.ToDate IS NULL LIMIT 1", connection))
                {
                    cmd.Parameters.AddWithValue("@citycode", model.CityCode);
                    cmd.Parameters.AddWithValue("@route", model.RouteCode);
                    var empObj = await cmd.ExecuteScalarAsync();
                    if (empObj == null || string.IsNullOrEmpty(empObj.ToString()))
                        throw new ArgumentException("This Route Code is not assign to any employee.");
                    emp_no = empObj.ToString()!;
                }

                decimal amount = model.Quantity * model.Rate;

                string updateQuery = @"UPDATE hr_employeecommissiondetails 
                                       SET Year=@Year, Month=@Month, Emp_No=@Emp_No, citycode=@citycode, cour_id=@cour_id, CommType=@CommType, Quantity=@Quantity, rate=@rate, Amount=@Amount, Reason=@Reason, UpdatedBy=@UpdatedBy, Updated_Date=NOW() 
                                       WHERE Code=@Code";

                using (var command = new MySqlCommand(updateQuery, connection))
                {
                    command.Parameters.AddWithValue("@Code", model.Code);
                    command.Parameters.AddWithValue("@Year", model.Year);
                    command.Parameters.AddWithValue("@Month", model.Month);
                    command.Parameters.AddWithValue("@Emp_No", emp_no);
                    command.Parameters.AddWithValue("@citycode", model.CityCode);
                    command.Parameters.AddWithValue("@cour_id", model.RouteCode);
                    command.Parameters.AddWithValue("@CommType", model.CommType);
                    command.Parameters.AddWithValue("@Quantity", model.Quantity);
                    command.Parameters.AddWithValue("@rate", model.Rate);
                    command.Parameters.AddWithValue("@Amount", amount);
                    command.Parameters.AddWithValue("@Reason", string.IsNullOrEmpty(model.Reason) ? DBNull.Value : model.Reason);
                    command.Parameters.AddWithValue("@UpdatedBy", currentUserId);

                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        public async Task<bool> DeleteEmployeeCommissionAsync(string id)
        {
            using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (connection == null) return false;
                await connection.OpenAsync();

                string query = "DELETE FROM hr_employeecommissiondetails WHERE Code=@Code";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Code", id);
                    int rows = await command.ExecuteNonQueryAsync();
                    return rows > 0;
                }
            }
        }

        #region Tag Commission
        public async Task<(bool success, string message)> ProcessTagCommissionAsync(TagCommissionViewModel model, string currentUserId)
        {
            using (var con = _connectionFactory.CreateConnection() as MySqlConnection)
            {
                if (con == null) return (false, "DB Error");
                await con.OpenAsync();

                try
                {
                    string fRouteIds = string.Join(",", model.RouteCodes.Split(',').Select(x => $"'{x.Trim().PadLeft(5, '0')}'"));
                    
                    var rs = await con.QueryAsync<string>(@"SELECT DISTINCT `BStationId` FROM lcs_hr.`hr_locationmapping` lm 
                                                            WHERE lm.GlLocationId IN(SELECT l.LocationID FROM lcs_setup.locations l
                                                            WHERE l.BILLINGCITYID = (SELECT c.station_id FROM lcs_hr.hr_city c WHERE c.Code = @citycode)) AND BStationId IS NOT NULL;", 
                                                            new { citycode = model.CityFrom }, commandTimeout: 600);

                    string stationIDTo = await con.ExecuteScalarAsync<string>("SELECT hc.station_id FROM hr_city hc WHERE hc.Code=@citycode", new { citycode = model.CityTo });
                    string stationsIDSFrom = "";

                    if (!rs.Any())
                    {
                        stationsIDSFrom = $"'{await con.ExecuteScalarAsync<string>("SELECT hc.station_id FROM hr_city hc WHERE hc.Code=@citycode", new { citycode = model.CityFrom })}'";
                    }
                    else
                    {
                        stationsIDSFrom = string.Join(",", rs.Select(x => $"'{x}'"));
                    }

                    var loc = await con.QueryAsync<string>($@"SELECT DISTINCT `GlLocationId` FROM lcs_hr.`hr_locationmapping` WHERE BStationId IN ({stationsIDSFrom});", commandTimeout: 600);
                    string locationIDFrom = string.Join(",", loc);
                    if (string.IsNullOrEmpty(locationIDFrom)) throw new ArgumentException("Location ID is not define for the selected from city");

                    var locTo = await con.QueryAsync<string>($@"SELECT DISTINCT `GlLocationId` FROM lcs_hr.`hr_locationmapping` WHERE BStationId IN ('{stationIDTo}');", commandTimeout: 600);
                    string locationIDTo = string.Join(",", locTo);
                    if (string.IsNullOrEmpty(locationIDTo)) throw new ArgumentException("Location ID is not define for the selected to city");

                    string checkQuery = $@"SELECT 1 FROM `hr_olecommission` WHERE commonth = {model.Month} AND comyear = {model.Year} AND StationID IN ({stationsIDSFrom}) AND CourierID IN ({fRouteIds}) LIMIT 1";
                    var dtRecordExist = await con.ExecuteScalarAsync(checkQuery);

                    if (dtRecordExist != null)
                    {
                        string updatequery = $@"UPDATE hr_olecommission Set StationId = '{stationIDTo}' Where commonth = {model.Month} AND comyear = {model.Year} AND StationID IN ({stationsIDSFrom}) AND CourierID IN ({fRouteIds})";
                        int rowsaffected = await con.ExecuteAsync(updatequery);

                        if (rowsaffected > 0)
                        {
                            string delquery = $"DELETE FROM `hr_olecommissionprocess` WHERE YEAR = @year AND MONTH = @month AND FIND_IN_SET(`GlLocationId`, @LocIDs) AND CourierID IN ({fRouteIds});";
                            await con.ExecuteAsync(delquery, new { year = model.Year, month = model.Month, LocIDs = locationIDFrom });

                            var dtRates = await con.QueryAsync<CommissionPolicy>("SELECT RateID, `Type`, `RateType`, Rate FROM hr_commissionpolicy WHERE `RateID` BETWEEN 1 AND 12;");
                            if (!dtRates.Any()) throw new ArgumentException("Commission Rates not defined.");

                            // Processing logic converted mapping
                            await CalculateCommissionOLE_Leopards(con, model.Year, model.Month, model.CityTo, dtRates.ToList(), locationIDTo);
                            await CalculateCommissionRBICredit(con, model.Year, model.Month, model.CityTo, locationIDTo);
                            await CalculateCommissionExpressBookingCreditProject(con, model.Year, model.Month, model.CityTo, locationIDTo);
                            await CalculateCommissionDeliveryCredit(con, model.Year, model.Month, model.CityTo, dtRates.ToList(), locationIDTo);

                            return (true, "Commission Taging Process Execute Successfully!");
                        }
                    }
                    else
                    {
                        return (false, "No Record Found for Tagging!");
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }

                return (false, "Unknown Error");
            }
        }

        private async Task CalculateCommissionOLE_Leopards(MySqlConnection con, int year, int month, string toCityCode, List<CommissionPolicy> rates, string locationIDTo)
        {
            var rs = await con.QueryAsync<TotalWeightLocationWise>(@"SELECT 
                 el.LocationId AS GlLocationId
                 ,a.CourierId
                 ,a.`RateID`
                 ,SUM(a.`Total_Weight`) AS Total_Weight
                  FROM lcs_hr.hr_olecommission a
                  INNER JOIN hr_locationmapping lm ON a.StationId=lm.BStationId
                  INNER JOIN lcs_hr.hr_employeeroutecode r ON r.LocationId=lm.GlLocationId AND a.CourierId=r.RouteCode AND r.ToDate IS NULL 
                  INNER JOIN lcs_hr.hr_employeepersonaldetail e ON r.Emp_No=e.EMP_NO
                  INNER JOIN lcs_hr.hr_employeelocationdetails el ON e.EMP_NO=el.Emp_No AND el.ToDate IS NULL
                  WHERE  a.`ComMonth`=@month AND a.`ComYear`= @year AND ifnull(r.CodeType,0) <> 3
                 AND r.citycode IN (@cityId) AND RateID = 5
                 GROUP BY a.StationId,a.CourierId,a.`RateID`;", new { year = year, month = month, cityId = toCityCode }, commandTimeout: 600);

            var rs2 = await con.QueryAsync<TotalWeightLocationWise>(@"SELECT 
                        e.`LocationId` AS `GlLocationId`,
                        c.`RouteCode` AS `CourierId`,
                        a.`RateID` AS `RateID`,
                        SUM(a.`Total_Weight`) AS Total_Weight
                        FROM hr_olecommission a
                        INNER JOIN lcs_hr.hr_city b ON a.StationId=b.station_id
                        INNER JOIN lcs_hr.hr_employeeroutecode c ON c.`citycode`=b.`Code` AND a.`CourierId`=c.`RouteCode` AND c.`ToDate` IS NULL
                        INNER JOIN lcs_hr.`hr_employeepersonaldetail` d ON c.`Emp_No`=d.`EMP_NO`
                        INNER JOIN lcs_hr.`hr_employeelocationdetails` e ON d.`EMP_NO`=e.`Emp_No` AND e.`ToDate` IS NULL
                        WHERE a.`ComMonth`= @month AND a.`ComYear`= @year AND ifnull(c.CodeType,0) <> 3 AND b.`Code`= @cityId AND a.`RateID` = 1
                        GROUP BY a.StationId,a.CourierId,a.`RateID`;", new { year = year, month = month, cityId = toCityCode }, commandTimeout: 600);

            var allData = rs.Concat(rs2).ToList();

            if (allData.Any())
            {
                var dtList = new List<OLECommissionPerKG>();
                foreach (var item in allData)
                {
                    var rateObj = rates.FirstOrDefault(x => x.RateID == item.RateID);
                    if (rateObj != null)
                    {
                        dtList.Add(new OLECommissionPerKG
                        {
                            GlLocationId = item.GlLocationId,
                            RateID = item.RateID,
                            CourierID = item.CourierId,
                            oleCommission = item.Total_Weight * rateObj.Rate
                        });
                    }
                }
                await SaveOLECommissionAfterProcess(con, dtList, "1,5", true, year, month, locationIDTo);
            }
        }

        private async Task CalculateCommissionExpressBookingCreditProject(MySqlConnection con, int year, int month, string toCityCode, string locationIDTo)
        {
            var rs = await con.QueryAsync<TotalWeightLocationWise>(@"SELECT 
                        e.`LocationId` AS `GlLocationId`,
                        c.`RouteCode` AS `CourierId`,
                        a.`RateID` AS `RateID`,
                        SUM(a.`No_Of_Shipment`) AS No_Of_Shipment
                        FROM hr_olecommission a
                        INNER JOIN lcs_hr.hr_city b ON a.StationId=b.station_id
                        INNER JOIN lcs_hr.hr_employeeroutecode c ON c.`citycode`=b.`Code` AND a.`CourierId`=c.`RouteCode` AND c.`ToDate` IS NULL
                        INNER JOIN lcs_hr.`hr_employeepersonaldetail` d ON c.`Emp_No`=d.`EMP_NO`
                        INNER JOIN lcs_hr.`hr_employeelocationdetails` e ON d.`EMP_NO`=e.`Emp_No` AND e.`ToDate` IS NULL
                        WHERE a.`ComMonth`= @month AND a.`ComYear`= @year AND ifnull(c.CodeType,0) IN (9) AND b.`Code`= @cityId AND a.`RateID` IN(6,7,8)
                        GROUP BY a.StationId,a.CourierId,a.`RateID`;", new { year = year, month = month, cityId = toCityCode }, commandTimeout: 600);

            if (rs.Any())
            {
                var dtList = new List<OLECommissionPerKG>();
                var courIds = new List<string>();
                foreach (var item in rs)
                {
                    var rate = item.RateID == 6 ? 0.5M : item.RateID == 7 ? 0.25M : item.RateID == 8 ? 50M : 0M;
                    dtList.Add(new OLECommissionPerKG
                    {
                        GlLocationId = item.GlLocationId,
                        RateID = item.RateID,
                        CourierID = item.CourierId,
                        oleCommission = item.No_Of_Shipment * rate
                    });
                    courIds.Add(item.CourierId);
                }

                await con.ExecuteAsync($"DELETE FROM `hr_olecommissionprocess` WHERE `Year` = {year} AND `Month` ={month} AND GlLocationId IN ({locationIDTo}) AND CourierID IN @courID AND FIND_IN_SET(`RateId`,'6,7,8');", new { courID = courIds });
                await SaveOLECommissionAfterProcess(con, dtList, "6,7,8", false, year, month, locationIDTo);
            }
        }

        private async Task CalculateCommissionDeliveryCredit(MySqlConnection con, int year, int month, string toCityCode, List<CommissionPolicy> rates, string locationIDTo)
        {
            var rs = await con.QueryAsync<TotalWeightLocationWise>(@"SELECT 
                el.LocationId AS GlLocationId
                ,a.CourierId
                ,a.`RateID`
                ,SUM(a.`No_Of_Shipment`) AS No_Of_Shipment
                 FROM lcs_hr.hr_olecommission a
                 INNER JOIN hr_locationmapping lm ON a.StationId=lm.BStationId
                 INNER JOIN lcs_hr.hr_employeeroutecode r ON r.LocationId=lm.GlLocationId AND a.CourierId=r.RouteCode AND r.ToDate IS NULL
                 INNER JOIN lcs_hr.hr_employeepersonaldetail e ON r.Emp_No=e.EMP_NO
                 INNER JOIN lcs_hr.hr_employeelocationdetails el ON e.EMP_NO=el.Emp_No AND el.ToDate IS NULL
                 WHERE  a.`ComMonth`=@month AND a.`ComYear`= @year  AND IFNULL(r.CodeType,0) <> 3
                AND r.citycode IN (@cityId) AND RateID IN (11,12) 
                GROUP BY a.StationId,a.CourierId,a.`RateID`;", new { year = year, month = month, cityId = toCityCode }, commandTimeout: 600);

            if (rs.Any())
            {
                var dtList = new List<OLECommissionPerKG>();
                foreach (var item in rs)
                {
                    var rateObj = rates.FirstOrDefault(x => x.RateID == item.RateID);
                    if (rateObj != null)
                    {
                        dtList.Add(new OLECommissionPerKG
                        {
                            GlLocationId = item.GlLocationId,
                            RateID = item.RateID,
                            CourierID = item.CourierId,
                            oleCommission = item.No_Of_Shipment * rateObj.Rate
                        });
                    }
                }
                await SaveOLECommissionAfterProcess(con, dtList, "11,12", true, year, month, locationIDTo);
            }
        }

        private async Task CalculateCommissionRBICredit(MySqlConnection con, int year, int month, string toCityCode, string locationIDTo)
        {
            var rs = await con.QueryAsync<OLECommissionPerKG>(@"SELECT 
                e.`LocationId` AS `GlLocationId`,
                a.`Cour_Id` AS `CourierId`,
                a.`RateID` AS `RateID`,
                CASE WHEN c.RBIExclude=b'1' THEN SUM(a.OldIncentive) ELSE SUM(a.FinalIncentive) END AS oleCommission
                FROM hr_rbi_incentive_detail a
                INNER JOIN lcs_hr.hr_city b ON a.Station_id=b.station_id
                INNER JOIN lcs_hr.hr_employeeroutecode c ON c.`citycode`=b.`Code` AND a.`Cour_Id`=c.`RouteCode` AND c.`ToDate` IS NULL
                INNER JOIN lcs_hr.`hr_employeepersonaldetail` d ON c.`Emp_No`=d.`EMP_NO`
                INNER JOIN lcs_hr.`hr_employeelocationdetails` e ON d.`EMP_NO`=e.`Emp_No` AND e.`ToDate` IS NULL
                WHERE a.`Month`= @month AND a.`Year`= @year AND ifnull(c.CodeType,0) Not IN (3,9) 
                AND b.`Code`= @cityId AND a.`RateID` IN(75,76,77,78)
                GROUP BY a.Station_id,a.Cour_Id,a.`RateID`;", new { year = year, month = month, cityId = toCityCode }, commandTimeout: 600);

            if (rs.Any())
            {
                await SaveOLECommissionAfterProcess(con, rs.ToList(), "75,76,77,78", true, year, month, locationIDTo);
            }
        }

        private async Task SaveOLECommissionAfterProcess(MySqlConnection con, List<OLECommissionPerKG> dtList, string rateID, bool delete, int year, int month, string locationIDTo)
        {
            using (var trans = await con.BeginTransactionAsync())
            {
                if (delete)
                {
                    await con.ExecuteAsync($"DELETE FROM `hr_olecommissionprocess` WHERE `Year` = {year} AND `Month` ={month} AND GlLocationId IN ({locationIDTo}) AND FIND_IN_SET(`RateId`, '{rateID}');", transaction: trans);
                }

                string query = @"INSERT INTO `lcs_hr`.`hr_olecommissionprocess` VALUES(@year, @month, @glLOCID, @courierId, @ratedID, @TotalAmt, @createdBy, @createdDate)";
                
                await con.ExecuteAsync(query, dtList.Select(x => new
                {
                    year = year,
                    month = month,
                    glLOCID = x.GlLocationId,
                    courierId = x.CourierID,
                    ratedID = x.RateID,
                    TotalAmt = x.oleCommission,
                    createdBy = "1", // Hardcoded per session pattern mapped outside
                    createdDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                }), transaction: trans);

                await trans.CommitAsync();
            }
        }
        #endregion
        
        public class CommissionPolicy
        {
            public int RateID { get; set; }
            public string Type { get; set; } = string.Empty;
            public int RateType { get; set; }
            public decimal Rate { get; set; }
        }
        
        public class OLECommissionPerKG
        {
            public int GlLocationId { get; set; }
            public decimal oleCommission { get; set; }
            public int RateID { get; set; }
            public string CourierID { get; set; } = string.Empty;
        }

        public class TotalWeightLocationWise
        {
            public int GlLocationId { get; set; }
            public decimal Total_Weight { get; set; }
            public int No_Of_Shipment { get; set; }
            public int RateID { get; set; }
            public string CourierId { get; set; } = string.Empty;
        }
    }
}