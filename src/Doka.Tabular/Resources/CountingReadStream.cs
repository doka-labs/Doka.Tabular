using System.Security.Cryptography;

namespace Doka.Tabular;

/// <summary>Counts bytes read from the original input, enforces <c>MaxInputBytes</c>, and optionally hashes them.</summary>
internal sealed class CountingReadStream : Stream
{
    private readonly IncrementalHash? _hash;
    private readonly Stream _inner;
    private readonly long? _limit;

    public CountingReadStream(
        Stream inner,
        long? limit,
        bool hash
    )
    {
        _inner = inner;
        _limit = limit;
        _hash = hash
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;
    }

    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public Sha256Hash? Hash() => _hash is null
        ? null
        : Sha256Hash.FromBytes(_hash.GetHashAndReset());

    public override void Flush() { }

    public override int Read(
        byte[] buffer,
        int offset,
        int count
    ) => Read(buffer.AsSpan(offset, count));

    public override int Read(
        Span<byte> buffer
    )
    {
        var read = _inner.Read(buffer);
        Count(buffer[..read]);

        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        var read = await _inner
            .ReadAsync(buffer, cancellationToken)
            .ConfigureAwait(false);

        Count(buffer.Span[..read]);

        return read;
    }

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
    ) => throw new NotSupportedException();

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
            _hash?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Count(
        ReadOnlySpan<byte> data
    )
    {
        BytesRead += data.Length;
        if (_limit is { } limit
            && BytesRead > limit)
        {
            throw TabularResourceException.Limit("input bytes", limit);
        }

        _hash?.AppendData(data);
    }
}
