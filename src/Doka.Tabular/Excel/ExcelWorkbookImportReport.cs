namespace Doka.Tabular.Excel;

/// <summary>Result of a multi-sheet import; <see cref="Get{T}" /> returns the typed result of a sheet.</summary>
[PublicAPI]
public sealed class ExcelWorkbookImportReport
{
    private readonly IReadOnlyList<object> _handles;
    private readonly IReadOnlyList<object> _sheets;

    internal ExcelWorkbookImportReport(
        ImportReportData data,
        IReadOnlyList<object> sheets,
        IReadOnlyList<object> handles
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
        _sheets = sheets;
        _handles = handles;
    }

    /// <summary>Gets the overall status.</summary>
    public ReportStatus Status { get; }

    /// <summary>Gets whether all selected rows of all sheets were checked.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the delimited records of all sheets.</summary>
    public long RowsRead { get; }

    /// <summary>Gets the imported rows of all sheets.</summary>
    public long RowsImported { get; }

    /// <summary>Gets the rejected rows of all sheets.</summary>
    public long RowsRejected { get; }

    /// <summary>Gets the skipped existing records of all sheets.</summary>
    public long RowsSkipped { get; }

    /// <summary>Gets the rows pending the application's commit.</summary>
    public long RowsPendingCommit { get; }

    /// <summary>Gets the rows with uncertain storage.</summary>
    public long RowsUncertain { get; }

    /// <summary>Gets the error details of the whole call.</summary>
    public IReadOnlyList<TabularError> Errors { get; }

    /// <summary>Gets the number of all errors.</summary>
    public long TotalErrorCount { get; }

    /// <summary>Gets the number of omitted error details.</summary>
    public long OmittedErrorCount { get; }

    /// <summary>Gets the error that ended the run.</summary>
    public TabularError? FatalError { get; }

    /// <summary>Gets correlation data.</summary>
    public TabularReportDiagnostics Diagnostics { get; }

    /// <summary>Gets the journal decision; <see langword="null" /> without import log.</summary>
    public ImportJournalResult? Journal { get; }

    /// <summary>Gets the result of a sheet definition: status, counters, and typed records; errors are in the overall report.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="sheet">A handle of the definition used for this call.</param>
    /// <returns>The sheet result.</returns>
    public ImportReport<T> Get<T>(
        ExcelImportSheet<T> sheet
    )
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.Ordinal >= _sheets.Count
            || !ReferenceEquals(_handles[sheet.Ordinal], sheet)
            || _sheets[sheet.Ordinal] is not ImportReport<T> report)
        {
            throw new ArgumentException("The sheet belongs to another workbook definition.", nameof(sheet));
        }

        return report;
    }
}
