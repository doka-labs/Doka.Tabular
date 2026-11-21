using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>
/// Context and transaction of one database description within one call. All sinks of the description share it; the
/// last released sink rolls back what is still open and disposes an own context.
/// </summary>
internal sealed partial class WriteSession
{
    private const string RowSavepoint = "tabular_row";
    private const string ImportSavepoint = "tabular_import";
    private readonly MatchQueries _matches = new();
    private readonly List<TabularError> _sessionErrors = [];

    private readonly IContextSource _source;
    private bool _importSavepointSet;
    private bool _owned;
    private bool _prepared;
    private bool _reserved;
    private TabularError? _sessionFatal;
    private int _tablesInImportSavepoint;
    private long _tentative;

    private long _tentativeImported;
    private IDbContextTransaction? _transaction;
    private int _users;

    private WriteSession(
        IContextSource source
    )
    {
        _source = source;
    }

    public DbContext? Context { get; private set; }

    private TabularWriteOptions Options { get; set; } = new();

    private ImportStrategy Strategy { get; set; }

    private bool IsWorkbook { get; set; }

    private bool CommitUnknown { get; set; }

    /// <summary>Gets or sets whether a rollback of the session was not confirmed; its open rows may remain.</summary>
    private bool RollbackUnconfirmed { get; set; }

    public IImportEvidence? Evidence { get; set; }

    private bool Joined => Options.JoinExistingTransaction;

    private bool SpansTables => Strategy == ImportStrategy.Strict && IsWorkbook && !Options.TransactionPerSheet;

    public static WriteSession Attach(
        ImportCall call,
        object database
    )
    {
        if (!call.Shared.TryGetValue(database, out var existing))
        {
            existing = new WriteSession((IContextSource)database);
            call.Shared[database] = existing;
        }

        var session = (WriteSession)existing;
        session._users++;

        return session;
    }

    /// <summary>Checks context, transaction, savepoints, retry strategy, and isolation once per call; returns call errors.</summary>
    public async Task<(IReadOnlyList<TabularError> Errors, TabularError? Fatal)> PrepareAsync(
        TabularWriteOptions options,
        ImportSinkStart start,
        CancellationToken cancellationToken
    )
    {
        if (_prepared)
        {
            // WHY: An explicit group size is forbidden with a joined transaction also when it equals the default.
            if (options is { JoinExistingTransaction: true, IsCommitGroupSizeSet: true })
            {
                return ([
                    TabularError.Configuration(
                        "A commit group size cannot be set when joining an existing transaction."),
                ], null);
            }

            return (SameTransactionSettings(options)
                ? []
                :
                [
                    TabularError.Configuration(
                        "Targets of one database description in one call must use the same transaction settings."),
                ], null);
        }

        _prepared = true;
        Options = options;
        Strategy = start.Strategy;
        IsWorkbook = start.IsWorkbook;
        if (!_source.TryReserve())
        {
            _sessionErrors.Add(TabularError.Configuration("The passed context is used by another import call."));

            return (_sessionErrors, null);
        }

        _reserved = true;
        try
        {
            (Context, _owned) = await _source
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(false);

            await CheckAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        // WHY: The requested cancellation is caught above. EF passes exceptions of interceptors, providers, and
        // factories through unchanged, so any other exception is a failure of the database operation.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _sessionFatal = TabularError.Create(TabularErrorCodes.Persistence.OperationFailed,
                TabularErrorKind.Technical,
                "Opening the context or checking the transaction failed.");
        }

        return (_sessionErrors, _sessionFatal);
    }

    /// <summary>
    /// Rolls back an open transaction that spans several tables after a later table failed. An unconfirmed rollback
    /// is reported as such, because rows pending in an application transaction are not counted as tentative.
    /// </summary>
    public async Task<(long Uncertain, bool RollbackUnconfirmed, TabularError? Fatal)> AbandonAsync()
    {
        // WHY: Rows of earlier tables are counted tentatively in the open transaction; an unknown commit or an
        // unconfirmed rollback leaves them uncertain.
        if (CommitUnknown)
        {
            return (_tentative, false, null);
        }

        if (RollbackUnconfirmed)
        {
            return (_tentative, true, null);
        }

        var outcome = new WriteResult();

        return await DiscardOpenAsync(outcome, 0)
            .ConfigureAwait(false)
            ? (0, false, outcome.Fatal)
            : (_tentative, true, outcome.Fatal);
    }

    /// <summary>Releases one sink; the last one rolls back what is still open and disposes an own context.</summary>
    public async ValueTask ReleaseAsync()
    {
        if (--_users > 0)
        {
            return;
        }

        // WHY: Each step runs even when an earlier one throws; the reservation of a borrowed context is always
        // returned, and the first exception reaches the run, which reports it.
        try
        {
            await ReleaseTransactionAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (_owned && Context is not null)
                {
                    await Context
                        .DisposeAsync()
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                if (_reserved)
                {
                    _source.Release();
                }
            }
        }
    }

    private async Task ReleaseTransactionAsync()
    {
        if (_transaction is null
            || Joined)
        {
            return;
        }

        try
        {
            await _transaction
                .RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // WHY: The owner disposes the context next; a rollback failure leaves nothing open beyond that.
        }
        finally
        {
            await _transaction
                .DisposeAsync()
                .ConfigureAwait(false);

            _transaction = null;
        }
    }

    /// <summary>Whether a further target of the same database uses the transaction settings of the session.</summary>
    private bool SameTransactionSettings(
        TabularWriteOptions options
    ) => options.JoinExistingTransaction == Options.JoinExistingTransaction
        && options.IsolationLevel == Options.IsolationLevel
        && options.TransactionPerSheet == Options.TransactionPerSheet
        && options.CommitGroupSize == Options.CommitGroupSize;

    private async Task CheckAsync(
        CancellationToken cancellationToken
    )
    {
        var database = Context!.Database;
        if (_source.IsBorrowed
            && Context.ChangeTracker.HasChanges())
        {
            _sessionErrors.Add(
                TabularError.Configuration(
                    "The passed context has pending changes; save or discard them before the import."));
        }

        if (database.CreateExecutionStrategy()
            .RetriesOnFailure)
        {
            _sessionErrors.Add(TabularError.Configuration(
                "The execution strategy of the context retries blocks; provide a context without retrying strategy for the import."));
        }

        var current = database.CurrentTransaction;
        if (Joined)
        {
            CheckJoined(current);
            _transaction = current;

            return;
        }

        if (current is not null)
        {
            _sessionErrors.Add(TabularError.Configuration(
                "The passed context has an active transaction; set JoinExistingTransaction to join it."));

            return;
        }

        if (Options.CommitGroupSize < 1)
        {
            _sessionErrors.Add(TabularError.Configuration("CommitGroupSize must be at least 1."));
        }

        var needsSavepoints = Strategy == ImportStrategy.Strict || Options.CommitGroupSize > 1;
        if (_sessionErrors.Count == 0
            && (needsSavepoints || Options.IsolationLevel is not null))
        {
            await ProbeAsync(database, needsSavepoints, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>A joined transaction must exist and support savepoints; group size and isolation are the caller's.</summary>
    private void CheckJoined(
        IDbContextTransaction? current
    )
    {
        if (current is null)
        {
            _sessionErrors.Add(
                TabularError.Configuration(
                    "JoinExistingTransaction needs a passed context with an active transaction."));
        }
        else if (!current.SupportsSavepoints)
        {
            _sessionErrors.Add(TabularError.Configuration("The joined transaction does not support savepoints."));
        }

        if (Options.IsCommitGroupSizeSet)
        {
            _sessionErrors.Add(
                TabularError.Configuration("A commit group size cannot be set when joining an existing transaction."));
        }

        if (Options.IsolationLevel is not null)
        {
            _sessionErrors.Add(
                TabularError.Configuration("The isolation level of a joined transaction is set by the application."));
        }
    }

    /// <summary>Opens and rolls back a probe transaction to check the isolation level and savepoint support.</summary>
    private async Task ProbeAsync(
        DatabaseFacade database,
        bool needsSavepoints,
        CancellationToken cancellationToken
    )
    {
        IDbContextTransaction probe;
        try
        {
            probe = Options.IsolationLevel is { } level
                ? await database
                    .BeginTransactionAsync(level, cancellationToken)
                    .ConfigureAwait(false)
                : await database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (Exception exception) when (Options.IsolationLevel is not null
            && exception is ArgumentException or NotSupportedException or DbException or InvalidOperationException)
        {
            _sessionErrors.Add(
                TabularError.Configuration(
                    $"The isolation level {Options.IsolationLevel} is not supported by the provider."));

            return;
        }

        await using (probe.ConfigureAwait(false))
        {
            if (needsSavepoints && !probe.SupportsSavepoints)
            {
                _sessionErrors.Add(TabularError.Configuration(
                    "The provider does not support savepoints; use a partial import with CommitGroupSize 1."));
            }

            await probe
                .RollbackAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            await _transaction
                .DisposeAsync()
                .ConfigureAwait(false);

            _transaction = null;
        }
    }

    private sealed record RowOutcome(
        bool Skipped,
        TabularError? Error,
        TabularError? Fatal,
        bool Canceled
    )
    {
        /// <summary>Gets whether the reset of the row was not confirmed, so that written parts of it may remain.</summary>
        public bool ResetUnconfirmed { get; init; }
    }
}
