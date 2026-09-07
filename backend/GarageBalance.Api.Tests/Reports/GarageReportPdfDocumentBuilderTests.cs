using GarageBalance.Api.Application.Reports;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace GarageBalance.Api.Tests.Reports;

public sealed class GarageReportPdfDocumentBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_KeepsGarageNumberTogetherAcrossPages(bool grouped)
    {
        var headers = grouped
            ? new[] { "Месяц", "Гараж", "Начисления", "Поступления", "Разница" }
            : new[] { "Месяц", "Гараж", "Услуга", "Начисления", "Поступления", "Разница" };
        var rows = Enumerable.Range(1, 40).Select(index =>
        {
            var row = new List<XlsxCell> { XlsxCell.Text("08.2026"), XlsxCell.Text($"А{index:D4}\nБ{index:D4}\nВ{index:D4}\nГ{index:D4}") };
            if (!grouped) row.Add(XlsxCell.Text("Обслуживание территории"));
            row.AddRange([XlsxCell.Number(1), XlsxCell.Number(0), XlsxCell.Number(1)]);
            return (IReadOnlyList<XlsxCell>)row;
        }).ToArray();
        var report = new GarageDetailReportDto(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), 40m, 0m, 40m, 40, [], 0, 40);
        var content = GarageReportPdfDocumentBuilder.Build(report, new(headers, rows, []), "Проверка переноса строк");
        using var document = PdfDocument.Open(content);
        var pages = document.GetPages().ToArray();
        var texts = pages.Select(page => ContentOrderTextExtractor.GetText(page)).ToArray();
        Assert.True(pages.Length > 1);
        for (var index = 1; index <= 40; index++)
        {
            var page = Assert.Single(texts, text => text.Contains($"А{index:D4}", StringComparison.Ordinal));
            Assert.Contains($"Б{index:D4}", page);
            Assert.Contains($"В{index:D4}", page);
            Assert.Contains($"Г{index:D4}", page);
        }
        Assert.All(pages, page => Assert.All(page.Letters, letter =>
        {
            Assert.InRange(letter.BoundingBox.Left, 23.5, page.Width - 23.5);
            Assert.InRange(letter.BoundingBox.Right, 23.5, page.Width - 23.5);
        }));
    }
}
