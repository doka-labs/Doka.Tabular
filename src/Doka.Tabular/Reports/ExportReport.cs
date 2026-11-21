namespace Doka.Tabular;

/// <summary>Immutable result of an export.</summary>
[PublicAPI]
public sealed class ExportReport
{
    internal ExportReport(
        ExportReportData data
    )
    {
        Status = data.Status;
        IsComplete = data.IsComplete;
        RowsRead = data.RowsRead;
        RowsWritten = data.RowsWritten;
        Errors = data.Errors;
        TotalErrorCount = data.TotalErrorCount;
        OmittedErrorCount = data.OmittedErrorCount;
        FatalError = data.FatalError;
        PathOutput = data.PathOutput;
        StreamOutput = data.StreamOutput;
        FileId = data.FileId;
        Diagnostics = data.Diagnostics;
    }

    /// <summary>Gets the overall status.</summary>
    public ReportStatus Status { get; }

    /// <summary>Gets whether all sources were enumerated and the document was produced.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the enumerated source objects.</summary>
    public long RowsRead { get; }

    /// <summary>Gets the completely written data rows without header; no statement about publication.</summary>
    public long RowsWritten { get; }

    /// <summary>Gets the error details.</summary>
    public IReadOnlyList<TabularError> Errors { get; }

    /// <summary>Gets the number of all errors.</summary>
    public long TotalErrorCount { get; }

    /// <summary>Gets the number of errors omitted because of the detail limit.</summary>
    public long OmittedErrorCount { get; }

    /// <summary>Gets the error that ended the run.</summary>
    public TabularError? FatalError { get; }

    /// <summary>Gets the output state of a path export; <see langword="null" /> for other targets.</summary>
    public PathOutputState? PathOutput { get; }

    /// <summary>Gets the output state of a stream export; <see langword="null" /> for other targets.</summary>
    public StreamOutputState? StreamOutput { get; }

    /// <summary>Gets the stored file of a file-store export; set only after the file was stored.</summary>
    public Guid? FileId { get; }

    /// <summary>Gets correlation data.</summary>
    public TabularReportDiagnostics Diagnostics { get; }
}

internal sealed record ExportReportData(
    ReportStatus Status,
    bool IsComplete,
    long RowsRead,
    long RowsWritten,
    IReadOnlyList<TabularError> Errors,
    long TotalErrorCount,
    long OmittedErrorCount,
    TabularError? FatalError,
    PathOutputState? PathOutput,
    StreamOutputState? StreamOutput,
    Guid? FileId,
    TabularReportDiagnostics Diagnostics
);

/// <summary>Result of an export into the output buffer: the report and, only on complete success, the file.</summary>
[PublicAPI]
public sealed class BufferedExportResult
{
    internal BufferedExportResult(
        ExportReport report,
        Stream? file
    )
    {
        Report = report;
        File = file;
    }

    /// <summary>Gets the report.</summary>
    public ExportReport Report { get; }

    /// <summary>
    /// Gets the readable, seekable, read-only file at position 0 on complete success; otherwise <see langword="null" />.
    /// The caller owns and disposes the stream.
    /// </summary>
    public Stream? File { get; }

    /// <summary>Deconstructs into report and file.</summary>
    /// <param name="report">The report.</param>
    /// <param name="file">The file or <see langword="null" />.</param>
    public void Deconstruct(
        out ExportReport report,
        out Stream? file
    )
    {
        report = Report;
        file = File;
    }
}
