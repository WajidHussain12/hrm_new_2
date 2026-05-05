using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using LCS_HR_MVC.Data;
using LCS_HR_MVC.Models.Support;
using LCS_HR_MVC.Utilities;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.StaticFiles;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class SupportService : ISupportService
    {
        private const int ErrorLogsPageSize = 16;
        private const int AuditLogsPageSize = 10;
        private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();
        private static readonly string[] BlockedSqlKeywords = { "delete", "update", "alter" };

        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<SupportService> _logger;

        public SupportService(IDbConnectionFactory connectionFactory, IWebHostEnvironment environment, ILogger<SupportService> logger)
        {
            _connectionFactory = connectionFactory;
            _environment = environment;
            _logger = logger;
        }

        public Task<DbBackUpViewModel> GetDbBackUpPageAsync(string? latestFileName = null)
        {
            var directory = EnsureBackupDirectory();
            var backups = new DirectoryInfo(directory)
                .GetFiles("*.sql", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTime)
                .Select(file => new SupportFileItem
                {
                    FileName = file.Name,
                    RelativePath = file.Name,
                    Length = file.Length,
                    LastModified = file.LastWriteTime
                })
                .ToList();

            return Task.FromResult(new DbBackUpViewModel
            {
                Backups = backups,
                LatestFileName = latestFileName
            });
        }

        public async Task<string> CreateDatabaseBackupAsync(string currentUserName, CancellationToken cancellationToken = default)
        {
            var backupDirectory = EnsureBackupDirectory();
            var connectionString = NormalizeConnectionString(_connectionFactory.ConnectionString, enableLocalInfile: false);

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var fileName = CreateBackupFileName(connection.Database, currentUserName);
            var fullPath = Path.Combine(backupDirectory, fileName);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            using var command = connection.CreateCommand();
            using var backup = new MySqlBackup(command);
            command.Connection = connection;
            backup.ExportToFile(fullPath);

            return fileName;
        }

        public async Task<SupportDownloadFile?> GetBackupFileAsync(string fileName, CancellationToken cancellationToken = default)
        {
            var fullPath = GetSafePath(EnsureBackupDirectory(), fileName);
            if (fullPath == null || !File.Exists(fullPath))
            {
                return null;
            }

            var content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return CreateDownloadFile(content, Path.GetFileName(fullPath));
        }

        public async Task<ResultSetExportResult> ExportResultSetAsync(ResultSetExporterViewModel model, CancellationToken cancellationToken = default)
        {
            ValidateResultSetQuery(model.Query);

            var data = await ExecuteResultSetQueryAsync(model, cancellationToken);
            if (data.Rows.Count == 0)
            {
                throw new ArgumentException("No data found.");
            }

            var exporter = new SupportExcelExporter
            {
                IncludeHeader = model.IncludeHeader,
                AddSummary = model.IncludeSummary,
                EnableGrouping = model.EnableGrouping,
                EnableFilter = model.Filtering,
                GroupingColumnName = model.GroupingColumnName.Trim(),
                GroupingColumnPrefix = model.GroupingColumnPrefix.Trim(),
                HighlightNegatives = model.HighlightNegatives,
                DateTimeFormate = string.IsNullOrWhiteSpace(model.DateTimeFormate) ? "dd-MMM-yyyy HH:mm:ss" : model.DateTimeFormate.Trim(),
                MainSheetHeaderText = string.IsNullOrWhiteSpace(model.MainSheetHeadingText) ? "Main Sheet" : model.MainSheetHeadingText.Trim(),
                MainSheetName = string.IsNullOrWhiteSpace(model.MainSheetName) ? "Main_Sheet" : model.MainSheetName.Trim(),
                FreezingColumnIndex = model.FilterIndex ?? 0,
                HeadingsFontSize = model.HeadingFontSize,
                ContentFontSize = model.ContentFontSize,
                ZoomLevel = model.Zoom
            };

            var baseName = string.IsNullOrWhiteSpace(model.ExportFileName)
                ? SanitizeFileName(exporter.MainSheetName)
                : SanitizeFileName(model.ExportFileName.Trim());

            if (model.ExportFormat == "1")
            {
                return new ResultSetExportResult
                {
                    Content = exporter.ExportToZip(data),
                    ContentType = "application/x-zip-compressed",
                    FileName = $"{baseName}.zip"
                };
            }

            return new ResultSetExportResult
            {
                Content = exporter.ExportToExcel(data),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = $"{baseName}.xlsx"
            };
        }

        public async Task<ErrorLogsViewModel> GetErrorLogsPageAsync(ErrorLogsQueryModel query, CancellationToken cancellationToken = default)
        {
            using var connection = new MySqlConnection(_connectionFactory.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var selectedUserId = string.Equals(query.SelectedUserId, "00", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : query.SelectedUserId.Trim();

            var gridTable = GetErrorLogsTable(connection, selectedUserId);
            var users = GetErrorLogUsers(connection);

            var warningMessage = string.Empty;
            var filteredView = ApplyLegacyFilter(gridTable, query.SearchColumn, query.SearchText, allValueText: "All", customExpressionLabel: null, ref warningMessage);
            var filteredTable = filteredView.ToTable();

            var page = Math.Max(1, query.Page);
            var totalCount = filteredTable.Rows.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)ErrorLogsPageSize));
            if (page > totalPages)
            {
                page = totalPages;
            }

            var pagedRows = filteredTable.AsEnumerable()
                .Skip((page - 1) * ErrorLogsPageSize)
                .Take(ErrorLogsPageSize)
                .Select(row => new ErrorLogRowViewModel
                {
                    ErrorID = row["ErrorID"]?.ToString() ?? string.Empty,
                    LogDateTime = row["LogDateTime"]?.ToString() ?? string.Empty,
                    Message = row["Message"]?.ToString() ?? string.Empty,
                    UserIP = row["UserIP"]?.ToString() ?? string.Empty,
                    UserName = row["UserName"]?.ToString() ?? string.Empty,
                    UserLocation = row["UserLocation"]?.ToString() ?? string.Empty
                })
                .ToList();

            var model = new ErrorLogsViewModel
            {
                SelectedUserId = string.IsNullOrWhiteSpace(query.SelectedUserId) ? "00" : query.SelectedUserId,
                SearchColumn = string.IsNullOrWhiteSpace(query.SearchColumn) ? "All" : query.SearchColumn,
                SearchText = query.SearchText ?? string.Empty,
                SelectedErrorId = query.SelectedErrorId,
                Page = page,
                PageSize = ErrorLogsPageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                WarningMessage = string.IsNullOrWhiteSpace(warningMessage) ? null : warningMessage,
                Users = users,
                SearchColumns = BuildSearchColumns(gridTable, "All"),
                Rows = pagedRows
            };

            if (!string.IsNullOrWhiteSpace(query.SelectedErrorId))
            {
                model.SelectedDetail = GetErrorLogDetail(connection, query.SelectedErrorId);
            }

            return model;
        }

        public async Task<AuditViewerViewModel> GetAuditViewerPageAsync(AuditViewerQueryModel query, CancellationToken cancellationToken = default)
        {
            using var connection = new MySqlConnection(_connectionFactory.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var gridTable = GetAuditTrailTable(connection);
            var warningMessage = string.Empty;
            var filteredView = ApplyLegacyFilter(gridTable, query.SearchColumn, query.SearchText, allValueText: null, customExpressionLabel: "Customer Query", ref warningMessage);
            var filteredTable = filteredView.ToTable();

            var page = Math.Max(1, query.Page);
            var totalCount = filteredTable.Rows.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)AuditLogsPageSize));
            if (page > totalPages)
            {
                page = totalPages;
            }

            var pagedRows = filteredTable.AsEnumerable()
                .Skip((page - 1) * AuditLogsPageSize)
                .Take(AuditLogsPageSize)
                .Select(row => new AuditTrailRowViewModel
                {
                    TableName = row["TableName"]?.ToString() ?? string.Empty,
                    Date = row.Field<DateTime>("Date"),
                    OptType = row["OptType"]?.ToString() ?? string.Empty,
                    TblPk = row["TblPk"]?.ToString() ?? string.Empty,
                    UserID = row["UserID"]?.ToString() ?? string.Empty,
                    UserName = row["UserName"]?.ToString() ?? string.Empty,
                    OperationText = GetOperationText(row["OptType"]?.ToString())
                })
                .ToList();

            var model = new AuditViewerViewModel
            {
                SearchColumn = string.IsNullOrWhiteSpace(query.SearchColumn) ? "Customer Query" : query.SearchColumn,
                SearchText = query.SearchText ?? string.Empty,
                TableName = query.TableName,
                AuditDate = query.AuditDate,
                OptType = query.OptType,
                TblPk = query.TblPk,
                UserID = query.UserID,
                Page = page,
                PageSize = AuditLogsPageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                WarningMessage = string.IsNullOrWhiteSpace(warningMessage) ? null : warningMessage,
                SearchColumns = BuildSearchColumns(gridTable, "Customer Query"),
                Rows = pagedRows
            };

            if (!string.IsNullOrWhiteSpace(query.TableName)
                && !string.IsNullOrWhiteSpace(query.AuditDate)
                && !string.IsNullOrWhiteSpace(query.OptType)
                && !string.IsNullOrWhiteSpace(query.TblPk)
                && !string.IsNullOrWhiteSpace(query.UserID))
            {
                model.SelectedDetail = GetAuditTrailDetail(connection, query);
            }

            return model;
        }

        public Task<bool> ValidateConnectionAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Task.FromResult(false);
            }

            try
            {
                using var connection = new MySqlConnection(connectionString.Trim());
                connection.Open();
                connection.Close();
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        public async Task<UploadUtilityResult> UploadDataAsync(DataUploadingUtilityViewModel model, string currentUserId, CancellationToken cancellationToken = default)
        {
            if (model.Document == null || model.Document.Length == 0)
            {
                throw new ArgumentException("Please upload excel sheet again.");
            }

            var useCustomConnection = string.Equals(model.ConnectionMode, "Custom", StringComparison.OrdinalIgnoreCase);
            if (useCustomConnection)
            {
                var isValid = await ValidateConnectionAsync(model.ConnectionString, cancellationToken);
                if (!isValid)
                {
                    throw new ArgumentException("Invalid Connection String. Please check user credentials.");
                }
            }

            var connectionString = useCustomConnection
                ? NormalizeConnectionString(model.ConnectionString, enableLocalInfile: true)
                : NormalizeConnectionString(_connectionFactory.ConnectionString, enableLocalInfile: true);

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            DataSet excelDataSet;
            await using (var inputStream = model.Document.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await inputStream.CopyToAsync(memoryStream, cancellationToken);
                excelDataSet = GetDataTableFromExcel(memoryStream.ToArray(), Path.GetExtension(model.Document.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase));
            }

            RemoveEmptyRows(excelDataSet);
            var dbTable = ValidateDataSource(excelDataSet, connection);

            if (string.Equals(model.UploadingMode, "A", StringComparison.OrdinalIgnoreCase))
            {
                FillDataTablesPrimaryKeys(excelDataSet, connection);
            }

            FillAuditColumns(excelDataSet, currentUserId);

            MySqlTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync(cancellationToken);
                var rowsInserted = BulkLoadDataSet(excelDataSet, connection, dbTable);
                if (rowsInserted == 0)
                {
                    throw new ArgumentException("No Record(s) inserted");
                }

                await transaction.CommitAsync(cancellationToken);
                return new UploadUtilityResult
                {
                    RowsInserted = rowsInserted,
                    Message = $"{rowsInserted} Record(s) inserted."
                };
            }
            catch
            {
                if (transaction != null && transaction.Connection != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                throw;
            }
        }

        public Task<SupportFileLibraryViewModel> GetFileLibraryAsync(string libraryKey, CancellationToken cancellationToken = default)
        {
            var folderName = GetLibraryFolderName(libraryKey);
            var fullPath = EnsureLibraryDirectory(folderName);

            var files = new DirectoryInfo(fullPath)
                .GetFiles("*", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name)
                .Select(file => new SupportFileItem
                {
                    FileName = file.Name,
                    RelativePath = file.Name,
                    Length = file.Length,
                    LastModified = file.LastWriteTime
                })
                .ToList();

            var title = libraryKey switch
            {
                "Docs" => "Document Samples",
                "Softwares" => "Download Softwares",
                "HR_Docs" => "HR Documents",
                _ => "Downloads"
            };

            return Task.FromResult(new SupportFileLibraryViewModel
            {
                Title = title,
                LibraryKey = libraryKey,
                FolderName = folderName,
                Files = files
            });
        }

        public async Task<SupportDownloadFile?> GetLibraryFileAsync(string libraryKey, string fileName, CancellationToken cancellationToken = default)
        {
            var fullPath = GetSafePath(EnsureLibraryDirectory(GetLibraryFolderName(libraryKey)), fileName);
            if (fullPath == null || !File.Exists(fullPath))
            {
                return null;
            }

            var content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return CreateDownloadFile(content, Path.GetFileName(fullPath));
        }
    }
}
