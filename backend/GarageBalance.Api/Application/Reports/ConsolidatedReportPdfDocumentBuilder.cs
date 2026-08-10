using GarageBalance.Api.Application.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GarageBalance.Api.Application.Reports;

internal static class ConsolidatedReportPdfDocumentBuilder
{
    private const string AccentColor = "#1D4ED8";
    private const string MonthHeaderColor = "#1E3A8A";
    private const string HeaderBackground = "#E8EEF8";
    private const string BorderColor = "#CBD5E1";
    private const string MutedTextColor = "#475569";

    static ConsolidatedReportPdfDocumentBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Build(ConsolidatedReportDto report, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(8.5f));

                page.Header()
                    .PaddingBottom(10)
                    .Column(column =>
                    {
                        column.Spacing(3);
                        column.Item()
                            .Text("Консолидированный отчёт")
                            .Bold()
                            .FontSize(18)
                            .FontColor(AccentColor);
                        column.Item()
                            .Text($"Период: {report.PeriodFrom:MM.yyyy} - {report.PeriodTo:MM.yyyy}")
                            .FontColor(MutedTextColor);
                    });

                page.Content()
                    .Column(column =>
                    {
                        column.Spacing(12);
                        foreach (var month in report.MonthlyRows)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            column.Item()
                                .EnsureSpace(110)
                                .Table(table => AddMonthTable(table, month));
                        }
                    });

                page.Footer()
                    .PaddingTop(8)
                    .AlignRight()
                    .DefaultTextStyle(style => style.FontSize(8).FontColor(MutedTextColor))
                    .Text(text =>
                    {
                        text.Span("Страница ");
                        text.CurrentPageNumber();
                        text.Span(" из ");
                        text.TotalPages();
                    });
            });
        }).GeneratePdf();
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    private static void AddMonthTable(TableDescriptor table, MonthlyReportRowDto month)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.RelativeColumn(3.4f);
            columns.ConstantColumn(86);
            columns.RelativeColumn(3.4f);
            columns.ConstantColumn(86);
        });

        table.Header(header =>
        {
            header.Cell()
                .ColumnSpan(4)
                .Element(MonthHeaderCell)
                .Text($"Месяц: {month.AccountingMonth:MM.yyyy}")
                .Bold()
                .FontSize(10)
                .FontColor(Colors.White);

            AddHeaderCell(header, "Наименование поступления");
            AddHeaderCell(header, "Поступления, руб.");
            AddHeaderCell(header, "Наименование выплаты");
            AddHeaderCell(header, "Выплаты, руб.");
        });

        var incomeRows = month.IncomeBreakdown ?? [];
        var expenseRows = month.ExpenseBreakdown ?? [];
        var rowCount = Math.Max(incomeRows.Count, expenseRows.Count);
        if (rowCount == 0)
        {
            AddBodyCell(table, "Нет поступлений", false);
            AddBodyCell(table, string.Empty, true);
            AddBodyCell(table, "Нет выплат", false);
            AddBodyCell(table, string.Empty, true);
        }
        else
        {
            for (var index = 0; index < rowCount; index++)
            {
                AddBodyCell(table, index < incomeRows.Count ? incomeRows[index].Name : string.Empty, false);
                AddBodyCell(table, index < incomeRows.Count ? MoneyFormatting.Format(incomeRows[index].Amount) : string.Empty, true);
                AddBodyCell(table, index < expenseRows.Count ? expenseRows[index].Name : string.Empty, false);
                AddBodyCell(table, index < expenseRows.Count ? MoneyFormatting.Format(expenseRows[index].Amount) : string.Empty, true);
            }
        }

        AddTotalCell(table, "ИТОГО");
        AddTotalCell(table, MoneyFormatting.Format(month.IncomeTotal), true);
        AddTotalCell(table, "ИТОГО");
        AddTotalCell(table, MoneyFormatting.Format(month.ExpenseTotal), true);

        table.Cell()
            .ColumnSpan(4)
            .Element(SummaryCell)
            .Row(row =>
            {
                AddSummaryValue(row, "Разница", month.IncomeTotal - month.ExpenseTotal);
                AddSummaryValue(row, "Остаток по счёту на начало", month.BankBalanceOpening);
                AddSummaryValue(row, "Остаток по счёту на конец", month.BankBalanceClosing);
            });
    }

    private static void AddHeaderCell(TableCellDescriptor header, string value)
    {
        header.Cell()
            .Element(HeaderCell)
            .Text(value)
            .Bold();
    }

    private static void AddBodyCell(TableDescriptor table, string value, bool alignRight)
    {
        var cell = table.Cell().Element(BodyCell);
        if (alignRight)
        {
            cell = cell.AlignRight();
        }

        cell.Text(value);
    }

    private static void AddTotalCell(TableDescriptor table, string value, bool alignRight = false)
    {
        var cell = table.Cell().Element(TotalCell);
        if (alignRight)
        {
            cell = cell.AlignRight();
        }

        cell.Text(value).Bold();
    }

    private static void AddSummaryValue(RowDescriptor row, string label, decimal value)
    {
        row.RelativeItem()
            .PaddingRight(12)
            .Text(text =>
            {
                text.Span($"{label}: ").FontColor(MutedTextColor);
                text.Span(MoneyFormatting.Format(value)).Bold();
            });
    }

    private static IContainer MonthHeaderCell(IContainer container)
    {
        return container
            .Background(MonthHeaderColor)
            .Border(0.75f)
            .BorderColor(BorderColor)
            .PaddingHorizontal(7)
            .PaddingVertical(6)
            .AlignMiddle()
            .AlignCenter();
    }

    private static IContainer HeaderCell(IContainer container)
    {
        return container
            .Background(HeaderBackground)
            .Border(0.75f)
            .BorderColor(BorderColor)
            .PaddingHorizontal(5)
            .PaddingVertical(5)
            .AlignMiddle()
            .AlignCenter();
    }

    private static IContainer BodyCell(IContainer container)
    {
        return container
            .ShowEntire()
            .MinHeight(22)
            .Border(0.5f)
            .BorderColor(BorderColor)
            .PaddingHorizontal(5)
            .PaddingVertical(4)
            .AlignMiddle();
    }

    private static IContainer TotalCell(IContainer container)
    {
        return container
            .ShowEntire()
            .Background("#EFF6FF")
            .Border(0.75f)
            .BorderColor(AccentColor)
            .PaddingHorizontal(5)
            .PaddingVertical(5)
            .AlignMiddle();
    }

    private static IContainer SummaryCell(IContainer container)
    {
        return container
            .ShowEntire()
            .Background("#F8FAFC")
            .Border(0.75f)
            .BorderColor(BorderColor)
            .PaddingHorizontal(7)
            .PaddingVertical(6)
            .AlignMiddle();
    }
}
