using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

/// <summary>
/// Summary description for AttendanceCalculator
/// </summary>
public class AttendanceCalculator : IDisposable
{

    private MySqlConnection _connection;

    private DataTable _shiftDetails;

    /// <summary>
    ///   Shift Details (i.e. Only Active employees)
    ///<para>Columns are : "</para>   
    ///<para>emp_no Varchar(14)</para>   
    ///<para>ShiftCode VarChar(3)</para>   
    ///<para>Start_Time (Time)</para>   
    ///<para>End_Time (Time)</para>   
    ///<para>Grace_Time_IN (Time)</para>   
    ///<para>Grace_Time_OUT (Time)</para>   
    ///<para>Begin_IN (Time)</para>   
    ///<para>End_IN (Time)</para>   
    ///<para>Begin_OUT (Time)</para>   
    ///<para>End_OUT (Time)</para>   
    ///<para>Active VarChar(1) i.e "Y" OR "N"</para>   
    ///<para>NightShift VarChar(1) i.e "Y" OR "N"</para>   
    /// </summary>


    public DataTable ShiftDetails
    {

        get
        {
            return _shiftDetails;
        }
        set
        {
            ValidateShiftDetail(value);
            _shiftDetails = value;
        }
    }

    /// <summary>
    /// Adjustment Details (i.e. Only Active employees)
    /// </summary>

    private DataTable _AttendanceAdjustments;

    /// <summary>
    ///   Adjustment Details (i.e. Only Active employees)
    ///<para>Columns are : "</para>   
    ///<para>emp_no (Varchar(14))</para>   
    ///<para>adjustmentType (VarChar(3))</para>   
    ///<para>Year (Int)</para>   
    ///<para>Month (Int)</para>   
    ///<para>adjustmentDate (DateTime)</para>       
    /// </summary>
    public DataTable AttendanceAdjustments
    {
        get
        {
            return _AttendanceAdjustments;
        }
        set
        {
            ValidateAttendanceAdjustments(value);
            _AttendanceAdjustments = value;
        }
    }
    /// <summary>
    /// All Attendance Logs of Current month(i.e. Only Active employees)
    /// </summary>

    private DataTable _AttendanceLogs;



    /// <summary>
    ///   All Attendance Logs of Current month(i.e. Only Active employees)
    ///<para>Columns are : "</para>   
    /// <para> emp_no (varchar(14))</para>   
    /// <para> Name (varchar(45))</para>   
    /// <para>CHECKTIME(DateTime)</para>   
    /// <para>CHECKTYPE(Varchar(1)) i.e "I" OR "O"</para>   
    /// <para>City(Varchar(60))</para>   
    /// <para>USERID (BigInt(20))</para> 
    /// </summary>
    public DataTable AttendanceLogs
    {
        get
        {
            return _AttendanceLogs;
        }
        set
        {
            ValidateAttenadnceLogs(value);
            _AttendanceLogs = value;
        }
    }

    /// <summary>
    /// Attendance table's clone from datatbase
    /// </summary>
    private DataTable _hr_employeeattendanceprocess;


    int _year;
    /// <summary>
    /// Selected Year
    /// </summary>      
    public int Year
    {
        get
        {
            return _year;
        }
    }

    int _month;

    /// <summary>
    /// Selected Month
    /// </summary>
    public int Month
    {
        get
        {
            return _month;
        }
    }
    //  public LogsDataMode LogsMode { get; set; }

    private bool _ProcessConsecutiveLates;

    /// <summary>
    /// Whether to process consecutive late or not
    /// </summary>
    public bool ProcessConsecutiveLates
    {
        get
        {
            return _ProcessConsecutiveLates;
        }
    }


    private List<int> _Sundays;

    /// <summary>
    /// List of Sundays in Current month (List<Int>>)
    /// </summary>
    public List<int> Sundays
    {
        get
        {
            return _Sundays;
        }
    }



    private DataTable _HolidaysFound;
    private DataTable _LeavesFound;

    public int TempHoliday;
    public int ReportPerameterSubday;

    /// <summary>
    /// Holidays Found in current month
    /// <para>
    /// FromDate (DateTime)
    /// </para>
    /// <para>
    /// ToDate (DateTime)
    /// </para>
    /// </summary>
    public DataTable HolidaysFound
    {
        get
        {
            return _HolidaysFound;
        }

    }
    public DataTable LeavesFound
    {
        get
        {
            return _LeavesFound;
        }

    }

    private List<int> _Holidays;
    private List<int> _Leaves;

    /// <summary>
    /// List of Holidays in current month (i.e List<<int>>)
    /// </summary>
    public List<int> Holidays
    {
        get
        {
            return _Holidays;
        }
    }
    public List<int> Leaves
    {
        get
        {
            return _Leaves;
        }
    }


    int __daysWorked;
    int __late;
    int __halfDay;
    int __notOut;
    int __early;
    int __ruleAbsents;
    int __absents;
    int __notIn;
    int __normal;
    decimal __avgHours;
    int __avgDays;
    double TotalExtrahrs;
    double AcualExtrahrs;
    int extraDay;
    string __ModeID = "";
    string __CityID = "";

    int __consecutiveLates;
    int __adjustmentLate;
    int __adjustmentAbsent;
    int __adjustmentRAbsent;
    public string TCity="";

    /// <summary>
    /// Total No of holidays in current month
    /// </summary>
    private int totalMonthHolidays;
    private int totalMonthLeaves = 0;

    /// <summary>
    /// List of working days in current month
    /// </summary>
    List<int> _workingDays = new List<int>();
    /// <summary>
    /// List of all rule absents in current month
    /// </summary>
    List<int> _ruleAbsents = new List<int>();


    DataTable LogsFlags_Dt;

    /// <summary>
    /// List of adjustments flags 
    /// </summary>
    Dictionary<int, string> ListOfAdjustmentStatus = new Dictionary<int, string>();




    /// <summary>
    /// Array of days excluding sunday in the current month
    /// </summary>
    private int[] _DaysExcludingSundays;
    private int[] TempMonthDays;

    /// <summary>
    /// List of days in the month
    /// </summary>
    public IEnumerable<int> _DaysInMonth;
    public IEnumerable<int> absentDays;
    public IEnumerable<int> _OffDays;

    public List<int> _tempDaysInMonth;


    /// <summary>
    /// Flag for mode of process , either Month wise or partially 
    /// </summary>
    private bool _PartialAttendance = false;
    private int _dayStart = -1;
    private int _dayEnd = -1;

    /// <summary>
    /// Constructor of Class
    /// </summary>
    /// <param name="year">Selected Year</param>
    /// <param name="month">Selected Month</param>
    /// <param name="_logsDmode"></param>
    /// <param name="ModeID">Current City</param>
    /// <param name="CalculateConsecutiveLates">Whether to process consecutive lates or not</param>    
    /// <param name="mySqlConnection">An Open Mysql Connection</param>
    /// <param name="PartialAttendance">Whether process logs partially or not</param>
    /// <param name="dayStart">Month start day</param>
    /// <param name="dayEnd">Month end day</param>
    /// 

    private DateTime getEmpJoiningDate(string empNo)
    {
        DateTime appointDate = new DateTime();
        using (var _connection = DAL.GetConnection())
        {
            string query = "select APPOINT_DATE from hr_employeepersonaldetail where emp_no='" + empNo + "'";
            MySqlCommand cmd = new MySqlCommand(query, _connection);
            MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                appointDate = Convert.ToDateTime(dr["APPOINT_DATE"].ToString());
            }
            _connection.Close();
        } 
        return appointDate;
    }


    //month start to end date
    DateTime from = new DateTime();
    DateTime to = new DateTime();
    public int OffdayIndex = 0;
    public string getOffdayOfEmployee(string empNO)
    {
        string day = "";
        using (var _connection = DAL.GetConnection())
        {
            string query = "select offDay from hr_employeeshifttimings where Emp_No='" + empNO + "'";
            MySqlCommand cmd = new MySqlCommand(query, _connection);
            MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                day = dr["offDay"].ToString();
            }
            _connection.Close();
        }

        return day;

    }
    public AttendanceCalculator(int year, int month, LogsDataMode _logsDmode, string ModeID, bool CalculateConsecutiveLates, MySqlConnection mySqlConnection,string TempCity="" ,bool PartialAttendance = false, int dayStart = -1, int dayEnd = -1,int city=0,int pDept=0)
    {
        TCity = TempCity;
        if (month - 1 == 0)
        {
            //if (getEmpJoiningDate(ModeID).Month == 12 )
            //{
            //    from = getEmpJoiningDate(ModeID);
            //}
            //else
            //{
                from = new DateTime(year - 1, 12, 26);
           // }
        }                                                             /*<<<<--- code by Muhammad Uzair  */
        else
        {
            //if (getEmpJoiningDate(ModeID).Month == month && getEmpJoiningDate(ModeID).Year==year)
            //{
            //    from = getEmpJoiningDate(ModeID);
            //}
            //else
            //{
                from = new DateTime(year, month - 1, 26);
          //  }
        }
        to = new DateTime(year, month, 25);

        _PartialAttendance = PartialAttendance;
        _dayStart = dayStart;
        _dayEnd = dayEnd;

        _year = year;
        _month = month;
        if (_PartialAttendance)
        {
            _DaysInMonth = GetInclusiveRange(_dayStart, _dayEnd);
        }
        else
        {
            _DaysInMonth = LCS.GetListOfDayss(to, from);
            //  _DaysInMonth = Enumerable.Range(1,DateTime.DaysInMonth(_year,_month));
        }


        if (_PartialAttendance)
        {

            from = new DateTime(year, month, _dayStart);
            to = new DateTime(year, month, _dayEnd);
            _DaysExcludingSundays = LCS.GetListOfDays(to, from, DayOfWeek.Sunday);
        }
        else
        {

            _DaysExcludingSundays = LCS.GetListOfDays(to, from, DayOfWeek.Sunday);


        }
        __ModeID = ModeID;

        _ProcessConsecutiveLates = CalculateConsecutiveLates;
        _connection = mySqlConnection;
        SetTotalSundaysAndHolidays();
        GetTblAttendanceProcessSchema();
        LogsFlags_Dt = GetLogsFlagDt(_DaysInMonth);



        if (_logsDmode == LogsDataMode.CityWise)
        {
            TCity = ModeID;
            ShiftDetails = LCS.GetEmployeesCurrentShiftDetailsCityWise(_connection, ModeID, year, month);
            if (!PartialAttendance)
            {
                AttendanceLogs = LCS.FetchAttendanceLogsCityWise(_connection, ModeID, year, month);
                AttendanceAdjustments = LCS.GetAdjustmentListsCityWise(_connection, ModeID, year, month);
            }
            else
            {
                AttendanceLogs = LCS.FetchAttendanceLogsCityWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
                AttendanceAdjustments = LCS.GetAdjustmentListsCityWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
            }

        }
        else if (_logsDmode == LogsDataMode.DepartmentWise)
        {     
            ShiftDetails = LCS.GetEmployeesCurrentShiftDetailsDepartmentWise(_connection, ModeID, year, month,city,pDept);
            if (!PartialAttendance)
            {
                AttendanceLogs = LCS.FetchAttendanceLogsDepartmentWise(_connection, ModeID, year, month,false,-1,-1,city,pDept);
                AttendanceAdjustments = LCS.GetAdjustmentListsDepartmentWise(_connection, ModeID, year, month,false,-1,-1,city,pDept);
            }
            else
            {
                AttendanceLogs = LCS.FetchAttendanceLogsDepartmentWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
                AttendanceAdjustments = LCS.GetAdjustmentListsDepartmentWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);

            }


        }
        else
        {
            /* In EmployeeWise mode Status won't be checked i.e A,I,S */
            var d = DAL.ExecuteDataTable(_connection, CommandType.Text, $@"SELECT p.`P_CITY_CODE` FROM hr_employeepersonaldetail p WHERE p.`EMP_NO`={ModeID} ;");
            TCity = d.Rows[0][0].ToString();

            ShiftDetails = LCS.GetEmployeeCurrentShiftDetails(_connection, ModeID, year, month);
            if (!PartialAttendance)
            {

                AttendanceLogs = LCS.FetchAttendanceLogsEmployeeWise(_connection, ModeID, year, month);
                AttendanceAdjustments = LCS.GetAdjustmentListsForEmployee(_connection, ModeID, year, month);

            }
            else
            {
                AttendanceLogs = LCS.FetchAttendanceLogsEmployeeWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
                AttendanceAdjustments = LCS.GetAdjustmentListsForEmployee(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
            }

        }

    }

    public AttendanceCalculator(int year, int month, LogsDataMode _logsDmode, string ModeID, string CityId, bool CalculateConsecutiveLates, MySqlConnection mySqlConnection, bool PartialAttendance = false, int dayStart = -1, int dayEnd = -1)
    {
        TCity = CityId;
        if (month - 1 == 0)
        {
            //if (getEmpJoiningDate(ModeID).Month == 12 )
            //{
            //    from = getEmpJoiningDate(ModeID);
            //}
            //else
            //{
            from = new DateTime(year - 1, 12, 26);
            // }
        }                                                             /*<<<<---- code by Muhammad Uzair  */
        else
        {
            //if (getEmpJoiningDate(ModeID).Month == month && getEmpJoiningDate(ModeID).Year==year)
            //{
            //    from = getEmpJoiningDate(ModeID);
            //}
            //else
            //{
            from = new DateTime(year, month - 1, 26);
            //  }
        }
        to = new DateTime(year, month, 25);

        _PartialAttendance = PartialAttendance;
        _dayStart = dayStart;
        _dayEnd = dayEnd;


        _year = year;
        _month = month;
        if (_PartialAttendance)
        {
            _DaysInMonth = GetInclusiveRange(_dayStart, _dayEnd);
        }
        else
        {
            _DaysInMonth = LCS.GetListOfDayss(to, from);

            //  _DaysInMonth = Enumerable.Range(1, DateTime.DaysInMonth(_year, _month));

        }

        if (_PartialAttendance)
        {
            var from = new DateTime(year, month, _dayStart);
            var to = new DateTime(year, month, _dayEnd);
            _DaysExcludingSundays = LCS.GetListOfDays(to, from, DayOfWeek.Sunday);
        }
        else
        {
            _DaysExcludingSundays = LCS.GetListOfDays(to, from, DayOfWeek.Sunday);
        }
        __ModeID = ModeID;
        __CityID = CityId;
        _ProcessConsecutiveLates = CalculateConsecutiveLates;
        _connection = mySqlConnection;
        SetTotalSundaysAndHolidays();
        GetTblAttendanceProcessSchema();
        LogsFlags_Dt = GetLogsFlagDt(_DaysInMonth);



        if (_logsDmode == LogsDataMode.CityWise)
        {
            ShiftDetails = LCS.GetEmployeesCurrentShiftDetailsCityWise(_connection, ModeID, year, month);
            if (!PartialAttendance)
            {
                AttendanceLogs = LCS.FetchAttendanceLogsCityWise(_connection, ModeID, year, month);
                AttendanceAdjustments = LCS.GetAdjustmentListsCityWise(_connection, ModeID, year, month);
            }
            else
            {
                AttendanceLogs = LCS.FetchAttendanceLogsCityWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
                AttendanceAdjustments = LCS.GetAdjustmentListsCityWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
            }

        }
        else if (_logsDmode == LogsDataMode.DepartmentWise)
        {
            ShiftDetails = LCS.GetEmployeesCurrentShiftDetailsDepartmentWise(_connection, ModeID, year, month);
            if (!PartialAttendance)
            {
                AttendanceLogs = LCS.FetchAttendanceLogsDepartmentWise(_connection, ModeID, year, month);
                AttendanceAdjustments = LCS.GetAdjustmentListsDepartmentWise(_connection, ModeID, year, month);
            }
            else
            {
                AttendanceLogs = LCS.FetchAttendanceLogsDepartmentWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
                AttendanceAdjustments = LCS.GetAdjustmentListsDepartmentWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);

            }

        }
        else
        {
            /* In EmployeeWise mode Status won't be checked i.e A,I,S */
            ShiftDetails = LCS.GetEmployeeCurrentShiftDetails(_connection, ModeID, year, month);
            if (!PartialAttendance)
            {
                AttendanceLogs = LCS.FetchAttendanceLogsEmployeeWise(_connection, ModeID, year, month, false, from.Day, to.Day);
                AttendanceAdjustments = LCS.GetAdjustmentListsForEmployee(_connection, ModeID, year, month, false, from.Day, to.Day);
            }
            else
            {
                AttendanceLogs = LCS.FetchAttendanceLogsEmployeeWise(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
                AttendanceAdjustments = LCS.GetAdjustmentListsForEmployee(_connection, ModeID, year, month, _PartialAttendance, _dayStart, _dayEnd);
            }

        }

    }

    /// <summary>
    /// To determine Employee attendance log status
    /// </summary>
    /// <param name="Emp_No">Employee No VarChar(14)</param>
    /// <param name="day">Selected Day (Integer)</param>
    /// <returns>Selected day status (e.g RA-N , i.e Rule Absent-Normal)</returns>
    public string this[string Emp_No, int day]
    {
        get
        { /* return the specified index here */
            string flag = "";
            if (_shiftDetails.Rows[0][0].ToString() == Emp_No)
            {
                if (ListOfAdjustmentStatus != null)
                {
                    if (ListOfAdjustmentStatus.Count > 0)
                    {
                        if (ListOfAdjustmentStatus.ContainsKey(day))
                        {
                            flag = ListOfAdjustmentStatus[day];
                        }
                    }
                }
            }
            return flag;
        }

    }

    /// <summary>
    /// To determine Employee attendance log status for the whole month
    /// </summary>
    /// <param name="Emp_No">Employee No VarChar(14)</param>
    /// <returns>Selected month attendance logs statuses in the Dictionary of Int-String (day is key,Status is Value)</returns>
    public Dictionary<int, string> this[string Emp_No]
    {
        get
        { /* return the specified index here */

            Dictionary<int, string> flags = new Dictionary<int, string>();

            if (_shiftDetails.Rows[0][0].ToString() == Emp_No)
            {
                if (ListOfAdjustmentStatus != null)
                {
                    if (ListOfAdjustmentStatus.Count > 0)
                    {

                        flags = ListOfAdjustmentStatus;

                    }
                }
            }
            return flags;
        }

    }
    /// <summary>
    /// Validating against database
    /// </summary>
    /// <param name="_ShiftDetails"> Shift detail data source</param>
    private void ValidateShiftDetail(DataTable _ShiftDetails)
    {


        if (_ShiftDetails == null)
        {
            throw new ArgumentException("Attendance Logs cannot be null.");
        }

        string[] columns = _ShiftDetails.Columns.OfType<DataColumn>()
                             .Select(a => a.ColumnName).OrderBy(a => a).ToArray();

        string[] defaultColumns = (new string[] {
        "emp_no","ShiftCode","Start_Time","End_Time",
        "Grace_Time_IN","Grace_Time_OUT","Begin_IN","End_IN",
        "Begin_OUT","End_OUT","Active","TotalHours","NightShift","FromDate","ToDate" }).OrderBy(a => a).ToArray();

        if (!columns.SequenceEqual(defaultColumns, StringComparer.InvariantCultureIgnoreCase))
        {
            throw new ArgumentException("Schema difference for Shift Details data source.");
        }
    }

    /// <summary>
    /// Validating data and schema of data source
    /// </summary>
    /// <param name="AttendanceLogsDt">Attendance log data source</param>
    private void ValidateAttenadnceLogs(DataTable AttendanceLogsDt)
    {
        if (AttendanceLogsDt == null)
        {
            throw new ArgumentException("Attendance Logs cannot be null.");
        }

        string[] columns = AttendanceLogsDt.Columns.OfType<DataColumn>()
                             .Select(a => a.ColumnName).OrderBy(a => a).ToArray();

        string[] defaultColumns = (new string[] { "emp_no", "Name", "depart", "CHECKTIME", "CHECKTYPE", "City", "USERID", "Year", "Month", "Day" }).OrderBy(a => a).ToArray();

        if (!columns.SequenceEqual(defaultColumns, StringComparer.InvariantCultureIgnoreCase))
        {
            throw new ArgumentException("Schema difference for Attendance Logs data source.");
        }


    }

    /// <summary>
    /// Validating attendance data and schema
    /// </summary>
    /// <param name="__AttendanceAdjustments">Attendance Adjustments data source</param>
    private void ValidateAttendanceAdjustments(DataTable __AttendanceAdjustments)
    {
        if (__AttendanceAdjustments == null)
        {
            throw new ArgumentException("Attendance Adjustments data source cannot be null.");
        }

        string[] columns = __AttendanceAdjustments.Columns.OfType<DataColumn>()
                             .Select(a => a.ColumnName).OrderBy(a => a).ToArray();

        string[] defaultColumns = (new string[] { "emp_no", "adjustmentType", "Year", "Month", "adjustmentDate" }).OrderBy(a => a).ToArray();

        if (!columns.SequenceEqual(defaultColumns, StringComparer.InvariantCultureIgnoreCase))
        {
            throw new ArgumentException("Schema difference for Attendance Adjustments data source.");
        }
    }

    /// <summary>
    /// Main Process for calculating attendance
    /// </summary>
    /// <returns>Returns Clone of attendance data table from database filled with data</returns>
    /// 
    int[] days;
    public DataTable ProcessAttendance()
    {

        if (_shiftDetails == null)
        {
            throw new ArgumentException("Shift Details data source cannot be empty.");
        }
        else
        {
            if (_shiftDetails.Rows.Count == 0)
            {
                throw new ArgumentException("Shift Details data source cannot be empty.");

            }

        }
        if (_AttendanceLogs == null)
        {
            throw new ArgumentException("Attendance Logs data source cannot be empty.");
        }
        if (_AttendanceAdjustments == null)
        {
            throw new ArgumentException("Attendance adjustment data source cannot be empty.");
        }



        var filterdDataByID = _AttendanceLogs.AsEnumerable().GroupBy(a => a.Field<string>("emp_no"));

        if (filterdDataByID.IsAny())
        {

            foreach (var recordsEmpWise in filterdDataByID)
            {
                //List<Tuple<DateTime,int>> flagsList = new List<Tuple<DateTime,int>>();

                /* Resetting Variables which have to be change for each employee */
                ResetGlobalVariables();
                SetTotalSundaysAndHolidays(recordsEmpWise.Key);
                ProcessTotalLeaves(recordsEmpWise.Key);

                #region geting Off days of employe Code by uzair
                OffdayIndex = 0;
               /* string d = getOffdayOfEmployee(recordsEmpWise.Key);
                if (d == "Monday")
                {
                    _OffDays = LCS.GetListOfDays(to, from, DayOfWeek.Monday);
                    OffdayIndex = 1;
                }
                else if (d == "Tuesday")
                {
                    _OffDays = LCS.GetListOfDays(to, from, DayOfWeek.Tuesday);
                    OffdayIndex = 2;
                }
                else if (d == "Wednesday")
                {
                    _OffDays = LCS.GetListOfDays(to, from, DayOfWeek.Wednesday);
                    OffdayIndex = 3;
                }
                else if (d == "Thursday")
                {
                    _OffDays = LCS.GetListOfDays(to, from, DayOfWeek.Thursday);
                    OffdayIndex = 4;
                }
                else if (d == "Friday")
                {
                    _OffDays = LCS.GetListOfDays(to, from, DayOfWeek.Friday);
                    OffdayIndex = 5;
                }
                else if (d == "Saturday")
                {
                    _OffDays = LCS.GetListOfDays(to, from, DayOfWeek.Saturday);
                    OffdayIndex = 6;
                }
                else
                {
                    _OffDays = null;
                    OffdayIndex = 0;
                } */
                #endregion

                /* If employee has any shift */
                var empShiftDetail = _shiftDetails.Select(string.Format("emp_no='{0}'", recordsEmpWise.Key));
                var tempmonth=0;
                //if (getEmpJoiningDate(recordsEmpWise.Key).Month==5)
                //{
                //     tempmonth = _month ;
                //}
                //else
                //{
                     tempmonth = _month - 1;
                //}
                
                var tempyear = Year;
                if (tempmonth == 0)
                {
                    tempmonth = 12;
                    tempyear = _year - 1;
                }
                var tempdate = new DateTime(tempyear, tempmonth, 26);

                if (empShiftDetail != null)
                {
                    if (empShiftDetail.Count() > 0)
                    {

                        /* Getting Shift Details */
                        // Employee_Shift_Detail sD = new Employee_Shift_Detail(empShiftDetail[0]);

                        var newDataRow = LogsFlags_Dt.NewRow();
                        newDataRow["emp_no"] = recordsEmpWise.Key;
                        newDataRow["Name"] = recordsEmpWise.FirstOrDefault()["Name"].ToString();
                        newDataRow["Department"] = recordsEmpWise.FirstOrDefault()["Depart"].ToString();




                        if (recordsEmpWise.FirstOrDefault()["CHECKTIME"] == null || recordsEmpWise.FirstOrDefault()["CHECKTIME"] == DBNull.Value)
                        {
                            GetAdujustmentStatusMonthWise(recordsEmpWise.CopyToDataTable());
                            goto Exit;
                        }

                    #region Review by Muhammad Uzair
                        int countloopstart = 0;
                        int condtion = 0;
                        for (int i = 0; i < empShiftDetail.Count(); i++)
                        {


                            /* Getting Shift Details */
                            Employee_Shift_Detail sD = new Employee_Shift_Detail(empShiftDetail[i]);
                            if (sD._ToDate == null)
                            {
                                if (countloopstart == 0)
                                {
                                    countloopstart = 0;
                                    condtion = _DaysInMonth.Count();
                                }
                                else
                                {
                                    if (sD._fromDate.Value <= to)
                                    {
                                        condtion = _DaysInMonth.Count();
                                    }
                                }
                            }
                            else if (sD._ToDate.Value >= from && sD._fromDate.Value <= to)
                            {
                                days = LCS.GetListOfDays(sD._ToDate.Value, from);
                                condtion = days.Count();
                            }
                            else { goto end; }


                            for (int day = _PartialAttendance ? _dayStart : countloopstart; day < (_PartialAttendance ? _dayEnd : condtion); day++)
                            {
                                string inStatus = "";
                                string outStatus = "";

                                tempdate = new DateTime(tempyear, tempmonth, ((int[])_DaysInMonth)[day]);


                                DateTime toDay = new DateTime(tempyear, tempmonth, tempdate.Day);
                                DateTime previousDay = toDay - (new TimeSpan(1, 0, 0, 0, 0));
                                TimeSpan noon = new TimeSpan(12, 0, 0);

                                var currentDayThumbs = recordsEmpWise
                                .Where(a => a.Field<int>("Year") == tempyear
                                    && a.Field<int>("Month") == tempmonth
                                    && a.Field<int>("Day") == tempdate.Day);
                                if (currentDayThumbs != null)
                                {
                                    if (currentDayThumbs.IsAny())
                                    {
                                        __daysWorked++;
                                        _workingDays.Add(tempdate.Day);
                                    }

                                    else
                                    {
                                        GetAdujustmentStatusMonthWise(recordsEmpWise.CopyToDataTable(), tempyear, tempmonth, tempdate.Day);
                                        //__absents++;
                                        //newDataRow[day + "_" + "3"] = ShiftFlags.Absent;
                                        //newDataRow[day + "_" + "4"] = ShiftFlags.Absent;
                                        var tmpd = new DateTime(_year, tempmonth, DateTime.DaysInMonth(_year, tempmonth));
                                        if (((int[])_DaysInMonth)[day] >= tmpd.Day)
                                        {
                                            tempmonth = _month;
                                            tempyear = _year;
                                        }
                                        continue;
                                    }
                                }
                                else
                                {
                                    GetAdujustmentStatusMonthWise(recordsEmpWise.CopyToDataTable());
                                    //__absents++;
                                    //newDataRow[day + "_" + "3"] = ShiftFlags.Absent;
                                    //newDataRow[day + "_" + "4"] = ShiftFlags.Absent;
                                    var tmpd = new DateTime(_year, tempmonth, DateTime.DaysInMonth(_year, tempmonth));
                                    if (((int[])_DaysInMonth)[day] >= tmpd.Day)
                                    {
                                        tempmonth = _month;
                                        tempyear = _year;
                                    }

                                    continue;

                                }
                                DateTime? _inTime = null;
                                DateTime? _outTime = null;
                                var inThumb = currentDayThumbs.Where(a => a.Field<string>("CHECKTYPE") == "I").FirstOrDefault();
                                var outThumbs = currentDayThumbs.Where(a => a.Field<string>("CHECKTYPE") == "O").FirstOrDefault();

                                #region ASIF REVIEW
                                #region Inthumb
                                if (inThumb != null)
                                {
                                    _inTime = inThumb.Field<DateTime>("CHECKTIME");
                                    newDataRow[tempdate.Day + "_" + "1"] = _inTime.Value.TimeOfDay.ToString(@"hh\:mm");
                                    if (!sD.NightShift)
                                    {
                                        if ((sD.Start_Time + sD.Grace_Time_IN) < _inTime.Value.TimeOfDay)
                                        {
                                            //if ((sD.End_IN) < _inTime.Value.TimeOfDay)
                                            //{
                                            //    __halfDay++;
                                            //    newDataRow[day + "_" + "3"] = ShiftFlags.HalfDay;
                                            //    inStatus = "HD";
                                            //}
                                            //else
                                            //{
                                            __late++;
                                            newDataRow[tempdate.Day + "_" + "3"] = ShiftFlags.Late;
                                            inStatus = "L";
                                            // }


                                        }
                                        else
                                        {
                                            //  flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                            newDataRow[tempdate.Day + "_" + "3"] = ShiftFlags.Normal;
                                            inStatus = "N";
                                        }
                                      
                                    }
                                    else
                                    {
                                        if (sD.End_IN.Value == TimeSpan.Zero || (sD.End_IN.Value >= TimeSpan.Zero && sD.End_IN < noon))
                                        {
                                            if ((previousDay) + (sD.Start_Time.Value + (sD.Start_Time == TimeSpan.Zero ? TimeSpan.FromHours(24) : TimeSpan.Zero) + sD.Grace_Time_IN.Value) < _inTime.Value)
                                            {
                                                __late++;
                                                //  flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Late));
                                                newDataRow[tempdate.Day + "_" + "3"] = ShiftFlags.Late;
                                                inStatus = "L";

                                            }
                                            else
                                            {
                                                //  flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                                newDataRow[tempdate.Day + "_" + "3"] = ShiftFlags.Normal;
                                                inStatus = "N";
                                            }

                                        }
                                        else
                                        {
                                            if ((previousDay) + (sD.Start_Time.Value + sD.Grace_Time_IN.Value) < _inTime.Value)
                                            {
                                                __late++;
                                                //dd(Tuple.Create(toDay,(int)ThumbFlag.Late));
                                                newDataRow[tempdate.Day + "_" + "3"] = ShiftFlags.Late;
                                                inStatus = "L";

                                            }
                                            else
                                            {
                                                //flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                                newDataRow[tempdate.Day + "_" + "3"] = ShiftFlags.Normal;
                                                inStatus = "N";
                                            }
                                        }
                                       
                                    }
                                    
                                  }

                                else
                                {
                                    __notIn++;
                                    newDataRow[tempdate.Day + "_" + "1"] = string.Empty;
                                    newDataRow[tempdate.Day + "_" + "3"] = ShiftFlags.NotIn;
                                    outStatus = "NI";
                                }
                                #endregion

                                #region OutThumb
                                if (outThumbs != null)
                                {
                                    _outTime = outThumbs.Field<DateTime>("CHECKTIME");
                                    newDataRow[tempdate.Day + "_" + "2"] = _outTime.Value.TimeOfDay.ToString(@"hh\:mm");
                                    if (!sD.NightShift)
                                    {
                                        TimeSpan duration = DateTime.Parse(sD.End_Time.Value.ToString()).Subtract(DateTime.Parse(_outTime.Value.ToShortTimeString()));
                                        if ((toDay + (sD.End_Time - sD.Grace_Time_OUT)) >= _outTime.Value)
                                        {
                                            __early++;
                                            newDataRow[tempdate.Day + "_" + "4"] = ShiftFlags.Early;
                                            outStatus = "E";
                                        }

                                        //else if (duration.Hours > 1)
                                        //{
                                        //    __halfDay++;
                                        //    newDataRow[day + "_" + "4"] = ShiftFlags.HalfDay;
                                        //    inStatus = "HD";
                                        //}
                                        else
                                        {
                                            newDataRow[tempdate.Day + "_" + "4"] = ShiftFlags.Normal;
                                            outStatus = "N";
                                        }
                                    }
                                    else
                                    {
                                        if (sD.Begin_OUT.Value == TimeSpan.Zero || (sD.Begin_OUT.Value > TimeSpan.Zero && sD.Begin_OUT.Value < noon))
                                        {
                                            if ((toDay + (sD.End_Time.Value - sD.Grace_Time_OUT.Value)) >= _outTime.Value)
                                            {
                                                __early++;
                                                // flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Early));
                                                newDataRow[tempdate.Day + "_" + "4"] = ShiftFlags.Early;
                                                outStatus = "E";
                                            }
                                            else
                                            {

                                                // flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                                newDataRow[tempdate.Day + "_" + "4"] = ShiftFlags.Normal;
                                                outStatus = "N";
                                            }
                                        }
                                        else
                                        {
                                            if ((previousDay + (sD.End_Time.Value - sD.Grace_Time_OUT.Value)) >= _outTime.Value)
                                            {
                                                __early++;
                                                // flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Early));
                                                newDataRow[tempdate.Day + "_" + "4"] = ShiftFlags.Early;
                                                outStatus = "E";
                                            }
                                            else
                                            {
                                                //flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                                newDataRow[tempdate.Day + "_" + "4"] = ShiftFlags.Normal;
                                                outStatus = "N";
                                            }
                                        }
                                    }
                                    
                                }
                                
                                else

                                {
                                    __notOut++;
                                    newDataRow[tempdate.Day + "_" + "2"] = string.Empty;
                                    newDataRow[tempdate.Day + "_" + "4"] = ShiftFlags.NotOut;
                                    outStatus = "NO";
                                }

                                if (inThumb != null && outThumbs != null)
                                {
                                    __avgDays++;
                                    decimal avghrs = 0;
                                    double extrahrs = 0;
                                    
                                    TimeSpan duration = DateTime.Parse(_outTime.Value.ToString()).Subtract(DateTime.Parse(_inTime.Value.ToString()));
                                    Decimal.TryParse(duration.TotalHours.ToString(), out avghrs);

                                    if (tempdate.DayOfWeek == DayOfWeek.Sunday || _Holidays.Contains(tempdate.Day))
                                    {
                                        extraDay++;
                                    }
                                    else
                                    { 
                                        if ((double)avghrs > sD.TotalHours)
                                        {
                                            extrahrs = (double)avghrs - sD.TotalHours;
                                       
                                            
                                            if (extrahrs >= 1)
                                            {
                                                TotalExtrahrs += extrahrs;
                                                AcualExtrahrs += extrahrs > 4 ? 4 : extrahrs;
                                            }
                                        }
                                    }
                                    __avgHours += avghrs;

                                    
                                }
                                else if (inThumb !=null || outThumbs !=null)
                                {
                                    decimal avghrs = 0;
                                    double extrahrs = 0;


                                    _inTime = inThumb != null ? _inTime : Convert.ToDateTime(tempdate + sD.Start_Time);
                                    _outTime = outThumbs != null ? _outTime : Convert.ToDateTime(tempdate + sD.End_Time);
                                    TimeSpan duration = DateTime.Parse(_outTime.Value.ToString()).Subtract(DateTime.Parse(_inTime.Value.ToString()));
                                    Decimal.TryParse(duration.TotalHours.ToString(), out avghrs);

                                    if (tempdate.DayOfWeek == DayOfWeek.Sunday || _Holidays.Contains(tempdate.Day))
                                    {
                                        extraDay++;
                                    }
                                    else
                                    {
                                        if ((double)avghrs > sD.TotalHours)
                                        {
                                            extrahrs = (double)avghrs - sD.TotalHours;
                                            
                                            if (extrahrs >= 1)
                                            {
                                                TotalExtrahrs += extrahrs;
                                                AcualExtrahrs += extrahrs > 4 ? 4 : extrahrs;
                                            }
                                         }
                                    }


                                }

                               




                                #endregion

                                #endregion

                                ListOfAdjustmentStatus.Add(tempdate.Day, string.Concat(inStatus, '-', outStatus));
                                // continue;
                                var aa = new DateTime(_year, tempmonth, DateTime.DaysInMonth(_year, tempmonth));

                                if (((int[])_DaysInMonth)[day] >= aa.Day)
                                {
                                    tempmonth = _month;
                                    tempyear = _year;
                                }

                            }

                        end:
                            if (days == null)
                            {
                                countloopstart = 0;
                            }
                            else
                            {
                                countloopstart = days.Count();
                            }


                        }
                    #endregion

                    #region prevois code for shift adjustment in comment
                    /*       for (int day = _PartialAttendance ? _dayStart : 0; day <= (_PartialAttendance ? _dayEnd : _DaysInMonth.Count()); day++)
                           {
                               string inStatus = "";
                               string outStatus = "";

                               tempdate = new DateTime(tempyear, tempmonth, DateTime.DaysInMonth(tempyear, ((int[])_DaysInMonth)[day]));


                               DateTime toDay = new DateTime(tempyear, tempmonth, tempdate.Day);
                               DateTime previousDay = toDay - (new TimeSpan(1, 0, 0, 0, 0));
                               TimeSpan noon = new TimeSpan(12, 0, 0);

                               var currentDayThumbs = recordsEmpWise
                               .Where(a => a.Field<int>("Year") == tempyear
                                   && a.Field<int>("Month") == tempmonth
                                   && a.Field<int>("Day") == tempdate.Day);
                               if (currentDayThumbs != null)
                               {
                                   if (currentDayThumbs.IsAny())
                                   {
                                       __daysWorked++;
                                       _workingDays.Add(tempdate.Day);
                                   }

                                   else
                                   {
                                       GetAdujustmentStatusMonthWise(recordsEmpWise.CopyToDataTable(), tempdate.Day);
                                       //__absents++;
                                       //newDataRow[day + "_" + "3"] = ShiftFlags.Absent;
                                       //newDataRow[day + "_" + "4"] = ShiftFlags.Absent;
                                       continue;
                                   }
                               }
                               else
                               {
                                   GetAdujustmentStatusMonthWise(recordsEmpWise.CopyToDataTable());
                                   //__absents++;
                                   //newDataRow[day + "_" + "3"] = ShiftFlags.Absent;
                                   //newDataRow[day + "_" + "4"] = ShiftFlags.Absent;
                                   continue;

                               }
                               DateTime? _inTime = null;
                               DateTime? _outTime = null;
                               var inThumb = currentDayThumbs.Where(a => a.Field<string>("CHECKTYPE") == "I").FirstOrDefault();
                               var outThumbs = currentDayThumbs.Where(a => a.Field<string>("CHECKTYPE") == "O").FirstOrDefault();

                               #region ASIF REVIEW
                               #region Inthumb
                               if (inThumb != null)
                               {
                                   _inTime = inThumb.Field<DateTime>("CHECKTIME");
                                   newDataRow[tempdate + "_" + "1"] = _inTime.Value.TimeOfDay.ToString(@"hh\:mm");
                                   if (!sD.NightShift)
                                   {
                                       if ((sD.Start_Time + sD.Grace_Time_IN) < _inTime.Value.TimeOfDay)
                                       {
                                           //if ((sD.End_IN) < _inTime.Value.TimeOfDay)
                                           //{
                                           //    __halfDay++;
                                           //    newDataRow[day + "_" + "3"] = ShiftFlags.HalfDay;
                                           //    inStatus = "HD";
                                           //}
                                           //else
                                           //{
                                           __late++;
                                           newDataRow[tempdate + "_" + "3"] = ShiftFlags.Late;
                                           inStatus = "L";
                                           // }


                                       }
                                       else
                                       {
                                           //  flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                           newDataRow[tempdate + "_" + "3"] = ShiftFlags.Normal;
                                           inStatus = "N";
                                       }
                                   }
                                   else
                                   {
                                       if (sD.End_IN.Value == TimeSpan.Zero || (sD.End_IN.Value >= TimeSpan.Zero && sD.End_IN < noon))
                                       {
                                           if ((previousDay) + (sD.Start_Time.Value + (sD.Start_Time == TimeSpan.Zero ? TimeSpan.FromHours(24) : TimeSpan.Zero) + sD.Grace_Time_IN.Value) < _inTime.Value)
                                           {
                                               __late++;
                                               //  flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Late));
                                               newDataRow[tempdate + "_" + "3"] = ShiftFlags.Late;
                                               inStatus = "L";

                                           }
                                           else
                                           {
                                               //  flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                               newDataRow[tempdate + "_" + "3"] = ShiftFlags.Normal;
                                               inStatus = "N";
                                           }

                                       }
                                       else
                                       {
                                           if ((previousDay) + (sD.Start_Time.Value + sD.Grace_Time_IN.Value) < _inTime.Value)
                                           {
                                               __late++;
                                               //dd(Tuple.Create(toDay,(int)ThumbFlag.Late));
                                               newDataRow[tempdate + "_" + "3"] = ShiftFlags.Late;
                                               inStatus = "L";

                                           }
                                           else
                                           {
                                               //flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                               newDataRow[tempdate + "_" + "3"] = ShiftFlags.Normal;
                                               inStatus = "N";
                                           }
                                       }

                                   }
                               }

                               else
                               {
                                   __notIn++;
                                   newDataRow[tempdate + "_" + "1"] = string.Empty;
                                   newDataRow[tempdate + "_" + "3"] = ShiftFlags.NotIn;
                                   outStatus = "NI";
                               }
                               #endregion

                               #region OutThumb
                               if (outThumbs != null)
                               {
                                   _outTime = outThumbs.Field<DateTime>("CHECKTIME");
                                   newDataRow[tempdate + "_" + "2"] = _outTime.Value.TimeOfDay.ToString(@"hh\:mm");
                                   if (!sD.NightShift)
                                   {
                                       TimeSpan duration = DateTime.Parse(sD.End_Time.Value.ToString()).Subtract(DateTime.Parse(_outTime.Value.ToShortTimeString()));
                                       if ((toDay + (sD.End_Time - sD.Grace_Time_OUT)) >= _outTime.Value)
                                       {
                                           __early++;
                                           newDataRow[tempdate + "_" + "4"] = ShiftFlags.Early;
                                           outStatus = "E";
                                       }

                                       //else if (duration.Hours > 1)
                                       //{
                                       //    __halfDay++;
                                       //    newDataRow[day + "_" + "4"] = ShiftFlags.HalfDay;
                                       //    inStatus = "HD";
                                       //}
                                       else
                                       {

                                           newDataRow[tempdate + "_" + "4"] = ShiftFlags.Normal;
                                           outStatus = "N";
                                       }
                                   }
                                   else
                                   {
                                       if (sD.Begin_OUT.Value == TimeSpan.Zero || (sD.Begin_OUT.Value > TimeSpan.Zero && sD.Begin_OUT.Value < noon))
                                       {
                                           if ((toDay + (sD.End_Time.Value - sD.Grace_Time_OUT.Value)) >= _outTime.Value)
                                           {
                                               __early++;
                                               // flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Early));
                                               newDataRow[tempdate + "_" + "4"] = ShiftFlags.Early;
                                               outStatus = "E";
                                           }
                                           else
                                           {

                                               // flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                               newDataRow[tempdate + "_" + "4"] = ShiftFlags.Normal;
                                               outStatus = "N";
                                           }
                                       }
                                       else
                                       {
                                           if ((previousDay + (sD.End_Time.Value - sD.Grace_Time_OUT.Value)) >= _outTime.Value)
                                           {
                                               __early++;
                                               // flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Early));
                                               newDataRow[tempdate + "_" + "4"] = ShiftFlags.Early;
                                               outStatus = "E";
                                           }
                                           else
                                           {

                                               //flagsList.Add(Tuple.Create(toDay,(int)ThumbFlag.Normal));
                                               newDataRow[tempdate + "_" + "4"] = ShiftFlags.Normal;
                                               outStatus = "N";
                                           }

                                       }
                                   }
                               }
                               else
                               {
                                   __notOut++;
                                   newDataRow[tempdate + "_" + "2"] = string.Empty;
                                   newDataRow[tempdate + "_" + "4"] = ShiftFlags.NotOut;
                                   outStatus = "NO";
                               }

                               if (inThumb != null && outThumbs != null)
                               {
                                   __avgDays++;
                                   decimal avghrs = 0;
                                   TimeSpan duration = DateTime.Parse(_outTime.Value.ToString()).Subtract(DateTime.Parse(_inTime.Value.ToString()));
                                   Decimal.TryParse(duration.TotalHours.ToString(), out avghrs);
                                   __avgHours += avghrs;
                               }
                               #endregion

                               #endregion

                               ListOfAdjustmentStatus.Add(tempdate.Day, string.Concat(inStatus, '-', outStatus));
                               // continue;
                               tempdate = new DateTime(tempyear, tempmonth, DateTime.DaysInMonth(tempyear, ((int[])_DaysInMonth)[day]));

                               if (((int[])_DaysInMonth)[day] >= tempdate.Day)
                               {
                                   tempmonth = _month;
                                   tempyear = _year;
                               }
                           } */
                    #endregion


                    Exit:

                        ProcessAdjustments(recordsEmpWise.Key);
                        ProcessAbsetnts(newDataRow, _workingDays);
                        ProccessAbsentSandwitch(recordsEmpWise.Key, empShiftDetail[0]);
                        if (ProcessConsecutiveLates)
                        {
                            __consecutiveLates = ProcessConsecutiveLatesFlags(newDataRow, "L", 3);
                        }
                        /* Total No of Sundays in current month */
                        //if (empJoinDate.Month == _month)
                        //{
                        //    totalSundays = LCS.GetNoOfDays(Year, Month, DayOfWeek.Sunday,empJoinDate.Day);
                        //}
                        //else
                        //{
                        //   totalSundays = LCS.GetNoOfDays(to, from, DayOfWeek.Sunday);
                        //}

                        var tempFromDate = from;
                        if (getEmpJoiningDate(recordsEmpWise.Key).Month == _month && getEmpJoiningDate(recordsEmpWise.Key).Year==_year)
                        {
                            tempFromDate = getEmpJoiningDate(recordsEmpWise.Key);
                        }
                        else
                        {
                            tempFromDate = from;
                        }

                        

                        var tempTotalSunday = LCS.GetListOfDays(to, tempFromDate, DayOfWeek.Sunday);

                        int totalSundays = tempTotalSunday.Count();
                      
                        var newDataRow_att = _hr_employeeattendanceprocess.NewRow();
                        int offdayCount = 0;
                        if (_OffDays != null)
                        {
                            offdayCount = _OffDays.Count();
                        }

                        var _loc_Absents = ProcessTotalAbsentCounts(_DaysInMonth.Count(), __daysWorked, totalMonthHolidays, totalSundays, totalMonthLeaves, offdayCount);
                        var _loc_ruleAbsents = ProcessRuleAbsents();

                        if (getEmpJoiningDate(recordsEmpWise.Key).Month == _month && getEmpJoiningDate(recordsEmpWise.Key).Year == _year)
                        {
                            _loc_ruleAbsents = 0;
                        }
                       



                        newDataRow_att["Year"] = Year;
                        newDataRow_att["Month"] = Month;
                        newDataRow_att["emp_no"] = recordsEmpWise.Key;
                        newDataRow_att["city"] = __CityID == string.Empty ? __ModeID : __CityID;
                        //newDataRow["Absents"] = DateTime.DaysInMonth(year, month) - daysWorked - totalMonthHolidays - totalSundays;
                        int absentFromPolicy = ((__late - (__late % 3)) / 3); //+ ((__halfDay - (__halfDay % 2)) / 2) ;

                        newDataRow_att["Absents"] = _loc_Absents; //+ absentFromPolicy;
                        newDataRow_att["Sundays"] = totalSundays;
                        //if (totalMonthHolidays>0)
                        //{
                        //    DateTime ghto = Convert.ToDateTime(HolidaysFound.Rows[0]["ToDate"]);

                        //    newDataRow_att["Holidays"] = getEmpJoiningDate(recordsEmpWise.Key) > ghto ?0 : totalMonthHolidays;
                        //}
                        //else
                        //{

                        //}
                        newDataRow_att["Holidays"] = totalMonthHolidays;
                        newDataRow_att["Leaves"] = totalMonthLeaves;
                        newDataRow_att["Late"] = __late - __adjustmentLate;
                        newDataRow_att["HalfDay"] = __halfDay;
                        newDataRow_att["AvgHrs"] = __avgHours > 0 && __avgDays > 0 ? __avgHours / __avgDays : 0;
                        newDataRow_att["ruleAbsents"] = _loc_ruleAbsents;
                        newDataRow_att["Notout"] = __notOut;
                        newDataRow_att["Early"] = __early;
                        newDataRow_att["adjustmentLate"] = __adjustmentLate;
                        newDataRow_att["adjustmentAbsent"] = __adjustmentAbsent;//> totalMonthLeaves ? __adjustmentAbsent - totalMonthLeaves : __adjustmentAbsent;
                        newDataRow_att["adjustmentRAbsent"] = __adjustmentRAbsent;
                        newDataRow_att["Total_Ex_Hrs"] = TotalExtrahrs;
                        newDataRow_att["Ext_Hrs"] = AcualExtrahrs;
                        newDataRow_att["Ext_Days"] = extraDay;
                        newDataRow_att["Missing_TimeIN"] = __notIn;
                        newDataRow_att["Consective_Late"] = __consecutiveLates;
                        newDataRow_att["CreatedBy"] = StateHelper.userid;
                        newDataRow_att["Created_Date"] = DateTime.Now;

                        newDataRow["emp_no"] = recordsEmpWise.Key;
                        newDataRow["Absents"] = _loc_Absents; //+ absentFromPolicy;
                        newDataRow["RuleAbsent"] = _loc_ruleAbsents;
                        newDataRow["Sundays"] = totalSundays;
                        newDataRow["Holidays"] = totalMonthHolidays;
                        newDataRow["Leaves"] = totalMonthLeaves;

                        newDataRow["Late"] = __late - __adjustmentLate;
                        newDataRow["HalfDay"] = __halfDay;
                        newDataRow["ConsecutiveLate"] = __consecutiveLates;
                        newDataRow["NotOut"] = __notOut;
                        newDataRow["Early"] = __early;
                        /* Not calculating adjustment in application right now. (May be in future !!!)
                          We're reusing ajustmentLate as RuleAbsent Adjustment in Report
                         */
                        newDataRow["AdjustLate"] = __adjustmentLate + __adjustmentRAbsent;
                        newDataRow["AdujstAbsent"] = __adjustmentAbsent - totalMonthLeaves;
                        newDataRow["daysWorked"] = __daysWorked;
                        newDataRow["StartIn"] = DBNull.Value; //sD.Start_Time.Value.ToString(@"hh\:mm");
                        newDataRow["EndIn"] = DBNull.Value; //sD.End_Time.Value.ToString(@"hh\:mm");

                        _hr_employeeattendanceprocess.Rows.Add(newDataRow_att);
                        LogsFlags_Dt.Rows.Add(newDataRow);
                        _workingDays.Clear();
                        _ruleAbsents.Clear();


                        #region geting count of holidays

                        _HolidaysFound = GetTotalHolidays();

                        if (_HolidaysFound != null)
                        {
                            _Holidays = new List<int>();

                            var currentMonthWorkingDays = GetInclusiveRange(_dayStart, _dayEnd);
                            foreach (DataRow row in _HolidaysFound.Rows)
                            {
                                if (_PartialAttendance)
                                {
                                    var __holidays = LCS.GetListOfDaysExcludingWeekDay(row.Field<DateTime>("ToDate"),
                                                row.Field<DateTime>("FromDate"), DayOfWeek.Sunday);

                                    foreach (int __day in currentMonthWorkingDays)
                                    {
                                        foreach (var holiday in __holidays)
                                        {
                                            if (__day == holiday)
                                            {
                                                _Holidays.Add(holiday);
                                            }
                                        }
                                    }


                                }
                                else
                                {
                                    _Holidays.AddRange(LCS.GetListOfDaysExcludingWeekDay(row.Field<DateTime>("ToDate"),
                                                row.Field<DateTime>("FromDate"), DayOfWeek.Sunday));
                                }

                            }
                            TempHoliday = _Holidays.Distinct().Count();
                        }
                        #endregion

                        var temp = LCS.GetListOfDays(to, from , DayOfWeek.Sunday);
                        ReportPerameterSubday = temp.Count();



                    }
                }
            }
        }
        return _hr_employeeattendanceprocess;
    }


    private void ProcessAbsetnts(DataRow newDataRow, List<int> daysWorkedList)
    {

        int[] daysExcludingSundays;
        int[] ListOFDaysOff;
        DateTime tempdate;
        daysExcludingSundays = LCS.GetListOfDays(to, from, DayOfWeek.Sunday);

        if (OffdayIndex == 1)
        { ListOFDaysOff = LCS.GetListOfDays(to, from, DayOfWeek.Monday); }
        else if (OffdayIndex == 2)
        { ListOFDaysOff = LCS.GetListOfDays(to, from, DayOfWeek.Tuesday); }
        else if (OffdayIndex == 3)
        { ListOFDaysOff = LCS.GetListOfDays(to, from, DayOfWeek.Wednesday); }
        else if (OffdayIndex == 4)
        { ListOFDaysOff = LCS.GetListOfDays(to, from, DayOfWeek.Thursday); }
        else if (OffdayIndex == 5)
        { ListOFDaysOff = LCS.GetListOfDays(to, from, DayOfWeek.Friday); }
        else if (OffdayIndex == 6)
        { ListOFDaysOff = LCS.GetListOfDays(to, from, DayOfWeek.Saturday); }
        else
        {
            ListOFDaysOff = LCS.GetListOfDays(to, from, DayOfWeek.Sunday);
        }

        absentDays = _DaysInMonth.Except(daysWorkedList).Except(Holidays).Except(daysExcludingSundays).Except(Leaves).Except(ListOFDaysOff);


        if (absentDays != null)
        {
            if (absentDays.Count() > 0)
            {

                for (int i = 0; i < absentDays.Count(); i++)
                {

                    if (absentDays.ToList()[i] == 26 || absentDays.ToList()[i] == 27 || absentDays.ToList()[i] == 28 || absentDays.ToList()[i] == 29 || absentDays.ToList()[i] == 30 || absentDays.ToList()[i] == 31)
                    {
                        if (Month - 1 == 0)
                        {
                            tempdate = new DateTime(Year - 1, 12, absentDays.ToList()[i]);
                        }
                        else
                        {
                            tempdate = new DateTime(Year, Month - 1, absentDays.ToList()[i]);
                        }

                        newDataRow[tempdate.Day + "_3"] = "A";
                        newDataRow[tempdate.Day + "_4"] = "A";
                    }
                    else
                    {
                        tempdate = new DateTime(Year, Month, absentDays.ToList()[i]);
                        newDataRow[tempdate.Day + "_3"] = "A";
                        newDataRow[tempdate.Day + "_4"] = "A";
                    }
                }

            }
            if (Leaves.Count > 0)
            {
                for (int i = 0; i < Leaves.Count; i++)
                {
                    if (Leaves[i] == 26 || Leaves[i] == 27 || Leaves[i] == 28 || Leaves[i] == 29 || Leaves[i] == 30 || Leaves[i] == 31)
                    {
                        if (Month - 1 == 0)
                        {
                            tempdate = new DateTime(Year - 1, 12, Leaves[i]);
                        }
                        else
                        {
                            tempdate = new DateTime(Year, Month - 1, Leaves[i]);
                        }
                        newDataRow[tempdate.Day + "_3"] = "LE";
                        newDataRow[tempdate.Day + "_4"] = "LE";
                    }
                    else
                    {
                        tempdate = new DateTime(Year, Month, Leaves[i]);
                        newDataRow[tempdate.Day + "_3"] = "LE";
                        newDataRow[tempdate.Day + "_4"] = "LE";
                    }

                }

            }
            if (Holidays.Count > 0)
            {
                for (int i = 0; i < Holidays.Count; i++)
                {
                    if (Holidays[i] == 26 || Holidays[i] == 27 || Holidays[i] == 28 || Holidays[i] == 29 || Holidays[i] == 30 || Holidays[i] == 31)
                    {
                        if (Month - 1 == 0)
                        {
                            tempdate = new DateTime(Year - 1, 12, Holidays[i]);
                        }
                        else
                        {
                            tempdate = new DateTime(Year, Month - 1, Holidays[i]);
                        }
                        newDataRow[tempdate.Day + "_3"] = "H";
                        newDataRow[tempdate.Day + "_4"] = "H";
                    }
                    else
                    {

                        tempdate = new DateTime(Year, Month, Holidays[i]);
                        newDataRow[tempdate.Day + "_3"] = "H";
                        newDataRow[tempdate.Day + "_4"] = "H";
                    }


                }

            }
        }

    }

    /// <summary>
    /// Procedure for resetting values globally
    /// </summary>
    void ResetGlobalVariables()
    {
        totalMonthHolidays = 0;
          __daysWorked = 0;
        __late = 0;
        __halfDay = 0;
        __notOut = 0;
        __early = 0;
        __ruleAbsents = 0;
        __absents = 0;
        __notIn = 0;
        __normal = 0;
        __avgHours = 0;
        __avgDays = 0;
        __consecutiveLates = 0;
        __adjustmentLate = 0;
        __adjustmentAbsent = 0;
        __adjustmentRAbsent = 0;
        _workingDays.Clear();
        _ruleAbsents.Clear();
        totalMonthLeaves=0;
        extraDay = 0;
        AcualExtrahrs = 0;
        TotalExtrahrs = 0;
        ListOfAdjustmentStatus.Clear();



    }

    /// <summary>
    /// Calculation for the selected employee passed
    /// </summary>
    /// <param name="EmpNo">Employee No (varchar 14)</param>
    private void ProcessAdjustments(string EmpNo)
    {
        if (_AttendanceAdjustments != null)
        {
            if (_AttendanceAdjustments.Rows.Count > 0)
            {
                var enumData = _AttendanceAdjustments.AsEnumerable();
                var enumDataByEmpNo = enumData.Where(a => a.Field<string>("emp_no").Equals(EmpNo, StringComparison.InvariantCultureIgnoreCase));
                if (enumDataByEmpNo != null)
                {
                    if (enumDataByEmpNo.IsAny())
                    {
                        var filterdByType = enumDataByEmpNo
                            .Where(a => a.Field<string>("adjustmentType").Equals("A", StringComparison.InvariantCultureIgnoreCase) || a.Field<string>("adjustmentType").Equals("L", StringComparison.InvariantCultureIgnoreCase) || a.Field<string>("adjustmentType").Equals("RA", StringComparison.InvariantCultureIgnoreCase))
                            .GroupBy(a => a.Field<string>("adjustmentType"));
                        if (filterdByType != null)
                        {
                            if (filterdByType.IsAny())
                            {
                                foreach (var item in filterdByType)
                                {
                                    if (item.Key.Equals("A", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        __adjustmentAbsent = item.Count();
                                        //adjustmentAbsent = item.Count();
                                    }
                                    else if (item.Key.Equals("L", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        __adjustmentLate = item.Count();
                                        //adjustmentLate = item.Count();
                                    }
                                    else
                                    {
                                        __adjustmentRAbsent = item.Count();
                                        //adjustmentRAbsent = item.Count();
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Procedure for calculating Absent Sandwiches
    /// </summary>
    /// <param name="EmpNo">Employee No (VarChar 14)</param>
    /// <param name="empShiftDetail">DataRow of Shift Detail</param>
    private void ProccessAbsentSandwitch(string EmpNo, DataRow empShiftDetail)
    {

        /* 
        * 1) Calculate Absents for Sundays
        * 2)Calculate Absents for Gazetted Holidays      
        * 
        */

        //DataTable employeeShift = empShiftDetail.Table.Clone();
        //employeeShift.ImportRow(empShiftDetail);

        //DataTable empAttlog = _AttendanceLogs
        //    .Select(string.Format("emp_no='{0}'",EmpNo))
        //    .CopyToDataTable();        

        List<int> currentEmpAdjustDays = new List<int>();
        var employeeAdjustments = _AttendanceAdjustments.AsEnumerable()
            .Where(a => a.Field<string>("emp_no") == EmpNo)
            .Select(a => a.Field<DateTime>("adjustmentDate"));

        if (employeeAdjustments != null)
        {
            if (employeeAdjustments.Count() > 0)
            {
                currentEmpAdjustDays = employeeAdjustments
                    .Select(a => a.Day)
                    .ToList();
            }
        }
        var tempmonth = Month - 1;
        int tempyear = _year;
        if (tempmonth == 0)
        {
            tempyear = _year - 1;
            tempmonth = 12;
        }
        for (int day = _PartialAttendance ? _dayStart : 0; day < (_PartialAttendance ? _dayEnd : _DaysInMonth.Count()); day++)
        {


            var _tempDate = new DateTime(tempyear, tempmonth, (((int[])_DaysInMonth))[day]);
            if (_tempDate.DayOfWeek == DayOfWeek.Sunday)
            {

                if (_Holidays.Contains(_tempDate.Day))
                {
                    if (!ListOfAdjustmentStatus.ContainsKey(_tempDate.Day))
                    {
                        ListOfAdjustmentStatus.Add(_tempDate.Day, "S-H");//sunday and holiday
                    }
                    else
                    {
                        ListOfAdjustmentStatus[_tempDate.Day] = string.Format("S-{0}", ListOfAdjustmentStatus[_tempDate.Day].Split('-')[1]);
                        //(day,"S-H")
                    }
                }
                else
                {
                    if (!ListOfAdjustmentStatus.ContainsKey(_tempDate.Day))
                    {
                        ListOfAdjustmentStatus.Add(_tempDate.Day, "S-S");//sunday     
                    }

                }



            }
            var d = new DateTime(_year, tempmonth, DateTime.DaysInMonth(_year, tempmonth));
            if ((((int[])_DaysInMonth))[day] >= d.Day)
            {
                tempmonth = Month;
                tempyear = _year;
            }
        }
        foreach (int holiday in _Holidays)
        {
            if (ListOfAdjustmentStatus.ContainsKey(holiday))
            {
                ListOfAdjustmentStatus[holiday] = string.Format("H-{0}", ListOfAdjustmentStatus[holiday].Split('-')[1]);
            }
        }

        List<int> AllAbsentDays = ListOfAdjustmentStatus.Where(a => a.Value.Split('-')[0] == "A")
            .Select(a => a.Key).ToList();
        AllAbsentDays.Sort();



        for (int counter = 0; counter < AllAbsentDays.Count - 1; counter++)
        {

            var firstAbsent = AllAbsentDays[counter];
            var nextAbsent = AllAbsentDays[counter + 1];
            var sandwitchDays = ListOfAdjustmentStatus.Where(a => a.Key > firstAbsent && a.Key < nextAbsent);

            if (sandwitchDays != null)
            {
                if (sandwitchDays.Count() > 0)
                {
                    // var filteredDaysFound = sandwitchDays.Where(a => a.Value.Split('-')[0] == "H" || a.Value.Split('-')[0] == "S");
                    foreach (var sandwitchDay in sandwitchDays)
                    {
                        if (sandwitchDay.Value.Split('-')[0] != "H" && sandwitchDay.Value.Split('-')[0] != "S")
                            goto LoopEnd;
                    }


                    if (currentEmpAdjustDays.Contains(firstAbsent) || currentEmpAdjustDays.Contains(nextAbsent))
                        continue;

                    __ruleAbsents += sandwitchDays.Count();

                    foreach (var sandwitchDay in sandwitchDays)
                    {

                        _ruleAbsents.Add(sandwitchDay.Key);

                    }
                   


                }
            }
        LoopEnd:
            var aa = 1;
        }


        // ----- Avoiding Sandwich Logic
        /*   var groupPresentDays = new HashSet<int>(currentEmpAdjustDays);
           var newArr = ListOfAdjustmentStatus.Where(x => !groupPresentDays.Contains(x.Key)).Select(a => a.Key).ToList();
           newArr = newArr.Where(x => x > currentEmpAdjustDays.Last()).ToList();
           newArr.Sort();

           bool Check = !newArr.Select((i, j) => i - j).Distinct().Skip(1).Any();

           if (Check) 
           {
              __ruleAbsents = 0;
               _ruleAbsents.Clear();
           }

           // ----- Avoiding Sandwich Logic
        */
    }

    private int ProcessRuleAbsents()
    {
        int totalRuleAbsent = 0;

        try
        {
            List<int> _totalRuleAbsents = new List<int>(_ruleAbsents);
            //_totalRuleAbsents.RemoveAll(a => _sundaysIntList.Contains(a));
            /* It can be sandwich :-) */
            // _totalRuleAbsents.RemoveAll(a => _HolidaysIntList.Contains(a));
            totalRuleAbsent = _totalRuleAbsents.Count;

        }
        catch (Exception)
        {


        }
        return totalRuleAbsent;
    }

    /// <summary>
    /// Calculation of all absents days of employee
    /// </summary>
    /// <param name="daysInCurrentMonth">Number of days in current month</param>
    /// <param name="__DaysWorked">Total days worked by employee</param>
    /// <param name="totalMonthHolidays">Total Months Holidays</param>
    /// <param name="totalSundays">Total Number of Sundays in current month</param>
    /// <returns>Total Absents of employee</returns>
    private int ProcessTotalAbsentCounts(int daysInCurrentMonth, int __DaysWorked, int totalMonthHolidays, int totalSundays, int totalLeaves, int offdays)
    {
        int count = 0;
        try
        {
            //int daysExtraWorkedSundays = _workingDays.Intersect(_sundays).Count();
            //int daysExtraWorkedOnHolidays = _HolidaysList.Intersect(_workingDays).Count();
            List<int> daysWorkClon = new List<int>(_workingDays);
            daysWorkClon.RemoveAll(a => Sundays.Contains(a));
            daysWorkClon.RemoveAll(a => Holidays.Contains(a));
            daysWorkClon.RemoveAll(a => Leaves.Contains(a));
            int totalExtraWorkingDays = daysWorkClon.Count;
            //int totalExtraDays = (_workingDays.Intersect(_sundays)).Intersect(_HolidaysList).Count();
            //count = daysInMonth - daysWorked - totalMonthHolidays - totalSundays ;
            count = daysInCurrentMonth - totalExtraWorkingDays - totalMonthHolidays - totalSundays - totalLeaves - offdays;
        }
        catch (Exception)
        {

            throw new ArgumentException("Error calculating Absents.");
        }

        return count;
    }

    /// <summary>
    /// Calculation for Current month's holidays and sundays
    /// </summary>
    private void SetTotalSundaysAndHolidays(string empNo="")
    {
        if (empNo == "")
        {
            _Sundays = new List<int>();
            if (_PartialAttendance)
            {

                //var __from = new DateTime(_year,_month,_dayStart);
                //var __to = new DateTime(_year,_month,_dayEnd);

                _Sundays.AddRange(LCS.GetListOfDays(to, from, DayOfWeek.Sunday));
            }
            else
            {
                //if (empJoinDate.Month == Month) {
                //    _Sundays.AddRange(LCS.GetListOfDays(from,to, DayOfWeek.Sunday)); }
                //else{
                _Sundays.AddRange(LCS.GetListOfDays(to, from, DayOfWeek.Sunday));

                //    }


            }
            _HolidaysFound = GetTotalHolidays();
        }
        else
        {
            var tempFromDate = from;
            if (getEmpJoiningDate(empNo).Month == _month && getEmpJoiningDate(empNo).Year == _year)
            {
                tempFromDate = getEmpJoiningDate(empNo);
            }
            else
            {
                tempFromDate = from;
            }
            _HolidaysFound = GetTotalHolidays(tempFromDate);
        }
        /* Holidays DataRows (FromDate , ToDate) for all cities */
       

      
        //if (!Convert.IsDBNull(holidaysFound))
        //{
        //    totalMonthHolidays = int.Parse(holidaysFound.ToString());
        //}

        if (_HolidaysFound != null)
        {
            _Holidays = new List<int>();

            var currentMonthWorkingDays = GetInclusiveRange(_dayStart, _dayEnd);
            foreach (DataRow row in _HolidaysFound.Rows)
            {
                if (_PartialAttendance)
                {
                    var __holidays = LCS.GetListOfDaysExcludingWeekDay(row.Field<DateTime>("ToDate"),
                                row.Field<DateTime>("FromDate"), DayOfWeek.Sunday);

                    foreach (int __day in currentMonthWorkingDays)
                    {
                        foreach (var holiday in __holidays)
                        {
                            if (__day == holiday)
                            {
                                _Holidays.Add(holiday);
                            }
                        }
                    }


                }
                else
                {
                    _Holidays.AddRange(LCS.GetListOfDaysExcludingWeekDay(row.Field<DateTime>("ToDate"),
                                row.Field<DateTime>("FromDate"), DayOfWeek.Sunday));
                }

            }
            totalMonthHolidays = _Holidays.Distinct().Count();
        }


    }
    DateTime[] Templeaves;
    int leaves_for_sunday = 0;
    private void ProcessTotalLeaves(string empNO)
    { /*Code by muhammad uzair  */
        int i = 0;
        leaves_for_sunday = 0;
        Templeaves = null;
        _Leaves = new List<int>();
        _LeavesFound = GetTotalLeaves(empNO);
        if (_LeavesFound != null)
        {
            foreach (DataRow row in _LeavesFound.Rows)
            {
                Templeaves  = LCS.GetListOfHoliDays(row.Field<DateTime>("LeaveToDate"),
                           row.Field<DateTime>("LeaveFromDate"));

                
                     if (Templeaves != null)
                {

                    int tempMonth = _month - 1;
                    int tempyear = _year;
                    if (tempMonth == 0)
                    {
                        tempMonth = 12;
                        tempyear = _year - 1;
                    }
                    for (int Tday = 0; Tday < _DaysInMonth.Count(); Tday++)
                    {
                        for (int day = 0; day < Templeaves.Count(); day++)
                        {
                            if (((new DateTime(tempyear, tempMonth, ((int[])_DaysInMonth)[Tday])) == Templeaves[day]))
                            {
                                var _tempDate = new DateTime(tempyear, tempMonth, ((int[])_DaysInMonth)[Tday]);

                                if (_tempDate.DayOfWeek != DayOfWeek.Sunday && !_Holidays.Contains(_tempDate.Day))
                                {
                                    totalMonthLeaves++;
                                   _Leaves.Add(Templeaves[day].Day);
                                        
                                }
                                else { i++; }
                            }

                        }
                        var a = new DateTime(_year, tempMonth, DateTime.DaysInMonth(_year, tempMonth));
                        if (((int[])_DaysInMonth)[Tday] >= a.Day)
                        {
                            tempMonth = _month;
                            tempyear = _year;
                        }
                    }
                }
                else
                {
                      leaves_for_sunday += 0;
                      totalMonthLeaves += 0;
                }
            }
        }

       
        /// END
    }

    /// <summary>
    /// Holidays DataRows (FromDate , ToDate) for all cities
    /// </summary>    
    /// <returns>Holidays DataRows (FromDate , ToDate) for all cities</returns>
    private DataTable GetTotalHolidays(DateTime? tempFromDate=null)
    {   
        if(tempFromDate==null)
        {
            tempFromDate = from;
        }

        string sqlQuery = @"SELECT 
         FromDate,
  ToDate
FROM
  `hr_gazetted_holidays` 
WHERE FromDate BETWEEN @Date1 AND @date2 AND Holiday_flag in ('All',@city)";

        var fetchedHolidays = DAL.ExecuteDataTable(_connection, CommandType.Text, sqlQuery, new MySqlParameter[] { new MySqlParameter("@Date1", tempFromDate), new MySqlParameter("@date2", to), new MySqlParameter("@city", TCity) });

        return fetchedHolidays;
    }


    private DataTable GetTotalLeaves(string empNO)
    {


        string sqlQuery = @"SELECT Emp_no,`LeaveFromDate`,`LeaveToDate`
 FROM `hr_employeeleaverequest` lr
 WHERE `LeaveFromDate` BETWEEN @Date1 AND @Date2 AND  Emp_no = @emp_no;";




        var fetchedHolidays = DAL.ExecuteDataTable(_connection, CommandType.Text, sqlQuery, new MySqlParameter[] { new MySqlParameter("@Date1", from), new MySqlParameter("@Date2", to), new MySqlParameter("@emp_no", empNO) });

        return fetchedHolidays;
    }

    /// <summary>
    /// Fetches Attendance Process Schema form DataBase
    /// </summary>
    private void GetTblAttendanceProcessSchema()
    {
        _hr_employeeattendanceprocess = LCS.GetDataTableSchema(_connection, "hr_employeeattendanceprocess").Tables[0];
        _hr_employeeattendanceprocess.TableName = "hr_employeeattendanceprocess";
    }
    /// <summary>
    /// Calculation for the Consecutive late
    /// </summary>
    /// <param name="dr">DataRow of Data</param>
    /// <param name="requireField">Flag's value</param>
    /// <param name="seriesLenght">Number of consecutive late to calculate</param>
    /// <returns>Total Consecutive late for the current month</returns>
    private int ProcessConsecutiveLatesFlags(DataRow dr, string requireField, int seriesLenght)
    {
        int count = 0;

        if (dr != null)
        {
            var columnNames = dr.Table.Columns
                    .OfType<DataColumn>()
                    .Where(a => a.ColumnName.EndsWith("_3"));//checking for intime
            DataTable dt = dr.Table.Clone();
            dt.PrimaryKey = new DataColumn[] { dt.Columns[0] };
            dt.Rows.Add(dr.ItemArray);

            DataView dv = new DataView(dt);

            var dtWithFilteredColumns = dv.ToTable(false, columnNames
                .Select(a => a.ColumnName)
                .ToArray());//Filtering on CheckIn Columns i-3 endsWith _3

            // dtWithFilteredColumns.PrimaryKey = new DataColumn[] { dtWithFilteredColumns.Columns[0] };//Defining PrimaryKey

            var filteredList = dtWithFilteredColumns.AsEnumerable()
                .Select(a => a.ItemArray.Select(b => b.ToString()))
                .FirstOrDefault();//Filtering DAtatable for CheckIn Columns

            if (filteredList != null)
            {
                if (filteredList.Count() > 0)
                {
                    var refilteredList = filteredList.ToList().GetRange(0, _DaysInMonth.Count());
                    for (int loopCounter = 0; loopCounter <= refilteredList.Count - seriesLenght;)
                    {
                        if (loopCounter + seriesLenght <= refilteredList.Count)
                        {
                            var consecutiveList = refilteredList.GetRange(loopCounter, seriesLenght);
                            if (consecutiveList.TrueForAll(a => a.Trim().Equals(requireField.Trim(), StringComparison.InvariantCultureIgnoreCase)))
                            {
                                count++;
                                loopCounter += seriesLenght;
                                continue;

                            }
                        }
                        else
                        {
                            break;
                        }

                        loopCounter++;
                    }
                }

            }

            return count;
        }

        return count;
    }
    /// <summary>
    /// Fetches Custom Built datatable for Attendance Flags
    /// </summary>
    /// <returns>Custom Built datatable for Attendance Flags</returns>
    public DataTable GetAttendanceFlags()
    {
        return LogsFlags_Dt;
    }

    /// <summary>
    /// Custom Built datatable for Attendance Flags
    /// </summary>
    /// <returns>Custom Built datatable for Attendance Flags</returns>
    private DataTable GetLogsFlagDt(IEnumerable<int> DaysInMonth)
    {
        DataTable _customDt = new DataTable();
        DataColumn _col = new DataColumn("emp_no", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = null;
        _customDt.Columns.Add(_col);

        _col = new DataColumn("Name", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = null;
        _customDt.Columns.Add(_col);
        var tempmonth = _month - 1;
        var tempyear = Year;
        if (tempmonth == 0)
        {
            tempmonth = 12;
            tempyear = _year - 1;
        }
        var tempdate = new DateTime(tempyear, tempmonth, 26);

        for (int day = 0; day < DaysInMonth.Count(); day++)
        {
            tempdate = new DateTime(tempyear, tempmonth, ((int[])_DaysInMonth)[day]);

            _col = new DataColumn(tempdate.Day + "_1", typeof(string));
            _col.AllowDBNull = true;
            _col.DefaultValue = null;
            _customDt.Columns.Add(_col);

            _col = new DataColumn(tempdate.Day + "_2", typeof(string));
            _col.AllowDBNull = true;
            _col.DefaultValue = null;
            _customDt.Columns.Add(_col);

            _col = new DataColumn(tempdate.Day + "_3", typeof(string));
            _col.AllowDBNull = true;
            _col.DefaultValue = null;
            _customDt.Columns.Add(_col);

            _col = new DataColumn(tempdate.Day + "_4", typeof(string));
            _col.AllowDBNull = true;
            _col.DefaultValue = null;
            _customDt.Columns.Add(_col);

            var aa = new DateTime(_year, tempmonth, DateTime.DaysInMonth(_year, tempmonth));
            if (((int[])_DaysInMonth)[day] >= aa.Day)
            {
                tempmonth = _month;
                tempyear = _year;
            }
        }

        _col = new DataColumn("Sundays", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("Holidays", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("Leaves", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("Late", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("HalfDay", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("RuleAbsent", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("ConsecutiveLate", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("NotOut", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("Early", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("AdjustLate", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("AdujstAbsent", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);
        _col = new DataColumn("daysWorked", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);

        _col = new DataColumn("Absents", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);

        _col = new DataColumn("Department", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "";
        _customDt.Columns.Add(_col);

        _col = new DataColumn("TotalAdjustment", typeof(string));
        _col.AllowDBNull = true;
        _col.DefaultValue = "0";
        _customDt.Columns.Add(_col);

        _col = new DataColumn("StartIn", typeof(string));
        _col.AllowDBNull = true;
        _customDt.Columns.Add(_col);

        _col = new DataColumn("EndIn", typeof(string));
        _col.AllowDBNull = true;
        _customDt.Columns.Add(_col);


        _customDt.PrimaryKey = new DataColumn[] { _customDt.Columns["emp_no"] };

        return _customDt;
    }


    public void Dispose()
    {
        if (_AttendanceLogs != null)
        {
            _AttendanceLogs.Dispose();
            _AttendanceLogs = null;
        }

        if (_shiftDetails != null)
        {
            _shiftDetails.Dispose();
            _shiftDetails = null;

        }
        if (_AttendanceAdjustments != null)
        {
            _AttendanceAdjustments.Dispose();
            _AttendanceAdjustments = null;

        }
    }

    private static int[] GetInclusiveRange(int start, int end)
    {
        return Enumerable.Range(start, (end - start) + 1).ToArray();
    }

    /// <summary>
    /// List of Holidays join together for reporting
    /// </summary>
    /// <returns>List of Holidays join together for reporting</returns>

    // Old Method Comment By Anas 
    //public string GetHolidaysJointStr()
    //{
    //    string jointStr = string.Empty;
    //    if (_HolidaysFound != null)
    //    {
    //        if (_HolidaysFound.Rows.Count > 0)
    //        {
    //            List<string> _dates = new List<string>();

    //            foreach (int hol in _Holidays)
    //            {
    //                _dates.Add(new DateTime(Year, Month, hol).ToString("yyyy/MM/dd"));
    //            }
    //            jointStr = string.Join("|", _dates.ToArray());

    //        }
    //    }

    //    return jointStr;

    //}


    public string GetHolidaysJointStr()
    {
        string jointStr = string.Empty;
        if (_HolidaysFound != null)
        {
            if (_HolidaysFound.Rows.Count > 0)
            {
                List<string> _dates = new List<string>();

                //foreach (int hol in _Holidays)
                //{
                //    _dates.Add(new DateTime(Year, Month, hol).ToString("yyyy/MM/dd"));
                //}

                foreach (int hol in _Holidays)
                {

                    if (IsValidDate(Year, Month, hol))
                    {
                        _dates.Add(new DateTime(Year, Month, hol).ToString("yyyy/MM/dd"));
                    }
                    else
                    {
                        // Optionally handle invalid dates here, for example, log it or skip it
                        Console.WriteLine($"Invalid date: {Year}/{Month}/{hol}");
                    }
                    jointStr = string.Join("|", _dates.ToArray());

                }


            }

            return jointStr;

        }
        return jointStr;
    }

    private bool IsValidDate(int year, int month, int day)
    {
        return day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    /// <summary>
    /// Process for attendance adjustments for attendance report
    /// </summary>
    /// 

    public string GetLeavesJointStr()
    {
        var tempyear = Year;
        var tempmonth = Month-1;
        if(Month - 1==0)
        {
            tempmonth = 12;
            tempyear = Year - 1;
        }
        string jointStr = string.Empty;
        if (_LeavesFound != null)
        {
            if (_LeavesFound.Rows.Count > 0)
            {
                List<string> _dates = new List<string>();

                foreach (int lev in _Leaves)
                { 
                    if (lev == 26 || lev == 27 || lev == 28 || lev == 29 || lev == 30 || lev == 31)
                    {   
                        _dates.Add(new DateTime(tempyear, tempmonth, lev).ToString("yyyy/MM/dd"));
                    }
                    else
                    {
                        _dates.Add(new DateTime(Year, Month, lev).ToString("yyyy/MM/dd"));
                    }
                }
                jointStr = string.Join("|", _dates.ToArray());

            }
        }

        return jointStr;

    }
    public void ProcessAjustmentsForReporting()
    {
        if (_AttendanceAdjustments != null)
        {
            var adjustmentListsEnums = _AttendanceAdjustments.AsEnumerable()
                .GroupBy(a => a.Field<string>("emp_no"));
            foreach (var employee in adjustmentListsEnums)
            {
                string _adjustmentDate = "";

                var adjustmentInStr = employee.Select(a => a.Field<DateTime>("adjustmentDate"));
                if (adjustmentInStr != null)
                {
                    if (adjustmentInStr.Count() > 0)
                    {
                        _adjustmentDate = string.Join("|", adjustmentInStr.Select(a => a.ToString("yyyy/MM/dd")));
                        var rptEmployee = LogsFlags_Dt.AsEnumerable().SingleOrDefault(
                               a => a.Field<string>("emp_no").Trim().Equals(employee.Key, StringComparison.InvariantCultureIgnoreCase)
                            );
                        if (rptEmployee != null)
                        {
                            rptEmployee.SetField<string>("TotalAdjustment", _adjustmentDate);
                        }
                    }
                }
            }

        }
    }


    /// <summary>
    /// Getting attendance flags for Sundays and Holidays
    /// </summary>
    /// <param name="attendanceLog">Attendance Data table data source</param>
    /// <param name="day">Day of month</param>
    private void GetAdujustmentStatusMonthWise(DataTable attendanceLog, int year, int month, int day)
    {


        if (ListOfAdjustmentStatus != null)
        {

            if ((new DateTime(year, month, day)).DayOfWeek == DayOfWeek.Sunday)
            {
                if (_Holidays.Contains(day))
                    ListOfAdjustmentStatus.Add(day, "H-S");
                else
                    ListOfAdjustmentStatus.Add(day, "S-S");
            }
            else
            {
                if (_Holidays.Contains(day))
                {
                    ListOfAdjustmentStatus.Add(day, "H-H");
                }
                else if (_Leaves.Contains(day))
                {
                    ListOfAdjustmentStatus.Add(day, "LE-LE");
                }
                else
                {
                    ListOfAdjustmentStatus.Add(day, "A-A");
                }
            }

        }


    }


    /// <summary>
    ///  Gets status of adjusted logs of employee
    /// </summary>
    /// <param name="attendanceLog">Attendance log for employee</param>
    private void GetAdujustmentStatusMonthWise(DataTable attendanceLog)
    {
        int tempMonth = _month - 1;
        int tempyear = _year;
        if (tempMonth == 0)
        {
            tempMonth = 12;
            tempyear = _year - 1;
        }
        if (attendanceLog != null)
        {
            if (attendanceLog.Rows.Count == 1 && attendanceLog.Rows[0]["CHECKTIME"] == DBNull.Value)
            {
                for (int day = _PartialAttendance ? _dayStart : 0; day < (_PartialAttendance ? _dayEnd : _DaysInMonth.Count()); day++)
                {

                    if (!((new DateTime(tempyear, tempMonth, ((int[])_DaysInMonth)[day])).DayOfWeek == DayOfWeek.Sunday))
                    {
                        if (!((int)(new DateTime(tempyear, tempMonth, ((int[])_DaysInMonth)[day])).DayOfWeek == OffdayIndex))
                        {
                            if (_Leaves.Contains(((int[])_DaysInMonth)[day]))
                            {
                                ListOfAdjustmentStatus.Add(((int[])_DaysInMonth)[day], "LE-LE");
                            }
                            else
                            {
                                ListOfAdjustmentStatus.Add(((int[])_DaysInMonth)[day], "A-A");
                            }
                        }

                    }
                    var a = new DateTime(_year, tempMonth, DateTime.DaysInMonth(_year, tempMonth));
                    if (((int[])_DaysInMonth)[day] >= a.Day)
                    {
                        tempMonth = _month;
                        tempyear = _year;
                    }

                }
            }

        }
        else
        {
            PassAbsentForWorkingDays();
        }


        if (ListOfAdjustmentStatus != null)
        {
            if (ListOfAdjustmentStatus.Count > 0)
            {
                var filteredDays = _DaysInMonth.Except(ListOfAdjustmentStatus.Select(a => a.Key).ToList()).Except(_DaysExcludingSundays);
                if (filteredDays != null)
                {
                    if (filteredDays.Count() > 0)
                    {
                        foreach (var absentAndHoliday in filteredDays)
                        {
                            if (_Holidays.Contains(absentAndHoliday))
                                ListOfAdjustmentStatus.Add(absentAndHoliday, "H-H");
                            else
                                ListOfAdjustmentStatus.Add(absentAndHoliday, "A-A");
                        }
                    }
                }
            }
        }


    }

    /// <summary>
    /// Returns Attendance flags for the range of selected days
    /// </summary>
    /// <returns>Attendance flags for the range of selected days</returns>
    public Dictionary<int, string> GetAdujustmentStatusMonthWise()
    {
        return ListOfAdjustmentStatus;
    }
    /// <summary>
    /// This function will fill dictionary with Absent status for the employee ,for the selected month , incase employee is new and has no attendance log in database
    /// </summary>    
    private void PassAbsentForWorkingDays()
    {
        IEnumerable<int> absentDays = _DaysInMonth.Except(ListOfAdjustmentStatus.Select(a => a.Key).ToList()).Except(_Holidays).Except(_DaysExcludingSundays);
        if (absentDays != null)
        {
            if (absentDays.Count() > 0)
            {
                foreach (var absetnDay in absentDays)
                {
                    ListOfAdjustmentStatus.Add(absetnDay, "A-A");
                }
            }
        }
    }
    private void PassAbsentAdjustmentStatuses()
    {
        for (int _day = 1; _day <= DateTime.DaysInMonth(_year, _month); _day++)
        {
            if (!((new DateTime(_year, _month, _day)).DayOfWeek == DayOfWeek.Sunday))
            {
                ListOfAdjustmentStatus.Add(_day, "A-A");
            }
        }
    }
}
