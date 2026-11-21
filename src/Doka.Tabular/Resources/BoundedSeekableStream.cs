namespace Doka.Tabular;

/// <summary>
/// A seekable, caller-owned input that is read up to a byte limit from its start position. The limit is checked on
/// every read, because a stream can grow after its length was checked. Disposing leaves the input open.
/// </summary>
internal sealed class BoundedSeekableStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private readonly long _start;

    public BoundedSeekableStream(
        Stream inner,
        long limit
    )
    {
        _inner = inner;
        _limit = limit;
        _start = inner.Position;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

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
    ) => Checked(_inner.Read(buffer, offset, count));

    public override int Read(
        Span<byte> buffer
    ) => Checked(_inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => Checked(await _inner
        .ReadAsync(buffer, cancellationToken)
        .ConfigureAwait(false));

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
        .AsTask();

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

    private int Checked(
        int read
    ) => _inner.Position - _start > _limit
        ? throw TabularResourceException.Limit("input bytes", _limit)
        : read;
}
