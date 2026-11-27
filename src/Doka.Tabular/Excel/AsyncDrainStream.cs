using System.IO.Compression;

namespace Doka.Tabular.Excel;

/// <summary>
/// Buffers what the ZIP archive writes and passes it on asynchronously at chosen points.
/// </summary>
/// <remarks>
/// The entry streams of <see cref="ZipArchive" /> in create mode flush their deflate buffer synchronously when they are
/// disposed, even through <c>DisposeAsync</c>. Outputs such as ASP.NET Core response bodies reject synchronous writes,
/// so every write lands here first and reaches the output only through <see cref="DrainAsync" />.
/// </remarks>
internal sealed class AsyncDrainStream : Stream
{
    private readonly Stream _output;
    private readonly MemoryStream _pending = new();

    public AsyncDrainStream(
        Stream output
    )
    {
        _output = output;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Writes the buffered bytes to the output when at least <paramref name="threshold" /> bytes are pending.</summary>
    public async Task DrainAsync(
        int threshold,
        CancellationToken cancellationToken
    )
    {
        if (_pending.Length == 0
            || _pending.Length < threshold)
        {
            return;
        }

        await _output
            .WriteAsync(
                _pending
                    .GetBuffer()
                    .AsMemory(0, (int)_pending.Length),
                cancellationToken)
            .ConfigureAwait(false);

        _pending.SetLength(0);
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
    ) => _pending.Write(buffer, offset, count);

    public override void Write(
        ReadOnlySpan<byte> buffer
    ) => _pending.Write(buffer);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        _pending.Write(buffer, offset, count);

        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        _pending.Write(buffer.Span);

        return ValueTask.CompletedTask;
    }

    protected override void Dispose(
        bool disposing
    )
    {
        if (disposing)
        {
            _pending.Dispose();
        }

        base.Dispose(disposing);
    }
}
