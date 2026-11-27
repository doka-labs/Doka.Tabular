using System.Collections.Concurrent;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>
/// Import log of one call. The start of the attempt and the end of failed attempts are written through own journal
/// contexts and survive a rollback of the data; the commit evidence is written in the data transaction.
/// </summary>
internal sealed partial class ImportJournalRun : IImportEvidence
{
    private static readonly ConcurrentDictionary<Guid, byte> s_running = new();

    private readonly ImportLogBinding _binding;
    private readonly WriteSession _session;
    private string? _claimScope;
    private ImportJournalDecision? _decision;
    private bool _evidenceCommitted;
    private bool _holdsClaim;
    private TabularImportLog? _log;
    private Guid _operationId;
    private TimeProvider _time = TimeProvider.System;
    private bool _uncertain;

    public ImportJournalRun(
        ImportLogBinding binding,
        WriteSession session
    )
    {
        _binding = binding;
        _session = session;
    }

    private JournalName Name => _binding.Journal.Name;

    /// <summary>Marks the claim as imported and records the progress inside the data transaction before a commit.</summary>
    public async Task RecordAsync(
        DbContext context,
        long rowsImported,
        CancellationToken cancellationToken
    )
    {
        if (_log is null
            || !_holdsClaim)
        {
            return;
        }

        var id = _log.Id;
        var scope = _claimScope!;
        var hash = _log.Hash!;
        var claims = await JournalStore
            .Claim(context, Name, scope, hash)
            .Where(claim => claim.CurrentImportId == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(claim => claim.State, ImportClaimState.Imported)
                    .SetProperty(claim => claim.UpdatedAt, _time.GetUtcNow())
                    .SetProperty(claim => EF.Property<Guid>(claim, "Version"), Guid.NewGuid()),
                cancellationToken)
            .ConfigureAwait(false);

        if (claims != 1)
        {
            throw new ClaimLostException("The import claim was taken over by another attempt.");
        }

        // WHY: Another call can end the log of a run it takes for crashed, by resolution and a later cleanup; evidence
        // of further groups then has no log, so the group is not committed.
        var logs = await context
            .Set<TabularImportLog>(JournalModel.ImportLog(Name))
            .Where(log => log.Id == id && log.CompletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(log => log.RowsImported, rowsImported)
                    .SetProperty(log => EF.Property<Guid>(log, "Version"), Guid.NewGuid()),
                cancellationToken)
            .ConfigureAwait(false);

        if (logs != 1)
        {
            throw new ClaimLostException("The import log was ended or deleted by another call while the import ran.");
        }
    }

    public static bool IsRunning(
        Guid importId
    ) => s_running.ContainsKey(importId);

    /// <summary>Checks options and registration against the model of the data context before the first read.</summary>
    public IEnumerable<TabularError> Check(
        DbContext context,
        ImportSinkStart start
    )
    {
        _time = start.Time;
        _operationId = start.OperationId;

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // WHY: A required member can still be set to null by callers without nullable analysis.
        if (_binding.Options.Scope is null)
        {
            yield return TabularError.Configuration("The import log needs a scope.");
        }

        if (_binding.Options.FileName is { Length: > 256 })
        {
            yield return TabularError.Configuration("The file name of the import log has at most 256 characters.");
        }

        if (_binding.Options.CompletionTimeout <= TimeSpan.Zero
            || _binding.Options.CompletionTimeout > CompletionRunner.MaxTimeout)
        {
            yield return TabularError.Configuration("CompletionTimeout must be positive and at most 49 days.");
        }

        if (JournalModel.Find(context, JournalModel.ImportLog(Name)) is null
            || JournalModel.Find(context, JournalModel.ImportClaim(Name)) is null)
        {
            yield return TabularError.Configuration(
                $"The import journal {Name} is not registered in the model of the target context.");
        }

        _session.Evidence = this;
    }

    /// <summary>Takes the outcome of a write: confirmed commits are evidence, an unknown commit leaves the claim unresolved.</summary>
    public void Observe(
        WriteResult result
    )
    {
        _evidenceCommitted |= result.RowsImported > 0;
        _uncertain |= result.Uncertain;
    }

    /// <summary>Completes log and claim and runs the completion function once per started journal operation.</summary>
    public async Task<IReadOnlyList<TabularError>> FinishAsync(
        ImportReportData report
    )
    {
        if (_log is null)
        {
            return [];
        }

        var errors = new List<TabularError>();
        var saved = false;
        try
        {
            await CompleteLogAsync(_log, report, () => saved = true)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // WHY: Any failure of the own journal context, also of its release, is a technical error of the
            // journal; the completion function still runs once.
            errors.Add(
                saved
                    ? TabularError.Create(
                        TabularErrorCodes.Persistence.OperationFailed,
                        TabularErrorKind.Technical,
                        "The context of the import log could not be released after its end was written.")
                    : TabularError.Create(
                        TabularErrorCodes.Persistence.WriteFailed,
                        TabularErrorKind.Technical,
                        "The end of the import log could not be written."));
        }
        finally
        {
            s_running.TryRemove(_log.Id, out _);
        }

        if (await RunCompletionAsync(_log, report)
                .ConfigureAwait(false) is { } error)
        {
            errors.Add(error);
        }

        return errors;
    }

    /// <summary>Writes the end of the log and, with a held claim, the claim state the import leaves behind.</summary>
    private async Task CompleteLogAsync(
        TabularImportLog started,
        ImportReportData report,
        Action saved
    )
    {
        var now = _time.GetUtcNow();
        var context = await _binding
            .Journal
            .CreateContextAsync(CancellationToken.None)
            .ConfigureAwait(false);

        await using (context.ConfigureAwait(false))
        {
            var log = await context
                .Set<TabularImportLog>(JournalModel.ImportLog(Name))
                .SingleAsync(entry => entry.Id == started.Id)
                .ConfigureAwait(false);

            var uncertain = _uncertain
                || report.RowsUncertain > 0
                || report.FatalError?.Code == TabularErrorCodes.Persistence.CommitUnknown;

            log.Complete(report, now, safelyEnded: !uncertain);
            JournalStore.Touch(context, log);
            if (_holdsClaim)
            {
                var claim = await JournalStore
                    .Claim(context, Name, _claimScope!, started.Hash!)
                    .SingleAsync()
                    .ConfigureAwait(false);

                if (claim is { State: ImportClaimState.Running }
                    && claim.CurrentImportId == started.Id)
                {
                    // WHY: Targets of other databases report no evidence here; their imported rows still mean that
                    // the file changed data, so the claim is not released.
                    claim.Set(
                        ClaimStateAfter(uncertain, _evidenceCommitted || report.RowsImported > 0, report.Status),
                        now);

                    JournalStore.Touch(context, claim);
                }
            }

            await context
                .SaveChangesAsync(CancellationToken.None)
                .ConfigureAwait(false);

            saved();
        }
    }

    private async Task<TabularError?> RunCompletionAsync(
        TabularImportLog started,
        ImportReportData report
    )
    {
        if (_binding.Options.OnCompletedAsync is not { } completed)
        {
            return null;
        }

        return await CompletionRunner
            .RunAsync(
                token => completed(
                    new TabularImportCompleted(
                        _operationId,
                        started.Id,
                        _binding.Options.Scope,
                        _decision ?? ImportJournalDecision.Processed,
                        report),
                    token),
                _binding.Options.CompletionTimeout,
                _time)
            .ConfigureAwait(false);
    }

    /// <summary>The claim state after the import; an uncertain commit can neither prove nor rule out the import.</summary>
    private static ImportClaimState ClaimStateAfter(
        bool uncertain,
        bool evidenceCommitted,
        ReportStatus status
    )
    {
        if (uncertain)
        {
            return ImportClaimState.Unresolved;
        }

        return evidenceCommitted || status == ReportStatus.Succeeded
            ? ImportClaimState.Imported
            : ImportClaimState.Released;
    }
}
