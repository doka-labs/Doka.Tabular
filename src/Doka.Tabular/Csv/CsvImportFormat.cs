using System.Text;

namespace Doka.Tabular.Csv;

/// <summary>CSV part of an import: content check and record source.</summary>
internal sealed class CsvImportFormat : IImportFormat
{
    private readonly Encoding _encoding;
    private readonly CsvReadSettings _settings;

    public CsvImportFormat(
        CsvReadSettings settings,
        Encoding encoding
    )
    {
        _settings = settings;
        _encoding = encoding;
    }

    public bool NeedsSeekableInput => false;

    public async ValueTask<(IRecordSource? Source, TabularError? Error, IAsyncDisposable? Owned)> OpenAsync(
        PreparedInput input,
        ConversionContext context,
        CancellationToken cancellationToken
    )
    {
        var prefix = new byte[8];
        var length = 0;
        while (length < prefix.Length)
        {
            var read = await input
                .Stream
                .ReadAsync(prefix.AsMemory(length), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            length += read;
        }

        var head = prefix.AsMemory(0, length);
        if (ContentSignatures.IsZip(head.Span)
            || ContentSignatures.IsCfb(head.Span))
        {
            return (null, TabularError.Create(TabularErrorCodes.Schema.FormatMismatch,
                TabularErrorKind.Data,
                ContentSignatures.IsZip(head.Span)
                    ? "CSV was specified, but the content is a ZIP container (for example XLSX)."
                    : "CSV was specified, but the content is a CFB container (for example XLS)."), null);
        }

        if (_encoding.CodePage == Encoding.UTF8.CodePage
            && head.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            head = head[3..];
        }

        var reader = new CharReader(input.Stream, _encoding, head);

        return (new CsvRecordSource(reader, _settings), null, null);
    }
}
