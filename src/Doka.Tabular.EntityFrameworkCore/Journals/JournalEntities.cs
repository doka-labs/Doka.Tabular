namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>State of an import claim.</summary>
[PublicAPI]
public enum ImportClaimState
{
    /// <summary>An attempt holds the claim and has not ended.</summary>
    Running = 0,

    /// <summary>Data of the file was committed, or the application resolved the claim as imported; the file is a duplicate.</summary>
    Imported = 1,

    /// <summary>The outcome of the holding attempt is unclear; a new attempt waits for recovery or resolution.</summary>
    Unresolved = 2,

    /// <summary>The holding attempt ended safely without changes; a new attempt may take the claim.</summary>
    Released = 3,
}

/// <summary>Status of a stored export.</summary>
[PublicAPI]
public enum TabularExportLogStatus
{
    /// <summary>The file is being produced.</summary>
    Generating = 0,

    /// <summary>The file is being stored.</summary>
    Storing = 1,

    /// <summary>The file is stored and can be downloaded until <see cref="TabularExportLog.ExpiresAt" />.</summary>
    Ready = 2,

    /// <summary>The export failed.</summary>
    Failed = 3,

    /// <summary>The export was canceled.</summary>
    Canceled = 4,

    /// <summary>The stored file was deleted.</summary>
    Deleted = 5,
}

/// <summary>Outcome recorded by <c>ResolveImportAsync</c>.</summary>
[PublicAPI]
public enum ImportResolutionOutcome
{
    /// <summary>The application confirms that no data was committed; the claim is released.</summary>
    NoChanges = 0,

    /// <summary>The application confirms that data was committed; the file counts as imported.</summary>
    Imported = 1,
}

/// <summary>Log entry of one journaled import call. Changed only through the journal calls.</summary>
[PublicAPI]
public sealed class TabularImportLog
{
    // ReSharper disable once UnusedMember.Local
    // WHY: EF Core materializes entities through this constructor.
    private TabularImportLog() { }

    internal TabularImportLog(
        Guid id,
        JournalScope scope,
        string? fileName,
        Guid? fileId,
        Guid operationId,
        DateTimeOffset startedAt
    )
    {
        Id = id;
        Scope = scope;
        FileName = fileName;
        FileId = fileId;
        OperationId = operationId;
        StartedAt = startedAt;
        Decision = ImportJournalDecision.Processed;
    }

    /// <summary>Gets the identifier of the entry.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the scope.</summary>
    public JournalScope Scope { get; private set; } = null!;

    /// <summary>Gets the display file name.</summary>
    public string? FileName { get; private set; }

    /// <summary>Gets the SHA-256 hash of the complete file; <see langword="null" /> until the file was read completely.</summary>
    public Sha256Hash? Hash { get; private set; }

    /// <summary>Gets the size of the complete file.</summary>
    public long? ByteSize { get; private set; }

    /// <summary>Gets the optional file identifier of the application.</summary>
    public Guid? FileId { get; private set; }

    /// <summary>Gets the operation identifier of the call.</summary>
    public Guid OperationId { get; private set; }

    /// <summary>Gets the start time.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets the end time.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Gets the duration in milliseconds.</summary>
    public long? DurationMilliseconds { get; private set; }

    /// <summary>Gets the time of a fully successful import.</summary>
    public DateTimeOffset? ImportedAt { get; private set; }

    /// <summary>Gets the report status; <see langword="null" /> as long as the attempt has not ended safely.</summary>
    public ReportStatus? Status { get; private set; }

    /// <summary>Gets the journal decision of the call.</summary>
    public ImportJournalDecision Decision { get; private set; }

    /// <summary>Gets the delimited records.</summary>
    public long RowsRead { get; private set; }

    /// <summary>Gets the committed rows.</summary>
    public long RowsImported { get; private set; }

    /// <summary>Gets the rejected rows.</summary>
    public long RowsRejected { get; private set; }

    /// <summary>Gets the skipped existing records.</summary>
    public long RowsSkipped { get; private set; }

    /// <summary>Gets the rows with uncertain storage.</summary>
    public long RowsUncertain { get; private set; }

    /// <summary>Gets the number of all errors.</summary>
    public long TotalErrorCount { get; private set; }

    /// <summary>Gets the outcome recorded by the application, separate from technical evidence.</summary>
    public ImportResolutionOutcome? ResolutionOutcome { get; private set; }

    /// <summary>Gets the reason of the application's resolution.</summary>
    public string? ResolutionReason { get; private set; }

    /// <summary>Gets the time of the resolution.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Records hash and size once the file was read completely.</summary>
    internal void Hashed(
        Sha256Hash hash,
        long byteSize
    )
    {
        Hash = hash;
        ByteSize = byteSize;
    }

    internal void Decide(
        ImportJournalDecision decision
    ) => Decision = decision;

    internal void Complete(
        ImportReportData report,
        DateTimeOffset now,
        bool safelyEnded
    )
    {
        CompletedAt = now;
        DurationMilliseconds = (long)(now - StartedAt).TotalMilliseconds;
        Status = safelyEnded
            ? report.Status
            : null;

        ImportedAt = safelyEnded
            && report.Status == ReportStatus.Succeeded
            && Decision == ImportJournalDecision.Processed
                ? now
                : null;

        RowsRead = report.RowsRead;
        RowsImported = report.RowsImported;
        RowsRejected = report.RowsRejected;
        RowsSkipped = report.RowsSkipped;
        RowsUncertain = report.RowsUncertain;
        TotalErrorCount = report.TotalErrorCount;
    }

    /// <summary>
    /// Ends the log of an attempt that never wrote its end, once its outcome is settled by the recovery or the
    /// application; the status stays unknown, since no report of the attempt exists.
    /// </summary>
    internal void EndUnreported(
        DateTimeOffset now
    )
    {
        if (CompletedAt is not null)
        {
            return;
        }

        CompletedAt = now;
        DurationMilliseconds = (long)(now - StartedAt).TotalMilliseconds;
    }

    internal void Resolve(
        ImportResolutionOutcome outcome,
        string reason,
        DateTimeOffset now
    )
    {
        ResolutionOutcome = outcome;
        ResolutionReason = reason;
        ResolvedAt = now;
    }
}

/// <summary>Claim of one file hash in one scope; the database guarantees one claim per journal, scope, and hash.</summary>
[PublicAPI]
public sealed class TabularImportClaim
{
    // ReSharper disable once UnusedMember.Local
    // WHY: EF Core materializes entities through this constructor.
    private TabularImportClaim() { }

    internal TabularImportClaim(
        string scope,
        Sha256Hash hash,
        Guid importId,
        DateTimeOffset now
    )
    {
        Scope = scope;
        ScopeKey = JournalStore.ScopeKey(scope);
        Hash = hash;
        CurrentImportId = importId;
        State = ImportClaimState.Running;
        UpdatedAt = now;
    }

    /// <summary>Gets the scope as compared by the journal; upper case when the journal ignores the scope case.</summary>
    public string Scope { get; private set; } = string.Empty;

    /// <summary>Gets the SHA-256 hash of the file.</summary>
    public Sha256Hash Hash { get; private set; } = null!;

    /// <summary>Gets the import that holds the claim.</summary>
    public Guid CurrentImportId { get; private set; }

    /// <summary>Gets the state.</summary>
    public ImportClaimState State { get; private set; }

    /// <summary>Gets the time of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    // WHY: Mapped by EF as part of the key; read only in queries through EF.Property.
    private string ScopeKey { get; set; } = string.Empty;

    internal void Take(
        Guid importId,
        DateTimeOffset now
    )
    {
        CurrentImportId = importId;
        State = ImportClaimState.Running;
        UpdatedAt = now;
    }

    internal void Set(
        ImportClaimState state,
        DateTimeOffset now
    )
    {
        State = state;
        UpdatedAt = now;
    }
}

/// <summary>Log entry of one stored export. File content and records are not stored.</summary>
[PublicAPI]
public sealed class TabularExportLog
{
    // ReSharper disable once UnusedMember.Local
    // WHY: EF Core materializes entities through this constructor.
    private TabularExportLog() { }

    internal TabularExportLog(
        Guid id,
        JournalScope scope,
        string fileName,
        TabularType format,
        Guid operationId,
        DateTimeOffset startedAt
    )
    {
        Id = id;
        Scope = scope;
        FileName = fileName;
        Format = format;
        OperationId = operationId;
        StartedAt = startedAt;
        Status = TabularExportLogStatus.Generating;
    }

    /// <summary>Gets the export identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the scope.</summary>
    public JournalScope Scope { get; private set; } = null!;

    /// <summary>Gets the display file name.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Gets the format.</summary>
    public TabularType Format { get; private set; }

    /// <summary>Gets the file identifier of the store; set once the store confirmed the file.</summary>
    public Guid? FileId { get; private set; }

    /// <summary>Gets the size of the stored file.</summary>
    public long? ByteSize { get; private set; }

    /// <summary>Gets the SHA-256 hash of the stored file.</summary>
    public Sha256Hash? Hash { get; private set; }

    /// <summary>Gets the operation identifier of the call.</summary>
    public Guid OperationId { get; private set; }

    /// <summary>Gets the start time.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Gets the end time.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Gets the duration in milliseconds.</summary>
    public long? DurationMilliseconds { get; private set; }

    /// <summary>Gets the status.</summary>
    public TabularExportLogStatus Status { get; private set; }

    /// <summary>Gets the time the file became ready.</summary>
    public DateTimeOffset? ReadyAt { get; private set; }

    /// <summary>Gets the expiry; at and after this time the export is no longer available.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Gets the time the file was deleted.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    internal void MarkStoring() => Status = TabularExportLogStatus.Storing;

    internal void MarkReady(
        TabularStoredFile file,
        DateTimeOffset now,
        TimeSpan? retention
    )
    {
        Status = TabularExportLogStatus.Ready;
        FileId = file.FileId;
        ByteSize = file.ByteSize;
        Hash = file.Sha256;
        ReadyAt = now;

        // WHY: The retention was checked against the time of the start; the clock moved until Ready, so a retention that
        // ended just before the latest date may now reach beyond it. It then ends at the latest date.
        ExpiresAt = retention is { } duration
            ? duration > DateTimeOffset.MaxValue - now
                ? DateTimeOffset.MaxValue
                : now + duration
            : null;

        CompletedAt = now;
        DurationMilliseconds = (long)(now - StartedAt).TotalMilliseconds;
    }

    internal void MarkEnded(
        ReportStatus status,
        Guid? fileId,
        DateTimeOffset now
    )
    {
        Status = status == ReportStatus.Canceled
            ? TabularExportLogStatus.Canceled
            : TabularExportLogStatus.Failed;

        // WHY: A file the store confirmed stays reachable through the log, even when Ready was never written.
        FileId ??= fileId;

        CompletedAt = now;
        DurationMilliseconds = (long)(now - StartedAt).TotalMilliseconds;
    }

    internal void Expire(
        DateTimeOffset now
    )
    {
        if (ExpiresAt is null
            || ExpiresAt > now)
        {
            ExpiresAt = now;
        }
    }

    internal void MarkDeleted(
        DateTimeOffset now
    )
    {
        Status = TabularExportLogStatus.Deleted;
        DeletedAt = now;
    }
}
