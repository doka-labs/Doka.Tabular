using System.Xml;

namespace Doka.Tabular.Excel;

/// <summary>Streams the rows of one worksheet as records.</summary>
internal sealed partial class ExcelSheetSource : IRecordSource, IAsyncDisposable
{
    private readonly List<SourceCell> _cells = [];
    private readonly char[] _chunk = new char[1024];
    private readonly ExcelPackage _package;
    private readonly SourceRecord _record = new(0, []);
    private readonly ExcelReadSettings _settings;
    private readonly ExcelSheetInfo _sheet;

    // WHY: Cell texts are views into this buffer; it is cleared per row, like the reused cell list.
    private readonly TextBuffer _text = new(4096);
    private SheetRow? _carry;
    private bool _drainPending;
    private bool _ended;
    private bool _headerPending;
    private int _inlineLength;
    private int _inlineStart;
    private bool _isFormula;
    private long _lastRowNumber;
    private XmlReader? _reader;
    private Stream? _stream;
    private int _valueLength;
    private int _valueStart;

    public ExcelSheetSource(
        ExcelPackage package,
        ExcelSheetInfo sheet,
        ExcelReadSettings settings
    )
    {
        _package = package;
        _sheet = sheet;
        _settings = settings;
        _headerPending = settings.EmitHeaderRow;
    }

    public async ValueTask DisposeAsync()
    {
        _reader?.Dispose();
        if (_stream is not null)
        {
            await _stream
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    public TabularSheetName SheetName => _sheet.Name;

    public bool MissingIsStructural => false;

    public async ValueTask<RecordReadResult> ReadAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await ReadRowAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (XmlException)
        {
            _ended = true;

            return RecordReadResult.FatalError(
                TabularError.Create(
                    TabularErrorCodes.Parsing.InvalidSyntax,
                    TabularErrorKind.Data,
                    "The worksheet is not well-formed XML.",
                    new TabularErrorLocation { SheetName = _sheet.Name }));
        }
        catch (InvalidDataException)
        {
            _ended = true;

            return RecordReadResult.FatalError(
                TabularError.Create(
                    TabularErrorCodes.Parsing.InvalidSyntax,
                    TabularErrorKind.Data,
                    "The worksheet part of the ZIP container is damaged.",
                    new TabularErrorLocation { SheetName = _sheet.Name }));
        }
        catch (FormatException exception)
        {
            _ended = true;

            return RecordReadResult.FatalError(
                TabularError.Create(
                    TabularErrorCodes.Parsing.InvalidSyntax,
                    TabularErrorKind.Data,
                    $"The worksheet contains an invalid cell: {exception.Message}",
                    new TabularErrorLocation { SheetName = _sheet.Name }));
        }
    }

    private async ValueTask<RecordReadResult> ReadRowAsync(
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            var row = _carry
                ?? await NextRowAsync(cancellationToken)
                    .ConfigureAwait(false);

            _carry = null;
            var result = _headerPending
                ? TakeHeader(row)
                : Select(row);

            if (result is not { } record)
            {
                continue;
            }

            if (_drainPending)
            {
                _drainPending = false;
                await DrainAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return record;
        }
    }

    /// <summary>The header record at the start row; an absent header row gives an empty header.</summary>
    private RecordReadResult? TakeHeader(
        SheetRow? row
    )
    {
        if (row is null
            || row.Value.Number > _settings.StartRow)
        {
            _carry = row;
            _headerPending = false;

            return RecordReadResult.Of(new SourceRecord(_settings.StartRow, []));
        }

        if (row.Value.Number != _settings.StartRow)
        {
            return null;
        }

        _headerPending = false;

        return RecordReadResult.Of(_record.Set(row.Value.Number, row.Value.Cells));
    }

    /// <summary>The record of a data row inside the read range; <see langword="null" /> skips the row.</summary>
    private RecordReadResult? Select(
        SheetRow? row
    )
    {
        if (row is not { } current)
        {
            return RecordReadResult.EndOfData;
        }

        if (current.Number < _settings.StartRow
            || (_settings.EmitHeaderRow && current.Number == _settings.StartRow))
        {
            return null;
        }

        if (_settings.EndRow is { } end
            && current.Number > end)
        {
            _ended = true;
            _drainPending = true;

            return RecordReadResult.EndOfData;
        }

        return !current.HasContent || (_settings.VisibleRowsOnly && current.Hidden)
            ? null
            : RecordReadResult.Of(_record.Set(current.Number, current.Cells));
    }

    private async ValueTask<SheetRow?> NextRowAsync(
        CancellationToken cancellationToken
    )
    {
        if (_ended)
        {
            return null;
        }

        if (_reader is null)
        {
            _stream = await _package
                .OpenPartAsync(_sheet.PartPath, cancellationToken)
                .ConfigureAwait(false);

            _reader = XmlReader.Create(_stream, ExcelXml.ReaderSettings);
        }

        var reader = _reader;
        while (await reader
            .ReadAsync()
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader is
                {
                    NodeType: XmlNodeType.Element,
                    LocalName: "row",
                    NamespaceURI: ExcelXml.TransitionalMain or ExcelXml.StrictMain,
                })
            {
                return await ReadRowCellsAsync(reader, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        _ended = true;

        return null;
    }

    /// <summary>Reads the cells of the current row element; missing cells before a cell become empty cells.</summary>
    private async ValueTask<SheetRow> ReadRowCellsAsync(
        XmlReader reader,
        CancellationToken cancellationToken
    )
    {
        // WHY: XmlReader creates a string for every attribute value it returns, also through ReadAttributeValue
        // and ReadValueChunk; attributes are therefore read as strings.
        var number = long.TryParse(
            reader.GetAttribute("r"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;

        var hidden = reader.GetAttribute("hidden") is "1" or "true";

        // WHY: Only one row is in use at a time, also when it is carried to the next read, so the list and the
        // text buffer are reused.
        _cells.Clear();
        _text.Clear();
        if (reader.IsEmptyElement)
        {
            return new SheetRow(NumberOf(number), hidden, _cells, false);
        }

        var hasContent = false;
        var rowDepth = reader.Depth;
        var nextColumn = 0;
        long? resolved = null;
        var selected = true;
        while (await reader
            .ReadAsync()
            .ConfigureAwait(false))
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == rowDepth)
            {
                break;
            }

            if (reader is not
                {
                    NodeType: XmlNodeType.Element,
                    LocalName: "c",
                    NamespaceURI: ExcelXml.TransitionalMain or ExcelXml.StrictMain,
                })
            {
                continue;
            }

            var reference = reader.GetAttribute("r");
            var column = reference is null
                ? nextColumn
                : ExcelXml.ColumnIndex(reference);

            if (number == 0
                && reference is not null)
            {
                number = ExcelXml.RowOf(reference);
            }

            nextColumn = column + 1;
            if (resolved is null)
            {
                resolved = NumberOf(number);
                selected = Selects(resolved.Value, hidden);
            }

            // WHY: Values outside the read range are neither processed nor validated; the XML is still parsed.
            if (!selected)
            {
                await SkipElementAsync(reader)
                    .ConfigureAwait(false);

                continue;
            }

            var cell = await ReadCellAsync(reader, cancellationToken)
                .ConfigureAwait(false);

            Place(_cells, column, cell);
            hasContent |= cell.HasContent;
        }

        return new SheetRow(resolved ?? NumberOf(number), hidden, _cells, hasContent);
    }

    /// <summary>Whether the cells of a row are read: the header row and data rows inside the selection.</summary>
    private bool Selects(
        long number,
        bool hidden
    ) => (_settings.EmitHeaderRow && number == _settings.StartRow)
        || (number >= _settings.StartRow
            && (_settings.EndRow is not { } end || number <= end)
            && !(_settings.VisibleRowsOnly && hidden));

    /// <summary>
    /// Reads the rest of the worksheet after the end row without interpreting it, so that damaged XML or a damaged
    /// ZIP entry is still a parser error.
    /// </summary>
    private async ValueTask DrainAsync(
        CancellationToken cancellationToken
    )
    {
        while (_reader is not null
            && await _reader
                .ReadAsync()
                .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    /// <summary>Moves to the end of the current element without reading its content.</summary>
    private static async ValueTask SkipElementAsync(
        XmlReader reader
    )
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var depth = reader.Depth;
        while (await reader
                .ReadAsync()
                .ConfigureAwait(false)
            && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)) { }
    }

    /// <summary>
    /// The row number from the row or its first cell reference; both are optional in a valid sheet, and a row without
    /// them follows the previous row.
    /// </summary>
    private long NumberOf(
        long found
    )
    {
        _lastRowNumber = found > 0
            ? found
            : _lastRowNumber + 1;

        return _lastRowNumber;
    }

    /// <summary>Adds the cell at its column; skipped columns become empty cells.</summary>
    private static void Place(
        List<SourceCell> cells,
        int column,
        SourceCell cell
    )
    {
        while (cells.Count < column)
        {
            cells.Add(SourceCell.Empty);
        }

        cells.Add(cell);
    }
}
