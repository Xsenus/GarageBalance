using System.Globalization;
using GarageBalance.Api.Application.Common;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GarageBalance.Api.Application.Reports;

internal static class GarageReportPdfDocumentBuilder
{
    private const string AccentColor = "#2563EB";
    private const string HeaderBackground = "#E8EEF8";
    private const string BorderColor = "#CBD5E1";
    private const string MutedTextColor = "#475569";

    static GarageReportPdfDocumentBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Build(
        GarageDetailReportDto report,
        GarageReportExportLayout layout,
        string comment)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(9));

                page.Header()
                    .PaddingBottom(12)
                    .Column(column =>
                    {
                        column.Spacing(4);
                        column.Item()
                            .Text("Отчёт по гаражам")
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
                        column.Spacing(10);
                        column.Item()
                            .Border(1)
                            .BorderColor(BorderColor)
                            .Background("#F8FAFC")
                            .Padding(8)
                            .Text(comment)
                            .FontColor(MutedTextColor);

                        column.Item()
                            .Row(row =>
                            {
                                AddSummary(row, "ИТОГО начислений", report.AccrualTotal);
                                AddSummary(row, "ИТОГО поступлений", report.IncomeTotal);
                                AddSummary(row, "Разница", report.Difference);
                            });

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(58);
                                columns.ConstantColumn(58);
                                if (layout.Headers.Count == 6)
                                {
                                    columns.RelativeColumn(1.8f);
                                }

                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                foreach (var value in layout.Headers)
                                {
                                    header.Cell()
                                        .Element(HeaderCell)
                                        .Text(value)
                                        .Bold();
                                }
                            });

                            foreach (var row in layout.Rows)
                            {
                                foreach (var cell in row)
                                {
                                    table.Cell()
                                        .Element(BodyCell)
                                        .Text(FormatCell(cell));
                                }
                            }

                            foreach (var cell in layout.Footer)
                            {
                                table.Cell()
                                    .Element(FooterCell)
                                    .Text(FormatCell(cell))
                                    .Bold();
                            }
                        });
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
    }

    private static void AddSummary(RowDescriptor row, string label, decimal value)
    {
        row.RelativeItem()
            .PaddingRight(8)
            .BorderBottom(2)
            .BorderColor(AccentColor)
            .PaddingBottom(5)
            .Column(column =>
            {
                column.Item().Text(label).FontSize(8).FontColor(MutedTextColor);
                column.Item().Text(MoneyFormatting.Format(value)).Bold().FontSize(12);
            });
    }

    private static string FormatCell(XlsxCell cell)
    {
        return cell.IsNumber
            ? MoneyFormatting.Format(decimal.Parse(cell.Value, CultureInfo.InvariantCulture))
            : cell.Value;
    }

    private static IContainer HeaderCell(IContainer container)
    {
        return container
            .Background(HeaderBackground)
            .BorderBottom(1)
            .BorderColor(BorderColor)
            .PaddingHorizontal(5)
            .PaddingVertical(6);
    }

    private static IContainer BodyCell(IContainer container)
    {
        return container
            .BorderBottom(0.5f)
            .BorderColor(BorderColor)
            .PaddingHorizontal(5)
            .PaddingVertical(5);
    }

    private static IContainer FooterCell(IContainer container)
    {
        return container
            .Background("#EFF6FF")
            .BorderTop(1)
            .BorderColor(AccentColor)
            .PaddingHorizontal(5)
            .PaddingVertical(6);
    }
}
