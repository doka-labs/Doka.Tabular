namespace Doka.Tabular;

/// <summary>
/// One export against its target: produces the document, publishes, buffers, or stores it, maintains the export log,
/// and builds the report. The run owns its state exclusively; every step records its effect on that state.
/// </summary>
internal sealed partial class ExportRun
{
    private readonly CancellationToken _cancellationToken;
    private readonly string _contentType;
    private readonly ErrorCollector _errors;
    private readonly TemporaryFiles _files;
    private readonly string _formatName;
    private readonly ResourceLimits _limits;
    private readonly Func<Stream, CancellationToken, Task<ProduceResult>> _produce;
    private readonly OperationScope _scope;
    private Stream? _file;
    private Guid? _fileId;
    private ExportJournalSession? _journal;
    private PathOutputState? _pathOutput;
    private ProduceResult _result = new(false, false, 0, 0);
    private StreamOutputState? _streamOutput;

    public ExportRun(
        Func<Stream, CancellationToken, Task<ProduceResult>> produce,
        ErrorCollector errors,
        OperationScope scope,
        ResourceLimits limits,
        string formatName,
        string contentType,
        CancellationToken cancellationToken
    )
    {
        _produce = produce;
        _errors = errors;
        _scope = scope;
        _limits = limits;
        _formatName = formatName;
        _contentType = contentType;
        _cancellationToken = cancellationToken;
        _files = new TemporaryFiles(limits);
    }

    public async Task<ExportRunResult> RunAsync(
        ExportTarget target
    )
    {
        _scope.Started();
        try
        {
            await (target switch
            {
                StreamExportTarget stream => WriteStreamAsync(stream.Stream),
                PathExportTarget path => WritePathAsync(path.Path),
                BufferedExportTarget => WriteBufferedAsync(),
                FileStoreExportTarget store => WriteFileStoreAsync(store.Target),
                _ => throw new UnreachableException($"The export target {target.GetType()} is not handled."),
            }).ConfigureAwait(false);
        }
        finally
        {
            CleanUpFiles();
        }

        return await FinishAsync()
            .ConfigureAwait(false);
    }

    private async Task<ExportRunResult> FinishAsync()
    {
        var published = _pathOutput == PathOutputState.Published;
        var canceled = _result.Canceled && !published && !_result.Success;
        var status = ExportStatusOf(_result.Success, canceled, _errors.Fatal is not null);
        await EndJournalAsync(status)
            .ConfigureAwait(false);

        var report = await CompleteJournalAsync(BuildReport(status), status)
            .ConfigureAwait(false);

        _scope.Completed(report.Status,
            report.IsComplete,
            report.FatalError,
            report.TotalErrorCount,
            report.OmittedErrorCount,
            new ReportCounters(report.RowsRead,
                0,
                0,
                0,
                0,
                0,
                report.RowsWritten),
            _errors.TotalCount > 0,
            _errors.HasTechnical);

        return new ExportRunResult(report,
            status == ReportStatus.Succeeded
                ? _file
                : null);
    }

    /// <summary>Writes the final state of an unsuccessful export to its log.</summary>
    private async Task EndJournalAsync(
        ReportStatus status
    )
    {
        if (_journal is null
            || status == ReportStatus.Succeeded)
        {
            return;
        }

        try
        {
            await _journal
                .MarkEndedAsync(status, _fileId)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.Add(TabularError.Create(TabularErrorCodes.Persistence.WriteFailed,
                TabularErrorKind.Technical,
                "The final export log state could not be written."));

            _scope.JournalUpdateFailed();
        }
    }

    /// <summary>Runs the completion function of the export log; its error is added to the report.</summary>
    private async Task<ExportReport> CompleteJournalAsync(
        ExportReport report,
        ReportStatus status
    )
    {
        if (_journal is null)
        {
            return report;
        }

        var completion = await _journal
            .CompleteAsync(report, status == ReportStatus.Succeeded && _fileId is not null)
            .ConfigureAwait(false);

        if (completion is null)
        {
            return report;
        }

        _errors.Add(completion);
        _scope.CompletionFailed();

        return BuildReport(status);
    }

    private ExportReport BuildReport(
        ReportStatus status
    ) => new(new ExportReportData(status,
        _result.Success,
        _result.RowsRead,
        _result.RowsWritten,
        _errors.Snapshot(),
        _errors.TotalCount,
        _errors.OmittedCount,
        _errors.Fatal,
        _pathOutput,
        _streamOutput,
        _fileId,
        _scope.Diagnostics));

    private SpillBuffer CreateBuffer() => new(_limits.OutputBufferBytes,
        _limits.AllowSpill,
        _files,
        "the output buffer");

    /// <summary>Records a fatal technical error that makes the produced result unsuccessful.</summary>
    private void Fail(
        TabularErrorCode code,
        string message
    )
    {
        _errors.AddFatal(TabularError.Create(code, TabularErrorKind.Technical, message));
        _result = _result with { Success = false };
    }

    private void CleanUpFiles()
    {
        if (_files.CleanupAll())
        {
            return;
        }

        _errors.AddFatal(TabularError.Create(TabularErrorCodes.Io.Failed,
            TabularErrorKind.Technical,
            "An own temporary file could not be deleted."));

        _scope.CleanupFailed();
    }

    private async Task<ProduceResult> ProduceAsync(
        Stream output
    )
    {
        try
        {
            return await _produce(output, _cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            return new ProduceResult(false, true, 0, 0);
        }
        catch (TabularResourceException resource)
        {
            _errors.AddFatal(resource.Error);

            return new ProduceResult(false, false, 0, 0);
        }

        // WHY: The cancellation the call requested is caught above; any other one is a failure like these.
        catch (Exception exception) when (exception is OperationCanceledException
            or IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or NotSupportedException)
        {
            _errors.AddFatal(TabularError.Create(TabularErrorCodes.Io.Failed,
                TabularErrorKind.Technical,
                "Writing the output failed."));

            return new ProduceResult(false, false, 0, 0);
        }
    }

    /// <summary>What a caller-owned output stream holds after the export.</summary>
    private static StreamOutputState StreamOutputOf(
        bool writeFailed,
        bool succeeded,
        long bytesWritten
    )
    {
        if (writeFailed)
        {
            return StreamOutputState.Unknown;
        }

        if (succeeded)
        {
            return StreamOutputState.Complete;
        }

        return bytesWritten == 0
            ? StreamOutputState.NotWritten
            : StreamOutputState.Partial;
    }

    /// <summary>The report status of an export; a fatal error always fails it.</summary>
    private static ReportStatus ExportStatusOf(
        bool succeeded,
        bool canceled,
        bool fatal
    )
    {
        if (fatal)
        {
            return ReportStatus.Failed;
        }

        if (succeeded)
        {
            return ReportStatus.Succeeded;
        }

        return canceled
            ? ReportStatus.Canceled
            : ReportStatus.Failed;
    }
}
