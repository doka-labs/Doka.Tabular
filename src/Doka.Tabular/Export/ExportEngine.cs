namespace Doka.Tabular;

/// <summary>Synchronous or asynchronous export source; each source is enumerated exactly once.</summary>
internal sealed class ExportSource<T>
{
    private readonly IAsyncEnumerable<T>? _async;
    private readonly IEnumerable<T>? _sync;

    private ExportSource(
        IEnumerable<T>? sync,
        IAsyncEnumerable<T>? asynchronous
    )
    {
        _sync = sync;
        _async = asynchronous;
    }

    public static ExportSource<T> From(
        IEnumerable<T> rows
    ) => new(rows, null);

    public static ExportSource<T> From(
        IAsyncEnumerable<T> rows
    ) => new(null, rows);

    /// <summary>Uses the asynchronous enumeration of a query; <see langword="null" /> when the provider offers none.</summary>
    public static ExportSource<T>? FromQuery(
        IQueryable<T> rows
    ) => rows is IAsyncEnumerable<T> asynchronous
        ? new ExportSource<T>(null, asynchronous)
        : null;

    public async IAsyncEnumerable<T> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (_async is not null)
        {
            await foreach (var row in _async
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var row in _sync!)
        {
            yield return row;
        }
    }
}

internal sealed record TableExportOutcome(
    bool Success,
    bool Canceled,
    long RowsRead,
    long RowsWritten
);

internal static class ExportEngine
{
    public static Task<TableExportOutcome> WriteTableAsync<TSource>(
        ExportSource<TSource> source,
        ExportPlan<TSource> plan,
        bool writeHeader,
        ITableWriter writer,
        ErrorCollector errors,
        TabularSheetName? sheetName,
        CancellationToken cancellationToken
    ) => cancellationToken.IsCancellationRequested
        ? Task.FromResult(new TableExportOutcome(false, true, 0, 0))
        : new TableWrite<TSource>(source, plan, writer, errors, sheetName, cancellationToken).RunAsync(writeHeader);

    /// <summary>One table write: the rows of the source, formatted by the plan, into the writer.</summary>
    private sealed class TableWrite<TSource>
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ErrorCollector _errors;
        private readonly ExportPlan<TSource> _plan;
        private readonly TabularSheetName? _sheetName;
        private readonly ExportSource<TSource> _source;
        private readonly ITableWriter _writer;
        private long _read;
        private long _written;

        public TableWrite(
            ExportSource<TSource> source,
            ExportPlan<TSource> plan,
            ITableWriter writer,
            ErrorCollector errors,
            TabularSheetName? sheetName,
            CancellationToken cancellationToken
        )
        {
            _source = source;
            _plan = plan;
            _writer = writer;
            _errors = errors;
            _sheetName = sheetName;
            _cancellationToken = cancellationToken;
        }

        private TableExportOutcome Canceled => new(false, true, _read, _written);

        private TableExportOutcome Failed => new(false, false, _read, _written);

        public async Task<TableExportOutcome> RunAsync(
            bool writeHeader
        )
        {
            try
            {
                return await WriteAsync(writeHeader)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                return Canceled;
            }
            catch (TabularResourceException resource)
            {
                _errors.AddFatal(resource.Error);

                return Failed;
            }
            catch (TabularRowException rowException)
            {
                _errors.AddFatal(rowException.Error);

                return Failed;
            }

            // WHY: The cancellation the call requested is caught above; any other one is a failure like these.
            catch (Exception exception) when (exception is OperationCanceledException
                or IOException
                or NotSupportedException
                or ObjectDisposedException
                or UnauthorizedAccessException)
            {
                _errors.AddFatal(
                    TabularError.Create(
                        TabularErrorCodes.Io.Failed,
                        TabularErrorKind.Technical,
                        "Writing the output failed.",
                        new TabularErrorLocation { SheetName = _sheetName }));

                return Failed;
            }
        }

        private async Task<TableExportOutcome> WriteAsync(
            bool writeHeader
        )
        {
            // WHY: An empty column selection is the user's choice and gives an empty table; no record could be
            // represented, so the source is not even queried.
            if (_plan.Columns.Count == 0)
            {
                await _writer
                    .CompleteTableAsync(_cancellationToken)
                    .ConfigureAwait(false);

                return new TableExportOutcome(true, false, 0, 0);
            }

            if (writeHeader)
            {
                await _writer
                    .WriteHeaderAsync(_plan.Columns, _cancellationToken)
                    .ConfigureAwait(false);
            }

            var enumerator = _source
                .EnumerateAsync(_cancellationToken)
                .GetAsyncEnumerator(_cancellationToken);

            TableExportOutcome outcome;
            try
            {
                outcome = await WriteRecordsAsync(enumerator)
                    .ConfigureAwait(false);
            }
            catch
            {
                await TryReleaseAsync(enumerator)
                    .ConfigureAwait(false);

                throw;
            }

            return await TryReleaseAsync(enumerator)
                    .ConfigureAwait(false) switch
                {
                    SourceRelease.Released => outcome,
                    SourceRelease.Canceled => Canceled,
                    _ => Failed,
                };
        }

        /// <summary>
        /// Releases the source; an exception of its cleanup ends the export with a report instead of escaping, and an
        /// earlier error stays the fatal error.
        /// </summary>
        private async ValueTask<SourceRelease> TryReleaseAsync(
            IAsyncEnumerator<TSource> enumerator
        )
        {
            try
            {
                await enumerator
                    .DisposeAsync()
                    .ConfigureAwait(false);

                return SourceRelease.Released;
            }

            // WHY: A cancellation of the cleanup while the call is canceled belongs to the call, as in E154.
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                return SourceRelease.Canceled;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _errors.AddFatal(
                    TabularError.Create(
                        TabularErrorCodes.Callback.Failed,
                        TabularErrorKind.Technical,
                        "The export source threw an exception while it was released.",
                        new TabularErrorLocation { SheetName = _sheetName }));

                return SourceRelease.Failed;
            }
        }

        private async Task<TableExportOutcome> WriteRecordsAsync(
            IAsyncEnumerator<TSource> enumerator
        )
        {
            var cells = new OutputCell[_plan.Columns.Count];
            while (!_cancellationToken.IsCancellationRequested)
            {
                switch (await MoveNextAsync(enumerator)
                    .ConfigureAwait(false))
                {
                    case SourceStep.Next:
                        break;
                    case SourceStep.End:
                        await _writer
                            .CompleteTableAsync(_cancellationToken)
                            .ConfigureAwait(false);

                        return new TableExportOutcome(true, false, _read, _written);
                    case SourceStep.Canceled:
                        return Canceled;
                    case SourceStep.Failed:
                        return Failed;
                    default:
                        throw new UnreachableException("The source step is not handled.");
                }

                _read++;
                if (!await WriteRecordAsync(enumerator.Current, cells)
                    .ConfigureAwait(false))
                {
                    return Failed;
                }

                _written++;
            }

            return Canceled;
        }

        /// <summary>Moves the source on; an exception of the source ends the export.</summary>
        private async ValueTask<SourceStep> MoveNextAsync(
            IAsyncEnumerator<TSource> enumerator
        )
        {
            try
            {
                return await enumerator
                    .MoveNextAsync()
                    .ConfigureAwait(false)
                    ? SourceStep.Next
                    : SourceStep.End;
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                return SourceStep.Canceled;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not TabularResourceException)
            {
                _errors.AddFatal(
                    TabularError.Create(
                        TabularErrorCodes.Callback.Failed,
                        TabularErrorKind.Technical,
                        "The export source threw an exception.",
                        new TabularErrorLocation
                        {
                            RecordNumber = _read + 1,
                            SheetName = _sheetName,
                        }));

                return SourceStep.Failed;
            }
        }

        private async ValueTask<bool> WriteRecordAsync(
            TSource record,
            OutputCell[] cells
        )
        {
            if (!_plan.TryFormat(
                record,
                _read,
                _writer,
                _sheetName,
                cells,
                _cancellationToken,
                out var error))
            {
                _errors.AddFatal(error!);

                return false;
            }

            try
            {
                await _writer
                    .WriteRowAsync(cells, _read, _cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (TabularRowException rowException)
            {
                _errors.AddFatal(rowException.Error);

                return false;
            }
        }
    }

    private enum SourceRelease
    {
        Released,
        Canceled,
        Failed,
    }

    private enum SourceStep
    {
        Next,
        End,
        Canceled,
        Failed,
    }
}

internal abstract record ExportTarget;

internal sealed record StreamExportTarget(Stream Stream) : ExportTarget;

internal sealed record PathExportTarget(string Path) : ExportTarget;

internal sealed record BufferedExportTarget : ExportTarget;

internal sealed record FileStoreExportTarget(TabularFileTarget Target) : ExportTarget;

/// <summary>Result of producing the document for a target.</summary>
internal sealed record ProduceResult(
    bool Success,
    bool Canceled,
    long RowsRead,
    long RowsWritten
);

internal sealed record ExportRunResult(
    ExportReport Report,
    Stream? File
);

/// <summary>Entry point of one export against its target: publication, buffering, storage, journal, and report.</summary>
internal static class ExportRunner
{
    /// <summary>The producer of an export that a configuration error stopped before the first row.</summary>
    public static Task<ProduceResult> NothingProduced(
        Stream output,
        CancellationToken cancellationToken
    ) => Task.FromResult(new ProduceResult(false, false, 0, 0));

    public static Task<ExportRunResult> RunAsync(
        ExportTarget target,
        Func<Stream, CancellationToken, Task<ProduceResult>> produce,
        ErrorCollector errors,
        OperationScope scope,
        ResourceLimits limits,
        string formatName,
        string contentType,
        CancellationToken cancellationToken
    ) => new ExportRun(
            produce,
            errors,
            scope,
            limits,
            formatName,
            contentType,
            cancellationToken)
        .RunAsync(target);
}

/// <summary>Write stream over an own temporary file that accounts its size against the temporary budget.</summary>
internal sealed class AccountingStream : Stream
{
    private readonly TemporaryFile _file;

    public AccountingStream(
        TemporaryFile file
    )
    {
        _file = file;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _file.Stream.Length;

    public override long Position
    {
        get => _file.Stream.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _file.Stream.Flush();

    public override Task FlushAsync(
        CancellationToken cancellationToken
    ) => _file.Stream.FlushAsync(cancellationToken);

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
    )
    {
        _file.Account(_file.Stream.Position + count);
        _file.Stream.Write(buffer, offset, count);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        _file.Account(_file.Stream.Position + buffer.Length);
        await _file
            .Stream
            .WriteAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken)
        .AsTask();
}
