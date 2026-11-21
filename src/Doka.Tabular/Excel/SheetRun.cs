namespace Doka.Tabular.Excel;

/// <summary>Read result of one sheet; writes and the sheet report are produced from it.</summary>
internal abstract class SheetRun
{
    public required TableCounts Counts { get; init; }

    public abstract Task<SinkCounts?> WriteAsync(
        ImportSinkWrite write,
        ErrorCollector errors,
        CancellationToken cancellationToken
    );

    public abstract Task<SinkCounts?> AbandonAsync(
        ErrorCollector errors
    );

    public abstract object Report(
        ReportStatus status,
        SinkCounts? written,
        bool releaseItems,
        TabularReportDiagnostics diagnostics
    );
}
