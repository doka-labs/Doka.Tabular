namespace Doka.Tabular;

internal sealed partial class ExportRun
{
    private async Task WriteStreamAsync(
        Stream stream
    )
    {
        // WHY: A stream that cannot be written is a configuration error before the source is read.
        if (!stream.CanWrite)
        {
            _errors.AddFatal(TabularError.Configuration("The output stream is not writable."));
            _streamOutput = StreamOutputState.NotWritten;

            return;
        }

        var tracking = new TrackingWriteStream(stream);
        _result = await ProduceAsync(tracking)
            .ConfigureAwait(false);

        if (_result.Success)
        {
            await FlushAsync(tracking)
                .ConfigureAwait(false);
        }

        _streamOutput = StreamOutputOf(tracking.WriteFailed, _result.Success, tracking.BytesWritten);
    }

    private async Task FlushAsync(
        TrackingWriteStream tracking
    )
    {
        try
        {
            await tracking
                .FlushAsync(_cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            // WHY: The table was written, but the final flush was canceled; the export ends as canceled.
            _result = _result with
            {
                Success = false,
                Canceled = true,
            };
        }

        // WHY: The cancellation the call requested is caught above; any other one is a failure like these.
        catch (Exception exception) when (exception is OperationCanceledException
            or IOException
            or NotSupportedException
            or ObjectDisposedException
            or UnauthorizedAccessException)
        {
            Fail(TabularErrorCodes.Io.Failed, "Flushing the output stream failed.");
        }
    }

    private async Task WritePathAsync(
        string path
    )
    {
        _pathOutput = PathOutputState.NotPublished;

        // WHY: A configuration error known before the start creates no temporary file.
        if (_errors.Fatal is not null)
        {
            return;
        }

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            _errors.AddFatal(TabularError.Configuration("The target path is not valid."));

            return;
        }

        if (CreateTemporary(directory) is not { } temporary)
        {
            return;
        }

        _result = await ProduceAsync(new AccountingStream(temporary))
            .ConfigureAwait(false);

        if (_result.Success
            && await CompleteTemporaryAsync(temporary)
                .ConfigureAwait(false))
        {
            Publish(temporary, path);
        }
    }

    private TemporaryFile? CreateTemporary(
        string? directory
    )
    {
        if (_limits.MaxTemporaryBytes == 0)
        {
            _errors.AddFatal(
                TabularError.Configuration("A path export needs a temporary file, but MaxTemporaryBytes is 0."));

            return null;
        }

        try
        {
            return _files.Create(directory);
        }
        catch (TabularResourceException resource)
        {
            _errors.AddFatal(resource.Error);

            return null;
        }
    }

    private async Task<bool> CompleteTemporaryAsync(
        TemporaryFile temporary
    )
    {
        try
        {
            await temporary
                .Stream
                .FlushAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await temporary
                .Stream
                .DisposeAsync()
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Fail(TabularErrorCodes.Io.Failed, "Completing the temporary file failed.");

            return false;
        }
    }

    private void Publish(
        TemporaryFile temporary,
        string path
    )
    {
        try
        {
            // WHY: Publication is the atomic rename; after it the export is complete even if cancellation follows.
            File.Move(temporary.Path, path, overwrite: true);
            temporary.Detach();
            _pathOutput = PathOutputState.Published;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _pathOutput = File.Exists(temporary.Path)
                ? PathOutputState.NotPublished
                : PathOutputState.Unknown;

            Fail(TabularErrorCodes.Io.Failed, "Publishing the file failed.");
        }
    }

    private async Task WriteBufferedAsync()
    {
        var buffer = CreateBuffer();
        _result = await ProduceAsync(new SpillWriteStream(buffer))
            .ConfigureAwait(false);

        if (_result.Success
            && await HandOverAsync(buffer)
                .ConfigureAwait(false) is { } file)
        {
            _file = file;

            return;
        }

        await buffer
            .DisposeAsync()
            .ConfigureAwait(false);
    }

    /// <summary>Hands the buffered document to the caller; a canceled or failed hand-over leaves no file.</summary>
    private async Task<Stream?> HandOverAsync(
        SpillBuffer buffer
    )
    {
        try
        {
            return await buffer
                .HandOverAsync(_scope.CleanupFailed, _cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            // WHY: The table was written, but flushing the spilled file was canceled; the export ends as canceled.
            _result = _result with
            {
                Success = false,
                Canceled = true,
            };
        }

        // WHY: The cancellation the call requested is caught above; any other one is a failure like these.
        catch (Exception exception) when (exception is OperationCanceledException
            or IOException
            or NotSupportedException
            or ObjectDisposedException
            or UnauthorizedAccessException)
        {
            Fail(TabularErrorCodes.Io.Failed, "Completing the buffered file failed.");
        }

        return null;
    }
}
