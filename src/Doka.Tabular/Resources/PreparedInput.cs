namespace Doka.Tabular;

/// <summary>Prepared input of an import: a stream for the reader, plus hash and size when requested.</summary>
internal sealed class PreparedInput : IAsyncDisposable
{
    private readonly IAsyncDisposable? _owned;

    private PreparedInput(
        Stream stream,
        Sha256Hash? hash,
        long? byteSize,
        IAsyncDisposable? owned
    )
    {
        Stream = stream;
        Hash = hash;
        ByteSize = byteSize;
        _owned = owned;
    }

    public Stream Stream { get; }

    public Sha256Hash? Hash { get; }

    public long? ByteSize { get; }

    public ValueTask DisposeAsync() => _owned?.DisposeAsync() ?? ValueTask.CompletedTask;

    private static async Task CopyAsync(
        Stream source,
        SpillBuffer buffer,
        CancellationToken cancellationToken
    )
    {
        var chunk = new byte[81920];
        while (true)
        {
            var read = await source
                .ReadAsync(chunk, cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                return;
            }

            await buffer
                .WriteAsync(chunk.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static async Task<PreparedInput> CreateAsync(
        Stream input,
        bool needsSeekable,
        bool needsHash,
        ResourceLimits limits,
        TemporaryFiles files,
        CancellationToken cancellationToken
    )
    {
        // ReSharper disable once MergeIntoPattern
        // WHY: Position throws on a stream that cannot seek. A property pattern does not fix the order of its
        // subpatterns (C# specification, property pattern), so only && guarantees that CanSeek is read first.
        if (!needsHash
            && (!needsSeekable || (input.CanSeek && input.Position == 0)))
        {
            if (input.CanSeek)
            {
                if (limits.MaxInputBytes is not { } maximum)
                {
                    return new PreparedInput(new NonClosingStream(input), null, null, null);
                }

                if (input.Length - input.Position > maximum)
                {
                    throw TabularResourceException.Limit("input bytes", maximum);
                }

                return new PreparedInput(new BoundedSeekableStream(input, maximum), null, null, null);
            }

            return new PreparedInput(
                new CountingReadStream(input, limits.MaxInputBytes, hash: false),
                null,
                null,
                null);
        }

        var buffer = new SpillBuffer(limits.ImportBufferBytes, limits.AllowSpill, files, "the import buffer");
        try
        {
            await using var counting = new CountingReadStream(input, limits.MaxInputBytes, needsHash);
            await CopyAsync(counting, buffer, cancellationToken)
                .ConfigureAwait(false);

            var hash = counting.Hash();
            var view = await buffer
                .CompleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new PreparedInput(view, hash, counting.BytesRead, view);
        }
        catch
        {
            await buffer
                .DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
    }
}
