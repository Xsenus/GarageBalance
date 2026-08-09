using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace GarageBalance.Api.Application.Reports;

internal static class XlsxWorkbookBuilder
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Build(IReadOnlyList<XlsxSheet> sheets, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", BuildContentTypes(sheets.Count));
            WriteEntry(archive, "_rels/.rels", BuildRootRelationships());
            WriteEntry(archive, "xl/workbook.xml", BuildWorkbook(sheets));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships(sheets.Count));
            WriteEntry(archive, "xl/styles.xml", BuildStyles());

            for (var index = 0; index < sheets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", BuildWorksheet(sheets[index], cancellationToken));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return stream.ToArray();
    }

    private static XDocument BuildContentTypes(int sheetCount)
    {
        XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        var types = new XElement(contentTypes + "Types",
            new XElement(contentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(contentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));

        for (var index = 1; index <= sheetCount; index++)
        {
            types.Add(new XElement(contentTypes + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{index}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }

        return new XDocument(types);
    }

    private static XDocument BuildRootRelationships()
    {
        return new XDocument(
            new XElement(PackageRelationships + "Relationships",
                new XElement(PackageRelationships + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument BuildWorkbook(IReadOnlyList<XlsxSheet> sheets)
    {
        var sheetElements = sheets.Select((sheet, index) =>
            new XElement(Spreadsheet + "sheet",
                new XAttribute("name", SanitizeSheetName(sheet.Name)),
                new XAttribute("sheetId", index + 1),
                new XAttribute(Relationships + "id", $"rId{index + 1}")));

        return new XDocument(
            new XElement(Spreadsheet + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", Relationships),
                new XElement(Spreadsheet + "sheets", sheetElements)));
    }

    private static XDocument BuildWorkbookRelationships(int sheetCount)
    {
        var relationships = new XElement(PackageRelationships + "Relationships");
        for (var index = 1; index <= sheetCount; index++)
        {
            relationships.Add(new XElement(PackageRelationships + "Relationship",
                new XAttribute("Id", $"rId{index}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{index}.xml")));
        }

        relationships.Add(new XElement(PackageRelationships + "Relationship",
            new XAttribute("Id", $"rId{sheetCount + 1}"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));

        return new XDocument(relationships);
    }

    private static XDocument BuildStyles()
    {
        return new XDocument(
            new XElement(Spreadsheet + "styleSheet",
                new XElement(Spreadsheet + "fonts", new XAttribute("count", 2),
                    new XElement(Spreadsheet + "font",
                        new XElement(Spreadsheet + "sz", new XAttribute("val", 11)),
                        new XElement(Spreadsheet + "name", new XAttribute("val", "Calibri"))),
                    new XElement(Spreadsheet + "font",
                        new XElement(Spreadsheet + "b"),
                        new XElement(Spreadsheet + "color", new XAttribute("rgb", "FFFFFFFF")),
                        new XElement(Spreadsheet + "sz", new XAttribute("val", 11)),
                        new XElement(Spreadsheet + "name", new XAttribute("val", "Calibri")))),
                new XElement(Spreadsheet + "fills", new XAttribute("count", 3),
                    new XElement(Spreadsheet + "fill", new XElement(Spreadsheet + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(Spreadsheet + "fill", new XElement(Spreadsheet + "patternFill", new XAttribute("patternType", "gray125"))),
                    new XElement(Spreadsheet + "fill", new XElement(Spreadsheet + "patternFill", new XAttribute("patternType", "solid"), new XElement(Spreadsheet + "fgColor", new XAttribute("rgb", "FF1D4ED8")), new XElement(Spreadsheet + "bgColor", new XAttribute("indexed", 64))))),
                new XElement(Spreadsheet + "borders", new XAttribute("count", 2),
                    new XElement(Spreadsheet + "border"),
                    new XElement(Spreadsheet + "border",
                        BuildBorderSide("left"),
                        BuildBorderSide("right"),
                        BuildBorderSide("top"),
                        BuildBorderSide("bottom"),
                        new XElement(Spreadsheet + "diagonal"))),
                new XElement(Spreadsheet + "cellStyleXfs", new XAttribute("count", 1), new XElement(Spreadsheet + "xf")),
                new XElement(Spreadsheet + "cellXfs", new XAttribute("count", 5),
                    new XElement(Spreadsheet + "xf", new XAttribute("xfId", 0)),
                    new XElement(Spreadsheet + "xf", new XAttribute("xfId", 0), new XAttribute("fontId", 1), new XAttribute("fillId", 2), new XAttribute("borderId", 1), new XAttribute("applyFont", 1), new XAttribute("applyFill", 1), new XAttribute("applyBorder", 1), new XAttribute("applyAlignment", 1), new XElement(Spreadsheet + "alignment", new XAttribute("horizontal", "center"), new XAttribute("vertical", "center"), new XAttribute("wrapText", 1))),
                    new XElement(Spreadsheet + "xf", new XAttribute("xfId", 0), new XAttribute("borderId", 1), new XAttribute("numFmtId", 4), new XAttribute("applyBorder", 1), new XAttribute("applyNumberFormat", 1), new XAttribute("applyAlignment", 1), new XElement(Spreadsheet + "alignment", new XAttribute("horizontal", "right"), new XAttribute("vertical", "top"))),
                    new XElement(Spreadsheet + "xf", new XAttribute("xfId", 0), new XAttribute("borderId", 1), new XAttribute("numFmtId", 3), new XAttribute("applyBorder", 1), new XAttribute("applyNumberFormat", 1), new XAttribute("applyAlignment", 1), new XElement(Spreadsheet + "alignment", new XAttribute("horizontal", "right"), new XAttribute("vertical", "top"))),
                    new XElement(Spreadsheet + "xf", new XAttribute("xfId", 0), new XAttribute("borderId", 1), new XAttribute("applyBorder", 1), new XAttribute("applyAlignment", 1), new XElement(Spreadsheet + "alignment", new XAttribute("vertical", "top"), new XAttribute("wrapText", 1)))),
                new XElement(Spreadsheet + "cellStyles", new XAttribute("count", 1), new XElement(Spreadsheet + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0)))));
    }

    private static XElement BuildBorderSide(string name) =>
        new(Spreadsheet + name,
            new XAttribute("style", "thin"),
            new XElement(Spreadsheet + "color", new XAttribute("rgb", "FFD0D5DD")));

    private static XDocument BuildWorksheet(XlsxSheet sheet, CancellationToken cancellationToken)
    {
        var rows = new List<XElement>
        {
            BuildRow(1, sheet.Headers.Select(header => XlsxCell.Text(header)).ToArray())
        };

        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            if ((index & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            rows.Add(BuildRow(index + 2, sheet.Rows[index]));
        }

        var lastColumn = ColumnName(Math.Max(1, sheet.Headers.Count));
        var lastRow = Math.Max(1, rows.Count);
        return new XDocument(
            new XElement(Spreadsheet + "worksheet",
                new XElement(Spreadsheet + "sheetPr", new XElement(Spreadsheet + "pageSetUpPr", new XAttribute("fitToPage", 1))),
                new XElement(Spreadsheet + "dimension", new XAttribute("ref", $"A1:{lastColumn}{lastRow}")),
                new XElement(Spreadsheet + "sheetViews",
                    new XElement(Spreadsheet + "sheetView", new XAttribute("workbookViewId", 0),
                        new XElement(Spreadsheet + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                BuildColumns(sheet),
                new XElement(Spreadsheet + "sheetData", rows),
                new XElement(Spreadsheet + "autoFilter", new XAttribute("ref", $"A1:{lastColumn}{lastRow}")),
                new XElement(Spreadsheet + "pageMargins", new XAttribute("left", 0.3), new XAttribute("right", 0.3), new XAttribute("top", 0.5), new XAttribute("bottom", 0.5), new XAttribute("header", 0.2), new XAttribute("footer", 0.2)),
                new XElement(Spreadsheet + "pageSetup", new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0), new XAttribute("paperSize", 9))));
    }

    private static XElement BuildColumns(XlsxSheet sheet)
    {
        var columns = sheet.Headers.Select((header, index) =>
        {
            var contentWidth = sheet.Rows.Count == 0
                ? 0
                : sheet.Rows.Max(row => index < row.Count ? row[index].DisplayLength : 0);
            var width = Math.Clamp(Math.Max(header.Length, contentWidth) + 2, 11, 42);
            return new XElement(Spreadsheet + "col",
                new XAttribute("min", index + 1),
                new XAttribute("max", index + 1),
                new XAttribute("width", width),
                new XAttribute("customWidth", 1));
        });
        return new XElement(Spreadsheet + "cols", columns);
    }

    private static XElement BuildRow(int rowIndex, IReadOnlyList<XlsxCell> cells)
    {
        return new XElement(Spreadsheet + "row",
            new XAttribute("r", rowIndex),
            rowIndex == 1 ? new XAttribute("ht", 30) : null,
            rowIndex == 1 ? new XAttribute("customHeight", 1) : null,
            cells.Select((cell, index) => BuildCell(rowIndex, index, cell)));
    }

    private static XElement BuildCell(int rowIndex, int columnIndex, XlsxCell cell)
    {
        var reference = $"{ColumnName(columnIndex + 1)}{rowIndex}";
        if (cell.Kind is XlsxCellKind.Decimal or XlsxCellKind.Integer)
        {
            return new XElement(Spreadsheet + "c",
                new XAttribute("r", reference),
                new XAttribute("s", cell.Kind == XlsxCellKind.Integer ? 3 : 2),
                new XElement(Spreadsheet + "v", cell.Value));
        }

        return new XElement(Spreadsheet + "c",
            new XAttribute("r", reference),
            new XAttribute("s", rowIndex == 1 ? 1 : 4),
            new XAttribute("t", "inlineStr"),
            new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", cell.Value)));
    }

    private static void WriteEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open());
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }

        return name;
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var sanitized = invalid.Aggregate(name, (current, symbol) => current.Replace(symbol, ' ')).Trim();
        return string.IsNullOrWhiteSpace(sanitized)
            ? "Report"
            : sanitized[..Math.Min(31, sanitized.Length)];
    }
}

internal sealed record XlsxSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<XlsxCell>> Rows);

internal enum XlsxCellKind
{
    Text,
    Decimal,
    Integer
}

internal sealed record XlsxCell(string Value, XlsxCellKind Kind)
{
    public bool IsNumber => Kind is XlsxCellKind.Decimal or XlsxCellKind.Integer;
    public int DisplayLength => Value.Length;

    public static XlsxCell Text(string? value) => new(value ?? string.Empty, XlsxCellKind.Text);

    public static XlsxCell Number(decimal value) => new(value.ToString(CultureInfo.InvariantCulture), XlsxCellKind.Decimal);

    public static XlsxCell Number(int value) => new(value.ToString(CultureInfo.InvariantCulture), XlsxCellKind.Integer);
}
