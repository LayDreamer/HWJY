using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Magicodes.ExporterAndImporter.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ZhonTai.Admin.Core.Helpers;
using ZhonTai.Admin.Services.Dict.Dto;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class DependencySecurityCompatibilityTests
{
    [Fact]
    public void DictionaryTemplateAndImportPreserveHeadersAndChineseBooleanMapping()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ExcelWorkbook.GenerateTemplate<DictImport>()));
        var sheet = workbook.Worksheet(1);
        Assert.Equal(new[] { "字典分类", "字典名称", "上级字典名称", "字典编码", "字典值", "启用", "排序", "说明" },
            sheet.Row(1).CellsUsed().Select(cell => cell.GetString()));
        Assert.Contains(sheet.DataValidations, validation => validation.MinValue.Contains("是,否", StringComparison.Ordinal));
        SetRow(sheet, 2, "类型", "名称", "", "code", "value", "是", "10", "说明");
        using var input = Save(workbook);
        var imported = ExcelWorkbook.Import<DictImport>(input);
        Assert.False(imported.HasError, imported.Exception?.ToString());
        var item = Assert.Single(imported.Data);
        Assert.True(item.Enabled);
        Assert.Equal(10, item.Sort);
        Assert.Equal("名称", item.Name);
        Assert.Equal(0, item.Id);
    }

    [Fact]
    public void ImportValidationAndErrorWorkbookPreserveRowNumbersAndSourceValues()
    {
        using var workbook = new XLWorkbook(new MemoryStream(ExcelWorkbook.GenerateTemplate<DictImport>()));
        var sheet = workbook.Worksheet(1);
        SetRow(sheet, 2, "类型", "名称", "", "code", "value", "是", "1", "说明");
        SetRow(sheet, 3, "", "名称", "", "code", "value", "错误", "错误", "保留文本");
        using var input = Save(workbook);
        var imported = ExcelWorkbook.Import<DictImport>(input);
        Assert.True(imported.HasError);
        var error = Assert.Single(imported.RowErrors);
        Assert.Equal(3, error.RowIndex);
        foreach (var name in new[] { "字典分类", "字典名称", "字典编码", "字典值", "启用", "排序" })
            Assert.Contains(name, error.FieldErrors.Keys);
        input.Position = 0;
        using var marked = new XLWorkbook(new MemoryStream(ExcelWorkbook.MarkErrors(input, imported.RowErrors)));
        Assert.Equal("保留文本", marked.Worksheet(1).Cell(3, 8).GetString());
        Assert.Contains("重复", marked.Worksheet(1).Cell(3, 2).GetComment().Text);
        Assert.False(marked.Worksheet(1).Cell(2, 2).HasComment);
    }

    [Fact]
    public void MissingHeadersAndInvalidWorkbookAreReportedThroughExistingImportResult()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Sheet1").Cell(1, 1).Value = "字典分类";
        using var input = Save(workbook);
        var imported = ExcelWorkbook.Import<DictImport>(input);
        Assert.True(imported.HasError);
        Assert.Contains(imported.TemplateErrors, error => error.RequireColumnName == "字典名称");
        Assert.Empty(imported.Data);
        using var invalid = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.NotNull(ExcelWorkbook.Import<DictImport>(invalid).Exception);
    }

    [Fact]
    public void DictionaryExportPreservesColumnOrderStylesFormatsAndLiteralText()
    {
        var time = new DateTime(2026, 10, 12, 8, 30, 0);
        var bytes = ExcelWorkbook.Export(new[] { new DictExport { Id = 123, ParentId = 456, Name = "=1+1", Enabled = true, CreatedTime = time } });
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("字典列表");
        Assert.Equal("字典编号", sheet.Cell(1, 1).GetString());
        Assert.Equal("上级字典编号", sheet.Cell(1, 2).GetString());
        Assert.Equal("字典名称", sheet.Cell(1, 4).GetString());
        Assert.Equal("修改时间", sheet.Cell(1, 14).GetString());
        Assert.Equal("=1+1", sheet.Cell(2, 4).GetString());
        Assert.False(sheet.Cell(2, 4).HasFormula);
        Assert.Equal(time, sheet.Cell(2, 12).GetDateTime());
        Assert.Equal("yyyy-MM-dd HH:mm:ss", sheet.Cell(2, 12).Style.NumberFormat.Format);
        Assert.Equal(XLTableTheme.TableStyleLight9, Assert.Single(sheet.Tables).Theme);
    }

    [Fact]
    public void ExcelImageExportDoesNotLoadImageSharp()
    {
        var png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACklEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==";
        var bytes = ExcelWorkbook.Export(new[] { new ImageExportRow { Picture = png } });
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Single(workbook.Worksheet(1).Pictures);
        using var archive = new ZipArchive(new MemoryStream(bytes));
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("xl/media/", StringComparison.Ordinal));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "SixLabors.ImageSharp");
    }

    [Fact]
    public async Task IEHelperPreservesDownloadAndImportCallbackContracts()
    {
        var helper = new IEHelper(null, null, null);
        var template = Assert.IsType<FileContentResult>(await helper.DownloadTemplateAsync(new DictImport(), "template.xlsx"));
        Assert.Equal("template.xlsx", template.FileDownloadName);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", template.ContentType);
        using var workbook = new XLWorkbook(new MemoryStream(template.FileContents));
        SetRow(workbook.Worksheet(1), 2, "类型", "名称", "", "code", "value", "否", "10", "说明");
        using var input = Save(workbook);
        var callbackInvoked = false;
        var imported = await helper.ImportDataAsync<DictImport>(new FormFile(input, 0, input.Length, "file", "input.xlsx"), "file-id", result =>
        {
            callbackInvoked = true;
            Assert.False(result.HasError);
            return Task.FromResult(result);
        });
        Assert.True(callbackInvoked);
        Assert.False(Assert.Single(imported.Data).Enabled);
        var export = Assert.IsType<FileContentResult>(await helper.ExportDataAsync(new[] { new DictExport { Name = "名称" } }, "export.xlsx", "自定义工作表"));
        Assert.Equal("export.xlsx", export.FileDownloadName);
        using var exported = new XLWorkbook(new MemoryStream(export.FileContents));
        Assert.Equal("名称", exported.Worksheet("自定义工作表").Cell(2, 4).GetString());
    }

    public sealed class ImageExportRow
    {
        [ExportImageField(20, 20, "compatibility")]
        public string Picture { get; set; }
    }

    private static void SetRow(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var index = 0; index < values.Length; index++) sheet.Cell(row, index + 1).Value = values[index];
    }

    private static MemoryStream Save(XLWorkbook workbook)
    {
        var output = new MemoryStream();
        workbook.SaveAs(output);
        output.Position = 0;
        return output;
    }
}
