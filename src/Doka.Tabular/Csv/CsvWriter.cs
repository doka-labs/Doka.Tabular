using System.Buffers;
using System.Text;

namespace Doka.Tabular.Csv;

internal sealed record CsvWriteSettings(
    char Delimiter,
    Encoding Encoding,
    bool WriteBom
);

/// <summary>RFC 4180 writer: delimiter between fields, CRLF between records, quoting where needed.</summary>
internal sealed class CsvTableWriter : ITableWriter
{
    private const char Quote = '"';
    private readonly byte[] _delimiter;
    private readonly Encoder _encoder;
    private readonly byte[] _lineBreak;
    private readonly Stream _output;
    private readonly MemoryStream _pending = new();
    private readonly SearchValues<char> _quoteTriggers;
    private readonly CsvWriteSettings _settings;
    private byte[] _bytes = new byte[1024];
    private long _nextLine = 1;
    private long _records;
    private bool _started;

    public CsvTableWriter(
        Stream output,
        CsvWriteSettings settings
    )
    {
        _output = output;
        _settings = settings;
        var strict = (Encoding)settings.Encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        _encoder = strict.GetEncoder();
        _lineBreak = strict.GetBytes("\r\n");
        _delimiter = strict.GetBytes([settings.Delimiter]);
        _quoteTriggers = SearchValues.Create(settings.Delimiter, Quote, '\r', '\n');
    }

    public long NextRowNumber => _nextLine;

    public long RowNumberOf(
        ReadOnlySpan<OutputCell> preceding
    )
    {
        var line = _nextLine;
        foreach (var cell in preceding)
        {
            if (cell.Kind != OutputKind.Empty)
            {
                line += LineBreaks(cell.Text.Span);
            }
        }

        return line;
    }

    public async ValueTask WriteHeaderAsync(
        IReadOnlyList<ExportColumn> columns,
        CancellationToken cancellationToken
    )
    {
        StartRecord();
        var line = _nextLine;
        for (var index = 0; index < columns.Count; index++)
        {
            var text = (columns[index].Header ?? string.Empty).AsSpan();
            WriteField(text, index, columns.Count, line, null);
            line += LineBreaks(text);
        }

        _nextLine = line + 1;

        await FlushIfNeededAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask WriteRowAsync(
        IReadOnlyList<OutputCell> cells,
        long recordNumber,
        CancellationToken cancellationToken
    )
    {
        StartRecord();
        var line = _nextLine;
        for (var index = 0; index < cells.Count; index++)
        {
            var text = cells[index].Kind == OutputKind.Empty
                ? ReadOnlySpan<char>.Empty
                : cells[index].Text.Span;

            WriteField(text, index, cells.Count, line, recordNumber);
            line += LineBreaks(text);
        }

        _nextLine = line + 1;

        await FlushIfNeededAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask CompleteTableAsync(
        CancellationToken cancellationToken
    )
    {
        EnsureStarted();
        await FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _pending.Dispose();

        return ValueTask.CompletedTask;
    }

    private void StartRecord()
    {
        EnsureStarted();
        if (_records++ > 0)
        {
            _pending.Write(_lineBreak);
        }
    }

    /// <summary>The physical line breaks inside a field: CR LF, a single LF, and a single CR each count once.</summary>
    private static int LineBreaks(
        ReadOnlySpan<char> text
    )
    {
        var count = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n'
                || (text[index] == '\r' && (index + 1 == text.Length || text[index + 1] != '\n')))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Writes one field starting on the physical <paramref name="line" />; a field with a delimiter, quote, or line
    /// break is quoted with doubled quotes. A header has no <paramref name="recordNumber" />.
    /// </summary>
    private void WriteField(
        ReadOnlySpan<char> text,
        int index,
        int count,
        long line,
        long? recordNumber
    )
    {
        if (index > 0)
        {
            _pending.Write(_delimiter);
        }

        var quoted = text.ContainsAny(_quoteTriggers) || (count == 1 && text.IsEmpty);
        try
        {
            if (!quoted)
            {
                Encode(text);

                return;
            }

            Encode([Quote]);
            var rest = text;
            for (var quote = rest.IndexOf(Quote); quote >= 0; quote = rest.IndexOf(Quote))
            {
                Encode(rest[..(quote + 1)]);
                Encode([Quote]);
                rest = rest[(quote + 1)..];
            }

            Encode(rest);
            Encode([Quote]);
        }
        catch (EncoderFallbackException)
        {
            _encoder.Reset();
            throw new TabularRowException(TabularError.Create(TabularErrorCodes.Parsing.InvalidEncoding,
                TabularErrorKind.Data,
                recordNumber is null
                    ? "A header contains a character that the encoding cannot represent."
                    : "A value contains a character that the encoding cannot represent.",
                new TabularErrorLocation
                {
                    RowNumber = line,
                    RecordNumber = recordNumber,
                    ColumnIndex = index,
                }));
        }
    }

    /// <summary>Encodes characters into the pending bytes; a field ends with a flush of the encoder state.</summary>
    private void Encode(
        ReadOnlySpan<char> text
    )
    {
        var length = _encoder.GetByteCount(text, flush: true);
        if (_bytes.Length < length)
        {
            _bytes = new byte[Math.Max(length, _bytes.Length * 2)];
        }

        var written = _encoder.GetBytes(text, _bytes, flush: true);
        _pending.Write(_bytes, 0, written);
    }

    private void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (_settings.WriteBom
            && _settings.Encoding.CodePage == Encoding.UTF8.CodePage)
        {
            _pending.Write([0xEF, 0xBB, 0xBF]);
        }
    }

    private async ValueTask FlushIfNeededAsync(
        CancellationToken cancellationToken
    )
    {
        if (_pending.Length >= 65536)
        {
            await FlushAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask FlushAsync(
        CancellationToken cancellationToken
    )
    {
        if (_pending.Length == 0)
        {
            return;
        }

        await _output
            .WriteAsync(_pending
                    .GetBuffer()
                    .AsMemory(0, (int)_pending.Length),
                cancellationToken)
            .ConfigureAwait(false);

        _pending.SetLength(0);
    }
}
