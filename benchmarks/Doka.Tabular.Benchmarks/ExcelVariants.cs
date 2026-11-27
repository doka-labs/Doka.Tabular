using System.IO.Compression;
using System.Text;

namespace Doka.Tabular.Benchmarks;

/// <summary>
/// Derives the import variants of a generated Transitional workbook: Strict with the namespaces of ECMA-376 Part 1
/// Strict, and XLSM with the macro-enabled workbook content type. The cell data stay the same.
/// </summary>
public static class ExcelVariants
{
    private const string TransitionalMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string StrictMain = "http://purl.oclc.org/ooxml/spreadsheetml/main";

    private const string TransitionalRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string StrictRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    private const string WorkbookType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string MacroWorkbookType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

    public static byte[] Convert(
        byte[] transitional,
        ProfileFormat format
    ) => format switch
    {
        ProfileFormat.Xlsx => transitional,
        ProfileFormat.XlsxStrict => Rewrite(transitional, ToStrict),
        ProfileFormat.Xlsm => Rewrite(
            transitional,
            (name, text) => name == "[Content_Types].xml"
                ? text.Replace(WorkbookType, MacroWorkbookType, StringComparison.Ordinal)
                : text),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is no Excel format."),
    };

    private static string ToStrict(
        string name,
        string text
    )
    {
        // WHY: Relationship types and the r prefix use the Strict namespace; the package relationship namespace of
        // the .rels parts stays, as in Strict files Excel writes.
        var strict = text
            .Replace(TransitionalMain, StrictMain, StringComparison.Ordinal)
            .Replace(TransitionalRelationships, StrictRelationships, StringComparison.Ordinal);

        return name == "xl/workbook.xml"
            ? strict.Replace("<workbook ", "<workbook conformance=\"strict\" ", StringComparison.Ordinal)
            : strict;
    }

    private static byte[] Rewrite(
        byte[] package,
        Func<string, string, string> rewrite
    )
    {
        using var source = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var target = new MemoryStream();
        using (var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = reader.ReadToEnd();
                var copy = archive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = new DateTimeOffset(
                    2024,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero);

                using var writer = new StreamWriter(copy.Open(), new UTF8Encoding(false));
                writer.Write(rewrite(entry.FullName, text));
            }
        }

        return target.ToArray();
    }
}
