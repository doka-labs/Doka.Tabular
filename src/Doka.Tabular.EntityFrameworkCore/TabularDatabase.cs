namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Entry point for database descriptions from which import targets are prepared.</summary>
[PublicAPI]
public static class TabularDatabase
{
    /// <summary>Describes a database through a context of the application; the context is used, never disposed.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="context">The context; it is reserved for the import during a call.</param>
    /// <returns>The description.</returns>
    public static TabularDatabase<TContext> ForContext<TContext>(
        TContext context
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TabularDatabase<TContext>(context, null);
    }

    /// <summary>Describes a database through a factory; every call creates its own context and disposes it.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="factory">The context factory.</param>
    /// <returns>The description.</returns>
    public static TabularDatabase<TContext> ForFactory<TContext>(
        IDbContextFactory<TContext> factory
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);

        return new TabularDatabase<TContext>(null, factory);
    }
}

/// <summary>
/// Description of a context and its transaction, from which the targets are prepared. Targets of the same description
/// share context and transaction within one call. Preparing is synchronous, does not access the database, and starts no run.
/// </summary>
/// <typeparam name="TContext">The context type.</typeparam>
[PublicAPI]
public sealed class TabularDatabase<TContext> : IContextSource
    where TContext : DbContext
{
    private readonly TContext? _context;
    private readonly IDbContextFactory<TContext>? _factory;
    private int _reserved;

    internal TabularDatabase(
        TContext? context,
        IDbContextFactory<TContext>? factory
    )
    {
        _context = context;
        _factory = factory;
    }

    bool IContextSource.IsBorrowed => _context is not null;

    async ValueTask<(DbContext Context, bool Owned)> IContextSource.AcquireAsync(
        CancellationToken cancellationToken
    )
    {
        if (_context is not null)
        {
            return (_context, false);
        }

        return (await _factory!
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false), true);
    }

    bool IContextSource.TryReserve() => _context is null || Interlocked.CompareExchange(ref _reserved, 1, 0) == 0;

    void IContextSource.Release()
    {
        if (_context is not null)
        {
            Volatile.Write(ref _reserved, 0);
        }
    }

    /// <summary>Prepares a pure insert target; with <paramref name="match" /> existing records are skipped and counted.</summary>
    /// <typeparam name="TDto">The record type.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="set">Selects the set of the entity.</param>
    /// <param name="create">Creates the entity from the record.</param>
    /// <param name="match">The match that detects existing records; <see langword="null" /> inserts every row.</param>
    /// <param name="errors">Assigns exceptions and database errors to row errors.</param>
    /// <returns>The target.</returns>
    public TabularWriteTarget<TDto, TEntity> PrepareInsert<TDto, TEntity>(
        Func<TContext, DbSet<TEntity>> set,
        Func<TDto, TEntity> create,
        TabularMatch<TDto, TEntity>? match = null,
        Action<TabularWriteErrors>? errors = null
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(create);

        return Write(TargetKind.Insert, set, match, (dto, _) => create(dto), null, errors);
    }

    /// <summary>Prepares a pure insert target whose entity uses values of other targets of the row.</summary>
    /// <typeparam name="TDto">The record type.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="set">Selects the set of the entity.</param>
    /// <param name="create">Creates the entity from the record and the values of earlier targets.</param>
    /// <param name="match">The match that detects existing records; <see langword="null" /> inserts every row.</param>
    /// <param name="errors">Assigns exceptions and database errors to row errors.</param>
    /// <returns>The target.</returns>
    public TabularWriteTarget<TDto, TEntity> PrepareInsert<TDto, TEntity>(
        Func<TContext, DbSet<TEntity>> set,
        Func<TDto, TabularTargetValues, TEntity> create,
        TabularMatch<TDto, TEntity>? match = null,
        Action<TabularWriteErrors>? errors = null
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(create);

        return Write(TargetKind.Insert, set, match, create, null, errors);
    }

    /// <summary>Prepares an insert target for records that are entities themselves.</summary>
    /// <typeparam name="TEntity">The entity and record type.</typeparam>
    /// <param name="set">Selects the set of the entity.</param>
    /// <param name="match">The match that detects existing records; <see langword="null" /> inserts every row.</param>
    /// <param name="errors">Assigns database errors to row errors.</param>
    /// <returns>The target.</returns>
    public TabularWriteTarget<TEntity, TEntity> PrepareInsert<TEntity>(
        Func<TContext, DbSet<TEntity>> set,
        TabularMatch<TEntity, TEntity>? match = null,
        Action<TabularWriteErrors>? errors = null
    )
        where TEntity : class => Write(TargetKind.Insert, set, match, null, null, errors);

    /// <summary>Prepares an upsert target: a new record runs <paramref name="create" />, an existing one <paramref name="update" />.</summary>
    /// <typeparam name="TDto">The record type.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="set">Selects the set of the entity.</param>
    /// <param name="match">The match that finds the existing record.</param>
    /// <param name="create">Creates the entity from the record.</param>
    /// <param name="update">Applies the record to the existing entity; it alone decides what changes.</param>
    /// <param name="errors">Assigns exceptions and database errors to row errors.</param>
    /// <returns>The target.</returns>
    public TabularWriteTarget<TDto, TEntity> PrepareUpsert<TDto, TEntity>(
        Func<TContext, DbSet<TEntity>> set,
        TabularMatch<TDto, TEntity> match,
        Func<TDto, TEntity> create,
        Action<TEntity, TDto> update,
        Action<TabularWriteErrors>? errors = null
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(update);

        return Write(TargetKind.Upsert, set, match, (dto, _) => create(dto), update, errors);
    }

    /// <summary>Prepares an upsert target whose new entity uses values of other targets of the row.</summary>
    /// <typeparam name="TDto">The record type.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="set">Selects the set of the entity.</param>
    /// <param name="match">The match that finds the existing record.</param>
    /// <param name="create">Creates the entity from the record and the values of earlier targets.</param>
    /// <param name="update">Applies the record to the existing entity; it alone decides what changes.</param>
    /// <param name="errors">Assigns exceptions and database errors to row errors.</param>
    /// <returns>The target.</returns>
    public TabularWriteTarget<TDto, TEntity> PrepareUpsert<TDto, TEntity>(
        Func<TContext, DbSet<TEntity>> set,
        TabularMatch<TDto, TEntity> match,
        Func<TDto, TabularTargetValues, TEntity> create,
        Action<TEntity, TDto> update,
        Action<TabularWriteErrors>? errors = null
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(update);

        return Write(TargetKind.Upsert, set, match, create, update, errors);
    }

    /// <summary>Prepares an upsert target for records that are entities themselves; a new record is inserted as it is.</summary>
    /// <typeparam name="TEntity">The entity and record type.</typeparam>
    /// <param name="set">Selects the set of the entity.</param>
    /// <param name="match">The match that finds the existing record.</param>
    /// <param name="update">Applies the record to the existing entity.</param>
    /// <param name="errors">Assigns exceptions and database errors to row errors.</param>
    /// <returns>The target.</returns>
    public TabularWriteTarget<TEntity, TEntity> PrepareUpsert<TEntity>(
        Func<TContext, DbSet<TEntity>> set,
        TabularMatch<TEntity, TEntity> match,
        Action<TEntity, TEntity> update,
        Action<TabularWriteErrors>? errors = null
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(update);

        return Write(TargetKind.Upsert, set, match, null, update, errors);
    }

    /// <summary>Prepares a read target: a row without a matching record is the row error <c>tabular.lookup.notFound</c>.</summary>
    /// <typeparam name="TDto">The record type.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="set">Selects the set of the entity.</param>
    /// <param name="match">The match that finds the record.</param>
    /// <returns>The target.</returns>
    public TabularLookupTarget<TDto, TEntity> PrepareLookup<TDto, TEntity>(
        Func<TContext, DbSet<TEntity>> set,
        TabularMatch<TDto, TEntity> match
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(match);
        var definition = new TargetDefinition<TContext, TDto, TEntity>(this,
            TargetKind.Lookup,
            set,
            match,
            null,
            null,
            new TabularWriteErrors());

        return new TabularLookupTarget<TDto, TEntity>(new TargetView(definition, new TabularWriteOptions(), null),
            match.SourceKey);
    }

    private TabularWriteTarget<TDto, TEntity> Write<TDto, TEntity>(
        TargetKind kind,
        Func<TContext, DbSet<TEntity>> set,
        TabularMatch<TDto, TEntity>? match,
        Func<TDto, TabularTargetValues, TEntity>? create,
        Action<TEntity, TDto>? update,
        Action<TabularWriteErrors>? errors
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(set);
        var assignment = new TabularWriteErrors();
        errors?.Invoke(assignment);
        var definition = new TargetDefinition<TContext, TDto, TEntity>(this,
            kind,
            set,
            match,
            create,
            update,
            assignment);

        return new TabularWriteTarget<TDto, TEntity>(new TargetView(definition, new TabularWriteOptions(), null),
            match?.SourceKey);
    }
}

/// <summary>Non-generic access to the context of a database description.</summary>
internal interface IContextSource
{
    bool IsBorrowed { get; }

    ValueTask<(DbContext Context, bool Owned)> AcquireAsync(
        CancellationToken cancellationToken
    );

    bool TryReserve();

    void Release();
}
