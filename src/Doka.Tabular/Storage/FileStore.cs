namespace Doka.Tabular;

/// <summary>Storage adapter for exported files, for example a file service or a directory.</summary>
[PublicAPI]
public interface ITabularFileStore
{
    /// <summary>Stores a finished file. A returned result means the file is stored durably.</summary>
    /// <param name="request">Identity and metadata of the file.</param>
    /// <param name="content">Readable, seekable content at position 0; read during the call, neither kept nor closed.</param>
    /// <param name="cancellationToken">Cancels the storage.</param>
    /// <returns>The stored file.</returns>
    Task<TabularStoredFile> SaveAsync(
        TabularFileWriteRequest request,
        Stream content,
        CancellationToken cancellationToken
    );

    /// <summary>Deletes the file of an export; succeeds when the file is already gone.</summary>
    /// <param name="exportId">The export.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes after deletion.</returns>
    Task DeleteAsync(
        Guid exportId,
        CancellationToken cancellationToken
    );
}

/// <summary>A file to store.</summary>
[PublicAPI]
public sealed class TabularFileWriteRequest
{
    internal TabularFileWriteRequest(
        Guid exportId,
        string fileName,
        string contentType,
        long byteSize,
        Sha256Hash sha256
    )
    {
        ExportId = exportId;
        FileName = fileName;
        ContentType = contentType;
        ByteSize = byteSize;
        Sha256 = sha256;
    }

    /// <summary>Gets the export the file belongs to.</summary>
    public Guid ExportId { get; }

    /// <summary>Gets the display file name.</summary>
    public string FileName { get; }

    /// <summary>Gets the MIME type, without charset.</summary>
    public string ContentType { get; }

    /// <summary>Gets the size of the final bytes.</summary>
    public long ByteSize { get; }

    /// <summary>Gets the hash of the final bytes.</summary>
    public Sha256Hash Sha256 { get; }
}

/// <summary>A stored file as reported by the storage adapter.</summary>
[PublicAPI]
public sealed class TabularStoredFile
{
    /// <summary>Creates the result of a storage.</summary>
    /// <param name="exportId">The export of the request.</param>
    /// <param name="fileId">The identifier assigned by the storage.</param>
    /// <param name="byteSize">The stored size.</param>
    /// <param name="sha256">The hash of the stored bytes.</param>
    public TabularStoredFile(
        Guid exportId,
        Guid fileId,
        long byteSize,
        Sha256Hash sha256
    )
    {
        ArgumentNullException.ThrowIfNull(sha256);
        ExportId = exportId;
        FileId = fileId;
        ByteSize = byteSize;
        Sha256 = sha256;
    }

    /// <summary>Gets the export.</summary>
    public Guid ExportId { get; }

    /// <summary>Gets the stored file.</summary>
    public Guid FileId { get; }

    /// <summary>Gets the stored size.</summary>
    public long ByteSize { get; }

    /// <summary>Gets the hash of the stored bytes.</summary>
    public Sha256Hash Sha256 { get; }
}

/// <summary>Journal hook of a stored export; implemented by the EF package.</summary>
internal interface IExportJournalHook
{
    /// <summary>Checks the registration of the journal before the first write; returns configuration errors.</summary>
    Task<IReadOnlyList<TabularError>> CheckAsync(
        CancellationToken cancellationToken
    );

    Task<ExportJournalSession> StartAsync(
        ExportJournalStart start,
        CancellationToken cancellationToken
    );
}

internal sealed record ExportJournalStart(
    Guid OperationId,
    string FileName,
    string Format,
    TimeSpan? Retention,
    TimeProvider Time
);

/// <summary>One journaled export; every step is written in its own journal context.</summary>
internal abstract class ExportJournalSession
{
    public abstract Guid ExportId { get; }

    /// <summary>Gets whether the entry of the start is stored, but its journal context could not be released.</summary>
    public bool StartReleaseFailed { get; protected set; }

    public abstract Task MarkStoringAsync(
        CancellationToken cancellationToken
    );

    public abstract Task MarkReadyAsync(
        TabularStoredFile file,
        CancellationToken cancellationToken
    );

    /// <summary>Ends an unsuccessful export; <paramref name="fileId" /> names a file the store confirmed.</summary>
    public abstract Task MarkEndedAsync(
        ReportStatus status,
        Guid? fileId
    );

    public abstract Task<TabularError?> CompleteAsync(
        ExportReport report,
        bool isReadyForDownload
    );
}

/// <summary>Export target that stores the finished file through an <see cref="ITabularFileStore" />.</summary>
[PublicAPI]
public sealed class TabularFileTarget
{
    private TabularFileTarget(
        ITabularFileStore store,
        string fileName,
        TimeSpan? retention,
        IExportJournalHook? journal
    )
    {
        Store = store;
        FileName = fileName;
        Retention = retention;
        Journal = journal;
    }

    internal ITabularFileStore Store { get; }

    internal string FileName { get; }

    internal TimeSpan? Retention { get; }

    internal IExportJournalHook? Journal { get; }

    /// <summary>Creates a target for a storage adapter.</summary>
    /// <param name="store">The storage adapter.</param>
    /// <param name="fileName">The display file name, at most 256 characters.</param>
    /// <returns>The target.</returns>
    public static TabularFileTarget To(
        ITabularFileStore store,
        string fileName
    )
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fileName.Length, 256, nameof(fileName));

        return new TabularFileTarget(store, fileName, null, null);
    }

    /// <summary>Sets a retention: on <c>Ready</c> the export expires after <paramref name="duration" />; requires an export log.</summary>
    /// <param name="duration">A positive duration.</param>
    /// <returns>A new target.</returns>
    public TabularFileTarget WithRetention(
        TimeSpan duration
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        return new TabularFileTarget(Store, FileName, duration, Journal);
    }

    internal TabularFileTarget WithJournal(
        IExportJournalHook journal
    ) => new(Store, FileName, Retention, journal);
}
