namespace Doka.Tabular.EntityFrameworkCore;

public sealed partial class TabularJournal
{
    /// <summary>Deletes completed export logs; logs with a file that is ready or not yet deleted stay.</summary>
    /// <param name="completedBefore">
    /// Logs completed before this time are deleted; a deleted export that was never completed counts by its deletion
    /// time.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call status and the number of deleted logs.</returns>
    public Task<TabularJournalResult> DeleteExportLogsAsync(
        DateTimeOffset completedBefore,
        CancellationToken cancellationToken
    ) => RunAsync("export",
        async context =>
        {
            var deleted = await context
                .Set<TabularExportLog>(JournalModel.ExportLog(Name))

                // WHY: A log deleted with AllowUnfinished may never have been completed; its deletion time counts.
                .Where(log => (log.CompletedAt != null
                        && log.CompletedAt < completedBefore
                        && (log.Status == TabularExportLogStatus.Deleted
                            || (log.FileId == null
                                && (log.Status == TabularExportLogStatus.Failed
                                    || log.Status == TabularExportLogStatus.Canceled))))
                    || (log.CompletedAt == null
                        && log.Status == TabularExportLogStatus.Deleted
                        && log.DeletedAt < completedBefore))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TabularJournalResult(JournalCallStatus.Succeeded, null, deleted);
        },
        error => error,
        cancellationToken);

    /// <summary>
    /// Deletes a stored export: first it becomes unavailable, then the store deletes the file, then the log is marked
    /// deleted. The log entry stays. After a failed file deletion a repeated call continues. An export that is still
    /// running is a conflict; repeat the call after it ended.
    /// </summary>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="store">The storage adapter of the file.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call status.</returns>
    public Task<TabularJournalResult> DeleteExportAsync(
        Guid exportId,
        ITabularFileStore store,
        CancellationToken cancellationToken
    ) => DeleteExportAsync(exportId, store, new ExportDeletionOptions(), cancellationToken);

    /// <summary>
    /// Deletes a stored export like <see cref="DeleteExportAsync(Guid, ITabularFileStore, CancellationToken)" />;
    /// with <see cref="ExportDeletionOptions.AllowUnfinished" /> also an export still marked as running.
    /// </summary>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="store">The storage adapter of the file.</param>
    /// <param name="options">The settings of this deletion.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call status.</returns>
    public Task<TabularJournalResult> DeleteExportAsync(
        Guid exportId,
        ITabularFileStore store,
        ExportDeletionOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);

        return RunAsync("export",
            async context =>
            {
                var log = await context
                    .Set<TabularExportLog>(JournalModel.ExportLog(Name))
                    .SingleOrDefaultAsync(entry => entry.Id == exportId, cancellationToken)
                    .ConfigureAwait(false);

                return log is null
                    ? new TabularJournalResult(JournalCallStatus.NotFound, null)
                    : await DeleteAsync(context, log, store, options.AllowUnfinished, cancellationToken)
                        .ConfigureAwait(false);
            },
            error => error,
            cancellationToken);
    }

    /// <summary>Deletes all expired stored exports in the same way and retries failed deletions; single failures do not stop the others.</summary>
    /// <param name="store">The storage adapter of the files.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call status, the first error, and the number of deleted exports.</returns>
    public Task<TabularJournalResult> DeleteExpiredExportsAsync(
        ITabularFileStore store,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(store);

        return RunAsync("export",
            async context =>
            {
                var now = Options.TimeProvider.GetUtcNow();
                var candidates = await context
                    .Set<TabularExportLog>(JournalModel.ExportLog(Name))

                    // WHY: A failed deletion leaves an expired entry in any ended state, also Failed or Canceled; the
                    // cleanup retries it. Running exports are deleted only explicitly with AllowUnfinished.
                    .Where(log => log.Status != TabularExportLogStatus.Deleted
                        && log.Status != TabularExportLogStatus.Generating
                        && log.Status != TabularExportLogStatus.Storing
                        && log.ExpiresAt != null
                        && log.ExpiresAt <= now)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                long deleted = 0;
                TabularError? first = null;

                // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var logIndex = 0; logIndex < candidates.Count; logIndex++)
                {
                    var log = candidates[logIndex];
                    var result = await DeleteCandidateAsync(context, log, store, cancellationToken)
                        .ConfigureAwait(false);

                    if (result.Status == JournalCallStatus.Succeeded)
                    {
                        deleted++;
                    }
                    else
                    {
                        first ??= result.Error;
                    }
                }

                return new TabularJournalResult(first is null
                        ? JournalCallStatus.Succeeded
                        : JournalCallStatus.Failed,
                    first,
                    deleted);
            },
            error => error,
            cancellationToken);
    }

    /// <summary>
    /// Deletes one expired export; a failing log update is its own error and leaves the other candidates unaffected.
    /// </summary>
    private async Task<TabularJournalResult> DeleteCandidateAsync(
        DbContext context,
        TabularExportLog log,
        ITabularFileStore store,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await DeleteAsync(context, log, store, allowUnfinished: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException
            && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // WHY: The failed entry must not take part in the save of the next candidate.
            context.Entry(log)
                .State = EntityState.Detached;

            return new TabularJournalResult(JournalCallStatus.Failed,
                TabularError.Create(exception is DbUpdateConcurrencyException
                        ? TabularErrorCodes.Persistence.Conflict
                        : TabularErrorCodes.Persistence.WriteFailed,
                    TabularErrorKind.Technical,
                    "The log of an expired export could not be updated; the export was not deleted."));
        }
    }

    private async Task<TabularJournalResult> DeleteAsync(
        DbContext context,
        TabularExportLog log,
        ITabularFileStore store,
        bool allowUnfinished,
        CancellationToken cancellationToken
    )
    {
        if (log.Status == TabularExportLogStatus.Deleted)
        {
            return new TabularJournalResult(JournalCallStatus.Succeeded, null);
        }

        // WHY: A running export would set Ready after this deletion and make the file available again.
        if (!allowUnfinished
            && log.Status is TabularExportLogStatus.Generating or TabularExportLogStatus.Storing)
        {
            return new TabularJournalResult(JournalCallStatus.Conflict,
                TabularError.Create(TabularErrorCodes.Persistence.Conflict,
                    TabularErrorKind.Technical,
                    "The export is still running; delete it after it ended."));
        }

        log.Expire(Options.TimeProvider.GetUtcNow());
        JournalStore.Touch(context, log);
        await context
            .SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await store
                .DeleteAsync(log.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new TabularJournalResult(JournalCallStatus.Failed,
                TabularError.Create(TabularErrorCodes.Io.Failed,
                    TabularErrorKind.Technical,
                    "The storage adapter failed to delete the file; the export stays unavailable."));
        }

        log.MarkDeleted(Options.TimeProvider.GetUtcNow());
        JournalStore.Touch(context, log);
        await context
            .SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);

        return new TabularJournalResult(JournalCallStatus.Succeeded, null);
    }
}
