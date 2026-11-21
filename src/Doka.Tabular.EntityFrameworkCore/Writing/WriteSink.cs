namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Sink of one destination in one call; writes through the shared session of its database description.</summary>
internal sealed class WriteSink<TItem> : IImportSink<TItem>
{
    private readonly ImportJournalRun? _journal;
    private readonly TabularWriteOptions _options;
    private readonly Func<TItem, IReadOnlyList<object>> _records;
    private readonly WriteSession _session;
    private readonly IReadOnlyList<BoundTarget> _targets;

    private bool _released;
    private long _tentativePending;
    private long _tentativeRows;
    private long _tentativeSkipped;

    public WriteSink(
        ImportCall call,
        object database,
        IReadOnlyList<BoundTarget> targets,
        TabularWriteOptions options,
        ImportLogBinding? journal,
        Func<TItem, IReadOnlyList<object>> records
    )
    {
        _session = WriteSession.Attach(call, database);
        _targets = targets;
        _options = options;
        _records = records;
        _journal = journal?.Start(_session);
    }

    public bool RequiresInputHash => _journal is not null;

    public object TransactionGroup => _session;

    public bool TransactionPerSheet => _options.TransactionPerSheet;

    public async Task<SinkPreparation> PrepareAsync(
        ImportSinkStart start,
        CancellationToken cancellationToken
    )
    {
        var preparation = new SinkPreparation();
        var (errors, fatal) = await _session
            .PrepareAsync(_options, start, cancellationToken)
            .ConfigureAwait(false);

        preparation.Errors.AddRange(errors);
        preparation.Fatal = fatal;
        if (_session.Context is { } context
            && fatal is null)
        {
            // WHY: Indexed access reads the list without an enumerator through the interface.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var targetIndex = 0; targetIndex < _targets.Count; targetIndex++)
            {
                var target = _targets[targetIndex];
                preparation.Errors.AddRange(target.Definition.Check(context));
            }

            if (_journal is not null)
            {
                if (_options.JoinExistingTransaction)
                {
                    preparation.Errors.Add(
                        TabularError.Configuration("An import log cannot be combined with JoinExistingTransaction."));
                }

                preparation.Errors.AddRange(_journal.Check(context, start));
            }
        }

        return preparation;
    }

    public Task<SinkPreparation> StartJournalAsync(
        CancellationToken cancellationToken
    ) => _journal!.StartAsync(cancellationToken);

    public Task<SinkPreparation> GateAsync(
        Sha256Hash hash,
        long byteSize,
        CancellationToken cancellationToken
    ) => _journal!.GateAsync(hash, byteSize, cancellationToken);

    public async Task<SinkOutcome<TItem>> WriteAsync(
        IReadOnlyList<ImportedRow<TItem>> rows,
        ImportSinkWrite write,
        CancellationToken cancellationToken
    )
    {
        var work = new List<RowWork>(rows.Count);

        // WHY: Fills the presized list with each row and its position, without iterator or delegate.
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            work.Add(new RowWork(index,
                _records(row.Item),
                new TabularErrorLocation
                {
                    RowNumber = row.RowNumber,
                    RecordNumber = row.RecordNumber,
                    SheetName = row.SheetName,
                }));
        }

        var result = await _session
            .WriteAsync(work, _targets, write, cancellationToken)
            .ConfigureAwait(false);

        var outcome = new SinkOutcome<TItem>
        {
            RowsImported = result.RowsImported,
            RowsPendingCommit = result.RowsPendingCommit,
            RowsSkipped = result.RowsSkipped,
            RowsUncertain = result.RowsUncertain,
            RowsRejected = result.RowsRejected,
            Fatal = result.Fatal,
            Canceled = result.Canceled,
            PersistenceUncertain = result.Uncertain,
            CommitConfirmed = result.CommitConfirmed,
            ScopeRolledBack = result.ScopeRolledBack,
        };

        outcome.Errors.AddRange(result.Errors);
        if (_options.ReturnItems)
        {
            // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
            // ReSharper disable once UseCollectionExpression
            outcome.Items = _options.JoinExistingTransaction
                ? []
                : result
                    .Committed
                    .Select(index => rows[index].Item)
                    .ToList();
        }

        // WHY: Rows of an application transaction are pending, not imported; a later unconfirmed rollback leaves
        // them uncertain as well.
        _tentativePending = outcome.RowsPendingCommit;
        _tentativeRows = outcome.RowsImported;
        _tentativeSkipped = outcome.RowsSkipped;
        _journal?.Observe(result);

        return outcome;
    }

    public async Task<SinkOutcome<TItem>> AbandonAsync()
    {
        var (uncertain, rollbackUnconfirmed, fatal) = await _session
            .AbandonAsync()
            .ConfigureAwait(false);

        var outcome = new SinkOutcome<TItem>
        {
            RowsUncertain = uncertain > 0 || rollbackUnconfirmed
                ? _tentativeRows + _tentativeSkipped + _tentativePending
                : 0,
            PersistenceUncertain = uncertain > 0 || rollbackUnconfirmed,
            Fatal = fatal,
            Items = _options.ReturnItems
                ? []
                : null,
        };

        return outcome;
    }

    public Task<IReadOnlyList<TabularError>> FinishAsync(
        ImportReportData report
    ) => _journal is null
        ? Task.FromResult<IReadOnlyList<TabularError>>([])
        : _journal.FinishAsync(report);

    public ValueTask DisposeAsync() => ReleaseAsync();

    /// <summary>Releases the share of this sink in the session once; the session counts its users.</summary>
    public ValueTask ReleaseAsync()
    {
        if (_released)
        {
            return ValueTask.CompletedTask;
        }

        _released = true;

        return _session.ReleaseAsync();
    }
}
