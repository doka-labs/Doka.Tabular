namespace Doka.Tabular;

/// <summary>Write-once storage in memory up to a size, spilling to an own temporary file when allowed.</summary>
internal sealed class SpillBuffer : IAsyncDisposable
{
    private readonly bool _allowSpill;
    private readonly TemporaryFiles _files;
    private readonly long _memoryLimit;
    private readonly string _resourceName;
    private TemporaryFile? _file;
    private MemoryStream? _memory = new();

    public SpillBuffer(
        long memoryLimit,
        bool allowSpill,
        TemporaryFiles files,
        string resourceName
    )
    {
        _memoryLimit = memoryLimit;
        _allowSpill = allowSpill;
        _files = files;
        _resourceName = resourceName;
    }

    public long Length { get; private set; }

    public ValueTask DisposeAsync()
    {
        _memory?.Dispose();
        _file?.TryDelete();

        return ValueTask.CompletedTask;
    }

    public async ValueTask WriteAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken
    )
    {
        if (_file is null
            && Length + data.Length > _memoryLimit)
        {
            if (!_allowSpill)
            {
                throw TabularResourceException.Limit(_resourceName, _memoryLimit);
            }

            _file = _files.Create();
            _memory!.Position = 0;
            _file.Account(_memory.Length + data.Length);
            await _memory
                .CopyToAsync(_file.Stream, cancellationToken)
                .ConfigureAwait(false);

            _memory = null;
        }

        if (_file is not null)
        {
            _file.Account(Length + data.Length);
            await _file
                .Stream
                .WriteAsync(data, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _memory!.Write(data.Span);
        }

        Length += data.Length;
    }

    /// <summary>
    /// Opens a read-only, seekable view at position 0 that the call releases itself; the file stays with the cleanup of
    /// the call, which reports a failed delete.
    /// </summary>
    public ValueTask<Stream> CompleteAsync(
        CancellationToken cancellationToken
    ) => OpenAsync(null, cancellationToken);

    /// <summary>
    /// Opens a read-only, seekable view at position 0 that outlives the call; the file leaves the cleanup of the call,
    /// and <paramref name="cleanupFailed" /> runs when disposing the view cannot delete it.
    /// </summary>
    public ValueTask<Stream> HandOverAsync(
        Action cleanupFailed,
        CancellationToken cancellationToken
    ) => OpenAsync(cleanupFailed, cancellationToken);

    private async ValueTask<Stream> OpenAsync(
        Action? cleanupFailed,
        CancellationToken cancellationToken
    )
    {
        if (_file is not null)
        {
            await _file
                .Stream
                .FlushAsync(cancellationToken)
                .ConfigureAwait(false);

            _file.Stream.Position = 0;
            var file = _file;
            _file = null;
            if (cleanupFailed is not null)
            {
                file.HandOver();
            }

            return new OwnedReadStream(file.Stream, file, cleanupFailed);
        }

        var memory = _memory!;
        _memory = null;

        return new OwnedReadStream(
            new MemoryStream(
                memory.GetBuffer(),
                0,
                (int)memory.Length,
                writable: false,
                publiclyVisible: true),
            null,
            null);
    }
}
