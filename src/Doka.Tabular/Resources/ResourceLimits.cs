namespace Doka.Tabular;

/// <summary>Resolved resource limits of a format instance; budgets apply per call.</summary>
internal sealed record ResourceLimits(
    long ImportBufferBytes,
    long OutputBufferBytes,
    bool AllowSpill,
    string TemporaryDirectory,
    long? MaxTemporaryBytes,
    long? MaxInputBytes,
    long? MaxFieldCharacters,
    long? MaxExpandedBytes
)
{
    public const long DefaultBufferBytes = 16L * 1024 * 1024;

    public const long DefaultMaxExpandedBytes = 1L * 1024 * 1024 * 1024;

    public IEnumerable<string> Problems()
    {
        if (ImportBufferBytes < 0
            || OutputBufferBytes < 0)
        {
            yield return "Buffer sizes must not be negative.";
        }

        if (MaxTemporaryBytes < 0
            || MaxInputBytes < 0
            || MaxFieldCharacters < 0
            || MaxExpandedBytes < 0)
        {
            yield return "Limits must not be negative.";
        }

        if (TemporaryDirectory.Length == 0)
        {
            yield return "The temporary directory must not be empty.";
        }
    }
}

/// <summary>Thrown inside the pipeline when a limit or an I/O step fails; converted to a technical error.</summary>
internal sealed class TabularResourceException : Exception
{
    private TabularResourceException(
        TabularError error
    )
        : base(error.Message)
    {
        Error = error;
    }

    public TabularError Error { get; }

    public static TabularResourceException Limit(
        string resource,
        long limit
    ) => new(TabularError.Create(TabularErrorCodes.Resource.LimitExceeded,
        TabularErrorKind.Technical,
        $"The limit for {resource} ({limit.ToString(CultureInfo.InvariantCulture)}) was reached."));

    public static TabularResourceException Io(
        string message
    ) => new(TabularError.Create(TabularErrorCodes.Io.Failed, TabularErrorKind.Technical, message));
}

/// <summary>Owns the temporary files of one call and enforces the temporary-bytes budget.</summary>
internal sealed class TemporaryFiles
{
    private readonly List<TemporaryFile> _files = [];
    private readonly ResourceLimits _limits;
    private long _reserved;

    public TemporaryFiles(
        ResourceLimits limits
    )
    {
        _limits = limits;
    }

    public TemporaryFile Create(
        string? directory = null
    )
    {
        var folder = directory ?? _limits.TemporaryDirectory;
        for (var attempt = 0;; attempt++)
        {
            var path = Path.Combine(folder, $"doka-tabular-{Guid.NewGuid():N}.tmp");
            try
            {
                var stream = new FileStream(path,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous);

                var file = new TemporaryFile(this, path, stream);
                lock (_files)
                {
                    _files.Add(file);
                }

                return file;
            }
            catch (IOException) when (attempt < 3 && File.Exists(path))
            {
                // A name collision is retried with a new random name.
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                // WHY: An invalid temporary directory is reported like a missing one instead of escaping.
                throw TabularResourceException.Io("A temporary file could not be created.");
            }
        }
    }

    public void Reserve(
        long bytes
    )
    {
        lock (_files)
        {
            if (_limits.MaxTemporaryBytes is { } maximum
                && _reserved + bytes > maximum)
            {
                throw TabularResourceException.Limit("temporary bytes", maximum);
            }

            _reserved += bytes;
        }
    }

    public void Release(
        TemporaryFile file,
        long bytes
    )
    {
        lock (_files)
        {
            _reserved -= bytes;
            _files.Remove(file);
        }
    }

    /// <summary>Deletes every remaining own file; returns whether all deletions succeeded.</summary>
    public bool CleanupAll()
    {
        List<TemporaryFile> remaining;
        lock (_files)
        {
            remaining = [.. _files];
        }

        var ok = true;

        // WHY: Every file is deleted even after a failed delete; the loop must not stop at the first failure.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var fileIndex = 0; fileIndex < remaining.Count; fileIndex++)
        {
            var file = remaining[fileIndex];
            ok &= file.TryDelete();
        }

        return ok;
    }
}

internal sealed class TemporaryFile
{
    private readonly TemporaryFiles _owner;
    private long _accounted;
    private bool _deleted;

    public TemporaryFile(
        TemporaryFiles owner,
        string path,
        FileStream stream
    )
    {
        _owner = owner;
        Path = path;
        Stream = stream;
    }

    public string Path { get; }

    public FileStream Stream { get; }

    /// <summary>Accounts written bytes against the temporary budget.</summary>
    public void Account(
        long totalLength
    )
    {
        if (totalLength > _accounted)
        {
            _owner.Reserve(totalLength - _accounted);
            _accounted = totalLength;
        }
    }

    public bool TryDelete()
    {
        if (_deleted)
        {
            return true;
        }

        try
        {
            Stream.Dispose();
            File.Delete(Path);
            _deleted = true;
            _owner.Release(this, _accounted);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Hands the open file to a stream that outlives the call; the cleanup of the call no longer deletes it, the owning
    /// stream does on dispose.
    /// </summary>
    public void HandOver()
    {
        _owner.Release(this, _accounted);
        _accounted = 0;
    }

    /// <summary>Closes the stream and hands the file over, for example for a publication rename.</summary>
    public void Detach()
    {
        Stream.Dispose();
        _deleted = true;
        _owner.Release(this, _accounted);
    }
}
