using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Reports;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace GarageBalance.Api.Tests.Reports;

public sealed class ConsolidatedReportPdfDocumentBuilderTests
{
    [Fact]
    public void Build_RendersCyrillicWrappedRowsAcrossLandscapePages()
    {
        var report = CreateReport(monthCount: 3, rowsPerMonth: 38);

        var content = ConsolidatedReportPdfDocumentBuilder.Build(report);

        using var document = PdfDocument.Open(content);
        var pages = document.GetPages().ToArray();
        Assert.True(pages.Length >= 4);
        Assert.All(pages, page =>
        {
            Assert.True(page.Width > page.Height);
            Assert.Contains("Страница", ContentOrderTextExtractor.GetText(page));
            Assert.All(page.Letters, letter =>
            {
                Assert.InRange(letter.BoundingBox.Left, -0.5, page.Width + 0.5);
                Assert.InRange(letter.BoundingBox.Right, -0.5, page.Width + 0.5);
                Assert.InRange(letter.BoundingBox.Bottom, -0.5, page.Height + 0.5);
                Assert.InRange(letter.BoundingBox.Top, -0.5, page.Height + 0.5);
            });
        });

        var text = string.Join(Environment.NewLine, pages.Select(page => ContentOrderTextExtractor.GetText(page)));
        var normalizedText = Regex.Replace(text, @"\s+", " ");
        Assert.Contains("Консолидированный отчёт", text);
        Assert.Contains("Наименование поступления", text);
        Assert.Contains("Наименование выплаты", text);
        Assert.Contains("Кириллическое наименование поступления", normalizedText);
        Assert.Contains("проверка многострочного переноса", normalizedText);
        Assert.Contains("Кириллическое наименование поступления 38", normalizedText);
        Assert.Contains("Выплата поставщику 38", normalizedText);
        Assert.Contains("Наименование поступления", ContentOrderTextExtractor.GetText(pages[1]));
        Assert.Contains("Остаток по счёту на начало", text);
        Assert.Contains("Остаток по счёту на конец", text);
        Assert.DoesNotContain("Kirillicheskoe", text);
    }

    [Fact]
    public void Build_RendersEmptyMonthWithTotalsAndNoDataLabels()
    {
        var report = CreateReport(monthCount: 1, rowsPerMonth: 0);

        var content = ConsolidatedReportPdfDocumentBuilder.Build(report);

        using var document = PdfDocument.Open(content);
        var page = Assert.Single(document.GetPages());
        var text = ContentOrderTextExtractor.GetText(page);
        Assert.True(page.Width > page.Height);
        Assert.Contains("Нет поступлений", text);
        Assert.Contains("Нет выплат", text);
        Assert.Contains("ИТОГО", text);
        Assert.Contains("1 000 000.00", text);
    }

    private static ConsolidatedReportDto CreateReport(int monthCount, int rowsPerMonth)
    {
        var months = Enumerable.Range(0, monthCount)
            .Select(monthOffset =>
            {
                var month = new DateOnly(2026, 1, 1).AddMonths(monthOffset);
                var income = Enumerable.Range(1, rowsPerMonth)
                    .Select(index => new NamedAmountTotalDto(
                        null,
                        $"Кириллическое наименование поступления {index}: обслуживание территории и проверка многострочного переноса",
                        index * 100m))
                    .ToArray();
                var expense = Enumerable.Range(1, rowsPerMonth)
                    .Select(index => new NamedAmountTotalDto(
                        null,
                        $"Выплата поставщику {index}: ремонт наружного освещения, охрана и содержание проездов кооператива",
                        index * 75m))
                    .ToArray();

                return new MonthlyReportRowDto(
                    AccountingMonth: month,
                    IncomeTotal: income.Sum(item => item.Amount),
                    ExpenseTotal: expense.Sum(item => item.Amount),
                    AccrualTotal: 0m,
                    Balance: 0m,
                    Debt: 0m,
                    OperationCount: rowsPerMonth * 2,
                    AccrualCount: 0,
                    MeterReadingCount: 0,
                    BankBalanceOpening: 1_000_000m,
                    BankBalanceClosing: 1_000_000m + income.Sum(item => item.Amount) - expense.Sum(item => item.Amount),
                    IncomeBreakdown: income,
                    ExpenseBreakdown: expense);
            })
            .ToArray();

        return new ConsolidatedReportDto(
            PeriodFrom: new DateOnly(2026, 1, 1),
            PeriodTo: new DateOnly(2026, Math.Max(1, monthCount), 1),
            IncomeTotal: months.Sum(item => item.IncomeTotal),
            ExpenseTotal: months.Sum(item => item.ExpenseTotal),
            AccrualTotal: 0m,
            Balance: 0m,
            Debt: 0m,
            OperationCount: months.Sum(item => item.OperationCount),
            AccrualCount: 0,
            MeterReadingCount: 0,
            MonthlyRows: months,
            GarageRowCount: 0,
            GarageRows: [],
            IncomeBreakdown: [],
            ExpenseBreakdown: []);
    }
}
