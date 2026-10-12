using ClosedXML.Excel;
using Magicodes.ExporterAndImporter.Core;
using Magicodes.ExporterAndImporter.Core.Filters;
using Magicodes.ExporterAndImporter.Core.Models;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

namespace ZhonTai.Admin.Core.Helpers;

/// <summary>Excel 文件适配：保留现有列注解、导入结果与业务错误回调。</summary>
public static class ExcelWorkbook
{
    public static byte[] GenerateTemplate<T>() where T : class, new()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("导入模板");
        var headers = ImportHeaders<T>();
        var headerRow = typeof(T).GetCustomAttribute<ImporterAttribute>()?.HeaderRowIndex ?? 1;
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            var cell = sheet.Cell(headerRow, index + 1);
            cell.Value = header.Header.Name;
            cell.Style.Font.Bold = true;
            if (header.IsRequired) cell.Style.Font.FontColor = XLColor.Red;
            if (header.MappingValues?.Count > 0)
                sheet.Range(headerRow + 1, index + 1, 10001, index + 1).CreateDataValidation()
                    .List("\"" + string.Join(",", header.MappingValues.Keys) + "\"", true);
        }
        sheet.Columns().AdjustToContents();
        return Save(workbook);
    }

    public static byte[] Export<T>(ICollection<T> rows, string sheetName = null) where T : class, new()
    {
        using var workbook = new XLWorkbook();
        var options = typeof(T).GetCustomAttribute<ExporterAttribute>() ?? new ExporterAttribute();
        var sheet = workbook.AddWorksheet(!string.IsNullOrEmpty(sheetName) ? sheetName : !string.IsNullOrEmpty(options.Name) ? options.Name : "数据列表");
        var properties = typeof(T).GetProperties().Where(property => property.CanRead)
            .Select(property => (Property: property, Header: property.GetCustomAttribute<ExporterHeaderAttribute>()))
            .Where(column => column.Header?.IsIgnore != true)
            .OrderBy(column => column.Header?.ColumnIndex > 0 ? column.Header.ColumnIndex : int.MaxValue).ToList();
        for (var index = 0; index < properties.Count; index++)
        {
            var (property, header) = properties[index];
            sheet.Cell(1, index + 1).Value = header?.DisplayName ?? property.Name;
            var column = sheet.Column(index + 1);
            if (header?.Width > 0) column.Width = header.Width;
            if (!string.IsNullOrEmpty(header?.Format)) column.Style.NumberFormat.Format = header.Format;
            if (header?.Hidden == true) column.Hide();
            var rowIndex = 2;
            foreach (var row in rows)
            {
                var value = property.GetValue(row);
                var image = property.GetCustomAttribute<ExportImageFieldAttribute>();
                if (image != null && value is string data && data.StartsWith("data:image/", StringComparison.Ordinal))
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]));
                    sheet.AddPicture(stream).MoveTo(sheet.Cell(rowIndex, index + 1)).WithSize(image.Width, image.Height);
                }
                else
                    // Setting a string as Value keeps user text (including '=...') out of formulas.
                    sheet.Cell(rowIndex, index + 1).Value = XLCellValue.FromObject(value, CultureInfo.InvariantCulture);
                rowIndex++;
            }
        }
        if (properties.Count > 0)
        {
            var table = sheet.Range(1, 1, Math.Max(2, rows.Count + 1), properties.Count).CreateTable();
            table.Theme = XLTableTheme.TableStyleLight9;
            if (options.AutoFitAllColumn)
                sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, options.AutoFitMaxRows > 0 ? options.AutoFitMaxRows : rows.Count + 1));
        }
        return Save(workbook);
    }

    public static ImportResult<T> Import<T>(Stream input) where T : class, new()
    {
        var result = new ImportResult<T> { Data = new List<T>(), RowErrors = new List<DataRowErrorInfo>(), TemplateErrors = new List<TemplateErrorInfo>() };
        try
        {
            using var workbook = new XLWorkbook(input);
            var sheet = workbook.Worksheet(1);
            var options = typeof(T).GetCustomAttribute<ImporterAttribute>() ?? new ImporterAttribute();
            var headers = ImportHeaders<T>();
            result.ImporterHeaderInfos = headers;
            var names = sheet.Row(options.HeaderRowIndex).CellsUsed().ToList();
            var comparison = options.IsIgnoreColumnCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var columns = headers.Select(header => (Header: header, Cell: names.FirstOrDefault(cell =>
                string.Equals(cell.GetString().Trim(), header.Header.Name, comparison)))).ToList();
            foreach (var column in columns.Where(column => column.Cell == null))
                result.TemplateErrors.Add(new TemplateErrorInfo { RequireColumnName = column.Header.Header.Name, ErrorLevel = ErrorLevels.Error });
            if (result.TemplateErrors.Count > 0) return result;
            var seen = headers.ToDictionary(header => header.PropertyName, _ => new Dictionary<string, int>());
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? options.HeaderRowIndex;
            for (var rowIndex = options.HeaderRowIndex + 1; rowIndex <= lastRow; rowIndex++)
            {
                if (columns.All(column => sheet.Cell(rowIndex, column.Cell.Address.ColumnNumber).IsEmpty())) continue;
                var item = new T();
                var errors = new DataRowErrorInfo { RowIndex = rowIndex };
                foreach (var (header, cell) in columns)
                {
                    var inputCell = sheet.Cell(rowIndex, cell.Address.ColumnNumber);
                    var text = inputCell.GetString();
                    if (header.Header.AutoTrim) text = text.Trim();
                    if (header.Header.FixAllSpace) text = text.Replace(" ", "");
                    try
                    {
                        var type = Nullable.GetUnderlyingType(header.PropertyInfo.PropertyType) ?? header.PropertyInfo.PropertyType;
                        object value = text.Length == 0 && type != typeof(string) ?
                            (Nullable.GetUnderlyingType(header.PropertyInfo.PropertyType) != null ? null : Activator.CreateInstance(type)) :
                            header.MappingValues?.TryGetValue(text, out var mapped) == true ? mapped :
                            type == typeof(string) ? text : type == typeof(DateTime) && inputCell.DataType == XLDataType.DateTime ? inputCell.GetDateTime() :
                            type.IsEnum ? Enum.Parse(type, text) : Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                        header.PropertyInfo.SetValue(item, value);
                        var validation = new List<ValidationResult>();
                        if (!Validator.TryValidateProperty(value, new ValidationContext(item) { MemberName = header.PropertyName }, validation))
                            errors.FieldErrors[header.Header.Name] = string.Join("；", validation.Select(error => error.ErrorMessage));
                        if (!header.Header.IsAllowRepeat && text.Length > 0)
                        {
                            if (seen[header.PropertyName].TryGetValue(text, out var firstRow))
                                errors.FieldErrors[header.Header.Name] = $"与第{firstRow}行重复";
                            else seen[header.PropertyName].Add(text, rowIndex);
                        }
                    }
                    catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
                    {
                        errors.FieldErrors[header.Header.Name] = "格式不正确";
                    }
                }
                result.Data.Add(item);
                if (errors.FieldErrors.Count > 0) result.RowErrors.Add(errors);
            }
        }
        catch (Exception exception)
        {
            result.Exception = exception;
        }
        return result;
    }

    public static byte[] MarkErrors(Stream input, IEnumerable<DataRowErrorInfo> errors)
    {
        using var workbook = new XLWorkbook(input);
        var sheet = workbook.Worksheet(1);
        foreach (var error in errors)
        {
            foreach (var field in error.FieldErrors)
            {
                var header = sheet.Row(1).CellsUsed().FirstOrDefault(cell => cell.GetString() == field.Key);
                var cell = sheet.Cell(error.RowIndex, header?.Address.ColumnNumber ?? 1);
                cell.Style.Fill.BackgroundColor = XLColor.LightPink;
                cell.GetComment().AddText(field.Key + "：" + field.Value);
            }
        }
        return Save(workbook);
    }

    private static List<ImporterHeaderInfo> ImportHeaders<T>()
    {
        var headers = typeof(T).GetProperties().Where(property => property.CanWrite)
            .Select(property => new ImporterHeaderInfo
            {
                PropertyName = property.Name,
                PropertyInfo = property,
                Header = property.GetCustomAttribute<ImporterHeaderAttribute>() ?? new ImporterHeaderAttribute { Name = property.Name },
                IsRequired = property.IsDefined(typeof(RequiredAttribute))
            }).Where(header => !header.Header.IsIgnore).ToList();
        var filter = typeof(T).GetCustomAttribute<ImporterAttribute>()?.ImportHeaderFilter;
        if (filter != null) headers = ((IImportHeaderFilter)Activator.CreateInstance(filter)).Filter(headers);
        return headers;
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
