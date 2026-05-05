using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    /// <summary>
    /// All salary/leave/tax configuration loaded from DB tables.
    /// Replaces every hardcoded scalar in PayrollService leave and salary partial files.
    /// Load once per salary run via <see cref="LoadAsync"/>.
    /// </summary>
    public class SalaryConfig
    {
        // ── Scalar values (hr_salary_config) ─────────────────────────────────

        /// <summary>Month number when the leave year starts (default 7 = July).</summary>
        public int LeaveYearStartMonth { get; private set; } = 7;

        /// <summary>Day of month when the leave year starts (default 1).</summary>
        public int LeaveYearStartDay { get; private set; } = 1;

        /// <summary>Gross salary threshold for leave encashment cashability rules (default Rs 50,000).</summary>
        public decimal LeaveEncashmentGrossThreshold { get; private set; } = 50000m;

        /// <summary>Standard days divisor for per-day salary calculation (default 30).</summary>
        public int SalaryDaysDivisor { get; private set; } = 30;

        /// <summary>GL code used for salary deductions in monthly tax correction (default 167).</summary>
        public int TaxSalaryGlCode { get; private set; } = 167;

        /// <summary>Minimum months of service required for leave encashment eligibility (default 6).</summary>
        public int LeaveMonthsMinForCashout { get; private set; } = 6;

        // ── Tax exclusion employees (hr_salary_tax_exclusions) ────────────────

        /// <summary>
        /// Employee numbers that bypass the monthly tax correction step.
        /// Defaults to the 9 known special-arrangement employees.
        /// </summary>
        public HashSet<string> TaxExcludedEmpNos { get; private set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "00000000013203",
            "00000000013548",
            "00000000032787",
            "00000000032864",
            "00000000032837",
            "00000000032838",
            "00000000032857",
            "00000000032861",
            "00000000032865"
        };

        /// <summary>Parameterless constructor — all values are at safe defaults. Use <see cref="LoadAsync"/> for production.</summary>
        public SalaryConfig() { }

        // ── Loader ────────────────────────────────────────────────────────────

        /// <summary>
        /// Load all salary configuration from S10 database tables.
        /// Falls back to hardcoded defaults if either table is missing (safe for first deployment).
        /// </summary>
        public static async Task<SalaryConfig> LoadAsync(MySqlConnection connection, MySqlTransaction? transaction = null)
        {
            var cfg = new SalaryConfig();
            try
            {
                // ── 1. Scalar config (hr_salary_config) ──────────────────────
                var scalars = await connection.QueryAsync<(string Key, string Value)>(
                    "SELECT ConfigKey, ConfigValue FROM lcs_hr.hr_salary_config",
                    transaction: transaction);

                foreach (var (key, value) in scalars)
                {
                    switch (key)
                    {
                        case "LEAVE_YEAR_START_MONTH":
                            cfg.LeaveYearStartMonth = int.TryParse(value, out var m) ? m : 7;
                            break;
                        case "LEAVE_YEAR_START_DAY":
                            cfg.LeaveYearStartDay = int.TryParse(value, out var d) ? d : 1;
                            break;
                        case "LEAVE_ENCASHMENT_GROSS_THRESHOLD":
                            cfg.LeaveEncashmentGrossThreshold = decimal.TryParse(value, out var t) ? t : 50000m;
                            break;
                        case "SALARY_DAYS_DIVISOR":
                            cfg.SalaryDaysDivisor = int.TryParse(value, out var s) ? s : 30;
                            break;
                        case "TAX_SALARY_GLCODE":
                            cfg.TaxSalaryGlCode = int.TryParse(value, out var g) ? g : 167;
                            break;
                        case "LEAVE_MONTHS_MIN_FOR_CASHOUT":
                            cfg.LeaveMonthsMinForCashout = int.TryParse(value, out var l) ? l : 6;
                            break;
                    }
                }

                // ── 2. Tax-excluded employee numbers (hr_salary_tax_exclusions) ──
                var exclusions = await connection.QueryAsync<string>(
                    "SELECT EmpNo FROM lcs_hr.hr_salary_tax_exclusions WHERE IsActive = 1",
                    transaction: transaction);

                if (exclusions.Any())
                    cfg.TaxExcludedEmpNos = new HashSet<string>(exclusions, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // Return safe defaults if tables are missing (first deployment).
            }

            return cfg;
        }
    }
}
