using LCS_HR_MVC.Models.Support;

namespace LCS_HR_MVC.Services
{
    public interface ISupportService
    {
        Task<DbBackUpViewModel> GetDbBackUpPageAsync(string? latestFileName = null);
        Task<string> CreateDatabaseBackupAsync(string currentUserName, CancellationToken cancellationToken = default);
        Task<SupportDownloadFile?> GetBackupFileAsync(string fileName, CancellationToken cancellationToken = default);
        Task<ResultSetExportResult> ExportResultSetAsync(ResultSetExporterViewModel model, CancellationToken cancellationToken = default);
        Task<ErrorLogsViewModel> GetErrorLogsPageAsync(ErrorLogsQueryModel query, CancellationToken cancellationToken = default);
        Task<AuditViewerViewModel> GetAuditViewerPageAsync(AuditViewerQueryModel query, CancellationToken cancellationToken = default);
        Task<bool> ValidateConnectionAsync(string connectionString, CancellationToken cancellationToken = default);
        Task<UploadUtilityResult> UploadDataAsync(DataUploadingUtilityViewModel model, string currentUserId, CancellationToken cancellationToken = default);
        Task<AttendanceLogViewerViewModel> GetAttendanceLogViewerPageAsync(DateTime workingDate, AttendanceLogViewerViewModel? existingModel = null, CancellationToken cancellationToken = default);
        Task<IEnumerable<dynamic>> SearchAttendanceLogEmployeesAsync(string term, string currentUserId, CancellationToken cancellationToken = default);
        Task<SupportDownloadFile> ExportAttendanceLogAsync(AttendanceLogViewerViewModel model, CancellationToken cancellationToken = default);
        Task<SupportFileLibraryViewModel> GetFileLibraryAsync(string libraryKey, CancellationToken cancellationToken = default);
        Task<SupportDownloadFile?> GetLibraryFileAsync(string libraryKey, string fileName, CancellationToken cancellationToken = default);
    }
}
