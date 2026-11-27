namespace Doka.Tabular.EntityFrameworkCore;

public sealed partial class TabularJournal
{
    /// <summary>
    /// Reconciles log, claim, and the evidence committed with the data, without file, map, or target. Committed evidence
    /// proves an import; missing evidence proves nothing while the owner could still run, then the state is unresolved.
    /// </summary>
    /// <param name="importId">The log entry of the import.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call status and the state.</returns>
    public Task<ImportRecoveryResult> RecoverImportAsync(
        Guid importId,
        CancellationToken cancellationToken
    ) => RunAsync(
        "import",
        async context =>
        {
            var (log, claim) = await LoadAsync(context, importId, cancellationToken)
                .ConfigureAwait(false);

            if (log is null)
            {
                return new ImportRecoveryResult(JournalCallStatus.NotFound, null, null);
            }

            // WHY: An attempt of this process that has not taken the claim yet, for example while it hashes the input,
            // still runs; its outcome is not known yet.
            if ((claim is null || claim.CurrentImportId != importId)
                && ImportJournalRun.IsRunning(importId))
            {
                return new ImportRecoveryResult(JournalCallStatus.Succeeded, ImportRecoveryState.InProgress, null);
            }

            // WHY: Without a claim no data can exist yet, but an attempt of another process that has not ended may still
            // take the claim; its outcome is open until the application resolves it.
            if ((claim is null || claim.CurrentImportId != importId)
                && log is { CompletedAt: null, ResolvedAt: null })
            {
                return new ImportRecoveryResult(JournalCallStatus.Succeeded, ImportRecoveryState.Unresolved, null);
            }

            if (claim is null
                || claim.CurrentImportId != importId)
            {
                return new ImportRecoveryResult(
                    JournalCallStatus.Succeeded,
                    log.RowsImported > 0
                        ? ImportRecoveryState.Imported
                        : ImportRecoveryState.NoChanges,
                    null);
            }

            switch (claim.State)
            {
                // WHY: The claim proves committed data, not the end of the attempt: it is imported after its first
                // commit group, while the owner may still run in another process. Recovery therefore leaves the log
                // as it is.
                case ImportClaimState.Imported:
                    return new ImportRecoveryResult(JournalCallStatus.Succeeded, ImportRecoveryState.Imported, null);
                case ImportClaimState.Released:
                    return new ImportRecoveryResult(JournalCallStatus.Succeeded, ImportRecoveryState.NoChanges, null);
                case ImportClaimState.Running when ImportJournalRun.IsRunning(importId):
                    return new ImportRecoveryResult(JournalCallStatus.Succeeded, ImportRecoveryState.InProgress, null);
                case ImportClaimState.Running:
                    claim.Set(ImportClaimState.Unresolved, Options.TimeProvider.GetUtcNow());
                    JournalStore.Touch(context, claim);
                    await context
                        .SaveChangesAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return new ImportRecoveryResult(JournalCallStatus.Succeeded, ImportRecoveryState.Unresolved, null);
                case ImportClaimState.Unresolved:
                // WHY: The state is read from the database; a stored value outside the enum is no proof of an
                // outcome, so recovery reports it as unresolved.
                default:
                    return new ImportRecoveryResult(JournalCallStatus.Succeeded, ImportRecoveryState.Unresolved, null);
            }
        },
        error => new ImportRecoveryResult(error.Status, null, error.Error),
        cancellationToken);

    /// <summary>
    /// Records the decision of the application for an unresolved import, or confirms the end of a crashed import whose
    /// claim holds commit evidence but whose log never ended. Existing commit evidence cannot be overruled:
    /// <see cref="ImportResolutionOutcome.NoChanges" /> against evidence, an import that ended itself or was resolved
    /// before, and an import still running in this process are conflicts.
    /// </summary>
    /// <param name="importId">The log entry of the import.</param>
    /// <param name="resolution">The outcome and its reason.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call status.</returns>
    /// <exception cref="ArgumentException">The reason is empty or longer than 1000 characters.</exception>
    public Task<TabularJournalResult> ResolveImportAsync(
        Guid importId,
        ImportResolution resolution,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution.Reason);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(resolution.Reason.Length, 1000, nameof(resolution));

        return RunAsync(
            "import",
            async context =>
            {
                var (log, claim) = await LoadAsync(context, importId, cancellationToken)
                    .ConfigureAwait(false);

                if (log is null)
                {
                    return new TabularJournalResult(JournalCallStatus.NotFound, null);
                }

                // WHY: Whatever another process recorded about the attempt, this process knows that it still runs; a
                // resolution here would end it.
                if (ImportJournalRun.IsRunning(importId))
                {
                    return Conflict("The import is still running in this process.");
                }

                var now = Options.TimeProvider.GetUtcNow();

                // WHY: An attempt that never took the claim wrote no data; NoChanges ends its log, Imported has nothing
                // to confirm.
                if ((claim is null || claim.CurrentImportId != importId)
                    && log is { CompletedAt: null, ResolvedAt: null })
                {
                    if (resolution.Outcome != ImportResolutionOutcome.NoChanges)
                    {
                        return Conflict("The import holds no claim, so no data of it can be imported.");
                    }

                    log.Resolve(resolution.Outcome, resolution.Reason, now);
                    log.EndUnreported(now);
                    JournalStore.Touch(context, log);

                    return await SaveResolutionAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                }

                // WHY: The claim proves committed data but not the end of the attempt; only the application can attest
                // that a crashed owner stopped. Imported agrees with the evidence and ends the log, NoChanges would
                // overrule it.
                if (claim is { State: ImportClaimState.Imported }
                    && claim.CurrentImportId == importId
                    && log is { CompletedAt: null, ResolvedAt: null })
                {
                    if (resolution.Outcome != ImportResolutionOutcome.Imported)
                    {
                        return Conflict("The claim holds commit evidence; NoChanges cannot overrule it.");
                    }

                    log.Resolve(resolution.Outcome, resolution.Reason, now);
                    log.EndUnreported(now);
                    JournalStore.Touch(context, log);

                    return await SaveResolutionAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (claim is not { State: ImportClaimState.Unresolved }
                    || claim.CurrentImportId != importId)
                {
                    return Conflict(
                        "The claim of the import is not unresolved; it may hold commit evidence or was resolved before.");
                }

                claim.Set(
                    resolution.Outcome == ImportResolutionOutcome.NoChanges
                        ? ImportClaimState.Released
                        : ImportClaimState.Imported,
                    now);

                log.Resolve(resolution.Outcome, resolution.Reason, now);
                log.EndUnreported(now);
                JournalStore.Touch(context, claim);
                JournalStore.Touch(context, log);

                return await SaveResolutionAsync(context, cancellationToken)
                    .ConfigureAwait(false);
            },
            error => error,
            cancellationToken);
    }

    private static async Task<TabularJournalResult> SaveResolutionAsync(
        DbContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await context
                .SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The claim changed during the call.");
        }

        return new TabularJournalResult(JournalCallStatus.Succeeded, null);
    }

    /// <summary>
    /// Deletes ended import logs whose outcome is settled. A log is ended when the import wrote its end or the
    /// application resolved it; its outcome is settled when it ended safely, was resolved, or its claim records it as
    /// imported or released. A log with an open outcome stays for <see cref="RecoverImportAsync" />. Claims stay, so the
    /// duplicate protection remains.
    /// </summary>
    /// <param name="completedBefore">Logs completed before this time are deleted.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The call status and the number of deleted logs.</returns>
    public Task<TabularJournalResult> DeleteImportLogsAsync(
        DateTimeOffset completedBefore,
        CancellationToken cancellationToken
    ) => RunAsync(
        "import",
        async context =>
        {
            var claims = context.Set<TabularImportClaim>(JournalModel.ImportClaim(Name));
            var deleted = await context
                .Set<TabularImportLog>(JournalModel.ImportLog(Name))
                .Where(log => log.CompletedAt != null
                    && log.CompletedAt < completedBefore
                    && (log.Status != null
                        || log.ResolvedAt != null
                        || claims.Any(claim => claim.CurrentImportId == log.Id
                            && (claim.State == ImportClaimState.Imported || claim.State == ImportClaimState.Released))))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new TabularJournalResult(JournalCallStatus.Succeeded, null, deleted);
        },
        error => error,
        cancellationToken);

    private async Task<(TabularImportLog? Log, TabularImportClaim? Claim)> LoadAsync(
        DbContext context,
        Guid importId,
        CancellationToken cancellationToken
    )
    {
        var log = await context
            .Set<TabularImportLog>(JournalModel.ImportLog(Name))
            .SingleOrDefaultAsync(entry => entry.Id == importId, cancellationToken)
            .ConfigureAwait(false);

        if (log?.Hash is null)
        {
            return (log, null);
        }

        var claim = await JournalStore
            .Claim(context, Name, JournalStore.ClaimScope(context, Name, log.Scope), log.Hash)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (log, claim);
    }
}
