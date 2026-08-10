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
        Assert.Contains(worksheet.Descendants(), element => element.Name.LocalName == "mergeCell" && element.Attribute("ref")?.Value == "A3:C3");
        Assert.Contains(worksheet.Descendants(), element => element.Name.LocalName == "autoFilter" && element.Attribute("ref")?.Value == "A1:C2");
    }
}
