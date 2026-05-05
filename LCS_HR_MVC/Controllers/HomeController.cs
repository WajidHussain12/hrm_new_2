using System.Data;
using System.Security.Claims;
using System.Text.Json;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models;
using LCS_HR_MVC.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Diagnostics;

namespace LCS_HR_MVC.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IDbConnectionFactory _connectionFactory;

        public HomeController(ILogger<HomeController> logger, IDbConnectionFactory connectionFactory)
        {
            _logger = logger;
            _connectionFactory = connectionFactory;
        }

        public async Task<IActionResult> Index()
        {
            var model = new DashboardViewModel
            {
                ShowDashboard = false,
                FiscalRange = await GetFiscalRangeAsync()
            };

            var userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (userRole == "006" || userRole == "002")
            {
                model.ShowDashboard = true;
                
                try
                {
                    using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
                    {
                        if (connection != null)
                        {
                            await connection.OpenAsync();

                            // Fetch Extras Data
                            string extrasQuery = @"
                                SELECT 
                                DATE_FORMAT(DATE(CONCAT(s.`SalaryYear`,'-',s.`SalaryMonth`,'-01')), '%b') AS MM,
                                SUM( (s.`extra_days_amt`+s.`extra_fuel_amt`+s.`extra_hours_amt`+s.`Extra_amount`)) AS Extras
                                FROM `hr_salaryprocessed_hdr` s
                                WHERE DATE(CONCAT(s.`SalaryYear`, '-', s.`SalaryMonth`, '-01')) < NOW() AND
                                DATE(CONCAT(s.`SalaryYear`, '-', s.`SalaryMonth`, '-01')) > DATE_ADD(NOW(), INTERVAL - 7 MONTH)
                                GROUP BY MM ORDER BY MONTH(DATE(CONCAT(s.`SalaryYear`,'-',s.`SalaryMonth`,'-01'))) ASC;";

                            var extrasList = new List<object>();
                            using (var cmdExtras = new MySqlCommand(extrasQuery, connection))
                            using (var reader = await cmdExtras.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    extrasList.Add(new
                                    {
                                        MM = reader["MM"].ToString(),
                                        Extras = Convert.ToDecimal(reader["Extras"] == DBNull.Value ? 0 : reader["Extras"])
                                    });
                                }
                            }

                            // Fetch Deductions Data
                            string deductionsQuery = @"
                                SELECT 
                                DATE_FORMAT(DATE(CONCAT(s.`SalaryYear`,'-',s.`SalaryMonth`,'-01')), '%b') AS MM,
                                SUM( (s.`Total_Deduction`)) AS Deduction
                                FROM `hr_salaryprocessed_hdr` s
                                WHERE DATE(CONCAT(s.`SalaryYear`, '-', s.`SalaryMonth`, '-01')) < NOW() AND
                                DATE(CONCAT(s.`SalaryYear`, '-', s.`SalaryMonth`, '-01')) > DATE_ADD(NOW(), INTERVAL - 7 MONTH)
                                GROUP BY MM ORDER BY MONTH(DATE(CONCAT(s.`SalaryYear`,'-',s.`SalaryMonth`,'-01'))) ASC;";

                            var deductionsList = new List<object>();
                            using (var cmdDeductions = new MySqlCommand(deductionsQuery, connection))
                            using (var reader = await cmdDeductions.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    deductionsList.Add(new
                                    {
                                        MM = reader["MM"].ToString(),
                                        Deduction = Convert.ToDecimal(reader["Deduction"] == DBNull.Value ? 0 : reader["Deduction"])
                                    });
                                }
                            }

                            model.ExtrasChartData = JsonSerializer.Serialize(extrasList);
                            model.DeductionChartData = JsonSerializer.Serialize(deductionsList);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching dashboard data.");
                }
            }

            return View(model);
        }

        private async Task<string> GetFiscalRangeAsync()
        {
            try
            {
                using (var connection = _connectionFactory.CreateConnection() as MySqlConnection)
                {
                    if (connection != null)
                    {
                        await connection.OpenAsync();
                        string query = "SELECT DATE_FORMAT(date_start, '%e %b %Y') date_start, DATE_FORMAT(date_end, '%e %b %Y') date_end FROM lcs_company";
                        using (var cmd = new MySqlCommand(query, connection))
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                return $"{reader["date_start"]} - {reader["date_end"]}";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching fiscal range.");
            }
            return string.Empty;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
