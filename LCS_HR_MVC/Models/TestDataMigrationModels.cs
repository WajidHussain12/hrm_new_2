namespace LCS_HR_MVC.Models
{
    public class MigrationTableSpec
    {
        public string LiveConnection { get; set; } = "";
        public string SchemaDb { get; set; } = "";
        public string TableName { get; set; } = "";
        public string? DateField { get; set; }
        public string? CnColumn { get; set; }       // null = not CN-filtered
        public string? YearField  { get; set; }     // used by "YearMonth" category
        public string? MonthField { get; set; }     // used by "YearMonth" category
        public string Category { get; set; } = "";  // "Source" | "Lookup" | "CnFiltered" | "YearMonth"
        public string Label { get; set; } = "";
        // Which commission type this table belongs to — used for view grouping
        public string CommissionGroup { get; set; } = "Shared"; // "Cash"|"COD"|"OverLand"|"ReturnCOD"|"Master"|"Shared"
        public string? CityColumn { get; set; }               // column to filter by city (Source tables only), null = no city filter
        public string FullTableName => $"`{SchemaDb}`.`{TableName}`";
    }

    public class TableMigrationResult
    {
        public string Label { get; set; } = "";
        public string FullTableName { get; set; } = "";
        public string Category { get; set; } = "";
        public string LiveConnection { get; set; } = "";
        public string CommissionGroup { get; set; } = "Shared"; // "Cash"|"COD"|"OverLand"|"ReturnCOD"|"Master"|"Shared"
        public string Status { get; set; } = "Pending"; // Pending / Running / Done / Failed / Skipped
        public long LiveRowCount { get; set; }
        public long LocalRowCount { get; set; }
        public string? Error { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool CountsMatch => Status == "Done" && LiveRowCount == LocalRowCount;
    }

    public class DestinationTableCheck
    {
        public string FullTableName { get; set; } = "";
        public bool Exists { get; set; }
        public long RowCount { get; set; }
        public bool IsReady => Exists && RowCount == 0;
    }

    public class CityOption
    {
        public string Id   { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class MigrationStatusViewModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime RequiredFrom { get; set; }
        public DateTime RequiredTo { get; set; }
        public int RowLimit { get; set; } = 0; // 0 = unlimited; otherwise max rows per table
        public string OverallStatus { get; set; } = "Idle"; // Idle / Running / Done / Failed
        public string? StartedAt { get; set; }
        public string? CompletedAt { get; set; }
        public int TotalTables { get; set; }
        public int DoneTables { get; set; }
        public int FailedTables { get; set; }
        // Progress tracking
        public long TotalLiveRows { get; set; }
        public long TotalLocalRows { get; set; }
        public int ProgressPercent => TotalLiveRows > 0
            ? (int)Math.Min(100, TotalLocalRows * 100L / TotalLiveRows)
            : 0;
        public List<TableMigrationResult> TableResults { get; set; } = new();
        public List<DestinationTableCheck> DestinationChecks { get; set; } = new();
        public List<string> Logs { get; set; } = new();
        // City filter
        public List<string> SelectedCities { get; set; } = new();      // empty = all cities
        public List<CityOption> AvailableCities { get; set; } = new(); // populated for the UI dropdown
    }
}
