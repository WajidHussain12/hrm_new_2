using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using LCS_HR_MVC.Models.Support;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class SupportService
    {
        private static void ValidateResultSetQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Query is required.");
            }

            var lowerQuery = query.ToLowerInvariant();
            if (BlockedSqlKeywords.Any(lowerQuery.Contains))
            {
                throw new ArgumentException("Update and Delete Query not supported.");
            }
        }

        private async Task<DataTable> ExecuteResultSetQueryAsync(ResultSetExporterViewModel model, CancellationToken cancellationToken)
        {
            var connectionString = model.UseCustomConnectionString
                ? model.ConnectionString.Trim()
                : _connectionFactory.ConnectionString;

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = model.Query.Trim();
            command.CommandType = CommandType.Text;
            command.CommandTimeout = model.ConnectionTime;

            using var adapter = new MySqlDataAdapter(command);
            var table = new DataTable();
            adapter.Fill(table);
            return table;
        }

        private static DataView ApplyLegacyFilter(DataTable source, string? selectedColumn, string? searchText, string? allValueText, string? customExpressionLabel, ref string warningMessage)
        {
            var dataView = source.DefaultView;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return dataView;
            }

            var column = string.IsNullOrWhiteSpace(selectedColumn) ? allValueText ?? customExpressionLabel ?? string.Empty : selectedColumn.Trim();
            if (!string.IsNullOrWhiteSpace(allValueText) && string.Equals(column, allValueText, StringComparison.OrdinalIgnoreCase))
            {
                return dataView;
            }

            if (!string.IsNullOrWhiteSpace(customExpressionLabel)
                && string.Equals(column, customExpressionLabel, StringComparison.OrdinalIgnoreCase))
            {
                dataView.RowFilter = searchText.Trim();
            }
            else
            {
                var dataColumn = dataView.Table.Columns[column];
                if (dataColumn.DataType != typeof(string))
                {
                    dataView.RowFilter = $"CONVERT({dataColumn.ColumnName}, System.String) LIKE '%{searchText.Trim()}%'";
                }
                else
                {
                    dataView.RowFilter = $"{dataColumn.ColumnName} like '%{searchText.Trim()}%'";
                }
            }

            if (dataView.Count == 0)
            {
                warningMessage = "Record not found.";
                return source.DefaultView;
            }

            return dataView;
        }

        private static IReadOnlyList<SelectListItem> BuildSearchColumns(DataTable table, string defaultLabel)
        {
            var items = new List<SelectListItem>();
            if (!string.IsNullOrWhiteSpace(defaultLabel))
            {
                items.Add(new SelectListItem(defaultLabel, defaultLabel));
            }

            items.AddRange(table.Columns
                .OfType<DataColumn>()
                .Select(column => new SelectListItem(column.ColumnName, column.ColumnName)));

            return items;
        }

        private static DataTable GetErrorLogsTable(MySqlConnection connection, string userId = "")
        {
            var query = @"
SELECT ge.ErrorID,
       DATE_FORMAT(ge.LogDateTime,'%d/%m/%Y %h:%i:%s %p') LogDateTime,
       ge.Message,
       ge.UserIP,
       ge.UserName,
       ge.UserLocation
FROM gl_errorlog ge
WHERE 1=1";

            using var command = connection.CreateCommand();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query += " AND ge.UserName = @userid";
                command.Parameters.AddWithValue("@userid", userId);
            }

            query += " ORDER BY ge.ErrorID DESC";
            command.CommandText = query;

            using var adapter = new MySqlDataAdapter(command);
            var table = new DataTable();
            adapter.Fill(table);
            return table;
        }

        private static IReadOnlyList<SelectListItem> GetErrorLogUsers(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT lu.Name userID, lu.username FROM lcs_users lu;";
            using var adapter = new MySqlDataAdapter(command);
            var users = new DataTable();
            adapter.Fill(users);

            var items = new List<SelectListItem>
            {
                new("All Users", "00")
            };

            items.AddRange(users.AsEnumerable().Select(row => new SelectListItem(
                row["username"]?.ToString() ?? string.Empty,
                row["userID"]?.ToString() ?? string.Empty)));

            return items;
        }

        private static ErrorLogDetailViewModel? GetErrorLogDetail(MySqlConnection connection, string errorId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT eLogs.LogDateTime,
       eLogs.Source,
       eLogs.Message,
       eLogs.TargetSite,
       eLogs.StackTrace,
       eLogs.RequestURL,
       eLogs.UserName
FROM gl_errorlog eLogs
WHERE eLogs.ErrorID = @ErrorID";
            command.Parameters.AddWithValue("@ErrorID", errorId);

            using var adapter = new MySqlDataAdapter(command);
            var result = new DataTable();
            adapter.Fill(result);

            if (result.Rows.Count != 1)
            {
                return null;
            }

            var row = result.Rows[0];
            var detailText = new StringBuilder()
                .Append("UserName :").Append(row["UserName"]).AppendLine()
                .Append("LogDateTime :").Append(row["LogDateTime"]).AppendLine()
                .Append("Source :").Append(row["Source"]).AppendLine()
                .Append("Message :").Append(row["Message"]).AppendLine()
                .Append("TargetSite :").Append(row["TargetSite"]).AppendLine()
                .Append("StackTrace :").Append(row["StackTrace"]).AppendLine()
                .Append("RequestURL :").Append(row["RequestURL"]).AppendLine()
                .ToString();

            return new ErrorLogDetailViewModel
            {
                ErrorID = errorId,
                DetailText = detailText
            };
        }

        private static DataTable GetAuditTrailTable(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT a.TableName,
       a.Date,
       a.OptType,
       a.TblPk,
       a.UserID,
       a.UserName
FROM audittrail a
ORDER BY date DESC";

            using var adapter = new MySqlDataAdapter(command);
            var table = new DataTable();
            adapter.Fill(table);
            return table;
        }

        private static AuditTrailDetailViewModel? GetAuditTrailDetail(MySqlConnection connection, AuditViewerQueryModel query)
        {
            if (!DateTime.TryParse(query.AuditDate, out var auditDate))
            {
                return null;
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT audit.`Values`
FROM audittrail audit
WHERE audit.TableName=@TableName
  AND audit.Date=@Date
  AND audit.OptType=@OptType
  AND audit.UserID=@UserID
  AND audit.TblPk=@TblPk";
            command.Parameters.AddWithValue("@TableName", query.TableName);
            command.Parameters.AddWithValue("@Date", auditDate);
            command.Parameters.AddWithValue("@OptType", query.OptType?.FirstOrDefault().ToString());
            command.Parameters.AddWithValue("@UserID", query.UserID);
            command.Parameters.AddWithValue("@TblPk", query.TblPk);

            var result = command.ExecuteScalar()?.ToString();
            if (string.IsNullOrWhiteSpace(result))
            {
                return null;
            }

            var detailTable = GetTableSchema(connection, query.TableName!, false);
            var constrainedTable = GetTableSchema(connection, query.TableName!, true);
            PopulateAuditDetailRows(detailTable, result);

            var rows = detailTable.AsEnumerable()
                .Select(row => detailTable.Columns
                    .OfType<DataColumn>()
                    .ToDictionary(column => column.ColumnName, column => FormatAuditValue(row[column])))
                .Cast<IReadOnlyDictionary<string, string>>()
                .ToList();

            var footerTotals = BuildAuditFooter(detailTable);
            var generatedSql = BuildAuditQuery(result, query.TableName!, query.OptType ?? string.Empty, constrainedTable);

            return new AuditTrailDetailViewModel
            {
                TableName = query.TableName!,
                Columns = detailTable.Columns.OfType<DataColumn>().Select(column => column.ColumnName).ToList(),
                Rows = rows,
                FooterTotals = footerTotals,
                GeneratedSql = generatedSql
            };
        }

        private static DataTable GetTableSchema(MySqlConnection connection, string tableName, bool includeConstraints)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {tableName} WHERE 1=0";
            using var adapter = new MySqlDataAdapter(command);
            var table = new DataTable(tableName);
            if (includeConstraints)
            {
                adapter.FillSchema(table, SchemaType.Source);
            }
            else
            {
                adapter.Fill(table);
            }

            return table;
        }

        private static void PopulateAuditDetailRows(DataTable table, string valuePayload)
        {
            var rowSegments = valuePayload.Split(new[] { "####" }, StringSplitOptions.RemoveEmptyEntries);
            var trimChars = "'".ToCharArray();

            foreach (var rowSegment in rowSegments)
            {
                var newRow = table.NewRow();
                foreach (var item in rowSegment.Split(new[] { "$$" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = item.Split('=', 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    var key = parts[0].Replace("[", string.Empty).Replace("]", string.Empty);
                    var value = parts[1].TrimStart(trimChars).TrimEnd(trimChars);

                    if (!table.Columns.Contains(key))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        newRow[key] = DBNull.Value;
                    }
                    else if (table.Columns[key].DataType == typeof(DateTime))
                    {
                        newRow[key] = DateTime.Parse(value, CultureInfo.InvariantCulture);
                    }
                    else if (table.Columns[key].DataType == typeof(TimeSpan))
                    {
                        newRow[key] = TimeSpan.Parse(value, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        newRow[key] = value;
                    }
                }

                table.Rows.Add(newRow);
            }
        }

        private static IReadOnlyDictionary<string, string> BuildAuditFooter(DataTable table)
        {
            var footer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (table.Columns.Count == 0)
            {
                return footer;
            }

            footer[table.Columns[0].ColumnName] = $"{table.Rows.Count} Row(s)";
            foreach (DataColumn column in table.Columns)
            {
                if (column.DataType == typeof(decimal) || column.DataType == typeof(int))
                {
                    var sum = table.Compute($"SUM({column.ColumnName})", null);
                    if (sum != DBNull.Value)
                    {
                        footer[column.ColumnName] = Convert.ToDecimal(sum, CultureInfo.InvariantCulture).ToString("#,##0.##", CultureInfo.InvariantCulture);
                    }
                }
            }

            return footer;
        }

        private static string FormatAuditValue(object? value)
        {
            if (value == null || value == DBNull.Value)
            {
                return string.Empty;
            }

            return value switch
            {
                DateTime dateTime => dateTime.ToString("dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                TimeSpan timeSpan => timeSpan.ToString(),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static string BuildAuditQuery(string payload, string tableName, string optType, DataTable constrainedTable)
        {
            if (string.Equals(optType, "D", StringComparison.OrdinalIgnoreCase))
            {
                return $"INSERT INTO {tableName} VALUES {GetSplittedValuesForDelete(payload)};";
            }

            var values = GetSplittedValuesForUpdate(payload, constrainedTable).Replace("[", string.Empty).Replace("]", string.Empty);
            var builder = new StringBuilder();
            foreach (var statement in values.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Append("UPDATE ").Append(tableName).Append(" SET ").Append(statement).AppendLine(";");
            }

            return builder.ToString().TrimEnd();
        }

        private static string GetSplittedValuesForUpdate(string payload, DataTable table)
        {
            var fragments = new List<string>();
            var rows = payload.Split(new[] { "####" }, StringSplitOptions.RemoveEmptyEntries);
            var primaryKeys = table.PrimaryKey.Select(column => column.ColumnName.ToLowerInvariant()).ToHashSet();

            foreach (var row in rows)
            {
                var assignments = new List<string>();
                var whereClauses = new List<string>();

                foreach (var item in row.Split(new[] { "$$" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = item.Split('=', 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    assignments.Add($" {parts[0]}={parts[1]}");
                    var normalizedKey = parts[0].ToLowerInvariant().Replace("[", string.Empty).Replace("]", string.Empty);
                    if (primaryKeys.Contains(normalizedKey))
                    {
                        whereClauses.Add($" {parts[0]}={parts[1]} ");
                    }
                }

                fragments.Add(string.Join(",", assignments));
                if (whereClauses.Count > 0)
                {
                    fragments.Add(" WHERE ");
                    fragments.Add(string.Join(" and ", whereClauses));
                }

                fragments.Add(";");
            }

            return string.Concat(fragments);
        }

        private static string GetSplittedValuesForDelete(string payload)
        {
            var rows = payload.Split(new[] { "####" }, StringSplitOptions.RemoveEmptyEntries);
            var fragments = new List<string>();

            foreach (var row in rows)
            {
                var columns = row.Split(new[] { "$$" }, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length == 1)
                {
                    fragments.Add(columns[0].Split('=', 2)[1]);
                }
                else
                {
                    var values = columns.Select(column => column.Split('=', 2)[1]).ToArray();
                    fragments.Add($"\n ({string.Join(",", values)})");
                }
            }

            return string.Join(",", fragments);
        }

        private static string GetOperationText(string? value)
        {
            return value switch
            {
                "D" => "Delete",
                "U" => "Update",
                _ => value ?? string.Empty
            };
        }
    }
}
