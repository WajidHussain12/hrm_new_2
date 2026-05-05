using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using MySql.Data.MySqlClient;

public static partial class LCS
{
    public static int[] GetListOfDaysExcludingWeekDay(DateTime toDate, DateTime fromDate, DayOfWeek dayOfWeek)
    {
        var days = new List<int>();
        var noOfDays = (toDate - fromDate).Days + 1;
        for (var i = 0; i < noOfDays; i++)
        {
            var day = fromDate.AddDays(i);
            if (day.DayOfWeek != dayOfWeek)
            {
                days.Add(day.Day);
            }
        }

        return days.ToArray();
    }

    public static int[] GetListOfDayss(DateTime toDate, DateTime fromDate)
    {
        var days = new List<int>();
        var noOfDays = (toDate - fromDate).Days + 1;
        for (var i = 0; i < noOfDays; i++)
        {
            days.Add(fromDate.AddDays(i).Day);
        }

        return days.ToArray();
    }

    public static int[] GetListOfDays(DateTime toDate, DateTime fromDate, DayOfWeek dayOfWeek)
    {
        var days = new List<int>();
        var noOfDays = (toDate - fromDate).Days + 1;
        for (var i = 0; i < noOfDays; i++)
        {
            var day = fromDate.AddDays(i);
            if (day.DayOfWeek == dayOfWeek)
            {
                days.Add(day.Day);
            }
        }

        return days.ToArray();
    }

    public static int[] GetListOfDays(DateTime toDate, DateTime fromDate)
    {
        var days = new List<int>();
        var noOfDays = (toDate - fromDate).Days + 1;
        for (var i = 0; i < noOfDays; i++)
        {
            days.Add(fromDate.AddDays(i).Day);
        }

        return days.ToArray();
    }

    public static DataSet GetDataTableSchema(MySqlConnection connection, params string[] tableNames)
    {
        var dataSet = new DataSet("LCS");
        var sqlQuery = string.Join(string.Empty, tableNames.Select(tableName => $"select * from {tableName} where 1=2;"));

        using var command = connection.CreateCommand();
        command.Connection = connection;
        command.CommandText = sqlQuery;
        command.CommandType = CommandType.Text;

        using var adapter = new MySqlDataAdapter(command);
        adapter.Fill(dataSet);

        for (var i = 0; i < tableNames.Length; i++)
        {
            dataSet.Tables[i].TableName = tableNames[i];
        }

        return dataSet;
    }

    public static DateTime[] GetListOfHoliDays(DateTime toDate, DateTime fromDate)
    {
        var days = new List<DateTime>();
        var noOfDays = (toDate - fromDate).Days + 1;
        for (var i = 0; i < noOfDays; i++)
        {
            days.Add(fromDate.AddDays(i));
        }

        return days.ToArray();
    }

    public static DataTable GetEmployeesCurrentShiftDetailsCityWise(MySqlConnection connection, string cityId, int year, int month, int cityI = 0)
    {
        var (fromDate, toDate) = GetPayrollPeriod(year, month);

        const string sqlQuery = @"
SELECT
  perDet.emp_no,
  empShifts.ShiftCode,
  shiftDet.Start_Time,
  shiftDet.End_Time,
  shiftDet.Grace_Time_IN,
  shiftDet.Grace_Time_OUT,
  shiftDet.Begin_IN,
  shiftDet.End_IN,
  shiftDet.Begin_OUT,
  shiftDet.End_OUT,
  shiftDet.Active,
  shiftDet.TotalHours,
  shiftDet.NightShift,
  empShifts.`FromDate`,
  empShifts.`ToDate`
FROM hr_employeeshifttimings empShifts
INNER JOIN hr_employeepersonaldetail perDet ON perDet.`EMP_NO` = empShifts.`Emp_No`
INNER JOIN hr_shiftdetails shiftDet ON shiftDet.`Code` = empShifts.`ShiftCode`
WHERE perDet.`LEFT_DATE` IS NULL
  AND perDet.APPOINT_DATE <= @ToDate
  AND ((DATE(empShifts.`FromDate`) <= @ToDate AND DATE(empShifts.`ToDate`) >= @FromDate) OR empShifts.`ToDate` IS NULL)
  AND perDet.P_CITY_CODE = @City;";

        return DAL.ExecuteDataTable(
            connection,
            CommandType.Text,
            sqlQuery,
            new MySqlParameter("@City", cityId),
            new MySqlParameter("@FromDate", fromDate),
            new MySqlParameter("@ToDate", toDate));
    }

    public static DataTable GetEmployeeCurrentShiftDetails(MySqlConnection connection, string empNo, int year, int month)
    {
        throw new NotSupportedException("Only city-wise attendance processing is currently ported.");
    }

    public static DataTable GetEmployeesCurrentShiftDetailsDepartmentWise(MySqlConnection connection, string departmentId, int year, int month, int city = 0, int pDept = 0)
    {
        throw new NotSupportedException("Only city-wise attendance processing is currently ported.");
    }

    public static DataTable GetAdjustmentListsCityWise(MySqlConnection connection, string cityId, int year, int month, bool partialAttendance = false, int dayStart = -1, int dayEnd = -1)
    {
        var (fromDate, toDate) = GetPayrollPeriod(year, month);

        var sqlQuery = @"
SELECT
  adj.emp_no,
  adj.adjustmentType,
  adj.Year,
  adj.Month,
  adj.adjustmentDate
FROM hr_employeeattandenceadjust adj
INNER JOIN hr_employeepersonaldetail perDet ON adj.emp_no = perDet.emp_no
WHERE adjustmentDate BETWEEN @FromDate AND @ToDate
  AND perDet.P_CITY_CODE = @City
  AND perDet.EMP_STATUS <> 'I'
  AND perDet.`LEFT_DATE` IS NULL
  AND perDet.APPOINT_DATE <= @CurrentDate";

        var parameters = new List<MySqlParameter>
        {
            new("@FromDate", fromDate),
            new("@ToDate", toDate),
            new("@City", cityId),
            new("@CurrentDate", toDate)
        };

        if (partialAttendance)
        {
            sqlQuery += " AND DAY(adj.adjustmentDate) >= @StartDay AND DAY(adj.adjustmentDate) <= @EndDay";
            parameters.Add(new MySqlParameter("@StartDay", dayStart));
            parameters.Add(new MySqlParameter("@EndDay", dayEnd));
        }

        return DAL.ExecuteDataTable(connection, CommandType.Text, sqlQuery, parameters.ToArray());
    }

    public static DataTable GetAdjustmentListsDepartmentWise(MySqlConnection connection, string departmentId, int year, int month, bool partialAttendance = false, int dayStart = -1, int dayEnd = -1, int cityid = 0, int pDept = 0)
    {
        throw new NotSupportedException("Only city-wise attendance processing is currently ported.");
    }

    public static DataTable GetAdjustmentListsForEmployee(MySqlConnection connection, string empNo, int year, int month, bool partialAttendance = false, int dayStart = -1, int dayEnd = -1)
    {
        throw new NotSupportedException("Only city-wise attendance processing is currently ported.");
    }

    public static DataTable FetchAttendanceLogsCityWise(MySqlConnection connection, string cityId, int year, int month, bool partialAttendance = false, int dayStart = -1, int dayEnd = -1)
    {
        const string employeeQuery = @"
SELECT
  perDet.emp_no,
  perDet.NAME AS `Name`,
  GetEmpDepartmentNameAll(perDet.Emp_No, @CurrentDate) AS Depart,
  (
      SELECT shitDet.ShiftCode
      FROM hr_employeeshifttimings shitDet
      WHERE shitDet.FromDate <= @CurrentDate
        AND shitDet.emp_no = perDet.emp_no
      ORDER BY shitDet.ID DESC
      LIMIT 1
  ) AS ShiftCode
FROM hr_employeepersonaldetail perDet
WHERE perDet.EMP_STATUS <> 'I'
  AND perDet.`LEFT_DATE` IS NULL
  AND perDet.APPOINT_DATE <= @CurrentDate
  AND perDet.P_CITY_CODE = @City
  AND GetEmpDepartmentNameAll(perDet.Emp_No, @CurrentDate) IS NOT NULL;";

        var employees = DAL.ExecuteDataTable(
            connection,
            CommandType.Text,
            employeeQuery,
            new MySqlParameter("@City", cityId),
            new MySqlParameter("@CurrentDate", new DateTime(year, month, 25)));

        if (employees == null || employees.Rows.Count == 0)
        {
            throw new ArgumentException("No data found in selected month.");
        }

        for (var i = employees.Rows.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(employees.Rows[i].Field<string>("ShiftCode")))
            {
                employees.Rows.RemoveAt(i);
            }
        }

        if (employees.Rows.Count == 0)
        {
            throw new ArgumentException("No data found in selected month.");
        }

        return ProcessAttendencelog(connection, year, month, LogsDataMode.CityWise, cityId, employees, partialAttendance, dayStart, dayEnd);
    }

    public static DataTable FetchAttendanceLogsDepartmentWise(MySqlConnection connection, string departmentId, int year, int month, bool partialAttendance = false, int dayStart = -1, int dayEnd = -1, int cityid = 0, int pDept = 0)
    {
        throw new NotSupportedException("Only city-wise attendance processing is currently ported.");
    }

    public static DataTable FetchAttendanceLogsEmployeeWise(MySqlConnection connection, string empNo, int year, int month, bool partialAttendance = false, int dayStart = -1, int dayEnd = -1)
    {
        throw new NotSupportedException("Only city-wise attendance processing is currently ported.");
    }

    public static DataTable ProcessAttendencelog(MySqlConnection connection, int year, int month, LogsDataMode logsDataMode, string modeId, DataTable shiftCodeEmpWiseDataTable, bool partialAttendance = false, int dayStart = -1, int dayEnd = -1, int cityid = 0, int pDept = 0)
    {
        var (fromDate, toDate) = GetPayrollPeriod(year, month);

        var sqlQuery = @"
SELECT
  att.emp_no,
  '' Name,
  '' Depart,
  att.CHECKTIME,
  att.Month,
  att.Year,
  att.Day,
  att.CHECKTYPE,
  att.City,
  att.USERID
FROM hr_employeeattendance att,
(
    SELECT
      perDet.emp_no,
      perDet.P_CITY_CODE cityID,
      (
          SELECT deptDet.DeptCode dCode
          FROM hr_employeedepartmentdetails deptDet
          WHERE deptDet.FromDate <= @CurrentDate
            AND deptDet.emp_no = perDet.emp_no
          ORDER BY deptDet.Code DESC
          LIMIT 1
      ) dCode
    FROM hr_employeepersonaldetail perDet
    WHERE perDet.EMP_STATUS <> 'I'
      AND perDet.`LEFT_DATE` IS NULL
      AND perDet.APPOINT_DATE <= @CurrentDate
      AND perDet.P_CITY_CODE = @City
) deptt
WHERE att.Status <> 'D'
  AND att.`CHECKTIME` BETWEEN @FromDate AND @ToDate
  AND att.emp_no = deptt.emp_no
  AND deptt.cityID = @City";

        var parameters = new List<MySqlParameter>
        {
            new("@CurrentDate", new DateTime(year, month, 25)),
            new("@FromDate", fromDate),
            new("@ToDate", toDate),
            new("@City", modeId)
        };

        if (partialAttendance)
        {
            sqlQuery += " AND att.Day >= @DayFrom AND att.Day <= @DayTo";
            parameters.Add(new MySqlParameter("@DayFrom", dayStart));
            parameters.Add(new MySqlParameter("@DayTo", dayEnd));
        }

        var fetchedAttendanceLogs = DAL.ExecuteDataTable(connection, CommandType.Text, sqlQuery, parameters.ToArray());
        var clonedDataTable = fetchedAttendanceLogs.Clone();
        clonedDataTable.Clear();

        foreach (DataRow employeeRow in shiftCodeEmpWiseDataTable.Rows)
        {
            var filteredLogs = fetchedAttendanceLogs.Select($"emp_no = '{employeeRow["emp_no"].ToString()!.Trim()}'");
            if (filteredLogs.Length > 0)
            {
                foreach (var logRow in filteredLogs)
                {
                    var row = clonedDataTable.NewRow();
                    row["emp_no"] = employeeRow["emp_no"].ToString();
                    row["Name"] = employeeRow["Name"].ToString();
                    row["Depart"] = employeeRow["Depart"].ToString();
                    row["CHECKTIME"] = logRow["CHECKTIME"];
                    row["CHECKTYPE"] = logRow["CHECKTYPE"];
                    row["City"] = logRow["City"];
                    row["USERID"] = logRow["USERID"];
                    row["Year"] = logRow["Year"];
                    row["Month"] = logRow["Month"];
                    row["Day"] = logRow["Day"];
                    clonedDataTable.Rows.Add(row);
                }
            }
            else
            {
                var row = clonedDataTable.NewRow();
                row["emp_no"] = employeeRow["emp_no"].ToString();
                row["Name"] = employeeRow["Name"].ToString();
                row["Depart"] = employeeRow["Depart"].ToString();
                row["CHECKTIME"] = DBNull.Value;
                row["CHECKTYPE"] = DBNull.Value;
                row["City"] = DBNull.Value;
                row["USERID"] = DBNull.Value;
                clonedDataTable.Rows.Add(row);
            }
        }

        return clonedDataTable;
    }

    private static (DateTime fromDate, DateTime toDate) GetPayrollPeriod(int year, int month)
    {
        var toDate = new DateTime(year, month, 25);
        var fromDate = month - 1 == 0
            ? new DateTime(year - 1, 12, 26)
            : new DateTime(year, month - 1, 26);
        return (fromDate, toDate);
    }
}
