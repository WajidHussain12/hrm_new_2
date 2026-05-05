using LCS_HR_MVC.Models.Support;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class SupportService
    {
        private static SupportDownloadFile CreateDownloadFile(byte[] content, string fileName)
        {
            if (!ContentTypeProvider.TryGetContentType(fileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            return new SupportDownloadFile
            {
                Content = content,
                FileName = fileName,
                ContentType = contentType
            };
        }

        private string EnsureBackupDirectory()
        {
            var fullPath = Path.Combine(_environment.ContentRootPath, "DbBackups");
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        private string EnsureLibraryDirectory(string folderName)
        {
            var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var fullPath = Path.Combine(webRoot, "support", "downloads", folderName);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        private string GetAttendanceLogSourcePath()
        {
            var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var preferredDirectory = Path.Combine(webRoot, "support", "attendance");
            Directory.CreateDirectory(preferredDirectory);

            var preferredPath = Path.Combine(preferredDirectory, "att2000.mdb");
            var legacyPath = Path.Combine(_environment.ContentRootPath, "Support", "att2000.mdb");

            if (File.Exists(preferredPath))
            {
                return preferredPath;
            }

            if (File.Exists(legacyPath))
            {
                return legacyPath;
            }

            return preferredPath;
        }

        private static string GetLibraryFolderName(string libraryKey)
        {
            return libraryKey switch
            {
                "Docs" => "Docs",
                "Softwares" => "Softwares",
                "HR_Docs" => "HR_Docs",
                _ => throw new ArgumentException($"Unsupported library '{libraryKey}'.")
            };
        }

        private static string? GetSafePath(string rootPath, string requestedFileName)
        {
            if (string.IsNullOrWhiteSpace(requestedFileName))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(Path.Combine(rootPath, requestedFileName));
            if (!fullPath.StartsWith(Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return fullPath;
        }

        private static string NormalizeConnectionString(string connectionString, bool enableLocalInfile)
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);
            builder.SslMode = MySqlSslMode.Disabled;
            builder.AllowUserVariables = true;
            if (enableLocalInfile)
            {
                builder["AllowLoadLocalInfile"] = true;
            }

            return builder.ConnectionString;
        }

        private static string CreateBackupFileName(string databaseName, string currentUserName)
        {
            var sanitizedUser = SanitizeFileName(currentUserName);
            var timestamp = SanitizeFileName(DateTime.Now.ToString("dd-MM-yyyy HH-mm-ss"));
            return $"{databaseName}_{timestamp}_by_{sanitizedUser}.sql";
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized.Trim();
        }
    }
}
