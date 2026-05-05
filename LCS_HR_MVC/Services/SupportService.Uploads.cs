using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using LCS_HR_MVC.Models.Support;
using MySql.Data.MySqlClient;

namespace LCS_HR_MVC.Services
{
    public partial class SupportService
    {
        private static void RemoveEmptyRows(DataSet sourceDataTables)
        {
            foreach (DataTable dataTable in sourceDataTables.Tables)
            {
                var rowsToDelete = new List<DataRow>();
                foreach (DataRow row in dataTable.Rows)
                {
                    var nullFlags = new List<bool>();
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        if (row.IsNull(column))
                        {
                            nullFlags.Add(true);
                        }
                    }

                    if (nullFlags.Count == dataTable.Columns.Count && nullFlags.All(flag => flag))
                    {
                        rowsToDelete.Add(row);
                    }
                }

                foreach (var row in rowsToDelete)
                {
                    dataTable.Rows.Remove(row);
                }
            }
        }

        private static DataTable ValidateDataSource(DataSet excelDataSet, MySqlConnection connection)
        {
            DataTable? dbTable = null;
            DataTable? schemaTable = null;

            foreach (DataTable excelTable in excelDataSet.Tables)
            {
                var sqlQuery = $"select * from {excelTable.TableName.Replace(" ", string.Empty)} where 1=0";
                using var command = connection.CreateCommand();
                command.CommandText = sqlQuery;
                dbTable = new DataTable(excelTable.TableName.Replace(" ", string.Empty));
                using var adapter = new MySqlDataAdapter(command);

                try
                {
                    adapter.Fill(dbTable);
                    schemaTable = new DataTable();
                    schemaTable.Load(dbTable.CreateDataReader());
                }
                catch (Exception ex) when (ex.HResult == -2147467259)
                {
                    throw new ArgumentException($"Table \"{excelTable.TableName}\" does not exist in database. Sheets in excel file should have as same as tables in database.");
                }
            }

            if (dbTable == null || schemaTable == null || excelDataSet.Tables.Count == 0)
            {
                throw new ArgumentException("Error reading file.Please check file format");
            }

            var clonedTable = excelDataSet.Tables[0].Clone();
            var tempClonedData = excelDataSet.Tables[0].Copy();

            for (var columnIndex = 0; columnIndex < dbTable.Columns.Count; columnIndex++)
            {
                if (!dbTable.Columns[columnIndex].AllowDBNull)
                {
                    clonedTable.Columns[columnIndex].DataType = dbTable.Columns[columnIndex].DataType;
                }
            }

            foreach (DataRow row in tempClonedData.Rows)
            {
                clonedTable.ImportRow(row);
            }

            excelDataSet.Tables[0].Rows.Clear();
            excelDataSet.Tables.Clear();
            excelDataSet.Tables.Add(clonedTable);

            return schemaTable;
        }

        private static DataSet GetDataTableFromExcel(byte[] excelStream, bool isOpenXml)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var stream = new MemoryStream(excelStream);
            using var reader = isOpenXml
                ? ExcelReaderFactory.CreateOpenXmlReader(stream)
                : ExcelReaderFactory.CreateBinaryReader(stream);

            try
            {
                return reader.AsDataSet(new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration
                    {
                        UseHeaderRow = true
                    }
                });
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Error reading file.Please check file format", ex);
            }
        }

        private static void FillDataTablesPrimaryKeys(DataSet excelDataSet, MySqlConnection connection)
        {
            var schemaName = connection.Database;
            foreach (DataTable dataTable in excelDataSet.Tables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COLUMN_NAME, CHARACTER_MAXIMUM_LENGTH
FROM information_schema.COLUMNS
WHERE TABLE_NAME = @TableName
  AND TABLE_SCHEMA = @SchemaName
  AND COLUMN_KEY = 'PRI'";
                command.Parameters.AddWithValue("@TableName", dataTable.TableName);
                command.Parameters.AddWithValue("@SchemaName", schemaName);

                using var adapter = new MySqlDataAdapter(command);
                var resultPrimaryKeys = new DataTable();
                adapter.Fill(resultPrimaryKeys);

                if (resultPrimaryKeys.Rows.Count == 0)
                {
                    throw new ArgumentException($"Table \"{dataTable.TableName}\" does not have a primary key defined");
                }

                if (resultPrimaryKeys.Rows.Count > 1)
                {
                    throw new ArgumentException($"Table \"{dataTable.TableName}\" have more than one primary key defined");
                }

                var primaryKey = resultPrimaryKeys.Rows[0]["COLUMN_NAME"]?.ToString() ?? string.Empty;
                var primaryKeyLength = Convert.ToUInt64(resultPrimaryKeys.Rows[0]["CHARACTER_MAXIMUM_LENGTH"], CultureInfo.InvariantCulture);
                var nextId = Convert.ToUInt64(GetNextId(connection, dataTable.TableName, primaryKey), CultureInfo.InvariantCulture);

                for (var rowIndex = 0; rowIndex < dataTable.Rows.Count; rowIndex++)
                {
                    dataTable.Rows[rowIndex][primaryKey] = CreateId(nextId++, primaryKeyLength);
                }
            }
        }

        private static string GetNextId(MySqlConnection connection, string tableName, string columnName)
        {
            using var lengthCommand = connection.CreateCommand();
            lengthCommand.CommandText = @"
SELECT CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @TableName
  AND TABLE_SCHEMA = @SchemaName
  AND COLUMN_NAME = @ColumnName";
            lengthCommand.Parameters.AddWithValue("@TableName", tableName);
            lengthCommand.Parameters.AddWithValue("@SchemaName", connection.Database);
            lengthCommand.Parameters.AddWithValue("@ColumnName", columnName);
            var lengthResult = lengthCommand.ExecuteScalar();
            var columnLength = lengthResult == null || lengthResult == DBNull.Value
                ? 0
                : Convert.ToInt32(lengthResult, CultureInfo.InvariantCulture);

            using var idCommand = connection.CreateCommand();
            idCommand.CommandText = $"SELECT IFNULL(MAX(CAST({columnName} AS UNSIGNED)),0)+1 ID FROM {tableName}";
            var meterId = idCommand.ExecuteScalar();
            if (meterId == null || meterId == DBNull.Value)
            {
                return "001";
            }

            var localInt = Convert.ToUInt64(meterId, CultureInfo.InvariantCulture);
            var length = columnLength - Convert.ToString(localInt, CultureInfo.InvariantCulture)!.Length;
            return new string('0', Math.Max(0, length)) + localInt.ToString(CultureInfo.InvariantCulture);
        }

        private static string CreateId(ulong primaryKey, ulong keyLength)
        {
            var length = Convert.ToInt32(keyLength, CultureInfo.InvariantCulture) - Convert.ToString(primaryKey, CultureInfo.InvariantCulture)!.Length;
            return new string('0', Math.Max(0, length)) + primaryKey.ToString(CultureInfo.InvariantCulture);
        }

        private static void FillAuditColumns(DataSet excelDataSet, string currentUserId)
        {
            var currentTime = DateTime.Now;
            foreach (DataTable table in excelDataSet.Tables)
            {
                var isCreatedBy = table.Columns.Contains("CreatedBy");
                var isCreatedDate = table.Columns.Contains("Created_Date");
                var isUpdatedBy = table.Columns.Contains("UpdatedBy");
                var isUpdatedDate = table.Columns.Contains("Updated_Date");

                foreach (DataRow row in table.Rows)
                {
                    if (isCreatedBy)
                    {
                        row["CreatedBy"] = currentUserId;
                    }

                    if (isCreatedDate)
                    {
                        row["Created_Date"] = currentTime;
                    }

                    if (isUpdatedBy)
                    {
                        row["UpdatedBy"] = currentUserId;
                    }

                    if (isUpdatedDate)
                    {
                        row["Updated_Date"] = currentTime;
                    }
                }
            }
        }

        private int BulkLoadDataSet(DataSet excelDataSet, MySqlConnection connection, DataTable dbDataTable)
        {
            var csvFiles = new List<string>();
            var result = 0;

            try
            {
                foreach (DataTable dataTable in excelDataSet.Tables)
                {
                    var csvFileName = ConvertDataTableToCsv(dataTable, dbDataTable);
                    csvFiles.Add(csvFileName);

                    var bulkLoader = new MySqlBulkLoader(connection)
                    {
                        TableName = dataTable.TableName,
                        FieldTerminator = "\t",
                        LineTerminator = "\n",
                        FileName = csvFileName,
                        NumberOfLinesToSkip = 0,
                        ConflictOption = MySqlBulkLoaderConflictOption.Replace,
                        Local = true
                    };

                    result += bulkLoader.Load();
                }
            }
            finally
            {
                foreach (var fileName in csvFiles)
                {
                    try
                    {
                        File.Delete(fileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Unable to delete temporary upload file {FileName}", fileName);
                    }
                }
            }

            return result;
        }

        private static string ConvertDataTableToCsv(DataTable dataTable, DataTable dbTable, string fieldTerminator = "\t", string lineTerminator = "\n")
        {
            var builder = new StringBuilder();

            foreach (DataRow row in dataTable.Rows)
            {
                var fields = new List<string>();
                for (var columnIndex = 0; columnIndex < row.ItemArray.Length; columnIndex++)
                {
                    var field = row.ItemArray[columnIndex];
                    var columnType = dbTable.Columns[columnIndex].DataType;

                    if (columnType == typeof(DateTime))
                    {
                        if (field == DBNull.Value || field == null || string.Equals(field.ToString(), @"\N", StringComparison.Ordinal))
                        {
                            fields.Add(@"\N");
                            continue;
                        }

                        if (DateTime.TryParse(field.ToString(), out var dateTime))
                        {
                            fields.Add(dateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture));
                            continue;
                        }

                        throw new ArgumentException($"Couldn't convert column '{dbTable.Columns[columnIndex].ColumnName}' .Invalid data in row ;\n{string.Join("\t", row.ItemArray)}");
                    }

                    if (field == DBNull.Value)
                    {
                        fields.Add(@"\N");
                    }
                    else
                    {
                        fields.Add(field?.ToString() ?? string.Empty);
                    }
                }

                builder.Append(string.Join(fieldTerminator, fields));
                builder.Append(lineTerminator);
            }

            var filePath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(Path.GetRandomFileName())}.csv");
            File.WriteAllText(filePath, builder.ToString());
            return filePath;
        }
    }
}
