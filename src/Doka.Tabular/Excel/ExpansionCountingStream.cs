namespace Doka.Tabular.Excel;

internal sealed class ExpansionCountingStream : Stream
{
    private readonly ExpansionBudget _budget;
    private readonly Stream _inner;

    public ExpansionCountingStream(
        Stream inner,
        ExpansionBudget budget
    )
    {
        _inner = inner;
        _budget = budget;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(
        byte[] buffer,
        int offset,
        int count
    )
    {
        var read = _inner.Read(buffer, offset, count);
        _budget.Add(read);

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

        _budget.Add(read);

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
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
