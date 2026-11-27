namespace Doka.Tabular.EntityFrameworkCore;

internal sealed partial class ImportJournalRun
{
    /// <summary>
    /// Writes the start of the attempt before the input is read, so that an abort while hashing still ends the log
    /// and runs the completion; hash and size follow once the file was read completely.
    /// </summary>
    public async Task<SinkPreparation> StartAsync(
        CancellationToken cancellationToken
    )
    {
        var preparation = new SinkPreparation();
        try
        {
            var context = await _binding
                .Journal
                .CreateContextAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (context.ConfigureAwait(false))
            {
                await StartLogAsync(context, _time.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);

                // WHY: From here on the log exists; a report that ends before the gate, also because the release of
                // this context fails, still refers to it.
                preparation.Journal = new ImportJournalResult(_log!.Id, ImportJournalDecision.Processed, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            preparation.Canceled = true;
        }

        // WHY: Any other failure of the own journal context, also of its release, is a technical error of the journal,
        // as at the end of the log.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            preparation.Fatal = StartFailed();
        }

        return preparation;
    }

    /// <summary>Records hash and size of the read file and decides the duplicate gate through the claim.</summary>
    public async Task<SinkPreparation> GateAsync(
        Sha256Hash hash,
        long byteSize,
        CancellationToken cancellationToken
    )
    {
        var preparation = new SinkPreparation();
        var now = _time.GetUtcNow();
        try
        {
            var context = await _binding
                .Journal
                .CreateContextAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (context.ConfigureAwait(false))
            {
                var started = _log!.Id;
                _log = await context
                    .Set<TabularImportLog>(JournalModel.ImportLog(Name))
                    .SingleAsync(entry => entry.Id == started, cancellationToken)
                    .ConfigureAwait(false);

                _log.Hashed(hash, byteSize);
                JournalStore.Touch(context, _log);
                await context
                    .SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);

                await TakeClaimAsync(context, hash, now, preparation, cancellationToken)
                    .ConfigureAwait(false);

                await RecordDecisionAsync(context, preparation, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            preparation.Canceled = true;
        }

        // WHY: Any other failure of the own journal context, also of its release, is a technical error of the journal,
        // as at the end of the log.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            preparation.Fatal = StartFailed();
        }

        return preparation;
    }

    private static TabularError StartFailed() => TabularError.Create(
        TabularErrorCodes.Persistence.WriteFailed,
        TabularErrorKind.Technical,
        "The start of the import log could not be written.");

    private async Task StartLogAsync(
        DbContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var log = new TabularImportLog(
            Guid.NewGuid(),
            _binding.Options.Scope,
            _binding.Options.FileName,
            _binding.Options.FileId,
            _operationId,
            now);

        context
            .Set<TabularImportLog>(JournalModel.ImportLog(Name))
            .Add(log);

        JournalStore.Touch(context, log);

        // WHY: The owner is known locally before the log becomes visible, so a recovery in this process sees the
        // attempt as running while it reads and hashes the input, before it takes the claim.
        s_running[log.Id] = 0;
        try
        {
            await context
                .SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            s_running.TryRemove(log.Id, out _);

            throw;
        }

        // WHY: Only a stored start makes the attempt started; a failed start is ended and completed by no one.
        _log = log;

        _claimScope = JournalStore.ClaimScope(context, Name, _binding.Options.Scope);
    }

    /// <summary>Inserts the claim; when another attempt holds it, its state decides the gate.</summary>
    private async Task TakeClaimAsync(
        DbContext context,
        Sha256Hash hash,
        DateTimeOffset now,
        SinkPreparation preparation,
        CancellationToken cancellationToken
    )
    {
        var claim = new TabularImportClaim(_claimScope!, hash, _log!.Id, now);
        JournalStore.AddClaim(context, Name, claim);

        // WHY: The owner is known locally before the claim becomes visible, so a recovery in this process never
        // takes a just stored claim for an orphan.
        s_running[_log.Id] = 0;
        try
        {
            await context
                .SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            _holdsClaim = true;
        }
        catch (DbUpdateException)
        {
            // WHY: The database guarantees one claim per scope and hash; a failed insert means another
            // attempt holds it, which the following read decides without a prior existence check.
            context.Entry(claim)
                .State = EntityState.Detached;

            if (!await DecideExistingClaimAsync(context, hash, now, preparation, cancellationToken)
                .ConfigureAwait(false))
            {
                throw;
            }
        }
        finally
        {
            ReleaseLocalOwner();
        }
    }

    /// <summary>Decides by the claim another attempt holds; <see langword="false" /> when no claim exists.</summary>
    private async Task<bool> DecideExistingClaimAsync(
        DbContext context,
        Sha256Hash hash,
        DateTimeOffset now,
        SinkPreparation preparation,
        CancellationToken cancellationToken
    )
    {
        var existing = await JournalStore
            .Claim(context, Name, _claimScope!, hash)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        switch (existing?.State)
        {
            case null:
                return false;
            case ImportClaimState.Released:
                await RetakeReleasedAsync(context, existing, now, preparation, cancellationToken)
                    .ConfigureAwait(false);

                break;
            case ImportClaimState.Imported:
                Decide(
                    preparation,
                    _binding.Options.DuplicateBehavior == ImportDuplicateBehavior.Skip
                        ? ImportJournalDecision.Skipped
                        : ImportJournalDecision.RejectedDuplicate,
                    existing.CurrentImportId);

                break;

            // WHY: The declared states are named so that a new state is not hidden by the fallback.
            // ReSharper disable once RedundantCaseLabel
            case ImportClaimState.Running:
            // ReSharper disable once RedundantCaseLabel
            case ImportClaimState.Unresolved:
            default:
                // WHY: A stored state outside the enum is no proof of an outcome; it blocks like a running import.
                Decide(preparation, ImportJournalDecision.Running, existing.CurrentImportId);
                break;
        }

        return true;
    }

    /// <summary>Takes over a released claim; a concurrent takeover leaves the import running elsewhere.</summary>
    private async Task RetakeReleasedAsync(
        DbContext context,
        TabularImportClaim existing,
        DateTimeOffset now,
        SinkPreparation preparation,
        CancellationToken cancellationToken
    )
    {
        existing.Take(_log!.Id, now);
        JournalStore.Touch(context, existing);
        s_running[_log.Id] = 0;
        try
        {
            await context
                .SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            _holdsClaim = true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // WHY: The lost takeover must not be saved again with the decision of this attempt.
            context.Entry(existing)
                .State = EntityState.Detached;

            Decide(preparation, ImportJournalDecision.Running, null);
        }
        finally
        {
            ReleaseLocalOwner();
        }
    }

    /// <summary>Removes the local owner entry of an attempt that did not get the claim.</summary>
    private void ReleaseLocalOwner()
    {
        if (!_holdsClaim)
        {
            s_running.TryRemove(_log!.Id, out _);
        }
    }

    /// <summary>Proceeds with a held claim; otherwise the decision is written to the log of the attempt.</summary>
    private async Task RecordDecisionAsync(
        DbContext context,
        SinkPreparation preparation,
        CancellationToken cancellationToken
    )
    {
        if (_holdsClaim)
        {
            s_running[_log!.Id] = 0;
            Decide(preparation, ImportJournalDecision.Processed, null);

            return;
        }

        _log!.Decide(_decision!.Value);
        JournalStore.Touch(context, _log);
        await context
            .SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void Decide(
        SinkPreparation preparation,
        ImportJournalDecision decision,
        Guid? existing
    )
    {
        _decision = decision;
        preparation.Gate = decision switch
        {
            ImportJournalDecision.Processed => ImportGate.Proceed,
            ImportJournalDecision.Skipped => ImportGate.Skip,
            ImportJournalDecision.RejectedDuplicate => ImportGate.RejectDuplicate,
            ImportJournalDecision.Running => ImportGate.Running,
            _ => throw new UnreachableException($"The journal decision {decision} is not handled."),
        };

        preparation.Journal = new ImportJournalResult(_log!.Id, decision, existing);
    }
}
