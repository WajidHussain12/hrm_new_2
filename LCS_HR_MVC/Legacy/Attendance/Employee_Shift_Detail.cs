using System;
using System.Data;
using System.Linq;

public class Employee_Shift_Detail
{
    private bool _nightShift;
    private bool _active;
    private TimeSpan? _endOut;
    private TimeSpan? _beginOut;
    private TimeSpan? _endIn;
    private TimeSpan? _beginIn;
    private TimeSpan? _graceTimeOut;
    private TimeSpan? _graceTimeIn;
    private TimeSpan? _endTime;
    private TimeSpan? _startTime;

    public TimeSpan? Start_Time => _startTime;
    public TimeSpan? End_Time => _endTime;
    public TimeSpan? Grace_Time_IN => _graceTimeIn;
    public TimeSpan? Grace_Time_OUT => _graceTimeOut;
    public TimeSpan? Begin_IN => _beginIn;
    public TimeSpan? End_IN => _endIn;
    public TimeSpan? Begin_OUT => _beginOut;
    public TimeSpan? End_OUT => _endOut;
    public bool Active => _active;
    public double TotalHours { get; set; }
    public bool NightShift => _nightShift;
    public DateTime? _fromDate { get; set; }
    public DateTime? _ToDate { get; set; }

    public Employee_Shift_Detail(DataRow shiftDetail)
    {
        ValidateShiftDetail(shiftDetail);
        SetProperties(shiftDetail);
    }

    private void ValidateShiftDetail(DataRow shiftDetails)
    {
        if (shiftDetails == null)
        {
            throw new ArgumentException("Shift Details cannot be null.");
        }

        var columns = shiftDetails.Table.Columns
            .OfType<DataColumn>()
            .Select(a => a.ColumnName)
            .OrderBy(a => a)
            .ToArray();

        var defaultColumns = new[]
        {
            "emp_no", "ShiftCode", "Start_Time", "End_Time", "Grace_Time_IN", "Grace_Time_OUT",
            "Begin_IN", "End_IN", "Begin_OUT", "End_OUT", "Active", "TotalHours", "NightShift",
            "FromDate", "ToDate"
        }.OrderBy(a => a).ToArray();

        if (!columns.SequenceEqual(defaultColumns, StringComparer.InvariantCultureIgnoreCase))
        {
            throw new ArgumentException("Schema difference for Shift Details data source.");
        }
    }

    private void SetProperties(DataRow shiftDetail)
    {
        if (shiftDetail.IsNull("Start_Time")) throw new ArgumentException("Start time cannot be null");
        _startTime = shiftDetail.Field<TimeSpan>("Start_Time");

        if (shiftDetail.IsNull("End_Time")) throw new ArgumentException("End time cannot be null");
        _endTime = shiftDetail.Field<TimeSpan>("End_Time");

        if (shiftDetail.IsNull("Grace_Time_IN")) throw new ArgumentException("Grace time in cannot be null");
        _graceTimeIn = shiftDetail.Field<TimeSpan>("Grace_Time_IN");

        if (shiftDetail.IsNull("Grace_Time_OUT")) throw new ArgumentException("Grace time out cannot be null");
        _graceTimeOut = shiftDetail.Field<TimeSpan>("Grace_Time_OUT");

        if (shiftDetail.IsNull("Begin_IN")) throw new ArgumentException("Begin in time cannot be null");
        _beginIn = shiftDetail.Field<TimeSpan>("Begin_IN");

        if (shiftDetail.IsNull("End_IN")) throw new ArgumentException("End in time cannot be null");
        _endIn = shiftDetail.Field<TimeSpan>("End_IN");

        if (shiftDetail.IsNull("Begin_OUT")) throw new ArgumentException("Begin out time cannot be null");
        _beginOut = shiftDetail.Field<TimeSpan>("Begin_OUT");

        if (shiftDetail.IsNull("End_OUT")) throw new ArgumentException("End out time cannot be null");
        _endOut = shiftDetail.Field<TimeSpan>("End_OUT");

        if (shiftDetail.IsNull("Active")) throw new ArgumentException("Shift Active Flag cannot be null");
        _active = shiftDetail.Field<string>("Active").Equals("y", StringComparison.InvariantCultureIgnoreCase);

        TotalHours = shiftDetail.IsNull("TotalHours") ? 0 : shiftDetail.Field<double>("TotalHours");

        if (shiftDetail.IsNull("NightShift")) throw new ArgumentException("Shift NightShift Flag cannot be null");
        _nightShift = shiftDetail.Field<string>("NightShift").Equals("y", StringComparison.InvariantCultureIgnoreCase);

        if (shiftDetail.IsNull("FromDate")) throw new ArgumentException("From date Flag cannot be null");
        _fromDate = shiftDetail.Field<DateTime>("FromDate");

        _ToDate = shiftDetail.IsNull("ToDate") ? null : shiftDetail.Field<DateTime>("ToDate");
    }
}
