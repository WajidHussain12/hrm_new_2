using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using OfficeOpenXml;
using OfficeOpenXml.ConditionalFormatting.Contracts;
using OfficeOpenXml.Style;

namespace LCS_HR_MVC.Utilities
{
    public sealed class SupportExcelExporter
    {
        public bool IncludeHeader { get; set; }
        public bool AddSummary { get; set; }
        public string MainSheetHeaderText { get; set; } = "Main Sheet";
        public string MainSheetName { get; set; } = "Main_Sheet";
        public bool EnableFilter { get; set; } = true;
        public int ZoomLevel { get; set; } = 100;
        public float ContentFontSize { get; set; } = 10;
        public float HeadingsFontSize { get; set; } = 13;
        public bool EnableGrouping { get; set; }
        public string GroupingColumnName { get; set; } = string.Empty;
        public string GroupingColumnPrefix { get; set; } = string.Empty;
        public string DateTimeFormate { get; set; } = "dd-MMM-yyyy HH:mm:ss";
        public bool HighlightNegatives { get; set; }
        public int FreezingColumnIndex { get; set; }

        public byte[] ExportToExcel(DataTable dataSource)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = BuildExcelPackage(dataSource);
            return package.GetAsByteArray();
        }

        public byte[] ExportToZip(DataTable dataSource)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = BuildExcelPackage(dataSource);
            using var outputStream = new MemoryStream();
            using (var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, true))
            {
                foreach (var sheet in package.Workbook.Worksheets)
                {
                    var entry = archive.CreateEntry($"{SanitizeSheetName(sheet.Name)}.xlsx", System.IO.Compression.CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    using var tempPackage = new ExcelPackage();
                    tempPackage.Workbook.Worksheets.Add(sheet.Name, sheet);
                    tempPackage.SaveAs(entryStream);
                }
            }

            return outputStream.ToArray();
        }

        private ExcelPackage BuildExcelPackage(DataTable dataSource)
        {
            if (dataSource == null || dataSource.Rows.Count == 0)
            {
                throw new ArgumentException("DataSource cannot be empty");
            }

            var decimalColumns = dataSource.Columns
                .OfType<DataColumn>()
                .Where(column => column.DataType == typeof(decimal))
                .Select(column => column.Ordinal)
                .ToArray();

            var dateColumns = dataSource.Columns
                .OfType<DataColumn>()
                .Where(column => column.DataType == typeof(DateTime))
                .Select(column => column.Ordinal)
                .ToArray();

            var package = new ExcelPackage();
            var mainSheet = package.Workbook.Worksheets.Add(SanitizeSheetName(MainSheetName));
            SetHeadingStyle(package, mainSheet, dataSource.Columns.Count);
            LoadDataTable(mainSheet, dataSource);
            PostProcessWorksheet(mainSheet, dataSource, decimalColumns, dateColumns);

            if (EnableGrouping)
            {
                if (!dataSource.Columns.Contains(GroupingColumnName))
                {
                    throw new ArgumentException($"Grouping column '{GroupingColumnName}' not found.");
                }

                var groupedRows = dataSource.AsEnumerable()
                    .GroupBy(row => (row[GroupingColumnName]?.ToString() ?? string.Empty).ToLowerInvariant());

                foreach (var group in groupedRows)
                {
                    var sheetName = SanitizeSheetName(ToTitleCase(string.IsNullOrWhiteSpace(group.Key) ? "Unknown" : group.Key));
                    var groupedSheet = package.Workbook.Worksheets.Add(sheetName);
                    SetGroupHeading(groupedSheet, dataSource.Columns.Count, group.Key);
                    LoadDataTable(groupedSheet, group.CopyToDataTable());
                    PostProcessWorksheet(groupedSheet, dataSource, decimalColumns, dateColumns);
                }
            }

            package.Workbook.Calculate();
            return package;
        }

        private void LoadDataTable(ExcelWorksheet worksheet, DataTable table)
        {
            var startRow = 1 + (IncludeHeader ? 1 : 0);
            var contentRange = worksheet.Cells[startRow, 1].LoadFromDataTable(table, true);
            worksheet.Cells[contentRange.Address].Style.Font.Size = ContentFontSize;
            worksheet.Row(startRow).Style.Font.Bold = true;
        }

        private void PostProcessWorksheet(ExcelWorksheet worksheet, DataTable sourceTable, IReadOnlyList<int> decimalColumns, IReadOnlyList<int> dateColumns)
        {
            if (AddSummary)
            {
                var lastRow = worksheet.Dimension.End.Row + 1;
                AddSumAndFormatDecimals(worksheet, decimalColumns, lastRow);
            }

            foreach (var columnIndex in dateColumns)
            {
                worksheet.Column(columnIndex + 1).Style.Numberformat.Format = DateTimeFormate;
            }

            if (FreezingColumnIndex == 0)
            {
                if (worksheet.Dimension.End.Column > 6)
                {
                    worksheet.View.FreezePanes(3, 6);
                }
                else
                {
                    worksheet.View.FreezePanes(3, 2);
                }
            }
            else
            {
                worksheet.View.FreezePanes(3, FreezingColumnIndex + 1);
            }

            worksheet.View.ZoomScale = ZoomLevel;

            var range = worksheet.Cells[worksheet.Dimension.Start.Row, worksheet.Dimension.Start.Column, worksheet.Dimension.End.Row, worksheet.Dimension.End.Column];
            range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            range.Style.Border.Top.Style = ExcelBorderStyle.Thin;

            worksheet.Cells[2, 1, 2, worksheet.Dimension.End.Column].Style.Border.BorderAround(ExcelBorderStyle.Medium, Color.Black);
            worksheet.Cells[2, 1, 2, worksheet.Dimension.End.Column].Style.Fill.PatternType = ExcelFillStyle.Solid;
            worksheet.Cells[2, 1, 2, worksheet.Dimension.End.Column].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(255, 217, 102));

            if (HighlightNegatives)
            {
                HighlightNegativeValues(worksheet);
            }

            if (EnableFilter)
            {
                var headerOffset = IncludeHeader ? 1 : 0;
                worksheet.Cells[worksheet.Dimension.Start.Row + headerOffset, worksheet.Dimension.Start.Column, worksheet.Dimension.End.Row, worksheet.Dimension.End.Column].AutoFilter = true;
            }

            worksheet.Cells.AutoFitColumns();
        }

        private void SetHeadingStyle(ExcelPackage package, ExcelWorksheet worksheet, int columnCount)
        {
            var styleName = $"HDR_{worksheet.Name}";
            var headerStyle = package.Workbook.Styles.NamedStyles.FirstOrDefault(x => x.Name == styleName)
                ?? package.Workbook.Styles.CreateNamedStyle(styleName);
            headerStyle.Style.Border.BorderAround(ExcelBorderStyle.Dashed, Color.Black);
            headerStyle.Style.Font.Size = HeadingsFontSize;
            headerStyle.Style.Font.Bold = true;
            headerStyle.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            var headerCell = worksheet.Cells[1, 1];
            headerCell.Value = MainSheetHeaderText;
            worksheet.Cells[1, 1, 1, columnCount].Merge = true;
            worksheet.Cells[1, 1, 1, columnCount].StyleName = styleName;
        }

        private void SetGroupHeading(ExcelWorksheet worksheet, int columnCount, string groupKey)
        {
            var headerCell = worksheet.Cells[1, 1];
            headerCell.Value = $"{GroupingColumnPrefix} {ToTitleCase(groupKey)}".Trim();
            worksheet.Cells[1, 1, 1, columnCount].Merge = true;
            worksheet.Cells[1, 1, 1, columnCount].Style.Font.Size = HeadingsFontSize;
            worksheet.Cells[1, 1, 1, columnCount].Style.Font.Bold = true;
            worksheet.Cells[1, 1, 1, columnCount].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        private static void HighlightNegativeValues(ExcelWorksheet worksheet)
        {
            IExcelConditionalFormattingLessThan condition = worksheet.ConditionalFormatting.AddLessThan(new ExcelAddress(worksheet.Dimension.Address));
            condition.Style.Fill.PatternType = ExcelFillStyle.Solid;
            condition.Style.Fill.BackgroundColor.Color = Color.Red;
            condition.Formula = "0";
        }

        private static void AddSumAndFormatDecimals(ExcelWorksheet worksheet, IReadOnlyList<int> decimalColumns, int rowIndex)
        {
            worksheet.InsertRow(rowIndex, 1);
            worksheet.Cells[rowIndex, 1, rowIndex, worksheet.Dimension.Columns].Style.Fill.PatternType = ExcelFillStyle.LightUp;
            worksheet.Cells[rowIndex, 1, rowIndex, worksheet.Dimension.Columns].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(192, 224, 180));
            worksheet.Cells[rowIndex, 1].Value = "Total";
            worksheet.Cells[rowIndex, 1, rowIndex, worksheet.Dimension.Columns].Style.Font.Bold = true;
            worksheet.Cells[rowIndex, 1, rowIndex, worksheet.Dimension.Columns].Style.Font.UnderLine = true;

            foreach (var columnIndex in decimalColumns)
            {
                var excelColumn = columnIndex + 1;
                worksheet.Cells[rowIndex, excelColumn].Formula = $"Sum({worksheet.Cells[3, excelColumn].Address}:{worksheet.Cells[rowIndex - 1, excelColumn].Address})";
                worksheet.Cells[rowIndex, excelColumn].Style.Font.Bold = true;
                worksheet.Column(excelColumn).Style.Numberformat.Format = "#,##0";
            }
        }

        private static string SanitizeSheetName(string? value)
        {
            var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            var sanitized = new string((value ?? "Sheet")
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "Sheet";
            }

            return sanitized.Length > 31 ? sanitized[..31] : sanitized;
        }

        private static string ToTitleCase(string value)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value);
        }
    }
}
