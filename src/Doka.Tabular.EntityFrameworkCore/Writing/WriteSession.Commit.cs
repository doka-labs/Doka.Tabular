namespace Doka.Tabular.EntityFrameworkCore;

internal sealed partial class WriteSession
{
    /// <summary>Discards, keeps pending, or commits the open group at the end of a table.</summary>
    private async Task FinishTableAsync(
        ImportSinkWrite write,
        bool strict,
        List<(int Index, bool Skipped)> group,
        WriteResult result
    )
    {
        if (result.Fatal is not null
            || result.Canceled
            || (strict && result.Errors.Count > 0)
            || (!strict && write.ReadAborted))
        {
            // WHY: After a parser abort the open group is incomplete; confirmed groups stay, the open one is discarded.
            await DiscardOpenAsync(result, group.Count)
                .ConfigureAwait(false);

            return;
        }

        if (Joined)
        {
            result.RowsPendingCommit += group.Count(entry => !entry.Skipped);
            result.RowsSkipped += group.Count(entry => entry.Skipped);

            return;
        }

        if (strict
            && SpansTables
            && !write.IsLastTable)
        {
            _tentative += group.Count;
            _tentativeImported += group.Count(entry => !entry.Skipped);
            Count(group, result);

            return;
        }

        if (group.Count > 0
            || _transaction is not null)
        {
            await CommitGroupAsync(group, result)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Commits the open group with its journal evidence; returns false when the run must end.</summary>
    private async Task<bool> CommitGroupAsync(
        List<(int Index, bool Skipped)> group,
        WriteResult result
    )
    {
        if (_transaction is null)
        {
            group.Clear();

            return true;
        }

        if (await RecordEvidenceAsync(group, result)
                .ConfigureAwait(false) is { } evidenceError)
        {
            result.Fatal = evidenceError;
            await DiscardOpenAsync(result, group.Count)
                .ConfigureAwait(false);

            return false;
        }

        try
        {
            await _transaction
                .CommitAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await FailCommitAsync(group, result)
                .ConfigureAwait(false);

            return false;
        }

        // WHY: The commit succeeded, so its rows count before the transaction is released; a failing release is the
        // fatal error of the run and must not lose them.
        _tentative = 0;
        _tentativeImported = 0;
        Count(group, result);
        result.CommitConfirmed = true;
        try
        {
            await DisposeTransactionAsync()
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _transaction = null;
            result.Fatal ??= TabularError.Create(
                TabularErrorCodes.Persistence.OperationFailed,
                TabularErrorKind.Technical,
                "The transaction could not be released after its commit; the committed rows stay.");

            return false;
        }
    }

    /// <summary>Writes the import log evidence inside the group transaction; returns the error when that fails.</summary>
    private async Task<TabularError?> RecordEvidenceAsync(
        List<(int Index, bool Skipped)> group,
        WriteResult result
    )
    {
        try
        {
            // WHY: A group without an imported row changes no data; it must not mark the file as imported.
            if (Evidence is not null
                && _tentativeImported + group.Count(entry => !entry.Skipped) > 0)
            {
                await Evidence
                    .RecordAsync(
                        Context!,
                        result.RowsImported + _tentativeImported + group.Count(entry => !entry.Skipped),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return null;
        }
        catch (ClaimLostException lost)
        {
            return TabularError.Create(
                TabularErrorCodes.Persistence.Conflict,
                TabularErrorKind.Technical,
                lost.Message);
        }

        // WHY: EF passes exceptions of interceptors and providers through unchanged; any of them fails the evidence.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return TabularError.Create(
                TabularErrorCodes.Persistence.WriteFailed,
                TabularErrorKind.Technical,
                "The import log evidence could not be written.");
        }
    }

    private async Task FailCommitAsync(
        List<(int Index, bool Skipped)> group,
        WriteResult result
    )
    {
        // WHY: No provider-independent signal proves that a failed commit left nothing behind: an interceptor that
        // fails after the data commit reaches the same error path as a rejection, and Npgsql keeps the connection of a
        // committed transaction. Every commit failure is therefore uncertain; the journal evidence or the application
        // clarifies it. The state is recorded before the transaction is released, so a second failure cannot lose it.
        CommitUnknown = true;
        result.Uncertain = true;
        result.RowsUncertain += group.Count;
        result.Fatal = TabularError.Create(
            TabularErrorCodes.Persistence.CommitUnknown,
            TabularErrorKind.Technical,
            "The outcome of a commit is unknown; clarify the state before repeating the import.");

        group.Clear();
        try
        {
            await DisposeTransactionAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // WHY: The commit outcome is already unknown and reported; a failing release adds nothing the application
            // could act on, and the session owner disposes the context next.
            _transaction = null;
        }
    }

    private static void Count(
        List<(int Index, bool Skipped)> group,
        WriteResult result
    )
    {
        foreach (var (index, skipped) in group)
        {
            if (skipped)
            {
                result.RowsSkipped++;
            }
            else
            {
                result.RowsImported++;
            }

            result.Committed.Add(index);
        }

        group.Clear();
    }

    /// <summary>
    /// Rolls back what is open; <see langword="false" /> when the rollback is not confirmed, which leaves the written
    /// rows of the open group and the pending rows of the table uncertain.
    /// </summary>
    private async Task<bool> DiscardOpenAsync(
        WriteResult result,
        int openRows
    )
    {
        var confirmed = true;
        try
        {
            if (Joined)
            {
                if (_importSavepointSet)
                {
                    await _transaction!
                        .RollbackToSavepointAsync(ImportSavepoint, CancellationToken.None)
                        .ConfigureAwait(false);

                    // WHY: Without own savepoints per sheet, the import savepoint also holds the rows of earlier
                    // tables of this call; they are no longer pending either.
                    result.ScopeRolledBack = _tablesInImportSavepoint > 1;
                }

                result.RowsPendingCommit = 0;
            }
            else if (_transaction is not null)
            {
                await _transaction
                    .RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                await DisposeTransactionAsync()
                    .ConfigureAwait(false);
            }
        }

        // WHY: EF passes exceptions of interceptors and providers through unchanged; any of them leaves the rollback
        // unconfirmed.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result.Fatal ??= TabularError.Create(
                TabularErrorCodes.Persistence.RollbackUnknown,
                TabularErrorKind.Technical,
                "The rollback was not confirmed.");

            result.Uncertain = true;
            result.RowsUncertain += openRows + result.RowsPendingCommit;
            result.RowsPendingCommit = 0;
            RollbackUnconfirmed = true;
            confirmed = false;
        }

        // WHY: After an unconfirmed rollback the tentative rows of earlier tables stay, so that abandoning those
        // tables reports them as uncertain.
        if (confirmed)
        {
            _tentative = 0;
            _tentativeImported = 0;
        }

        return confirmed;
    }
}
