using GarageBalance.Api.Application.Reports;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace GarageBalance.Api.Tests.Reports;

public sealed class TabularReportPdfDocumentBuilderTests
{
    [Fact]
    public void Build_AllowsACommentLongerThanOnePageWithoutDroppingItsEnd()
    {
        var comment = "Начало большого комментария\n" + string.Join("\n", Enumerable.Repeat("Строка большого комментария", 150)) + "\nКонец большого комментария";
        var content = TabularReportPdfDocumentBuilder.Build("Длинная строка", null, [],
            [new(null, [new("Запись"), new("Комментарий", 2)], [["Операция", comment]])]);
        using var document = PdfDocument.Open(content);
        var pages = document.GetPages().ToArray();
        Assert.True(pages.Length > 1);
        var text = string.Join(" ", pages.Select(page => ContentOrderTextExtractor.GetText(page)));
        Assert.Contains("Начало большого комментария", text);
        Assert.Contains("Конец большого комментария", text);
    }

    [Fact]
    public void Build_KeepsOrdinaryMultilineRowsTogetherAtPageBoundaries()
    {
        var rows = Enumerable.Range(1, 40)
            .Select(index => (IReadOnlyList<string>)[
                $"Запись-{index:D4}",
                $"Начало-{index:D4}\n" + string.Join("\n", Enumerable.Repeat("Комментарий к операции", 7)) + $"\nКонец-{index:D4}"
            ]).ToArray();
        var content = TabularReportPdfDocumentBuilder.Build("Перенос строк", null, [],
            [new(null, [new("Запись"), new("Комментарий", 2)], rows)]);
        using var document = PdfDocument.Open(content);
        var texts = document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)).ToArray();
        Assert.True(texts.Length > 1);
        for (var index = 1; index <= 40; index++)
        {
            var page = Assert.Single(texts, text => text.Contains($"Начало-{index:D4}", StringComparison.Ordinal));
            Assert.Contains($"Конец-{index:D4}", page);
            Assert.Contains($"Запись-{index:D4}", page);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void Build_KeepsWideTablesAndLongCyrillicValuesInsideEveryPage(int columnCount)
    {
        var columns = Enumerable.Range(1, columnCount)
            .Select(index => new TabularPdfColumn($"Столбец {index}", index == columnCount ? 2 : 1,
                AlignRight: index % 3 == 0, AlignCenter: index % 3 == 1))
            .ToArray();
        var rows = Enumerable.Range(1, 65)
            .Select(row => (IReadOnlyList<string>)Enumerable.Range(1, columnCount)
                .Select(column => column == columnCount
                    ? $"Конечная ячейка {row}: обслуживание территории кооператива и ремонт оборудования"
                    : column % 3 == 0 ? "1 234 567.89" : $"Запись {row}, значение {column}")
                .ToArray())
            .ToArray();

        var content = TabularReportPdfDocumentBuilder.Build("Проверка широкого отчёта", "01.01.2026–31.12.2026",
            [new("Итог", "80 246 912.85")], [new(null, columns, rows)]);

        using var document = PdfDocument.Open(content);
        var pages = document.GetPages().ToArray();
        Assert.True(pages.Length > 1);
        foreach (var page in pages)
        {
            Assert.True(page.Width > page.Height);
            var text = ContentOrderTextExtractor.GetText(page);
            Assert.Contains("Страница", text);
            Assert.Contains($"Столбец {columnCount}", text);
            Assert.All(page.Letters, letter =>
            {
                Assert.InRange(letter.BoundingBox.Left, 23.5, page.Width - 23.5);
                Assert.InRange(letter.BoundingBox.Right, 23.5, page.Width - 23.5);
                Assert.InRange(letter.BoundingBox.Bottom, 23.5, page.Height - 23.5);
                Assert.InRange(letter.BoundingBox.Top, 23.5, page.Height - 23.5);
            });
        }

        var allText = string.Join(" ", pages.Select(page => ContentOrderTextExtractor.GetText(page)));
        Assert.Contains("Конечная ячейка 65", System.Text.RegularExpressions.Regex.Replace(allText, @"\s+", " "));
        Assert.Contains("80 246 912.85", allText);
    }
}
