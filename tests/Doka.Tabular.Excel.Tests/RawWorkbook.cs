using System.IO.Compression;

namespace Doka.Tabular.Excel.Tests;

/// <summary>Writes minimal XLSX packages from raw part XML, for cases Excel itself cannot produce.</summary>
internal static class RawWorkbook
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static MemoryStream Create(
        string sheetData,
        string? sharedStrings = null,
        string? sheetPrefix = null,
        string sheetAttributes = "",
        string workbookBeforeSheets = "",
        string workbookAfterSheets = "",
        string? styles = null
    )
    {
        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] =
                """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>""",
            ["_rels/.rels"] =
                """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] =
                $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="{Main}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">{workbookBeforeSheets}<sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>{workbookAfterSheets}</workbook>""",
            ["xl/_rels/workbook.xml.rels"] =
                """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/></Relationships>""",
            ["xl/worksheets/sheet1.xml"] =
                $"""{sheetPrefix ?? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"}<worksheet xmlns="{Main}"{sheetAttributes}><sheetData>{sheetData}</sheetData></worksheet>""",
            ["xl/sharedStrings.xml"] =
                $"""<?xml version="1.0" encoding="UTF-8"?><sst xmlns="{Main}">{sharedStrings}</sst>""",
        };

        if (styles is not null)
        {
            parts["[Content_Types].xml"] = parts["[Content_Types].xml"]
                .Replace("</Types>",
                    """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""",
                    StringComparison.Ordinal);

            parts["xl/_rels/workbook.xml.rels"] = parts["xl/_rels/workbook.xml.rels"]
                .Replace("</Relationships>",
                    """<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""",
                    StringComparison.Ordinal);

            parts["xl/styles.xml"] =
                $"""<?xml version="1.0" encoding="UTF-8"?><styleSheet xmlns="{Main}">{styles}</styleSheet>""";
        }

        return Zip(parts);
    }

    public static MemoryStream Zip(
        IReadOnlyDictionary<string, string> parts,
        CompressionLevel level = CompressionLevel.Optimal
    )
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                using var writer = new StreamWriter(archive
                    .CreateEntry(name, level)
                    .Open());

                writer.Write(content);
            }
        }

        stream.Position = 0;

        return stream;
    }

    /// <summary>A workbook whose sheet expands to about <paramref name="megabytes" /> MiB of repeated empty rows.</summary>
    public static MemoryStream Expanding(
        int megabytes
    )
    {
        const string row = "<row r=\"1\"/>";
        var data = string.Concat(Enumerable.Repeat(row, megabytes * 1024 * 1024 / row.Length));

        return Create(data);
    }

    public static byte[] Bytes(
        Stream stream
    )
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }
}
