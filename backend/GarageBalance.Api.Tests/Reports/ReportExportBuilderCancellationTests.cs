using System.IO.Compression;
using System.Xml.Linq;
using GarageBalance.Api.Application.Reports;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ReportExportBuilderCancellationTests
{
    [Fact]
    public void XlsxBuilderHonorsCancellationBeforeAllocatingWorkbook()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            XlsxWorkbookBuilder.Build(
                [new XlsxSheet("Отчёт", ["Колонка"], [[XlsxCell.Text("Значение")]])],
                cancellation.Token));
    }

    [Fact]
    public void PdfBuilderHonorsCancellationBeforeAllocatingDocument()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            TabularReportPdfDocumentBuilder.Build(
                "Отчёт",
                null,
                [],
                [new TabularPdfSection(null, [new TabularPdfColumn("Колонка")], [["Строка"]])],
                cancellation.Token));
    }

    [Fact]
    public void XlsxBuilderUsesCompactCalendarColumnsAndCenteredCalendarCells()
    {
        var content = XlsxWorkbookBuilder.Build(
        [
            new XlsxSheet(
                "Отчёт",
                ["Месяц", "Дата", "Комментарий"],
                [[XlsxCell.Text("08.2026"), XlsxCell.Text("10.08.2026"), XlsxCell.Text(new string('я', 100))]],
                "Пояснение к печатной форме")
        ]);

        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var worksheet = XDocument.Load(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var columns = worksheet.Descendants().Where(element => element.Name.LocalName == "col").ToArray();
        Assert.Equal(["13", "14", "42"], columns.Select(column => column.Attribute("width")?.Value ?? string.Empty).ToArray());

        var bodyCells = worksheet.Descendants()
            .Single(element => element.Name.LocalName == "row" && element.Attribute("r")?.Value == "2")
            .Elements()
            .Where(element => element.Name.LocalName == "c")
            .ToArray();
        Assert.Equal(["5", "5", "4"], bodyCells.Select(cell => cell.Attribute("s")?.Value ?? string.Empty).ToArray());
        Assert.Contains(worksheet.Descendants(), element => element.Name.LocalName == "sheetView" && element.Attribute("showGridLines")?.Value == "0");
        Assert.DoesNotContain(worksheet.Descendants(), element => element.Name.LocalName is "mergeCell" or "mergeCells");
        Assert.Contains(worksheet.Descendants(), element => element.Name.LocalName == "autoFilter" && element.Attribute("ref")?.Value == "A1:C2");
    }

    [Theory]
    [InlineData("85", "85", false)]
    [InlineData("001", "1", false)]
    [InlineData("999999999999999", "999999999999999", false)]
    [InlineData("1000000000000000", "1000000000000000", true)]
    [InlineData("А-20", "А-20", true)]
    [InlineData("85/1", "85/1", true)]
    [InlineData("٨٥", "٨٥", true)]
    [InlineData("85.1", "85.1", true)]
    [InlineData("=1+1", "=1+1", true)]
    [InlineData("", "", true)]
    [InlineData("ИТОГО", "ИТОГО", true)]
    public void XlsxBuilderExportsGarageNumbersWithoutLosingLetteredIdentifiers(string garage, string expected, bool text)
    {
        var content = XlsxWorkbookBuilder.Build([new XlsxSheet("Гаражи", ["Гараж", "Документ"], [[XlsxCell.Text(garage), XlsxCell.Text("123")]], "Примечание")]);
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var worksheet = XDocument.Load(worksheetStream);
        var cells = worksheet.Descendants().Where(element => element.Name.LocalName == "c").ToArray();
        var garageCell = cells.Single(element => element.Attribute("r")?.Value == "A2");
        Assert.Equal(text ? "inlineStr" : null, garageCell.Attribute("t")?.Value);
        Assert.Equal(expected, garageCell.Value);
        Assert.Equal("inlineStr", cells.Single(element => element.Attribute("r")?.Value == "B2").Attribute("t")?.Value);
        Assert.Equal("inlineStr", cells.Single(element => element.Attribute("r")?.Value == "A1").Attribute("t")?.Value);
        Assert.DoesNotContain(worksheet.Descendants(), element => element.Name.LocalName is "mergeCell" or "mergeCells");
        Assert.Contains(worksheet.Descendants(), element => element.Name.LocalName == "autoFilter" && element.Attribute("ref")?.Value == "A1:B2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Пояснение пустого отчёта")]
    public void EmptyGarageWorkbookKeepsFilterOnHeadersAndNeverMergesNote(string? note)
    {
        var content = XlsxWorkbookBuilder.Build([new XlsxSheet("Гаражи", ["Гараж", "Сумма"], [], note)]);
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var worksheet = XDocument.Load(worksheetStream);
        Assert.DoesNotContain(worksheet.Descendants(), element => element.Name.LocalName is "mergeCell" or "mergeCells" or "f");
        Assert.Contains(worksheet.Descendants(), element => element.Name.LocalName == "autoFilter" && element.Attribute("ref")?.Value == "A1:B1");
        var garageHeader = worksheet.Descendants().Single(element => element.Name.LocalName == "c" && element.Attribute("r")?.Value == "A1");
        Assert.Equal("inlineStr", garageHeader.Attribute("t")?.Value);
        Assert.Equal("Гараж", garageHeader.Value);
        if (note is not null)
        {
            Assert.Contains(worksheet.Descendants(), element => element.Name.LocalName == "c" && element.Value == note);
        }
    }

    [Fact]
    public void UnmergedExplanationWrapsWithoutFixedRowHeightOutsideTheSortableRange()
    {
        const string note = "Начисления и поступления сопоставлены по месяцу, гаражу и услуге. Разница = начисления - поступления. Группировка объединяет услуги в одну строку по гаражу и месяцу.";
        var content = XlsxWorkbookBuilder.Build([new XlsxSheet("Гаражи", ["Месяц", "Гараж"], [[XlsxCell.Text("09.2026"), XlsxCell.Text("85")]], note)]);
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var worksheet = XDocument.Load(worksheetStream);
        var noteRow = worksheet.Descendants().Single(element => element.Name.LocalName == "row" && element.Attribute("r")?.Value == "3");
        Assert.Null(noteRow.Attribute("ht"));
        Assert.Null(noteRow.Attribute("customHeight"));
        var noteCell = Assert.Single(noteRow.Elements());
        Assert.Equal("A3", noteCell.Attribute("r")?.Value);
        Assert.Equal("inlineStr", noteCell.Attribute("t")?.Value);
        Assert.Equal(note, noteCell.Value);
        Assert.DoesNotContain(worksheet.Descendants(), element => element.Name.LocalName is "mergeCell" or "mergeCells");
        Assert.Equal("A1:B2", worksheet.Descendants().Single(element => element.Name.LocalName == "autoFilter").Attribute("ref")?.Value);
        using var stylesStream = archive.GetEntry("xl/styles.xml")!.Open();
        var styles = XDocument.Load(stylesStream);
        var formats = styles.Descendants().Single(element => element.Name.LocalName == "cellXfs").Elements().ToArray();
        var noteFormat = formats[int.Parse(noteCell.Attribute("s")!.Value)];
        Assert.Equal("1", noteFormat.Elements().Single(element => element.Name.LocalName == "alignment").Attribute("wrapText")?.Value);
    }
}
