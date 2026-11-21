namespace Doka.Tabular;

/// <summary>Immutable result of an import. Counters count source rows (logical records), not records per row.</summary>
/// <typeparam name="T">The record type; <see cref="TabularRow" /> for a <see cref="RowMap" />.</typeparam>
[PublicAPI]
public sealed class ImportReport<T>
{
    internal ImportReport(
        ImportReportData data,
        IReadOnlyList<T>? items
    )
    {
        Status = data.Status;
        IsComplete = data.IsComplete;
        RowsRead = data.RowsRead;
        RowsImported = data.RowsImported;
        RowsRejected = data.RowsRejected;
        RowsSkipped = data.RowsSkipped;
        RowsPendingCommit = data.RowsPendingCommit;
        RowsUncertain = data.RowsUncertain;
        Errors = data.Errors;
        TotalErrorCount = data.TotalErrorCount;
        OmittedErrorCount = data.OmittedErrorCount;
        FatalError = data.FatalError;
        Diagnostics = data.Diagnostics;
        Journal = data.Journal;

        // WHY: The report is immutable; a list handed out as IReadOnlyList could still be cast back and changed.
        Items = items switch
        {
            null => null,
            List<T> list => list.AsReadOnly(),
            _ => Array.AsReadOnly(items.ToArray()),
        };
    }

    /// <summary>Gets the overall status.</summary>
    public ReportStatus Status { get; }

    /// <summary>Gets whether all selected source rows were checked; data errors alone do not make it <see langword="false" />.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the delimited records in the read range, without header and ignored empty rows.</summary>
    public long RowsRead { get; }

    /// <summary>Gets the returned rows without write target, or the confirmed committed rows with EF.</summary>
    public long RowsImported { get; }

    /// <summary>Gets the rows rejected because of their own data errors.</summary>
    public long RowsRejected { get; }

    /// <summary>Gets existing records skipped by an insert rule; not an error.</summary>
    public long RowsSkipped { get; }

    /// <summary>Gets the rows written in an application transaction whose commit is up to the application.</summary>
    public long RowsPendingCommit { get; }

    /// <summary>Gets the rows whose storage or rollback cannot be determined.</summary>
    public long RowsUncertain { get; }

    /// <summary>Gets the error details, limited by <c>MaxErrorDetails</c>.</summary>
    public IReadOnlyList<TabularError> Errors { get; }

    /// <summary>Gets the number of all errors of all kinds.</summary>
    public long TotalErrorCount { get; }

    /// <summary>Gets the number of errors omitted because of the detail limit.</summary>
    public long OmittedErrorCount { get; }

    /// <summary>Gets the error that ended the run, independent of the detail limit; <see langword="null" /> after a regular end.</summary>
    public TabularError? FatalError { get; }

    /// <summary>Gets correlation data.</summary>
    public TabularReportDiagnostics Diagnostics { get; }

    /// <summary>Gets the journal decision; <see langword="null" /> without import log.</summary>
    public ImportJournalResult? Journal { get; }

    /// <summary>
    /// Gets the imported records without write target; empty after a strict failure. With EF it is <see langword="null" />
    /// unless requested, then only confirmed committed rows.
    /// </summary>
    public IReadOnlyList<T>? Items { get; }
}

internal sealed record ImportReportData(
    ReportStatus Status,
    bool IsComplete,
    long RowsRead,
    long RowsImported,
    long RowsRejected,
    long RowsSkipped,
    long RowsPendingCommit,
    long RowsUncertain,
    IReadOnlyList<TabularError> Errors,
    long TotalErrorCount,
    long OmittedErrorCount,
    TabularError? FatalError,
    TabularReportDiagnostics Diagnostics,
    ImportJournalResult? Journal
);
