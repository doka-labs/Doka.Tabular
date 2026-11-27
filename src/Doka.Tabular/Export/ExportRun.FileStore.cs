using System.Security.Cryptography;

namespace Doka.Tabular;

internal sealed partial class ExportRun
{
    private async Task WriteFileStoreAsync(
        TabularFileTarget target
    )
    {
        // WHY: A configuration error known before the start writes no export log and runs no completion.
        if (_errors.Fatal is not null)
        {
            return;
        }

        if (target is { Journal: null, Retention: not null })
        {
            _errors.AddFatal(TabularError.Configuration("A retention needs an export log."));

            return;
        }

        // WHY: The expiry is the time of Ready plus the retention; a retention beyond the latest date would fail only
        // after the file is stored.
        if (target.Retention is { } retention
            && retention > DateTimeOffset.MaxValue - _scope.Time.GetUtcNow())
        {
            _errors.AddFatal(TabularError.Configuration("The retention reaches beyond the latest representable date."));

            return;
        }

        if (target.Journal is { } hook
            && !await StartJournalAsync(hook, target)
                .ConfigureAwait(false))
        {
            return;
        }

        var buffer = CreateBuffer();
        try
        {
            await ProduceAndStoreAsync(buffer, target)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _result = _result with
            {
                Success = false,
                Canceled = true,
            };
        }
        catch (TabularResourceException resource)
        {
            _errors.AddFatal(resource.Error);
            _result = _result with { Success = false };
        }

        // WHY: The cancellation the call requested is caught above; any other one is a failure like these.
        catch (Exception exception) when (exception is OperationCanceledException
            or IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or NotSupportedException)
        {
            Fail(TabularErrorCodes.Io.Failed, "Completing the buffered file failed.");
        }
        finally
        {
            await buffer
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>Checks the export log and starts its entry; <see langword="false" /> stops the export.</summary>
    private async Task<bool> StartJournalAsync(
        IExportJournalHook hook,
        TabularFileTarget target
    )
    {
        try
        {
            var problems = await hook
                .CheckAsync(_cancellationToken)
                .ConfigureAwait(false);

            // WHY: Indexed access reads the list without an enumerator through the interface.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var problemIndex = 0; problemIndex < problems.Count; problemIndex++)
            {
                var problem = problems[problemIndex];
                _errors.AddFatal(problem);
            }

            if (problems.Count > 0)
            {
                return false;
            }

            _journal = await hook
                .StartAsync(
                    new ExportJournalStart(
                        _scope.OperationId,
                        target.FileName,
                        _formatName,
                        target.Retention,
                        _scope.Time),
                    _cancellationToken)
                .ConfigureAwait(false);

            if (!_journal.StartReleaseFailed)
            {
                return true;
            }

            // WHY: The entry exists; the export stops but still ends it and runs its completion.
            _errors.AddFatal(
                TabularError.Create(
                    TabularErrorCodes.Persistence.OperationFailed,
                    TabularErrorKind.Technical,
                    "The context of the export log could not be released after the start."));

            _scope.JournalUpdateFailed();

            return false;
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _result = new ProduceResult(false, true, 0, 0);

            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.AddFatal(
                TabularError.Create(
                    TabularErrorCodes.Persistence.WriteFailed,
                    TabularErrorKind.Technical,
                    "The export log could not be started."));

            _scope.JournalUpdateFailed();

            return false;
        }
    }

    private async Task ProduceAndStoreAsync(
        SpillBuffer buffer,
        TabularFileTarget target
    )
    {
        _result = await ProduceAsync(new SpillWriteStream(buffer))
            .ConfigureAwait(false);

        if (!_result.Success)
        {
            return;
        }

        await using var view = await buffer
            .CompleteAsync(_cancellationToken)
            .ConfigureAwait(false);

        var hash = Sha256Hash.FromBytes(
            await SHA256
                .HashDataAsync(view, _cancellationToken)
                .ConfigureAwait(false));

        view.Position = 0;
        var request = new TabularFileWriteRequest(
            _journal?.ExportId ?? Guid.NewGuid(),
            target.FileName,
            _contentType,
            view.Length,
            hash);

        if (_journal is not null
            && !await TryMarkStoringAsync(_journal)
                .ConfigureAwait(false))
        {
            return;
        }

        if (await StoreAsync(target.Store, request, view)
                .ConfigureAwait(false) is { } stored)
        {
            await MarkReadyAsync(stored)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Records that the file is being stored; a failing log ends the export before the store is called.</summary>
    private async Task<bool> TryMarkStoringAsync(
        ExportJournalSession journal
    )
    {
        try
        {
            await journal
                .MarkStoringAsync(_cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException)
            || (exception is OperationCanceledException && !_cancellationToken.IsCancellationRequested))
        {
            Fail(TabularErrorCodes.Persistence.WriteFailed, "The export log could not be updated before storing.");
            _scope.JournalUpdateFailed();

            return false;
        }
    }

    /// <summary>Stores the file; <see langword="null" /> when storing failed or the result does not match.</summary>
    private async Task<TabularStoredFile?> StoreAsync(
        ITabularFileStore store,
        TabularFileWriteRequest request,
        Stream view
    )
    {
        TabularStoredFile stored;
        try
        {
            stored = await store
                .SaveAsync(request, view, _cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _result = _result with
            {
                Success = false,
                Canceled = true,
            };

            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(TabularErrorCodes.Io.Failed, "The storage adapter failed to store the file.");

            return null;
        }

        return Accept(stored, request);
    }

    /// <summary>Accepts the stored file only if its metadata matches the request.</summary>
    private TabularStoredFile? Accept(
        TabularStoredFile? stored,
        TabularFileWriteRequest request
    )
    {
        // WHY: The storage adapter belongs to the application; a null result is reported, not trusted.
        _fileId = stored?.FileId;
        if (stored is not null
            && stored.ExportId == request.ExportId
            && stored.ByteSize == request.ByteSize
            && stored.Sha256.Equals(request.Sha256))
        {
            return stored;
        }

        Fail(
            TabularErrorCodes.Callback.InvalidResult,
            "The storage adapter returned metadata that does not match the file.");

        return null;
    }

    private async Task MarkReadyAsync(
        TabularStoredFile stored
    )
    {
        if (_journal is null)
        {
            return;
        }

        try
        {
            await _journal
                .MarkReadyAsync(stored, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Fail(
                TabularErrorCodes.Persistence.WriteFailed,
                "The export log could not be set to Ready; the stored file remains.");

            _scope.JournalUpdateFailed();
        }
    }
}
