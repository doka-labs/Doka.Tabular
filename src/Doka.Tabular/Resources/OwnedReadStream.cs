namespace Doka.Tabular;

/// <summary>Read-only stream over buffered content; disposing deletes an own temporary file.</summary>
internal sealed class OwnedReadStream : Stream
{
    private readonly Action? _cleanupFailed;
    private readonly TemporaryFile? _file;
    private readonly Stream _inner;

    /// <param name="inner">The buffered content.</param>
    /// <param name="file">The own temporary file behind the content, if any.</param>
    /// <param name="cleanupFailed">Runs when a handed-over file cannot be deleted; <see langword="null" /> while the
    /// file is still registered with its call.</param>
    public OwnedReadStream(
        Stream inner,
        TemporaryFile? file,
        Action? cleanupFailed
    )
    {
        _inner = inner;
        _file = file;
        _cleanupFailed = cleanupFailed;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() { }

    public override int Read(
        byte[] buffer,
        int offset,
        int count
    ) => _inner.Read(buffer, offset, count);

    public override int Read(
        Span<byte> buffer
    ) => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(
        long offset,
        SeekOrigin origin
    ) => _inner.Seek(offset, origin);

    public override void SetLength(
        long value
    ) => throw new NotSupportedException();

    public override void Write(
        byte[] buffer,
        int offset,
        int count
    ) => throw new NotSupportedException();

    protected override void Dispose(
        bool disposing
    )
    {
        if (disposing)
        {
            _inner.Dispose();

            // WHY: A file of the call stays registered when its delete fails, and the cleanup of the call reports it. A
            // handed-over file is disposed after the call has ended, so only the log of the call can report it.
            if (_file?.TryDelete() == false)
            {
                _cleanupFailed?.Invoke();
            }
        }

        base.Dispose(disposing);
    }
}
