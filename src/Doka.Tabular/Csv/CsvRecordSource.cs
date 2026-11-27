using System.Buffers;
using System.Text;

namespace Doka.Tabular.Csv;

/// <summary>
/// RFC 4180 record reader with the named dialect rules of Doka.Tabular. The fields of a record are views into a
/// record buffer that the reader reuses; they are valid until the next record is read.
/// </summary>
internal sealed partial class CsvRecordSource : IRecordSource
{
    private const char Quote = '"';
    private readonly List<SourceCell> _cells = [];
    private readonly List<int> _lineOffsets = [];
    private readonly SearchValues<char> _quotedStops = SearchValues.Create("\"\r\n");
    private readonly CharReader _reader;
    private readonly SourceRecord _record = new(0, []);
    private readonly CsvReadSettings _settings;
    private readonly TextBuffer _text = new(1024);
    private readonly SearchValues<char> _unquotedStops;
    private bool _ended;
    private long _line = 1;
    private long _recordLine;

    public CsvRecordSource(
        CharReader reader,
        CsvReadSettings settings
    )
    {
        _reader = reader;
        _settings = settings;
        _unquotedStops = SearchValues.Create(settings.Delimiter, Quote, '\r', '\n');
    }

    public TabularSheetName? SheetName => null;

    public bool MissingIsStructural => true;

    public async ValueTask<RecordReadResult> ReadAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await ReadRecordAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DecoderFallbackException)
        {
            _ended = true;

            return RecordReadResult.FatalError(
                TabularError.Create(
                    TabularErrorCodes.Parsing.InvalidEncoding,
                    TabularErrorKind.Data,
                    "The input contains bytes that are invalid in the chosen encoding.",
                    new TabularErrorLocation { RowNumber = _line }));
        }
    }

    private async ValueTask<RecordReadResult> ReadRecordAsync(
        CancellationToken cancellationToken
    )
    {
        while (!_ended)
        {
            if (!await _reader
                .EnsureAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                _ended = true;

                break;
            }

            var recordLine = _line;
            _recordLine = recordLine;
            if (_reader.Buffered[0] is not ('\r' or '\n'))
            {
                _text.Clear();
                _cells.Clear();
                _lineOffsets.Clear();

                return await ReadFieldsAsync(recordLine, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (await ReadEmptyLineAsync(recordLine, cancellationToken)
                    .ConfigureAwait(false) is { } empty)
            {
                return empty;
            }
        }

        return RecordReadResult.EndOfData;
    }

    /// <summary>Reads an empty physical line outside quoted fields; <see langword="null" /> when it is skipped.</summary>
    private async ValueTask<RecordReadResult?> ReadEmptyLineAsync(
        long recordLine,
        CancellationToken cancellationToken
    )
    {
        var lineBreak = await ReadLineBreakAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lineBreak is LineBreak.LoneCr
            || (lineBreak is LineBreak.LoneLf && _settings.RequireCrLf))
        {
            return RecordReadResult.Broken(
                SyntaxError(
                    recordLine,
                    null,
                    lineBreak == LineBreak.LoneCr
                        ? "A carriage return without line feed is not a record separator."
                        : "A single line feed is not allowed as record separator."));
        }

        // WHY: A single trailing line break is consumed with its record, so every empty line seen here is a
        //      complete physical line.
        return _settings.EmptyLinesAsRecords
            ? RecordReadResult.Of(new SourceRecord(recordLine, [SourceCell.FromText(ReadOnlyMemory<char>.Empty)]))
            : null;
    }

    private async ValueTask<RecordReadResult> EndRecordAsync(
        long recordLine,
        CancellationToken cancellationToken
    )
    {
        var lineBreak = await ReadLineBreakAsync(cancellationToken)
            .ConfigureAwait(false);

        return lineBreak switch
        {
            LineBreak.LoneCr => RecordReadResult.Broken(
                SyntaxError(
                    recordLine,
                    null,
                    "A carriage return without line feed is not a record separator.")),
            LineBreak.LoneLf when _settings.RequireCrLf => RecordReadResult.Broken(
                SyntaxError(
                    recordLine,
                    null,
                    "A single line feed is not allowed as record separator.")),
            LineBreak.CrLf or LineBreak.LoneLf => Record(recordLine),
            _ => throw new UnreachableException("The line break reader returns only declared values."),
        };
    }

    private RecordReadResult Record(
        long recordLine
    ) => RecordReadResult.Of(_record.Set(recordLine, _cells, _lineOffsets));

    /// <summary>Reads the line break at the current position, which the caller has seen.</summary>
    private async ValueTask<LineBreak> ReadLineBreakAsync(
        CancellationToken cancellationToken
    )
    {
        var character = await _reader
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);

        _line++;
        if (character != '\r')
        {
            return LineBreak.LoneLf;
        }

        if (await _reader
                .PeekAsync(cancellationToken)
                .ConfigureAwait(false)
            != '\n')
        {
            return LineBreak.LoneCr;
        }

        _reader.Advance(1);

        return LineBreak.CrLf;
    }

    /// <summary>Consumes a line break whose first character was already read.</summary>
    private async ValueTask FinishLineBreakAsync(
        int first,
        CancellationToken cancellationToken
    )
    {
        _line++;
        if (first == '\r'
            && await _reader
                .PeekAsync(cancellationToken)
                .ConfigureAwait(false)
            == '\n')
        {
            _reader.Advance(1);
        }
    }

    private async ValueTask SkipLineAsync(
        CancellationToken cancellationToken
    )
    {
        while (await _reader
            .EnsureAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            var buffered = _reader.Buffered;
            var end = buffered.IndexOfAny('\r', '\n');
            if (end < 0)
            {
                _reader.Advance(buffered.Length);
                continue;
            }

            var lineBreak = buffered[end];
            _reader.Advance(end + 1);
            await FinishLineBreakAsync(lineBreak, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        _ended = true;
    }

    /// <summary>Appends field text and enforces the field length limit.</summary>
    private void Append(
        ReadOnlySpan<char> text,
        int fieldStart
    )
    {
        _text.Append(text);
        if (_settings.MaxFieldCharacters is { } limit
            && _text.Length - fieldStart > limit)
        {
            throw TabularResourceException.Limit("field characters", limit);
        }
    }

    /// <summary>Adds the field read since <paramref name="fieldStart" />, which starts on the physical line.</summary>
    private void AddField(
        int fieldStart,
        long fieldLine
    )
    {
        _cells.Add(SourceCell.FromText(_text.Slice(fieldStart)));
        _lineOffsets.Add((int)(fieldLine - _recordLine));
    }

    private static TabularError SyntaxError(
        long line,
        int? columnIndex,
        string message
    ) => TabularError.Create(
        TabularErrorCodes.Parsing.InvalidSyntax,
        TabularErrorKind.Data,
        message,
        new TabularErrorLocation
        {
            RowNumber = line,
            ColumnIndex = columnIndex,
        });

    private enum LineBreak
    {
        CrLf,
        LoneLf,
        LoneCr,
    }
}
