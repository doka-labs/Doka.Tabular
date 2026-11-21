namespace Doka.Tabular;

/// <summary>Overall result of an import or export.</summary>
[PublicAPI]
public enum ReportStatus
{
    /// <summary>Completed without errors; in an application transaction storage may still be pending.</summary>
    Succeeded,

    /// <summary>An explicit partial import or independent sheets ended regularly with data errors.</summary>
    CompletedWithErrors,

    /// <summary>A strict import was discarded or a non-recoverable error occurred.</summary>
    Failed,

    /// <summary>The cancellation token ended the run before completion; not a data error.</summary>
    Canceled,

    /// <summary>A sheet of a multi-sheet call was not started.</summary>
    NotStarted,
}

/// <summary>Output state of an export to a local path.</summary>
[PublicAPI]
public enum PathOutputState
{
    /// <summary>The new version was not published.</summary>
    NotPublished,

    /// <summary>The new version was published atomically under the target name.</summary>
    Published,

    /// <summary>The outcome of a started replacement cannot be determined.</summary>
    Unknown,
}

/// <summary>Output state of an export to a caller-owned stream.</summary>
[PublicAPI]
public enum StreamOutputState
{
    /// <summary>No byte of this call reached the stream.</summary>
    NotWritten,

    /// <summary>Some bytes were written; the content may be incomplete.</summary>
    Partial,

    /// <summary>The library completed its own stream operations.</summary>
    Complete,

    /// <summary>The outcome of a write cannot be determined.</summary>
    Unknown,
}

/// <summary>Correlation data of one call.</summary>
[PublicAPI]
public sealed class TabularReportDiagnostics
{
    internal TabularReportDiagnostics(
        Guid operationId
    )
    {
        OperationId = operationId;
    }

    /// <summary>Gets the identifier of the call, also present in every log event and as activity tag.</summary>
    public Guid OperationId { get; }
}

/// <summary>Journal decision of an import.</summary>
[PublicAPI]
public enum ImportJournalDecision
{
    /// <summary>The file was processed.</summary>
    Processed,

    /// <summary>The file was already imported and skipped.</summary>
    Skipped,

    /// <summary>The file was already imported and rejected as duplicate.</summary>
    RejectedDuplicate,

    /// <summary>The same import is currently running.</summary>
    Running,
}

/// <summary>Journal outcome of an import with import log.</summary>
[PublicAPI]
public sealed class ImportJournalResult
{
    internal ImportJournalResult(
        Guid entryId,
        ImportJournalDecision decision,
        Guid? existingImportId
    )
    {
        EntryId = entryId;
        Decision = decision;
        ExistingImportId = existingImportId;
    }

    /// <summary>Gets the log entry of this call.</summary>
    public Guid EntryId { get; }

    /// <summary>Gets the decision.</summary>
    public ImportJournalDecision Decision { get; }

    /// <summary>Gets the existing import for a duplicate decision.</summary>
    public Guid? ExistingImportId { get; }
}
