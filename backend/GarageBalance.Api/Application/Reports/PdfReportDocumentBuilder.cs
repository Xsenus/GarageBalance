using System.Globalization;
using System.Text;

namespace GarageBalance.Api.Application.Reports;

internal static class PdfReportDocumentBuilder
{
    private const int PageWidth = 595;
    private const int PageHeight = 842;
    private const int Left = 42;
    private const int Top = 794;
    private const int LineHeight = 15;
    private const int LinesPerPage = 48;

    public static byte[] Build(string title, IReadOnlyList<string> lines)
    {
        var normalizedLines = new List<string> { title, string.Empty };
        normalizedLines.AddRange(lines);
        var pages = normalizedLines
            .Select(NormalizeText)
            .SelectMany(WrapLine)
            .Chunk(LinesPerPage)
            .Select(chunk => chunk.ToArray())
            .ToArray();

        if (pages.Length == 0)
        {
            pages = [[NormalizeText(title)]];
        }

        var objects = new List<byte[]>();
        var catalogId = AddObject(objects, "<< /Type /Catalog /Pages 2 0 R >>");
        var pagesId = 2;
        var fontId = 3;
        var firstPageId = 4;
        var pageIds = new List<int>();
        var contentIds = new List<int>();

        for (var index = 0; index < pages.Length; index++)
        {
            pageIds.Add(firstPageId + index * 2);
            contentIds.Add(firstPageId + index * 2 + 1);
        }

        var kids = string.Join(" ", pageIds.Select(id => $"{id} 0 R"));
        AddObject(objects, $"<< /Type /Pages /Kids [{kids}] /Count {pageIds.Count} >>");
        AddObject(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        for (var index = 0; index < pages.Length; index++)
        {
            var pageObject = $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentIds[index]} 0 R >>";
            AddObject(objects, pageObject);
            AddStreamObject(objects, BuildContentStream(pages[index], index + 1, pages.Length));
        }

        return BuildPdf(objects, catalogId);
    }

    private static int AddObject(List<byte[]> objects, string content)
    {
        objects.Add(Encoding.ASCII.GetBytes(content));
        return objects.Count;
    }

    private static void AddStreamObject(List<byte[]> objects, string stream)
    {
        var bytes = Encoding.ASCII.GetBytes(stream);
        var header = Encoding.ASCII.GetBytes($"<< /Length {bytes.Length} >>\nstream\n");
        var footer = Encoding.ASCII.GetBytes("\nendstream");
        objects.Add(header.Concat(bytes).Concat(footer).ToArray());
    }

    private static byte[] BuildPdf(IReadOnlyList<byte[]> objects, int catalogId)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };

        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{index + 1} 0 obj\n");
            stream.Write(objects[index]);
            WriteAscii(stream, "\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(stream, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(stream, $"trailer\n<< /Size {objects.Count + 1} /Root {catalogId} 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return stream.ToArray();
    }

    private static string BuildContentStream(IReadOnlyList<string> lines, int pageNumber, int pageCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BT");
        builder.AppendLine("/F1 10 Tf");
        builder.AppendLine("12 TL");
        builder.AppendLine($"{Left.ToString(CultureInfo.InvariantCulture)} {Top.ToString(CultureInfo.InvariantCulture)} Td");

        for (var index = 0; index < lines.Count; index++)
        {
            if (index == 0)
            {
                builder.AppendLine($"/F1 14 Tf ({Escape(lines[index])}) Tj");
                builder.AppendLine("/F1 10 Tf");
            }
            else
            {
                builder.AppendLine($"({Escape(lines[index])}) Tj");
            }

            if (index < lines.Count - 1)
            {
                builder.AppendLine($"0 -{LineHeight} Td");
            }
        }

        builder.AppendLine("ET");
        builder.AppendLine("BT");
        builder.AppendLine("/F1 9 Tf");
        builder.AppendLine($"{(PageWidth - 88).ToString(CultureInfo.InvariantCulture)} 28 Td");
        builder.AppendLine($"(Page {pageNumber}/{pageCount}) Tj");
        builder.AppendLine("ET");
        return builder.ToString();
    }

    private static IEnumerable<string> WrapLine(string line)
    {
        if (line.Length <= 112)
        {
            yield return line;
            yield break;
        }

        for (var index = 0; index < line.Length; index += 112)
        {
            yield return line.Substring(index, Math.Min(112, line.Length - index));
        }
    }

    private static string NormalizeText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var symbol in value)
        {
            builder.Append(symbol switch
            {
                'А' => "A",
                'Б' => "B",
                'В' => "V",
                'Г' => "G",
                'Д' => "D",
                'Е' or 'Ё' => "E",
                'Ж' => "Zh",
                'З' => "Z",
                'И' => "I",
                'Й' => "Y",
                'К' => "K",
                'Л' => "L",
                'М' => "M",
                'Н' => "N",
                'О' => "O",
                'П' => "P",
                'Р' => "R",
                'С' => "S",
                'Т' => "T",
                'У' => "U",
                'Ф' => "F",
                'Х' => "Kh",
                'Ц' => "Ts",
                'Ч' => "Ch",
                'Ш' => "Sh",
                'Щ' => "Sch",
                'Ъ' => string.Empty,
                'Ы' => "Y",
                'Ь' => string.Empty,
                'Э' => "E",
                'Ю' => "Yu",
                'Я' => "Ya",
                'а' => "a",
                'б' => "b",
                'в' => "v",
                'г' => "g",
                'д' => "d",
                'е' or 'ё' => "e",
                'ж' => "zh",
                'з' => "z",
                'и' => "i",
                'й' => "y",
                'к' => "k",
                'л' => "l",
                'м' => "m",
                'н' => "n",
                'о' => "o",
                'п' => "p",
                'р' => "r",
                'с' => "s",
                'т' => "t",
                'у' => "u",
                'ф' => "f",
                'х' => "kh",
                'ц' => "ts",
                'ч' => "ch",
                'ш' => "sh",
                'щ' => "sch",
                'ъ' => string.Empty,
                'ы' => "y",
                'ь' => string.Empty,
                'э' => "e",
                'ю' => "yu",
                'я' => "ya",
                '№' => "No.",
                '–' or '—' => "-",
                '·' => "-",
                '\u00a0' => " ",
                _ when symbol <= 127 => symbol.ToString(),
                _ => "?"
            });
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        stream.Write(Encoding.ASCII.GetBytes(value));
    }
}
