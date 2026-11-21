using System.Xml;

// WHY: These writers write into the XmlWriter buffer of a ZIP entry whose output lands in the workbook's drain
// buffer first (AsyncDrainStream); element writes never reach the caller's stream synchronously, and the flushes
// that move data on are asynchronous.
// ReSharper disable MethodHasAsyncOverload

namespace Doka.Tabular.Excel;

/// <summary>Writes one worksheet; buffers a sample of at most 200 rows or 1 MiB to estimate column widths.</summary>
internal sealed partial class ExcelSheetWriter : ITableWriter
{
    private const int SampleRows = 200;
    private const long SampleBytes = 1024 * 1024;
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly ExcelHorizontalAlignment?[] _alignments;
    private readonly int[] _headerStyles;
    private readonly int _index;

    // WHY: A column writes the same number format for nearly every cell; the last style per column skips the two
    // registry lookups. The format is compared by reference, so an equal format in a new string only costs a lookup.
    private readonly string?[] _lastFormats;
    private readonly int[] _lastStyles;
    private readonly ExcelSheetPlan _plan;
    private readonly List<OutputCell[]> _sample = [];
    private readonly char[] _scratch = new char[64];
    private readonly double[] _widths;
    private readonly ExcelWorkbookWriter _workbook;
    private long _dataRows;
    private Stream? _entry;
    private bool _headerPending;
    private long _sampleBytes;
    private bool _streaming;
    private XmlWriter? _xml;

    public ExcelSheetWriter(
        ExcelWorkbookWriter workbook,
        ExcelSheetPlan plan,
        int index
    )
    {
        _workbook = workbook;
        _plan = plan;
        _index = index;
        _widths = new double[plan.Columns.Count];
        _headerStyles = new int[plan.Columns.Count];
        _alignments = new ExcelHorizontalAlignment?[plan.Columns.Count];
        _lastFormats = new string?[plan.Columns.Count];
        _lastStyles = new int[plan.Columns.Count];
        Array.Fill(_lastStyles, -1);
        for (var position = 0; position < plan.Columns.Count; position++)
        {
            var column = plan.Columns[position];
            var layout = column.Field is null
                ? null
                : plan.Layout.Columns?.GetValueOrDefault(column.Field.Key);

            _widths[position] = column.IsGap
                ? 3
                : BaseWidth(column);

            if (column is { IsGap: false, Header: { } header })
            {
                _widths[position] = Math.Max(_widths[position],
                    Math.Min(header.Length + 2, plan.Layout.MaximumAutoWidth));
            }

            _headerStyles[position] = workbook.Styles.HeaderStyle(plan.Layout, layout?.HorizontalAlignment);
            _alignments[position] = layout?.HorizontalAlignment;
        }
    }

    public long NextRowNumber => _plan.Layout.StartRow
        + (_plan.WriteHeader
            ? 1
            : 0)
        + _dataRows;

    public long RowNumberOf(
        ReadOnlySpan<OutputCell> preceding
    ) => NextRowNumber;

    public ValueTask WriteHeaderAsync(
        IReadOnlyList<ExportColumn> columns,
        CancellationToken cancellationToken
    )
    {
        _headerPending = true;

        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteRowAsync(
        IReadOnlyList<OutputCell> cells,
        long recordNumber,
        CancellationToken cancellationToken
    )
    {
        var row = NextRowNumber;
        CheckLimits(cells, row, recordNumber);
        _dataRows++;
        if (!_streaming)
        {
            var bytes = TextBytes(cells);
            if (_sampleBytes + bytes <= SampleBytes)
            {
                // WHY: Cell texts are views into the row buffer of the export; a sampled row outlives it, so it
                // keeps copies of its texts.
                var copy = new OutputCell[cells.Count];
                for (var position = 0; position < copy.Length; position++)
                {
                    copy[position] = cells[position] with
                    {
                        Text = cells[position]
                            .Text
                            .ToString()
                            .AsMemory(),
                    };
                }

                if (Sample(copy, bytes))
                {
                    await StartStreamingAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                return;
            }

            // WHY: A row beyond the preview budget ends the sample without being kept; it is written directly.
            await StartStreamingAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        WriteDataRow(cells, row);
        await _xml!
            .FlushAsync()
            .ConfigureAwait(false);

        await _workbook
            .DrainAsync(force: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask CompleteTableAsync(
        CancellationToken cancellationToken
    )
    {
        if (!_streaming)
        {
            await StartStreamingAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var xml = _xml!;
        xml.WriteEndElement();
        var range = DataRange();
        var hasData = _dataRows > 0;
        var filter = _plan is { WriteHeader: true, Layout.Filter: not ExcelHeaderFeature.Disabled } && hasData;
        var table = _plan is { IsTable: true, WriteHeader: true } && hasData;
        int? tableId = table
            ? _workbook.NextTableId()
            : null;

        WriteSheetEnd(xml, range, filter && !table, tableId is not null);
        await CloseEntryAsync(xml, cancellationToken)
            .ConfigureAwait(false);

        if (tableId is { } id)
        {
            await WriteTablePartsAsync(id, range, filter, cancellationToken)
                .ConfigureAwait(false);
        }

        _workbook.SheetCompleted(_plan,
            _index,
            filter && !table
                ? range
                : null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_xml is not null)
        {
            await _xml
                .DisposeAsync()
                .ConfigureAwait(false);
        }

        if (_entry is not null)
        {
            await _entry
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rejects a row beyond the last sheet row, a text beyond the cell limits of Excel, and a record without any cell,
    /// which the import skips as an empty row.
    /// </summary>
    private void CheckLimits(
        IReadOnlyList<OutputCell> cells,
        long row,
        long recordNumber
    )
    {
        if (row > ExcelWorkbookWriter.MaxRows)
        {
            throw new TabularRowException(TabularError.Create(TabularErrorCodes.Schema.LimitExceeded,
                TabularErrorKind.Data,
                "The sheet exceeds 1,048,576 rows.",
                new TabularErrorLocation
                {
                    RowNumber = row,
                    RecordNumber = recordNumber,
                    SheetName = _plan.Name,
                }));
        }

        if (AllEmpty(cells))
        {
            throw new TabularRowException(TabularError.Create(TabularErrorCodes.Conversion.MissingRepresentation,
                TabularErrorKind.Data,
                "Every field of the record is empty; Excel stores no cell, and the import skips the row.",
                new TabularErrorLocation
                {
                    RowNumber = row,
                    RecordNumber = recordNumber,
                    SheetName = _plan.Name,
                }));
        }

        for (var position = 0; position < cells.Count; position++)
        {
            if (cells[position] is { Kind: OutputKind.Text, Text: var text }
                && (text.Length > ExcelWorkbookWriter.MaxCellCharacters
                    || text.Span.Count('\n') > ExcelWorkbookWriter.MaxLineBreaks))
            {
                throw new TabularRowException(TabularError.Create(TabularErrorCodes.Schema.LimitExceeded,
                    TabularErrorKind.Data,
                    "A cell text exceeds 32,767 characters or 253 line breaks.",
                    new TabularErrorLocation
                    {
                        RowNumber = row,
                        RecordNumber = recordNumber,
                        SheetName = _plan.Name,
                        ColumnKey = _plan.Columns[position].Field?.Key,
                    }));
            }
        }
    }

    private static bool AllEmpty(
        IReadOnlyList<OutputCell> cells
    )
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var position = 0; position < cells.Count; position++)
        {
            if (cells[position].Kind != OutputKind.Empty)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The preview payload of a row: two bytes per character of its texts.</summary>
    private static long TextBytes(
        IReadOnlyList<OutputCell> cells
    )
    {
        long bytes = 0;

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var position = 0; position < cells.Count; position++)
        {
            bytes += (long)cells[position].Text.Length * 2;
        }

        return bytes;
    }

    /// <summary>Keeps a row of the width sample; returns whether the sample is complete and streaming starts.</summary>
    private bool Sample(
        OutputCell[] row,
        long bytes
    )
    {
        _sample.Add(row);
        _sampleBytes += bytes;
        for (var position = 0; position < row.Length; position++)
        {
            if (!_plan.Columns[position].IsGap
                && row[position].Kind == OutputKind.Text)
            {
                _widths[position] = Math.Max(_widths[position],
                    Math.Min(row[position].Text.Length + 2, _plan.Layout.MaximumAutoWidth));
            }
        }

        return _sample.Count >= SampleRows || _sampleBytes >= SampleBytes;
    }

    /// <summary>The range of header and data rows; an empty table still spans its first row.</summary>
    private string DataRange()
    {
        var headerRows = _plan.WriteHeader
            ? 1
            : 0;

        var lastRow = _plan.Layout.StartRow + headerRows + _dataRows - 1;
        var firstColumn = _plan.Layout.StartColumn - 1;
        var lastColumn = firstColumn + Math.Max(_plan.Columns.Count, 1) - 1;

        return
            $"{ExcelXml.Reference(firstColumn, _plan.Layout.StartRow)}:{ExcelXml.Reference(lastColumn, Math.Max(lastRow, _plan.Layout.StartRow))}";
    }

    /// <summary>Writes the sheet filter or the table part reference and closes the worksheet element.</summary>
    private static void WriteSheetEnd(
        XmlWriter xml,
        string range,
        bool sheetFilter,
        bool tablePart
    )
    {
        if (sheetFilter)
        {
            xml.WriteStartElement("autoFilter", MainNamespace);
            xml.WriteAttributeString("ref", range);
            xml.WriteEndElement();
        }

        if (tablePart)
        {
            xml.WriteStartElement("tableParts", MainNamespace);
            xml.WriteAttributeString("count", "1");
            xml.WriteStartElement("tablePart", MainNamespace);
            xml.WriteAttributeString("id",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
                "rId1");

            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    private async Task CloseEntryAsync(
        XmlWriter xml,
        CancellationToken cancellationToken
    )
    {
        await xml
            .FlushAsync()
            .ConfigureAwait(false);

        await xml
            .DisposeAsync()
            .ConfigureAwait(false);

        await _entry!
            .DisposeAsync()
            .ConfigureAwait(false);

        await _workbook
            .DrainAsync(force: true, cancellationToken)
            .ConfigureAwait(false);

        _xml = null;
        _entry = null;
    }

    /// <summary>Writes the relationship of the sheet to its table and the table part itself.</summary>
    private async Task WriteTablePartsAsync(
        int id,
        string range,
        bool filter,
        CancellationToken cancellationToken
    )
    {
        var number = id.ToString(CultureInfo.InvariantCulture);
        await _workbook
            .WriteEntryAsync($"xl/worksheets/_rels/sheet{_index}.xml.rels",
                ExcelWorkbookWriter.Raw(
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + $"<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/table\" Target=\"../tables/table{number}.xml\"/>"
                    + "</Relationships>"),
                cancellationToken)
            .ConfigureAwait(false);

        await _workbook
            .WriteEntryAsync($"xl/tables/table{number}.xml",
                TableXml(id, range, filter),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Writes the column widths: the configured width, otherwise the width measured from the sample.</summary>
    private void WriteColumns(
        XmlWriter xml
    )
    {
        if (_plan.Columns.Count == 0)
        {
            return;
        }

        xml.WriteStartElement("cols", MainNamespace);
        for (var position = 0; position < _plan.Columns.Count; position++)
        {
            var width = LayoutOf(_plan.Columns[position])
                    ?.Width
                ?? _widths[position];

            var number = (_plan.Layout.StartColumn + position).ToString(CultureInfo.InvariantCulture);
            xml.WriteStartElement("col", MainNamespace);
            xml.WriteAttributeString("min", number);
            xml.WriteAttributeString("max", number);
            xml.WriteAttributeString("width", width.ToString("0.##", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("customWidth", "1");
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private ExcelColumnLayout? LayoutOf(
        ExportColumn column
    ) => column.Field is null
        ? null
        : _plan.Layout.Columns?.GetValueOrDefault(column.Field.Key);

    private async Task StartStreamingAsync(
        CancellationToken cancellationToken
    )
    {
        _streaming = true;
        var entry = _workbook.CreateEntry($"xl/worksheets/sheet{_index}.xml");
        _entry = await entry
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        // WHY: The sheet XML is written synchronously into the archive, which buffers in AsyncDrainStream; the output
        // is written only by DrainAsync with the token. An async overload per element would add a task per cell.
        var xml = XmlWriter.Create(_entry, ExcelXml.WriterSettings);
        _xml = xml;
        xml.WriteStartDocument(standalone: true);
        xml.WriteStartElement("worksheet", MainNamespace);
        xml.WriteAttributeString("xmlns",
            "r",
            null,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        WriteViews(xml);
        xml.WriteStartElement("sheetFormatPr", MainNamespace);
        xml.WriteAttributeString("defaultRowHeight", "15");
        xml.WriteEndElement();
        WriteColumns(xml);
        xml.WriteStartElement("sheetData", MainNamespace);
        if (_headerPending)
        {
            WriteHeaderRow(xml);
        }

        var row = _plan.Layout.StartRow
            + (_plan.WriteHeader
                ? 1
                : 0);

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var cellsIndex = 0; cellsIndex < _sample.Count; cellsIndex++)
        {
            var cells = _sample[cellsIndex];
            WriteDataRow(cells, row++);
        }

        _sample.Clear();
        await xml
            .FlushAsync()
            .ConfigureAwait(false);
    }
}
