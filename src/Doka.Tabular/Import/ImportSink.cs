namespace Doka.Tabular;

/// <summary>A validated row ready for a write target.</summary>
internal sealed record ImportedRow<TItem>(
    TItem Item,
    long RowNumber,
    long RecordNumber,
    TabularSheetName? SheetName
);

/// <summary>Outcome of writing rows to a target.</summary>
internal sealed class SinkOutcome<TItem>
{
    public long RowsImported { get; init; }

    public long RowsPendingCommit { get; init; }

    public long RowsSkipped { get; init; }

    public long RowsUncertain { get; init; }

    public long RowsRejected { get; init; }

    public List<TabularError> Errors { get; } = [];

    public TabularError? Fatal { get; init; }

    public bool Canceled { get; init; }

    public List<TItem>? Items { get; set; }

    public bool PersistenceUncertain { get; init; }

    /// <summary>Gets whether the commit of the written rows succeeded, also when an error followed it.</summary>
    public bool CommitConfirmed { get; init; }

    /// <summary>
    /// Gets whether a rollback also removed the pending rows of earlier tables in the same transaction group.
    /// </summary>
    public bool ScopeRolledBack { get; init; }
}

internal enum ImportGate
{
    Proceed,
    Skip,
    RejectDuplicate,
    Running,
}

/// <summary>Decision of the write target before reading: configuration errors and the journal gate.</summary>
internal sealed class SinkPreparation
{
    public List<TabularError> Errors { get; } = [];

    public ImportGate Gate { get; set; } = ImportGate.Proceed;

    public ImportJournalResult? Journal { get; set; }

    public TabularError? Fatal { get; set; }

    public bool Canceled { get; set; }
}

internal sealed record ImportSinkStart(
    ImportStrategy Strategy,

    // WHY: WriteSession and ImportJournalRun in the EF package read this value; the inspection misses those reads.
    // ReSharper disable once NotAccessedPositionalProperty.Global
    bool IsWorkbook,
    Guid OperationId,

    // WHY: WriteSession and ImportJournalRun in the EF package read this value; the inspection misses those reads.
    // ReSharper disable once NotAccessedPositionalProperty.Global
    TimeProvider Time
);

internal sealed record ImportSinkWrite(
    ImportStrategy Strategy,
    bool ReadAborted,
    bool IsLastTable
);

/// <summary>Write target of an import, implemented by the EF package.</summary>
internal interface IImportSink<TItem> : IAsyncDisposable
{
    /// <summary>Gets whether the target needs the SHA-256 hash of the input before reading (import journal).</summary>
    bool RequiresInputHash { get; }

    /// <summary>Gets the identity of the shared context and transaction; targets with the same group can commit together.</summary>
    object TransactionGroup { get; }

    /// <summary>Gets whether a multi-sheet import uses own transaction boundaries per sheet.</summary>
    bool TransactionPerSheet { get; }

    /// <summary>Checks configuration, context, and transaction before the first read.</summary>
    Task<SinkPreparation> PrepareAsync(
        ImportSinkStart start,
        CancellationToken cancellationToken
    );

    /// <summary>Writes the start of the import log before the input is read, so that an abort while hashing is logged.</summary>
    Task<SinkPreparation> StartJournalAsync(
        CancellationToken cancellationToken
    );

    /// <summary>Decides the journal gate from the hash of the input bytes, before the first data write.</summary>
    Task<SinkPreparation> GateAsync(
        Sha256Hash hash,
        long byteSize,
        CancellationToken cancellationToken
    );

    /// <summary>Writes one table (CSV file or sheet) of validated rows according to the strategy.</summary>
    Task<SinkOutcome<TItem>> WriteAsync(
        IReadOnlyList<ImportedRow<TItem>> rows,
        ImportSinkWrite write,
        CancellationToken cancellationToken
    );

    /// <summary>Rolls back an open transaction that spans several tables after a later table failed.</summary>
    Task<SinkOutcome<TItem>> AbandonAsync();

    /// <summary>Releases the own resources of the import before any completion callback; repeated calls do nothing.</summary>
    ValueTask ReleaseAsync();

    /// <summary>Completes journal records and runs the completion callback; returns an error of the callback.</summary>
    Task<IReadOnlyList<TabularError>> FinishAsync(
        ImportReportData report
    );
}
