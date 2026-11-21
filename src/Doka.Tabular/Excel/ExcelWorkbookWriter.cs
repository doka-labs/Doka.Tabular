using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Doka.Tabular.Excel;

/// <summary>Writes an XLSX Transitional workbook sheet by sheet into a (possibly forward-only) stream.</summary>
internal sealed class ExcelWorkbookWriter : IAsyncDisposable
{
    public const int MaxRows = 1048576;
    public const int MaxColumns = 16384;
    public const int MaxCellCharacters = 32767;
    public const int MaxLineBreaks = 253;
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const int DrainThreshold = 64 * 1024;
    private readonly AsyncDrainStream _output;
    private readonly List<(ExcelSheetPlan Plan, int Index, bool HasFilter, string? FilterRange)> _sheets = [];
    private ZipArchive? _archive;
    private int _tables;

    public ExcelWorkbookWriter(
        Stream output
    )
    {
        _output = new AsyncDrainStream(output);
    }

    public ExcelStyleRegistry Styles { get; } = new();

    /// <summary>
    /// Releases an archive that was not completed; its buffered bytes are discarded and never reach the output, so a
    /// call that stops before its first output writes nothing.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_archive is not null)
        {
            try
            {
                await _archive
                    .DisposeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                or NotSupportedException
                or ObjectDisposedException
                or InvalidOperationException)
            {
                // WHY: Disposal after a failed export only releases resources; the failure is already reported.
            }

            _archive = null;
        }

        await _output
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an archive entry with the fixed DOS epoch as its time, so equal data give equal bytes; the archive would
    /// otherwise stamp the clock of the machine, past the time provider of the call.
    /// </summary>
    internal ZipArchiveEntry CreateEntry(
        string name
    )
    {
        var entry = _archive!.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

        return entry;
    }

    public async Task<ExcelSheetWriter> AddSheetAsync(
        ExcelSheetPlan plan,
        CancellationToken cancellationToken
    )
    {
        _archive ??= await ZipArchive
            .CreateAsync(_output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken)
            .ConfigureAwait(false);

        return new ExcelSheetWriter(this, plan, _sheets.Count + 1);
    }

    /// <summary>Writes buffered archive bytes to the output; without <paramref name="force" /> only above a threshold.</summary>
    internal Task DrainAsync(
        bool force,
        CancellationToken cancellationToken
    ) => _output.DrainAsync(force
            ? 0
            : DrainThreshold,
        cancellationToken);

    internal int NextTableId() => ++_tables;

    internal void SheetCompleted(
        ExcelSheetPlan plan,
        int index,
        string? filterRange
    ) => _sheets.Add((plan, index, filterRange is not null, filterRange));

    public async Task CompleteAsync(
        CancellationToken cancellationToken
    )
    {
        _archive ??= await ZipArchive
            .CreateAsync(_output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken)
            .ConfigureAwait(false);

        await WriteEntryAsync("xl/workbook.xml", WorkbookXml(), cancellationToken)
            .ConfigureAwait(false);

        await WriteEntryAsync("xl/_rels/workbook.xml.rels", WorkbookRelationships(), cancellationToken)
            .ConfigureAwait(false);

        await WriteEntryAsync("xl/styles.xml", Styles.ToXml(), cancellationToken)
            .ConfigureAwait(false);

        await WriteEntryAsync("_rels/.rels",
                Raw(
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                    + "</Relationships>"),
                cancellationToken)
            .ConfigureAwait(false);

        await WriteEntryAsync("[Content_Types].xml", ContentTypes(), cancellationToken)
            .ConfigureAwait(false);

        await _archive
            .DisposeAsync()
            .ConfigureAwait(false);

        _archive = null;
        await DrainAsync(force: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Writes a part that was built in memory as one archive entry.</summary>
    internal async Task WriteEntryAsync(
        string name,
        byte[] content,
        CancellationToken cancellationToken
    )
    {
        var entry = CreateEntry(name);
        var stream = await entry
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            await stream
                .WriteAsync(content, cancellationToken)
                .ConfigureAwait(false);
        }

        await DrainAsync(force: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The UTF-8 bytes of a part given as markup, with the XML declaration.</summary>
    internal static byte[] Raw(
        string xml
    ) => Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml);

    /// <summary>Builds the workbook part in memory.</summary>
    private byte[] WorkbookXml()
    {
        using var memory = new MemoryStream();
        using (var writer = XmlWriter.Create(memory, ExcelXml.WriterSettings))
        {
            WriteWorkbook(writer);
        }

        return memory.ToArray();
    }

    private void WriteWorkbook(
        XmlWriter writer
    )
    {
        writer.WriteStartDocument(standalone: true);
        writer.WriteStartElement("workbook", MainNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipNamespace);
        writer.WriteStartElement("workbookPr", MainNamespace);
        writer.WriteEndElement();
        writer.WriteStartElement("sheets", MainNamespace);
        foreach (var (plan, index, _, _) in _sheets)
        {
            writer.WriteStartElement("sheet", MainNamespace);
            writer.WriteAttributeString("name", ExcelXml.Escape(plan.Name.Value, attribute: true));
            writer.WriteAttributeString("sheetId", index.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("id", RelationshipNamespace, $"rId{index}");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        var filters = _sheets
            .Where(sheet => sheet.HasFilter)
            .ToList();

        if (filters.Count > 0)
        {
            writer.WriteStartElement("definedNames", MainNamespace);
            foreach (var (plan, index, _, range) in filters)
            {
                writer.WriteStartElement("definedName", MainNamespace);
                writer.WriteAttributeString("name", "_xlnm._FilterDatabase");
                writer.WriteAttributeString("localSheetId", (index - 1).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("hidden", "1");

                // WHY: The defined name is an ST_Formula, an ST_Xstring; characters XML cannot hold are escaped like the
                // sheet name attribute.
                writer.WriteString(ExcelXml.Escape(
                    $"'{plan.Name.Value.Replace("'", "''", StringComparison.Ordinal)}'!{AbsoluteRange(range!)}",
                    attribute: false));

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private byte[] WorkbookRelationships()
    {
        var builder =
            new StringBuilder("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

        foreach (var (_, index, _, _) in _sheets)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"<Relationship Id=\"rId{index}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{index}.xml\"/>");
        }

        builder.Append(CultureInfo.InvariantCulture,
            $"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");

        builder.Append("</Relationships>");
        return Raw(builder.ToString());
    }

    private byte[] ContentTypes()
    {
        var builder =
            new StringBuilder("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");

        builder.Append(
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");

        builder.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        builder.Append(
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");

        builder.Append(
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");

        foreach (var (_, index, _, _) in _sheets)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"<Override PartName=\"/xl/worksheets/sheet{index}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        for (var table = 1; table <= _tables; table++)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"<Override PartName=\"/xl/tables/table{table}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml\"/>");
        }

        builder.Append("</Types>");
        return Raw(builder.ToString());
    }

    private static string AbsoluteRange(
        string range
    )
    {
        var parts = range.Split(':');

        return string.Join(':',
            parts.Select(part =>
            {
                var letters = new string(part
                    .TakeWhile(char.IsAsciiLetter)
                    .ToArray());

                return $"${letters}${part[letters.Length..]}";
            }));
    }
}
