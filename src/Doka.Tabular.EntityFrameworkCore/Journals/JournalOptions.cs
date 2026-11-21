namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Behavior when the same bytes were already imported in the same journal and scope.</summary>
[PublicAPI]
public enum ImportDuplicateBehavior
{
    /// <summary>Nothing is read or written; the report refers to the existing import.</summary>
    Skip = 0,

    /// <summary>The call is rejected as a duplicate.</summary>
    Reject = 1,
}

/// <summary>Settings of the standalone journal calls (recover, resolve, delete); imports and exports use their own settings.</summary>
[PublicAPI]
public sealed record TabularJournalOptions
{
    /// <summary>Gets the logging and tracing settings; the default logs nothing.</summary>
    public TabularDiagnosticsOptions Diagnostics { get; init; } = new();

    /// <summary>Gets the clock of the standalone calls; the default is <see cref="TimeProvider.System" />.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}

/// <summary>Settings of the import log of one destination view.</summary>
[PublicAPI]
public sealed record TabularImportLogOptions
{
    /// <summary>Gets the business scope; required.</summary>
    public required JournalScope Scope { get; init; }

    /// <summary>Gets the display file name, at most 256 characters.</summary>
    public string? FileName { get; init; }

    /// <summary>Gets an optional file identifier of the application.</summary>
    public Guid? FileId { get; init; }

    /// <summary>Gets the behavior for an already imported file; the default is <see cref="ImportDuplicateBehavior.Skip" />.</summary>
    public ImportDuplicateBehavior DuplicateBehavior { get; init; } = ImportDuplicateBehavior.Skip;

    /// <summary>Gets the completion function, awaited once per started journal operation before the call returns.</summary>
    public Func<TabularImportCompleted, CancellationToken, Task>? OnCompletedAsync { get; init; }

    /// <summary>
    /// Gets the time after which the token of the completion function is canceled; the default is 30 seconds, positive
    /// and at most about 49 days. The call still awaits the function, so it never outlives the call; exceeding the time
    /// is a technical error.
    /// </summary>
    public TimeSpan CompletionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Settings of the export log of one stored export target.</summary>
[PublicAPI]
public sealed record TabularExportLogOptions
{
    /// <summary>Gets the business scope; required.</summary>
    public required JournalScope Scope { get; init; }

    /// <summary>Gets the completion function, awaited once per started journal operation before the call returns.</summary>
    public Func<TabularExportCompleted, CancellationToken, Task>? OnCompletedAsync { get; init; }

    /// <summary>
    /// Gets the time after which the token of the completion function is canceled; the default is 30 seconds, positive
    /// and at most about 49 days. The call still awaits the function, so it never outlives the call; exceeding the time
    /// is a technical error.
    /// </summary>
    public TimeSpan CompletionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Content of the completion function of a journaled import; it carries no context, stream, or records.</summary>
[PublicAPI]
public sealed class TabularImportCompleted
{
    internal TabularImportCompleted(
        Guid operationId,
        Guid journalEntryId,
        JournalScope scope,
        ImportJournalDecision decision,
        ImportReportData report
    )
    {
        OperationId = operationId;
        JournalEntryId = journalEntryId;
        Scope = scope;
        Decision = decision;
        Status = report.Status;
        IsComplete = report.IsComplete;
        TotalErrorCount = report.TotalErrorCount;
        OmittedErrorCount = report.OmittedErrorCount;
        RowsRead = report.RowsRead;
        RowsImported = report.RowsImported;
        RowsRejected = report.RowsRejected;
        RowsSkipped = report.RowsSkipped;
        RowsUncertain = report.RowsUncertain;
    }

    /// <summary>Gets the operation identifier of the call.</summary>
    public Guid OperationId { get; }

    /// <summary>Gets the log entry of the call.</summary>
    public Guid JournalEntryId { get; }

    /// <summary>Gets the scope.</summary>
    public JournalScope Scope { get; }

    /// <summary>Gets the journal decision.</summary>
    public ImportJournalDecision Decision { get; }

    /// <summary>Gets the report status.</summary>
    public ReportStatus Status { get; }

    /// <summary>Gets whether all selected rows were checked.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the number of all errors.</summary>
    public long TotalErrorCount { get; }

    /// <summary>Gets the number of omitted error details.</summary>
    public long OmittedErrorCount { get; }

    /// <summary>Gets the delimited records.</summary>
    public long RowsRead { get; }

    /// <summary>Gets the committed rows.</summary>
    public long RowsImported { get; }

    /// <summary>Gets the rejected rows.</summary>
    public long RowsRejected { get; }

    /// <summary>Gets the skipped existing records.</summary>
    public long RowsSkipped { get; }

    /// <summary>Gets the rows with uncertain storage.</summary>
    public long RowsUncertain { get; }
}

/// <summary>Content of the completion function of a journaled export.</summary>
[PublicAPI]
public sealed class TabularExportCompleted
{
    internal TabularExportCompleted(
        Guid operationId,
        Guid exportId,
        JournalScope scope,
        ExportReport report,
        bool isReadyForDownload
    )
    {
        OperationId = operationId;
        JournalEntryId = exportId;
        ExportId = exportId;
        Scope = scope;
        Status = report.Status;
        IsComplete = report.IsComplete;
        TotalErrorCount = report.TotalErrorCount;
        OmittedErrorCount = report.OmittedErrorCount;
        FileId = report.FileId;
        IsReadyForDownload = isReadyForDownload;
    }

    /// <summary>Gets the operation identifier of the call.</summary>
    public Guid OperationId { get; }

    /// <summary>Gets the log entry of the call; equal to <see cref="ExportId" />.</summary>
    public Guid JournalEntryId { get; }

    /// <summary>Gets the export identifier.</summary>
    public Guid ExportId { get; }

    /// <summary>Gets the scope.</summary>
    public JournalScope Scope { get; }

    /// <summary>Gets the report status.</summary>
    public ReportStatus Status { get; }

    /// <summary>Gets whether all sources were enumerated and the document was produced.</summary>
    public bool IsComplete { get; }

    /// <summary>Gets the number of all errors.</summary>
    public long TotalErrorCount { get; }

    /// <summary>Gets the number of omitted error details.</summary>
    public long OmittedErrorCount { get; }

    /// <summary>Gets the file identifier of the store.</summary>
    public Guid? FileId { get; }

    /// <summary>Gets whether the stored file is ready for download.</summary>
    public bool IsReadyForDownload { get; }
}

/// <summary>Status of a standalone journal call.</summary>
[PublicAPI]
public enum JournalCallStatus
{
    /// <summary>The call completed.</summary>
    Succeeded = 0,

    /// <summary>The entry does not exist in this journal.</summary>
    NotFound = 1,

    /// <summary>The state contradicts the request, for example evidence of a commit against <see cref="ImportResolutionOutcome.NoChanges" />.</summary>
    Conflict = 2,

    /// <summary>The call failed technically; <see cref="TabularJournalResult.Error" /> names the cause.</summary>
    Failed = 3,

    /// <summary>The call was canceled.</summary>
    Canceled = 4,
}

/// <summary>State of an import determined by <c>RecoverImportAsync</c>.</summary>
[PublicAPI]
public enum ImportRecoveryState
{
    /// <summary>The attempt ended without changes; a new attempt is possible.</summary>
    NoChanges = 0,

    /// <summary>Data was committed; the file counts as imported.</summary>
    Imported = 1,

    /// <summary>The attempt is still running in this process.</summary>
    InProgress = 2,

    /// <summary>The evidence does not decide the state; the application resolves it with <c>ResolveImportAsync</c>.</summary>
    Unresolved = 3,
}

/// <summary>Result of a standalone journal call.</summary>
[PublicAPI]
public class TabularJournalResult
{
    internal TabularJournalResult(
        JournalCallStatus status,
        TabularError? error,
        long count = 0
    )
    {
        Status = status;
        Error = error;
        Count = count;
    }

    /// <summary>Gets the call status.</summary>
    public JournalCallStatus Status { get; }

    /// <summary>Gets the error of a failed or conflicting call.</summary>
    public TabularError? Error { get; }

    /// <summary>Gets the number of processed entries of a delete call.</summary>
    public long Count { get; }
}

/// <summary>Result of <c>RecoverImportAsync</c>: the call status separate from the import state.</summary>
[PublicAPI]
public sealed class ImportRecoveryResult : TabularJournalResult
{
    internal ImportRecoveryResult(
        JournalCallStatus status,
        ImportRecoveryState? state,
        TabularError? error
    )
        : base(status, error)
    {
        State = state;
    }

    /// <summary>Gets the state; <see langword="null" /> when the call did not succeed.</summary>
    public ImportRecoveryState? State { get; }
}

/// <summary>Settings of one deletion of a stored export.</summary>
[PublicAPI]
public sealed record ExportDeletionOptions
{
    /// <summary>
    /// Gets whether an export still marked as running may be deleted. The application sets it only after it made sure
    /// the export ended, for example after the journal database failed during the last steps of an export; deleting
    /// an export that still runs lets it become ready again.
    /// </summary>
    public bool AllowUnfinished { get; init; }
}

/// <summary>Decision of the application for an unresolved import.</summary>
[PublicAPI]
public sealed record ImportResolution
{
    /// <summary>Gets the outcome.</summary>
    public required ImportResolutionOutcome Outcome { get; init; }

    /// <summary>Gets the reason; required, at most 1000 characters.</summary>
    public required string Reason { get; init; }
}
