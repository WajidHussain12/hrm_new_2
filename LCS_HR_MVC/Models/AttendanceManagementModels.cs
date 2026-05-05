using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models
{
    // ── Filter / Pagination ──────────────────────────────────────────────────────
    public class AttendanceManagementFilter
    {
        public string? EmpNo      { get; set; }
        public string? EmpName    { get; set; }
        public int     Year       { get; set; } = DateTime.Now.Year;
        public int     Month      { get; set; } = DateTime.Now.Month;
        public string? CityCode   { get; set; }
        public string? DeptCode   { get; set; }
        public string? AttSource  { get; set; }   // Biometric | Mobile App | Adjustment
        public string? AttStatus  { get; set; }   // Present | Absent | Leave | Holiday | Weekend | WFH
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate   { get; set; }
        public int     Page       { get; set; } = 1;
        public int     PageSize   { get; set; } = 50;
    }

    // ── Individual daily row returned from the combined query ─────────────────────
    public class AttendanceDailyRow
    {
        public string    EmpNo        { get; set; } = "";
        public string    EmpName      { get; set; } = "";
        public string    City         { get; set; } = "";
        public string    Department   { get; set; } = "";
        public DateTime  AttDate      { get; set; }
        public string    DayName      { get; set; } = "";
        public TimeSpan? CheckIn      { get; set; }
        public TimeSpan? CheckOut     { get; set; }
        public string    AttSource    { get; set; } = "";   // Biometric | Mobile App | Adjustment | None
        public bool      IsWFH        { get; set; }
        public bool      IsWeekend    { get; set; }
        public bool      IsHoliday    { get; set; }
        public bool      IsLeave      { get; set; }
        public string?   LeaveCode    { get; set; }
        public string?   LeaveCategory{ get; set; }
        public string?   LeaveReason  { get; set; }
        public string?   LeaveStatus  { get; set; }
        public string?   AdjType      { get; set; }
        public string?   AdjReason    { get; set; }
        public double?   Latitude     { get; set; }
        public double?   Longitude    { get; set; }

        // ── Computed ────────────────────────────────────────────────────────
        public string AttendanceStatus
        {
            get
            {
                if (IsHoliday)  return "Holiday";
                if (IsWeekend)  return "Weekend";
                if (AdjType == "A") return "Absent";
                if (IsWFH)      return "Work From Home";
                if (IsLeave && (AttSource == "None" || string.IsNullOrEmpty(AttSource))) return "On Leave";
                if (AttSource == "Biometric" || AttSource == "Mobile App")
                    return IsWFH ? "Work From Home" : "Present";
                if (AdjType == "RA") return "Rev. Absent";
                if (AdjType == "L")  return "Late";
                if (AdjType == "E")  return "Early";
                if (!string.IsNullOrEmpty(AdjType)) return "Adjusted";
                return "N/A";
            }
        }

        public string StatusBadgeCss
        {
            get => AttendanceStatus switch
            {
                "Present"        => "att-badge-present",
                "Work From Home" => "att-badge-wfh",
                "Absent"         => "att-badge-absent",
                "On Leave"       => "att-badge-leave",
                "Holiday"        => "att-badge-holiday",
                "Weekend"        => "att-badge-weekend",
                "Late"           => "att-badge-late",
                "Early"          => "att-badge-late",
                "Rev. Absent"    => "att-badge-radj",
                _                => "att-badge-na"
            };
        }

        public string SourceBadgeCss
        {
            get => AttSource switch
            {
                "Biometric"  => "src-badge-bio",
                "Mobile App" => "src-badge-mob",
                _            => "src-badge-manual"
            };
        }

        public string AdjTypeName
        {
            get => AdjType switch
            {
                "A"  => "Absent",
                "RA" => "Reverse Absent",
                "L"  => "Late",
                "E"  => "Early",
                _    => AdjType ?? ""
            };
        }
    }

    // ── Summary aggregate stats for the filtered result set ─────────────────────
    public class AttendanceSummaryStats
    {
        public int TotalRecords  { get; set; }
        public int TotalPresent  { get; set; }
        public int TotalWFH      { get; set; }
        public int TotalAbsent   { get; set; }
        public int TotalLeave    { get; set; }
        public int TotalHoliday  { get; set; }
        public int TotalWeekend  { get; set; }
        public int TotalLate     { get; set; }
        public int TotalBiometric{ get; set; }
        public int TotalMobile   { get; set; }
        public int TotalManual   { get; set; }
    }

    // ── Monthly employee-level processed summary ────────────────────────────────
    public class AttendanceMonthlySummaryRow
    {
        public string EmpNo      { get; set; } = "";
        public string EmpName    { get; set; } = "";
        public string City       { get; set; } = "";
        public string Department { get; set; } = "";
        public int    Year       { get; set; }
        public int    Month      { get; set; }
        public int    Absents    { get; set; }
        public int    Sundays    { get; set; }
        public int    Holidays   { get; set; }
        public int    Leaves     { get; set; }
        public int    Late       { get; set; }
        public int    HalfDay    { get; set; }
        public bool   IsProcessed{ get; set; }
    }

    // ── Full view-model sent to the Razor view ───────────────────────────────────
    public class AttendanceManagementViewModel
    {
        public AttendanceManagementFilter    Filter       { get; set; } = new();
        public List<AttendanceDailyRow>      Records      { get; set; } = new();
        public AttendanceSummaryStats        Stats        { get; set; } = new();
        public int                           TotalRecords { get; set; }
        public int                           PageCount    => Filter.PageSize > 0
                                                              ? (int)Math.Ceiling((double)TotalRecords / Filter.PageSize)
                                                              : 1;

        // Dropdowns
        public List<SelectListItem> YearList    { get; set; } = new();
        public List<SelectListItem> MonthList   { get; set; } = new();
        public List<SelectListItem> CityList    { get; set; } = new();
        public List<SelectListItem> SourceList  { get; set; } = new();
        public List<SelectListItem> StatusList  { get; set; } = new();
        public List<SelectListItem> LeaveTypeList { get; set; } = new();
    }
}
