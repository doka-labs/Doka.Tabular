namespace Doka.Tabular.EntityFrameworkCore;

internal sealed partial class WriteSession
{
    private async Task<RowOutcome> RejectAsync(
        RowTracking tracking,
        bool savepoint,
        TabularError? error,
        TabularError? fatal
    )
    {
        if (!await TryRollBackRowAsync(savepoint)
            .ConfigureAwait(false))
        {
            return new RowOutcome(false,
                null,
                TabularError.Create(TabularErrorCodes.Persistence.RollbackUnknown,
                    TabularErrorKind.Technical,
                    "Resetting the row was not confirmed."),
                false) { ResetUnconfirmed = true };
        }

        try
        {
            tracking.Restore();
        }

        // WHY: Restoring runs EF state changes that call into the model and the entities; any failure leaves the
        // context unusable for the next row.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new RowOutcome(false,
                null,
                TabularError.Create(TabularErrorCodes.Persistence.ContextRestoreFailed,
                    TabularErrorKind.Technical,
                    "The EF state of the rejected row could not be reset."),
                false);
        }

        return new RowOutcome(false, error, fatal, false);
    }

    /// <summary>Rolls back to the row savepoint, or the whole own transaction without savepoints.</summary>
    private async Task<bool> TryRollBackRowAsync(
        bool savepoint
    )
    {
        try
        {
            if (savepoint)
            {
                await _transaction!
                    .RollbackToSavepointAsync(RowSavepoint, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else if (_transaction is not null)
            {
                await _transaction
                    .RollbackAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                await _transaction
                    .DisposeAsync()
                    .ConfigureAwait(false);

                _transaction = null;
            }

            return true;
        }

        // WHY: EF passes exceptions of interceptors and providers through unchanged; any of them leaves the reset
        // unconfirmed.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private async Task ReleaseSavepointAsync()
    {
        try
        {
            await _transaction!
                .ReleaseSavepointAsync(RowSavepoint, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // WHY: Releasing is optional; a provider without it keeps the savepoint until the transaction ends.
        }
    }
}
