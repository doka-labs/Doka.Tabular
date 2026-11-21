using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>The transaction failure a test scenario injects.</summary>
public enum Failure
{
    None,
    CommitRejected,
    CommitThrowsAfterwards,
    RollbackFails,
}

/// <summary>Fails commits or rollbacks once armed, before or after the database acted.</summary>
public sealed class TransactionFaults : DbTransactionInterceptor
{
    private readonly Failure _failure;

    public TransactionFaults(
        Failure failure
    )
    {
        _failure = failure;
    }

    public bool Armed { get; set; }

    /// <summary>Gets how often the armed failure was injected, so that a scenario proves its failure took effect.</summary>
    public int Injected { get; private set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => Fires(Failure.CommitRejected)
        ? throw new InvalidOperationException("The commit was rejected.")
        : ValueTask.FromResult(result);

    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    ) => Fires(Failure.CommitThrowsAfterwards)
        ? throw new InvalidOperationException("The connection broke after the commit.")
        : Task.CompletedTask;

    public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => Fires(Failure.RollbackFails)
        ? throw new InvalidOperationException("The rollback failed.")
        : ValueTask.FromResult(result);

    public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    ) => Fires(Failure.RollbackFails)
        ? throw new InvalidOperationException("The rollback to the savepoint failed.")
        : ValueTask.FromResult(result);

    private bool Fires(
        Failure failure
    )
    {
        if (!Armed
            || _failure != failure)
        {
            return false;
        }

        Injected++;

        return true;
    }
}
