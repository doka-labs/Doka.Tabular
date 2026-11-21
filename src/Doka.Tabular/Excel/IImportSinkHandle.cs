namespace Doka.Tabular.Excel;

/// <summary>Non-generic view of a sheet's sink.</summary>
internal interface IImportSinkHandle
{
    bool RequiresInputHash { get; }

    object TransactionGroup { get; }

    bool TransactionPerSheet { get; }

    Task<SinkPreparation> PrepareAsync(
        ImportSinkStart start,
        CancellationToken cancellationToken
    );

    Task<SinkPreparation> StartJournalAsync(
        CancellationToken cancellationToken
    );

    Task<SinkPreparation> GateAsync(
        Sha256Hash hash,
        long byteSize,
        CancellationToken cancellationToken
    );

    /// <summary>Releases the own resources of the import before any completion callback.</summary>
    ValueTask ReleaseAsync();

    Task<IReadOnlyList<TabularError>> FinishAsync(
        ImportReportData report
    );

    ValueTask DisposeAsync();
}
