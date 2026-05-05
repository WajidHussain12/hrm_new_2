using System.Globalization;

namespace LCS_HR_MVC.Models
{
    public class AcTestFilterRequest
    {
        // REQUIRED filters
        public string? CityCode  { get; set; }
        public int?    Year      { get; set; }
        public int?    Month     { get; set; }

        // OPTIONAL filters
        public string? CommissionType { get; set; }
        // Values: "Cash","COD","OverLand","ReturnCOD","Master","All" (default All)

        public string? StationId { get; set; }
        // Optional: filter by station within city

        public string? RouteCode { get; set; }
        // Optional: filter by specific courier

        public string? EmpNo { get; set; }
        // Optional: filter by specific employee number

        // ── Computed helpers ──────────────────────────────────────────────────

        public bool HasRequiredFilters =>
            !string.IsNullOrWhiteSpace(CityCode) &&
            Year.HasValue && Month.HasValue;

        public string DisplayLabel =>
            HasRequiredFilters
                ? $"{CityCode} — {MonthName} {Year}"
                : "No filter applied";

        public string MonthName => Month.HasValue
            ? new DateTime(Year ?? 2026, Month.Value, 1)
                .ToString("MMMM", CultureInfo.InvariantCulture)
            : "";

        // Commission period: 21st of previous month → 20th of current month
        public DateTime PeriodFrom =>
            new DateTime(
                Month == 1 ? (Year ?? 2026) - 1 : (Year ?? 2026),
                Month == 1 ? 12 : (Month ?? 4) - 1,
                21);

        public DateTime PeriodTo =>
            new DateTime(Year ?? 2026, Month ?? 4, 20);
    }

    public class AcTestTableComparisonRow
    {
        public string  RealTable       { get; set; } = "";
        public string  TestTable       { get; set; } = "";
        public string  CommissionGroup { get; set; } = "";
        // Cash | COD | OverLand | ReturnCOD | Master | Log

        public long    RealRows        { get; set; }
        public long    TestRows        { get; set; }
        public bool    RealExists      { get; set; }
        public bool    TestExists      { get; set; }

        public bool    IsFiltered      { get; set; }
        // true  = row count uses WHERE clause (city/year/month)
        // false = total count (no filter applicable for this table)

        public string Status =>
            !TestExists                          ? "Missing"    :
            !RealExists                          ? "RealMissing":
            RealRows == 0 && TestRows == 0       ? "BothEmpty"  :
            TestRows  == 0                       ? "TestEmpty"  :
            RealRows  == TestRows                ? "Match"      :
            TestRows  >  RealRows                ? "TestMore"   :
                                                   "TestLess"   ;
    }

    public class AcTestComparisonViewModel
    {
        public AcTestFilterRequest            Filter         { get; set; } = new();
        public List<AcTestTableComparisonRow> Rows           { get; set; } = new();
        public bool                           IsTestMode     { get; set; }
        public bool                           FiltersApplied { get; set; }

        // Available cities loaded from hr_city
        public List<(string Code, string Name)> Cities { get; set; } = new();

        // Summary counts
        public int TotalMatch    => Rows.Count(r => r.Status == "Match");
        public int TotalMissing  => Rows.Count(r => r.Status is "Missing" or "TestEmpty");
        public int TotalMismatch => Rows.Count(r => r.Status is "TestMore" or "TestLess");
        public int TotalEmpty    => Rows.Count(r => r.Status == "BothEmpty");
    }
}
