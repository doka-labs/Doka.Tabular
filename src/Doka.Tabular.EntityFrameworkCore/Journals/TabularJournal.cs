using Microsoft.Extensions.Logging;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>
/// Immutable description of one journal. It opens no context on creation; every step uses a fresh context of the
/// factory and disposes it, because the history of failed attempts must survive a rollback of the data.
/// </summary>
[PublicAPI]
public sealed partial class TabularJournal
{
    private readonly Func<CancellationToken, Task<DbContext>> _factory;

    private TabularJournal(
        Func<CancellationToken, Task<DbContext>> factory,
        JournalName name,
        TabularJournalOptions options
    )
    {
        _factory = factory;
        Name = name;
        Options = options;
    }

    internal JournalName Name { get; }

    private TabularJournalOptions Options { get; }

    /// <summary>Describes a journal of a context factory; without a name the standard journal.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="factory">The context factory.</param>
    /// <param name="journalName">The name of a named journal; <see langword="null" /> for the standard journal.</param>
    /// <returns>The description.</returns>
    public static TabularJournal ForFactory<TContext>(
        IDbContextFactory<TContext> factory,
        JournalName? journalName = null
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);

        return new TabularJournal(async token => await factory
                .CreateDbContextAsync(token)
                .ConfigureAwait(false),
            journalName ?? JournalName.Default,
            new TabularJournalOptions());
    }

    /// <summary>Describes a journal of a context-creating function; without a name the standard journal.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="factory">Creates a fresh context per step; Tabular disposes it.</param>
    /// <param name="journalName">The name of a named journal; <see langword="null" /> for the standard journal.</param>
    /// <returns>The description.</returns>
    public static TabularJournal ForFactory<TContext>(
        Func<CancellationToken, Task<TContext>> factory,
        JournalName? journalName = null
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);

        return new TabularJournal(async token => await factory(token)
                .ConfigureAwait(false),
            journalName ?? JournalName.Default,
            new TabularJournalOptions());
    }

    /// <summary>Returns a description with other settings for the standalone journal calls.</summary>
    /// <param name="options">The settings.</param>
    /// <returns>The new description.</returns>
    public TabularJournal WithOptions(
        TabularJournalOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Diagnostics);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);

        return new TabularJournal(_factory, Name, options);
    }

    /// <summary>Queries the import logs of this journal; runs no query by itself.</summary>
    /// <param name="context">A context whose model contains the journal.</param>
    /// <returns>The query.</returns>
    public IQueryable<TabularImportLog> QueryImports(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return context
            .Set<TabularImportLog>(JournalModel.ImportLog(Name))
            .AsNoTracking();
    }

    /// <summary>Queries the import claims of this journal; runs no query by itself.</summary>
    /// <param name="context">A context whose model contains the journal.</param>
    /// <returns>The query.</returns>
    public IQueryable<TabularImportClaim> QueryImportClaims(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return context
            .Set<TabularImportClaim>(JournalModel.ImportClaim(Name))
            .AsNoTracking();
    }

    /// <summary>Queries the export logs of this journal; runs no query by itself.</summary>
    /// <param name="context">A context whose model contains the journal.</param>
    /// <returns>The query.</returns>
    public IQueryable<TabularExportLog> QueryExports(
        DbContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        return context
            .Set<TabularExportLog>(JournalModel.ExportLog(Name))
            .AsNoTracking();
    }

    internal Task<DbContext> CreateContextAsync(
        CancellationToken cancellationToken
    ) => _factory(cancellationToken);

    private static TabularJournalResult Conflict(
        string message
    ) => new(JournalCallStatus.Conflict,
        TabularError.Create(TabularErrorCodes.Persistence.Conflict, TabularErrorKind.Technical, message));

    private async Task<TResult> RunAsync<TResult>(
        string direction,
        Func<DbContext, Task<TResult>> action,
        Func<TabularJournalResult, TResult> failed,
        CancellationToken cancellationToken
    )
    {
        if (Options.Diagnostics.LoggerFactory is null)
        {
            return failed(new TabularJournalResult(JournalCallStatus.Failed,
                TabularError.Configuration(
                    "The diagnostics settings need a logger factory; use NullLoggerFactory.Instance to disable logging.")));
        }

        var operationId = Guid.NewGuid();
        try
        {
            var context = await CreateContextAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (context.ConfigureAwait(false))
            {
                if (JournalModel.Find(context,
                    direction == "import"
                        ? JournalModel.ImportLog(Name)
                        : JournalModel.ExportLog(Name)) is null)
                {
                    return failed(new TabularJournalResult(JournalCallStatus.Failed,
                        TabularError.Configuration($"The {direction} journal {Name} is not registered in the model.")));
                }

                return await action(context)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return failed(new TabularJournalResult(JournalCallStatus.Canceled, null));
        }

        // WHY: The requested cancellation is caught above. The context factory, its release, interceptors, and the
        // provider may throw any exception; each is a failure of the journal call, reported as its result.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log(logger => TabularLog.JournalUpdateFailed(logger, operationId, direction, "journal"));

            return failed(new TabularJournalResult(JournalCallStatus.Failed,
                TabularError.Create(TabularErrorCodes.Persistence.OperationFailed,
                    TabularErrorKind.Technical,
                    "Reading or writing the journal failed.")));
        }
    }

    private void Log(
        Action<ILogger> write
    )
    {
        try
        {
            write(Options.Diagnostics.LoggerFactory!.CreateLogger("Doka.Tabular.Journal"));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // WHY: A failing logger never changes the outcome of a journal call.
        }
    }
}
