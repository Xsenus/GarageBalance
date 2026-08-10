using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GarageBalance.Api.Application.Reports;

internal sealed record TabularPdfSummary(string Label, string Value);

internal sealed record TabularPdfColumn(
    string Header,
    float RelativeWidth = 1,
    bool AlignRight = false,
    bool AlignCenter = false);

internal sealed record TabularPdfSection(
    string? Title,
    IReadOnlyList<TabularPdfColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? Footer = null,
    string EmptyMessage = "Данных за выбранный период нет");

internal static class TabularReportPdfDocumentBuilder
{
    private const string AccentColor = "#2563EB";
    private const string HeaderBackground = "#E8EEF8";
    private const string BorderColor = "#CBD5E1";
    private const string MutedTextColor = "#475569";

    static TabularReportPdfDocumentBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Build(
        string title,
        string? period,
        IReadOnlyList<TabularPdfSummary> summaries,
        IReadOnlyList<TabularPdfSection> sections,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(8.5f));

                page.Header().PaddingBottom(10).Column(column =>
                {
                    column.Spacing(3);
                    column.Item().Text(title).Bold().FontSize(18).FontColor(AccentColor);
                    if (!string.IsNullOrWhiteSpace(period))
                    {
                        column.Item().Text(period).FontColor(MutedTextColor);
                    }
                });

                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    if (summaries.Count > 0)
                    {
                        column.Item().Row(row =>
                        {
                            foreach (var summary in summaries)
                            {
                                row.RelativeItem().PaddingRight(8).BorderBottom(2).BorderColor(AccentColor).PaddingBottom(5).Column(summaryColumn =>
                                {
                                    summaryColumn.Item().Text(summary.Label).FontSize(8).FontColor(MutedTextColor);
                                    summaryColumn.Item().Text(summary.Value).Bold().FontSize(11);
                                });
                            }
                        });
                    }

                    foreach (var section in sections)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        column.Item().EnsureSpace(80).Column(sectionColumn =>
                        {
                            sectionColumn.Spacing(5);
                            if (!string.IsNullOrWhiteSpace(section.Title))
                            {
                                sectionColumn.Item().Text(section.Title).Bold().FontSize(11).FontColor("#1E3A8A");
                            }

                            sectionColumn.Item().Table(table => AddSection(table, section, cancellationToken));
                        });
                    }
                });

                page.Footer().PaddingTop(8).AlignRight().DefaultTextStyle(style => style.FontSize(8).FontColor(MutedTextColor)).Text(text =>
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

    private static void AddSection(TableDescriptor table, TabularPdfSection section, CancellationToken cancellationToken)
    {
        table.ColumnsDefinition(columns =>
        {
            foreach (var column in section.Columns)
            {
                columns.RelativeColumn(Math.Max(column.RelativeWidth, 0.2f));
            }
        });

        table.Header(header =>
        {
            foreach (var column in section.Columns)
            {
                header.Cell().Element(HeaderCell).AlignCenter().Text(column.Header).Bold();
            }
        });

        if (section.Rows.Count == 0)
        {
            table.Cell().ColumnSpan((uint)section.Columns.Count).Element(EmptyCell).AlignCenter().Text(section.EmptyMessage).FontColor(MutedTextColor);
        }
        else
        {
            foreach (var row in section.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var index = 0; index < section.Columns.Count; index++)
                {
                    var cell = table.Cell().Element(BodyCell);
                    cell = AlignCell(cell, section.Columns[index]);

                    cell.Text(index < row.Count ? row[index] : string.Empty);
                }
            }
        }

        if (section.Footer is { Count: > 0 })
        {
            for (var index = 0; index < section.Columns.Count; index++)
            {
                var cell = table.Cell().Element(FooterCell);
                cell = AlignCell(cell, section.Columns[index]);

                cell.Text(index < section.Footer.Count ? section.Footer[index] : string.Empty).Bold();
            }
        }
    }

    private static IContainer AlignCell(IContainer container, TabularPdfColumn column)
    {
        if (column.AlignRight)
        {
            return container.AlignRight();
        }

        return column.AlignCenter ? container.AlignCenter() : container;
    }

    private static IContainer HeaderCell(IContainer container) => container
        .Background(HeaderBackground)
        .Border(0.75f)
        .BorderColor(BorderColor)
        .PaddingHorizontal(4)
        .PaddingVertical(5)
        .AlignMiddle();

    private static IContainer BodyCell(IContainer container) => container
        .MinHeight(21)
        .Border(0.5f)
        .BorderColor(BorderColor)
        .PaddingHorizontal(4)
        .PaddingVertical(4)
        .AlignMiddle();

    private static IContainer EmptyCell(IContainer container) => container
        .MinHeight(42)
        .Border(0.5f)
        .BorderColor(BorderColor)
        .Padding(10)
        .AlignMiddle();

    private static IContainer FooterCell(IContainer container) => container
        .Background("#EFF6FF")
        .Border(0.75f)
        .BorderColor(AccentColor)
        .PaddingHorizontal(4)
        .PaddingVertical(5)
        .AlignMiddle();
}
