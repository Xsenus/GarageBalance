using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace GarageBalance.Api.Application.Reports;

internal static class XlsxWorkbookBuilder
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Build(IReadOnlyList<XlsxSheet> sheets)
    {
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
                WriteEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", BuildWorksheet(sheets[index]));
            }
        }

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
                new XElement(Spreadsheet + "fonts", new XAttribute("count", 1), new XElement(Spreadsheet + "font")),
                new XElement(Spreadsheet + "fills", new XAttribute("count", 1), new XElement(Spreadsheet + "fill")),
                new XElement(Spreadsheet + "borders", new XAttribute("count", 1), new XElement(Spreadsheet + "border")),
                new XElement(Spreadsheet + "cellStyleXfs", new XAttribute("count", 1), new XElement(Spreadsheet + "xf")),
                new XElement(Spreadsheet + "cellXfs", new XAttribute("count", 1), new XElement(Spreadsheet + "xf")),
                new XElement(Spreadsheet + "cellStyles", new XAttribute("count", 1), new XElement(Spreadsheet + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0)))));
    }

    private static XDocument BuildWorksheet(XlsxSheet sheet)
    {
        var rows = new List<XElement>
        {
            BuildRow(1, sheet.Headers.Select(header => XlsxCell.Text(header)).ToArray())
        };

        for (var index = 0; index < sheet.Rows.Count; index++)
        {
            rows.Add(BuildRow(index + 2, sheet.Rows[index]));
        }

        return new XDocument(
            new XElement(Spreadsheet + "worksheet",
                new XElement(Spreadsheet + "sheetData", rows)));
    }

    private static XElement BuildRow(int rowIndex, IReadOnlyList<XlsxCell> cells)
    {
        return new XElement(Spreadsheet + "row",
            new XAttribute("r", rowIndex),
            cells.Select((cell, index) => BuildCell(rowIndex, index, cell)));
    }

    private static XElement BuildCell(int rowIndex, int columnIndex, XlsxCell cell)
    {
        var reference = $"{ColumnName(columnIndex + 1)}{rowIndex}";
        if (cell.IsNumber)
        {
            return new XElement(Spreadsheet + "c",
                new XAttribute("r", reference),
                new XElement(Spreadsheet + "v", cell.Value));
        }

        return new XElement(Spreadsheet + "c",
            new XAttribute("r", reference),
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

internal sealed record XlsxCell(string Value, bool IsNumber)
{
    public static XlsxCell Text(string? value) => new(value ?? string.Empty, false);

    public static XlsxCell Number(decimal value) => new(value.ToString(CultureInfo.InvariantCulture), true);

    public static XlsxCell Number(int value) => new(value.ToString(CultureInfo.InvariantCulture), true);
}
