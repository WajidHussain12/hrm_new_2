using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Dapper;
using LCS_HR_MVC.Models.Payroll;
using MySql.Data.MySqlClient;

public static partial class LCS
{
    public enum EExtraType
    {
        Day = 1,
        Hour = 2,
        Fuel = 3,
        Amount = 4,
        TeaAllowance = 5,
        TargetIncentive_Retailsales = 6,
        CODPickupCommission = 7,
        ExtraDayAmount = 8,
        ExtraFuelAmount = 9,
        ExtraHoursAmount = 10,
        LoadingunloadingCharges = 11,
        WardrobeAllowance = 12,
        ConvenceCharges = 13,
        EntertainmentCharges = 14,
        ReatilCommission = 15,
        SaleCommission = 16,
        RecoveryCommission = 17,
        MiscellaneousAllowance = 18,
        TargetIncentive_CorporateSales = 19,
        TargetIncentive_CorporateRec = 20
    }

    public static PayrollEmployeeType GetEmployeeType(string empNo, MySqlConnection connection)
    {
        return connection.QueryFirstOrDefault<PayrollEmployeeType>(
                   @"SELECT p.EMPLOYEE_TYPE AS EmpType, p.IsFiler
                     FROM hr_employeepersonaldetail p
                     WHERE p.EMP_NO = @EmpNo",
                   new { EmpNo = empNo })
               ?? new PayrollEmployeeType();
    }

    public static decimal GetCurrentSalary(
        MySqlConnection connection,
        string employeeNo,
        DateTime fromDate,
        out decimal taxPayableAmount,
        out string paymentMode)
    {
        decimal currentSalary = 0m;
        taxPayableAmount = 0m;
        paymentMode = string.Empty;

        if (string.IsNullOrWhiteSpace(employeeNo))
        {
            throw new ArgumentException("For Current salary calculation Employee No should be provided.", nameof(employeeNo));
        }

        if (employeeNo.Trim().Length != 14)
        {
            throw new ArgumentException("Invalid Employee No. It's length should be 14.");
        }

        if (fromDate < new DateTime(2000, 1, 1))
        {
            throw new ArgumentException("For Current salary FromDate should be provided.");
        }

        try
        {
            const string currentSalaryQuery = @"
SELECT (
  CASE
    WHEN @IncreAmount <= he.EffectiveFrom THEN he.SalaryAmount
    WHEN @IncreAmount > he.EffectiveFrom THEN he.IncrementAmount
    ELSE NULL
  END
) AS basicSalary,
he.AmtCash,
he.Current_Flag
FROM hr_employeesalarydetails he
WHERE he.emp_no = @EmpNo;

SELECT inc.Type, SUM(IFNULL(inc.Amount, 0)) amount
FROM hr_increment inc
WHERE inc.ID = @EmpNo
  AND inc.IncStatusID = 2
  AND fromDate <= @IncreAmount
GROUP BY inc.type;";

            var results = DAL.ExecuteDataset(
                connection,
                CommandType.Text,
                currentSalaryQuery,
                new MySqlParameter("@IncreAmount", fromDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)),
                new MySqlParameter("@EmpNo", employeeNo));

            if (results != null && results.Tables.Count > 0)
            {
                decimal basicSalary = 0m;
                decimal oldIncrements = 0m;
                decimal oldDecrements = 0m;

                if (results.Tables[0].Rows.Count > 0)
                {
                    decimal.TryParse(results.Tables[0].Rows[0][0]?.ToString(), out basicSalary);
                    decimal.TryParse(results.Tables[0].Rows[0][1]?.ToString(), out taxPayableAmount);
                    paymentMode = results.Tables[0].Rows[0][2]?.ToString() ?? string.Empty;
                }

                if (results.Tables.Count > 1 && results.Tables[1].Rows.Count > 0)
                {
                    foreach (DataRow row in results.Tables[1].Rows)
                    {
                        if (string.Equals(row[0]?.ToString(), "I", StringComparison.OrdinalIgnoreCase))
                        {
                            oldIncrements = Convert.ToDecimal(row[1]);
                        }
                        else
                        {
                            oldDecrements = Convert.ToDecimal(row[1]);
                        }
                    }
                }

                currentSalary = (basicSalary + oldIncrements) - oldDecrements;
            }
        }
        catch
        {
            throw new ArgumentException("Error calculating current salary for Emp_No : " + employeeNo);
        }

        return currentSalary;
    }

    public static decimal GetFuelAvrfPrice(MySqlConnection connection, DateTime fromDate, DateTime toDate, int year, int month)
    {
        decimal totalPrice = 0m;
        int[] daysInMonth = GetListOfDayss(toDate, fromDate);
        if (daysInMonth.Length == 0)
        {
            return 0m;
        }

        int adjustedMonth = month;
        int adjustedYear = year;
        if (daysInMonth.Any(static day => day >= 26))
        {
            adjustedMonth = month - 1;
            if (adjustedMonth == 0)
            {
                adjustedMonth = 12;
                adjustedYear = year - 1;
            }
        }

        string dateConditions = string.Join(
            ", ",
            daysInMonth
                .Select(day => new DateTime(adjustedYear, adjustedMonth, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Select(static date => $"'{date}'"));

        string fuelQuery = $@"
SELECT DATE(`FromDate`) AS FromDate,
       IF(`ToDate` IS NULL, '{toDate:yyyy-MM-dd}', DATE(`ToDate`)) AS ToDate,
       Price
FROM hr_fuelprices
WHERE (`FromDate` <= '{toDate:yyyy-MM-dd}' AND (`ToDate` >= '{fromDate:yyyy-MM-dd}' OR `ToDate` IS NULL))
  AND `TYPE` = '002'
  AND DATE(`FromDate`) IN ({dateConditions})
ORDER BY CODE;";

        using var command = new MySqlCommand(fuelQuery, connection) { CommandTimeout = 300 };
        using var adapter = new MySqlDataAdapter(command);
        var dt = new DataTable();
        adapter.Fill(dt);

        foreach (DataRow row in dt.Rows)
        {
            totalPrice += Convert.ToDecimal(row["Price"]);
        }

        return Math.Round(totalPrice / daysInMonth.Length, 2);
    }

    public static decimal GetFuelAvrfPrice1(MySqlConnection connection, DateTime fromDate, DateTime toDate, int year, int month)
    {
        decimal totalPrice = 0m;
        int[] daysInMonth = GetListOfDayss(toDate, fromDate);
        if (daysInMonth.Length == 0)
        {
            return 0m;
        }

        int adjustedMonth = month;
        int adjustedYear = year;
        if (daysInMonth.Any(static day => day >= 26))
        {
            adjustedMonth = month - 1;
            if (adjustedMonth == 0)
            {
                adjustedMonth = 12;
                adjustedYear = year - 1;
            }
        }

        foreach (int day in daysInMonth)
        {
            int tempMonth;
            int tempYear;
            if (day >= 26)
            {
                tempMonth = adjustedMonth;
                tempYear = adjustedYear;
            }
            else
            {
                tempMonth = month;
                tempYear = year;
            }

            DateTime currentDate = new(tempYear, tempMonth, day);
            string fuelQuery = $@"
SELECT DATE(`FromDate`) AS FromDate,
       IF(`ToDate` IS NULL, '{toDate:yyyy-MM-dd}', DATE(`ToDate`)) AS ToDate,
       Price
FROM hr_fuelprices
WHERE (fromdate <= '{currentDate:yyyy-MM-dd}' AND Todate >= '{currentDate:yyyy-MM-dd}')
   OR ToDate IS NULL AND `TYPE`='002'
ORDER BY CODE
LIMIT 1;";

            using var command = new MySqlCommand(fuelQuery, connection) { CommandTimeout = 300 };
            using var adapter = new MySqlDataAdapter(command);
            var dt = new DataTable();
            adapter.Fill(dt);

            if (dt.Rows.Count > 0)
            {
                totalPrice += Convert.ToDecimal(dt.Rows[0]["Price"]);
            }
        }

        return Math.Round(totalPrice, 2);
    }

    public static DateTime StartDdateFiscalDate(int month, int year)
    {
        return month > 6 ? new DateTime(year, 7, 1) : new DateTime(year - 1, 7, 1);
    }

    public static List<int> GetHolidaysCityWise(
        MySqlConnection connection,
        int year,
        int month,
        string cityCode,
        bool excludeSundays = true)
    {
        var holidays = new List<int>();

        const string sqlQuery = @"
SELECT
  ho.FromDate,
  ho.ToDate
FROM hr_gazetted_holidays ho
WHERE YEAR = @Year AND MONTH = @Month AND Holiday_flag = 'All'
GROUP BY YEAR, MONTH, DAY(ho.FromDate)
UNION DISTINCT
SELECT
  hol.FromDate,
  hol.ToDate
FROM hr_gazetted_holidays hol
INNER JOIN hr_department hd
  ON hol.Holiday_flag = hd.city_id
WHERE hol.YEAR = @Year AND hol.MONTH = @Month AND hd.city_id = @City
GROUP BY YEAR, MONTH, DAY(hol.FromDate)";

        var fetchedHolidays = DAL.ExecuteDataTable(
            connection,
            CommandType.Text,
            sqlQuery,
            new MySqlParameter("@Year", year),
            new MySqlParameter("@Month", month),
            new MySqlParameter("@City", cityCode.Trim()));

        if (fetchedHolidays == null)
        {
            return holidays;
        }

        foreach (DataRow row in fetchedHolidays.Rows)
        {
            if (excludeSundays)
            {
                holidays.AddRange(GetListOfDaysExcludingWeekDay(
                    row.Field<DateTime>("ToDate"),
                    row.Field<DateTime>("FromDate"),
                    DayOfWeek.Sunday));
            }
            else
            {
                holidays.AddRange(GetListOfDays(
                    row.Field<DateTime>("ToDate"),
                    row.Field<DateTime>("FromDate")));
            }
        }

        return holidays;
    }

    public static DateTime EndDdateFiscalDate(int month, int year)
    {
        return month > 6 ? new DateTime(year + 1, 6, 30) : new DateTime(year, 6, 30);
    }

    public static Array GetMonths(DateTime date1, DateTime date2)
    {
        return GetDates(date1, date2)
            .Select(static date => date.ToString("yyyy-MM-01", CultureInfo.InvariantCulture))
            .ToArray();
    }

    public static IEnumerable<DateTime> GetDates(DateTime date1, DateTime date2)
    {
        while (date1 < date2)
        {
            yield return date1;
            date1 = date1.AddMonths(1);
        }

        if (date1 > date2 && date1.Month == date2.Month)
        {
            yield return date1;
        }
    }
}
