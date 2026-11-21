namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Export log for stored exports.</summary>
[PublicAPI]
public static class TabularFileTargetExtensions
{
    /// <summary>Returns a target that records the stored export in <paramref name="journal" />.</summary>
    /// <param name="target">The storage target.</param>
    /// <param name="journal">The journal.</param>
    /// <param name="options">Scope and completion function.</param>
    /// <returns>The new target.</returns>
    public static TabularFileTarget WithExportLog(
        this TabularFileTarget target,
        TabularJournal journal,
        TabularExportLogOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Scope);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.CompletionTimeout, TimeSpan.Zero, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.CompletionTimeout,
            CompletionRunner.MaxTimeout,
            nameof(options));

        return target.WithJournal(new ExportJournalHook(journal, options));
    }
}

internal sealed class ExportJournalHook : IExportJournalHook
{
    private readonly TabularJournal _journal;
    private readonly TabularExportLogOptions _options;

    public ExportJournalHook(
        TabularJournal journal,
        TabularExportLogOptions options
    )
    {
        _journal = journal;
        _options = options;
    }

    public async Task<IReadOnlyList<TabularError>> CheckAsync(
        CancellationToken cancellationToken
    )
    {
        var context = await _journal
            .CreateContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (context.ConfigureAwait(false))
        {
            return JournalModel.Find(context, JournalModel.ExportLog(_journal.Name)) is null
                ? [TabularError.Configuration($"The export journal {_journal.Name} is not registered in the model.")]
                : [];
        }
    }

    public async Task<ExportJournalSession> StartAsync(
        ExportJournalStart start,
        CancellationToken cancellationToken
    )
    {
        var log = new TabularExportLog(Guid.NewGuid(),
            _options.Scope,
            start.FileName,
            start.Format == "excel"
                ? TabularType.Excel
                : TabularType.Csv,
            start.OperationId,
            start.Time.GetUtcNow());

        var session = new Session(_journal, _options, log.Id, start);
        await session
            .UpdateAsync(static _ => { }, log, cancellationToken)
            .ConfigureAwait(false);

        return session;
    }

    private sealed class Session : ExportJournalSession
    {
        private readonly TabularJournal _journal;
        private readonly TabularExportLogOptions _options;
        private readonly ExportJournalStart _start;

        public Session(
            TabularJournal journal,
            TabularExportLogOptions options,
            Guid exportId,
            ExportJournalStart start
        )
        {
            _journal = journal;
            _options = options;
            ExportId = exportId;
            _start = start;
        }

        public override Guid ExportId { get; }

        public override Task MarkStoringAsync(
            CancellationToken cancellationToken
        ) => UpdateAsync(log => log.MarkStoring(), null, cancellationToken);

        public override Task MarkReadyAsync(
            TabularStoredFile file,
            CancellationToken cancellationToken
        ) => UpdateAsync(log => log.MarkReady(file, _start.Time.GetUtcNow(), _start.Retention),
            null,
            cancellationToken);

        public override Task MarkEndedAsync(
            ReportStatus status,
            Guid? fileId
        ) => UpdateAsync(log => log.MarkEnded(status, fileId, _start.Time.GetUtcNow()), null, CancellationToken.None);

        public override async Task<TabularError?> CompleteAsync(
            ExportReport report,
            bool isReadyForDownload
        ) => _options.OnCompletedAsync is { } completed
            ? await CompletionRunner
                .RunAsync(
                    token => completed(new TabularExportCompleted(_start.OperationId,
                            ExportId,
                            _options.Scope,
                            report,
                            isReadyForDownload),
                        token),
                    _options.CompletionTimeout,
                    _start.Time)
                .ConfigureAwait(false)
            : null;

        /// <summary>Writes one step through a fresh journal context; <paramref name="insert" /> adds a new entry.</summary>
        public async Task UpdateAsync(
            Action<TabularExportLog> change,
            TabularExportLog? insert,
            CancellationToken cancellationToken
        )
        {
            var context = await _journal
                .CreateContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var saved = false;
            try
            {
                var logs = context.Set<TabularExportLog>(JournalModel.ExportLog(_journal.Name));
                TabularExportLog log;
                if (insert is not null)
                {
                    log = insert;
                    logs.Add(log);
                }
                else
                {
                    var id = ExportId;
                    log = await logs
                        .SingleAsync(entry => entry.Id == id, cancellationToken)
                        .ConfigureAwait(false);
                }

                change(log);
                JournalStore.Touch(context, log);
                await context
                    .SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                saved = true;
            }
            finally
            {
                try
                {
                    await context
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (saved && insert is not null && exception is not OutOfMemoryException)
                {
                    // WHY: The start entry is stored; the export must still end it and run its completion, so the
                    // failing release is reported by the run instead of hiding the entry.
                    StartReleaseFailed = true;
                }
            }
        }
    }
}
