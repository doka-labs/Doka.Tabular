namespace Doka.Tabular;

/// <summary>Writes the rows of one table into an output stream; implemented per format.</summary>
internal interface ITableWriter : IAsyncDisposable
{
    /// <summary>Gets the file row of the next data row, for error locations.</summary>
    long NextRowNumber { get; }

    /// <summary>Gets the file row where a field of the next data row starts behind the given preceding cells.</summary>
    long RowNumberOf(
        ReadOnlySpan<OutputCell> preceding
    );

    /// <summary>Writes the header row; called only when the call writes a header.</summary>
    ValueTask WriteHeaderAsync(
        IReadOnlyList<ExportColumn> columns,
        CancellationToken cancellationToken
    );

    /// <summary>Writes one data row; throws <see cref="TabularResourceException" /> for format limits.</summary>
    ValueTask WriteRowAsync(
        IReadOnlyList<OutputCell> cells,
        long recordNumber,
        CancellationToken cancellationToken
    );

    /// <summary>Finishes the table.</summary>
    ValueTask CompleteTableAsync(
        CancellationToken cancellationToken
    );
}

/// <summary>Thrown by a table writer for a data-dependent problem of one row, such as an Excel cell limit.</summary>
internal sealed class TabularRowException : Exception
{
    public TabularRowException(
        TabularError error
    )
        : base(error.Message)
    {
        Error = error;
    }

    public TabularError Error { get; }
}

/// <summary>Tracks the bytes that reach a caller-owned output stream.</summary>
internal sealed class TrackingWriteStream : Stream
{
    private readonly Stream _inner;

    public TrackingWriteStream(
        Stream inner
    )
    {
        _inner = inner;
    }

    public long BytesWritten { get; private set; }

    /// <summary>Gets whether a write to the inner stream threw, so that the landed bytes are unknown.</summary>
    public bool WriteFailed { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesWritten;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        try
        {
            _inner.Flush();
        }
        catch
        {
            WriteFailed = true;
            throw;
        }
    }

    public override async Task FlushAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            await _inner
                .FlushAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // WHY: An interrupted write, also a canceled one, may already have handed bytes to the stream.
            WriteFailed = true;
            throw;
        }
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count
    ) => throw new NotSupportedException();

    public override long Seek(
        long offset,
        SeekOrigin origin
    ) => throw new NotSupportedException();

    public override void SetLength(
        long value
    ) => throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count
    ) => Write(buffer.AsSpan(offset, count));

    public override void Write(
        ReadOnlySpan<byte> buffer
    )
    {
        try
        {
            _inner.Write(buffer);
        }
        catch
        {
            WriteFailed = true;
            throw;
        }

        BytesWritten += buffer.Length;
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await _inner
                .WriteAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // WHY: An interrupted write, also a canceled one, may already have handed bytes to the stream.
            WriteFailed = true;
            throw;
        }

        BytesWritten += buffer.Length;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken)
        .AsTask();
}

/// <summary>Write side of a <see cref="SpillBuffer" /> as a stream.</summary>
internal sealed class SpillWriteStream : Stream
{
    private readonly SpillBuffer _buffer;

    public SpillWriteStream(
        SpillBuffer buffer
    )
    {
        _buffer = buffer;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _buffer.Length;

    public override long Position
    {
        get => _buffer.Length;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override Task FlushAsync(
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    public override int Read(
        byte[] buffer,
        int offset,
        int count
    ) => throw new NotSupportedException();

    public override long Seek(
        long offset,
        SeekOrigin origin
    ) => throw new NotSupportedException();

    public override void SetLength(
        long value
    ) => throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count
    ) => _buffer
        .WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None)
        .AsTask()
        .GetAwaiter()
        .GetResult();

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => _buffer.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => _buffer
        .WriteAsync(buffer.AsMemory(offset, count), cancellationToken)
        .AsTask();
}
