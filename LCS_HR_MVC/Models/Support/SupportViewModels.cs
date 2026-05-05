using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace LCS_HR_MVC.Models.Support
{
    public class DbBackUpViewModel
    {
        public IReadOnlyList<SupportFileItem> Backups { get; set; } = Array.Empty<SupportFileItem>();
        public string? LatestFileName { get; set; }
    }

    public class ResultSetExporterViewModel
    {
        public const string DefaultConnectionStringPlaceholder = "Data Source=host;\nInitial Catalog=defaultDb;\nUser Id=demo;\nPassword=demo";

        [Required(ErrorMessage = "Query is required.")]
        public string Query { get; set; } = string.Empty;

        public bool IncludeHeader { get; set; } = true;
        public bool IncludeSummary { get; set; } = true;
        public bool HighlightNegatives { get; set; } = true;
        public bool Filtering { get; set; }
        public bool EnableGrouping { get; set; } = true;
        public bool UseCustomConnectionString { get; set; }

        public string GroupingColumnName { get; set; } = string.Empty;
        public string GroupingColumnPrefix { get; set; } = string.Empty;
        public string DateTimeFormate { get; set; } = "dd-MMM-yyyy HH:mm:ss";
        public string MainSheetHeadingText { get; set; } = string.Empty;
        public string MainSheetName { get; set; } = string.Empty;
        public string ExportFileName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = DefaultConnectionStringPlaceholder;

        [Range(1, int.MaxValue, ErrorMessage = "Connection Time must be greater than zero.")]
        public int ConnectionTime { get; set; } = 300;

        [Range(0, int.MaxValue, ErrorMessage = "Column Freeze Index cannot be negative.")]
        public int? FilterIndex { get; set; }

        [Range(10, 400, ErrorMessage = "Zoom Level must be between 10 and 400.")]
        public int Zoom { get; set; } = 100;

        [Range(1, 72, ErrorMessage = "Heading Font Size must be between 1 and 72.")]
        public float HeadingFontSize { get; set; } = 16;

        [Range(1, 72, ErrorMessage = "Content Font Size must be between 1 and 72.")]
        public float ContentFontSize { get; set; } = 12;

        public string ExportFormat { get; set; } = "0";
    }

    public class ResultSetExportResult
    {
        public required byte[] Content { get; init; }
        public required string ContentType { get; init; }
        public required string FileName { get; init; }
    }

    public class ErrorLogsQueryModel
    {
        public string SelectedUserId { get; set; } = "00";
        public string SearchColumn { get; set; } = "All";
        public string SearchText { get; set; } = string.Empty;
        public string? SelectedErrorId { get; set; }
        public int Page { get; set; } = 1;
    }

    public class ErrorLogsViewModel : ErrorLogsQueryModel
    {
        public IReadOnlyList<SelectListItem> Users { get; set; } = Array.Empty<SelectListItem>();
        public IReadOnlyList<SelectListItem> SearchColumns { get; set; } = Array.Empty<SelectListItem>();
        public IReadOnlyList<ErrorLogRowViewModel> Rows { get; set; } = Array.Empty<ErrorLogRowViewModel>();
        public ErrorLogDetailViewModel? SelectedDetail { get; set; }
        public string? WarningMessage { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public int PageSize { get; set; }
    }

    public class ErrorLogRowViewModel
    {
        public string ErrorID { get; set; } = string.Empty;
        public string LogDateTime { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string UserIP { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string UserLocation { get; set; } = string.Empty;
    }

    public class ErrorLogDetailViewModel
    {
        public string ErrorID { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
    }

    public class AuditViewerQueryModel
    {
        public string SearchColumn { get; set; } = "Customer Query";
        public string SearchText { get; set; } = string.Empty;
        public string? TableName { get; set; }
        public string? AuditDate { get; set; }
        public string? OptType { get; set; }
        public string? TblPk { get; set; }
        public string? UserID { get; set; }
        public int Page { get; set; } = 1;
    }

    public class AuditViewerViewModel : AuditViewerQueryModel
    {
        public IReadOnlyList<SelectListItem> SearchColumns { get; set; } = Array.Empty<SelectListItem>();
        public IReadOnlyList<AuditTrailRowViewModel> Rows { get; set; } = Array.Empty<AuditTrailRowViewModel>();
        public AuditTrailDetailViewModel? SelectedDetail { get; set; }
        public string? WarningMessage { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public int PageSize { get; set; }
    }

    public class AuditTrailRowViewModel
    {
        public string TableName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string OptType { get; set; } = string.Empty;
        public string TblPk { get; set; } = string.Empty;
        public string UserID { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string OperationText { get; set; } = string.Empty;
    }

    public class AuditTrailDetailViewModel
    {
        public string TableName { get; set; } = string.Empty;
        public string GeneratedSql { get; set; } = string.Empty;
        public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();
        public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; set; } = Array.Empty<IReadOnlyDictionary<string, string>>();
        public IReadOnlyDictionary<string, string> FooterTotals { get; set; } = new Dictionary<string, string>();
    }

    public class DataUploadingUtilityViewModel
    {
        public const string DefaultConnectionStringPlaceholder = "Data Source=host;\nInitial Catalog=defaultDb;\nUser Id=demo;\nPassword=demo";

        public string ConnectionMode { get; set; } = "Default";
        public string UploadingMode { get; set; } = "A";
        public string ConnectionString { get; set; } = DefaultConnectionStringPlaceholder;
        public IFormFile? Document { get; set; }
        public bool? LastConnectionCheckPassed { get; set; }
    }

    public class AttendanceLogViewerViewModel
    {
        [Required(ErrorMessage = "Required")]
        public string EmployeeDescription { get; set; } = string.Empty;

        [Required(ErrorMessage = "Required")]
        public string EmpNo { get; set; } = string.Empty;

        [Range(2000, 9999, ErrorMessage = "Select a valid year.")]
        public int Year { get; set; }

        [Range(1, 12, ErrorMessage = "Select a valid month.")]
        public int Month { get; set; }

        public int WorkingYear { get; set; }
        public int WorkingMonth { get; set; }
        public string WorkingMonthName { get; set; } = string.Empty;
        public bool SourceFileExists { get; set; }
        public string SourceFilePath { get; set; } = string.Empty;
        public IReadOnlyList<SelectListItem> Years { get; set; } = Array.Empty<SelectListItem>();
        public IReadOnlyList<SelectListItem> Months { get; set; } = Array.Empty<SelectListItem>();
    }

    public class SupportFileLibraryViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string LibraryKey { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public IReadOnlyList<SupportFileItem> Files { get; set; } = Array.Empty<SupportFileItem>();
    }

    public class SupportFileItem
    {
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long Length { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class SupportDownloadFile
    {
        public required byte[] Content { get; init; }
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
    }

    public class UploadUtilityResult
    {
        public int RowsInserted { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
