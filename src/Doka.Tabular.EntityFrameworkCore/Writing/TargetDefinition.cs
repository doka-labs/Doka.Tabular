using Microsoft.EntityFrameworkCore.Metadata;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Immutable description of one target; shared by all views created with <c>WithOptions</c> or <c>WithImportLog</c>.</summary>
internal abstract class TargetDefinition
{
    private readonly Lock _gate = new();
    private readonly List<TabularTargetValue> _values = [];

    protected TargetDefinition(
        object database,
        TargetKind kind,
        IReadOnlyList<MatchPart> parts,
        bool uniqueKey,
        TabularWriteErrors errors,
        Type entityType
    )
    {
        Database = database;
        Kind = kind;
        Parts = parts;
        UniqueKey = uniqueKey;
        Errors = errors;
        EntityType = entityType;
    }

    public object Database { get; }

    protected TargetKind Kind { get; }

    /// <summary>Gets whether the target can write its entity; a lookup only finds it.</summary>
    public bool Writes => Kind != TargetKind.Lookup;

    public IReadOnlyList<MatchPart> Parts { get; }

    private bool UniqueKey { get; }

    public TabularWriteErrors Errors { get; }

    public Type EntityType { get; }

    public IReadOnlyList<TabularTargetValue> Values
    {
        get
        {
            lock (_gate)
            {
                return [.. _values];
            }
        }
    }

    public void AddValue(
        TabularTargetValue value
    )
    {
        lock (_gate)
        {
            _values.Add(value);
        }
    }

    /// <summary>Checks the entity type and the key coverage of the match against the model.</summary>
    public IEnumerable<TabularError> Check(
        DbContext context
    )
    {
        // WHY: The lookup by CLR type over the model needs no trimming annotation of the entity type, unlike
        // FindEntityType(Type); it finds the same entity type, which is never a shared-type entity here.
        var entityType = context
            .Model
            .GetEntityTypes()
            .FirstOrDefault(candidate => candidate.ClrType == EntityType && !candidate.HasSharedClrType);

        if (entityType is null)
        {
            yield return TabularError.Configuration($"The entity type {EntityType.Name} is not part of the model.");
            yield break;
        }

        if (Parts.Count == 0)
        {
            yield break;
        }

        var paired = Parts
            .Select(part => part.EntityProperty.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (!UniqueKey)
        {
            if (entityType.FindPrimaryKey() is not { } primary
                || !Covers(primary.Properties))
            {
                yield return TabularError.Configuration(
                    $"The match of {EntityType.Name} must cover the primary key exactly; use UseUniqueKey for another unique key.");
            }

            yield break;
        }

        if (!entityType
                .GetKeys()
                .Any(key => Covers(key.Properties))
            && !entityType
                .GetIndexes()
                .Any(index => index.IsUnique && index.GetFilter() is null && Covers(index.Properties)))
        {
            yield return TabularError.Configuration(
                $"The pairs of the match of {EntityType.Name} do not form a unique key or unfiltered unique index.");
        }

        yield break;

        bool Covers(
            IEnumerable<IReadOnlyProperty> properties
        ) => properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(paired);
    }

    /// <summary>The query state of the match for one write session.</summary>
    public abstract MatchQuery CreateQuery();

    public abstract Task<TargetStep> ApplyAsync(
        DbContext context,
        object dto,
        TabularTargetValues values,
        RowTracking tracking,
        MatchQueries queries,
        TabularErrorLocation location,
        CancellationToken cancellationToken
    );
}

internal sealed class TargetDefinition<TContext, TDto, TEntity> : TargetDefinition
    where TContext : DbContext
    where TEntity : class
{
    private readonly Func<TDto, TabularTargetValues, TEntity>? _create;
    private readonly Func<TContext, DbSet<TEntity>> _set;
    private readonly Action<TEntity, TDto>? _update;

    public TargetDefinition(
        object database,
        TargetKind kind,
        Func<TContext, DbSet<TEntity>> set,
        TabularMatch<TDto, TEntity>? match,
        Func<TDto, TabularTargetValues, TEntity>? create,
        Action<TEntity, TDto>? update,
        TabularWriteErrors errors
    )
        : base(database, kind, match?.Parts ?? [], match?.UniqueKey ?? false, errors, typeof(TEntity))
    {
        _set = set;
        _create = create;
        _update = update;
    }

    public override async Task<TargetStep> ApplyAsync(
        DbContext context,
        object dto,
        TabularTargetValues values,
        RowTracking tracking,
        MatchQueries queries,
        TabularErrorLocation location,
        CancellationToken cancellationToken
    )
    {
        var record = (TDto)dto;
        DbSet<TEntity> set;
        try
        {
            set = _set((TContext)context);
        }

        // WHY: Application code receives no token; its cancellation counts as the call's only while the call is
        // canceled.
        catch (Exception exception) when (Callbacks.Failed(exception, cancellationToken))
        {
            return Failed("set", location);
        }

        var (existing, failure) = Parts.Count == 0
            ? (null, null)
            : await FindAsync(set, dto, values, queries.For(this), location, cancellationToken)
                .ConfigureAwait(false);

        if (failure is not null)
        {
            return failure;
        }

        return (Kind, existing) switch
        {
            (TargetKind.Lookup, null) => new TargetStep(null,
                false,
                TabularError.Create(TabularErrorCodes.Lookup.NotFound,
                    TabularErrorKind.Data,
                    $"No {EntityType.Name} matches the row.",
                    location),
                null),
            (TargetKind.Lookup, { } found) => new TargetStep(found, false, null, null),
            (TargetKind.Insert, { } found) => new TargetStep(found, true, null, null),
            (TargetKind.Upsert, { } found) => Update(context, found, record, tracking, location, cancellationToken),
            (TargetKind.Insert or TargetKind.Upsert, null) => Create(set, dto, values, location, cancellationToken),
            _ => throw new UnreachableException($"The target kind {Kind} is not handled."),
        };
    }

    /// <summary>The entity the match finds; a step is returned instead when the key is incomplete or not unique.</summary>
    private async Task<(TEntity? Existing, TargetStep? Failure)> FindAsync(
        DbSet<TEntity> set,
        object dto,
        TabularTargetValues values,
        MatchQuery query,
        TabularErrorLocation location,
        CancellationToken cancellationToken
    )
    {
        for (var index = 0; index < Parts.Count; index++)
        {
            if (!Parts[index]
                .Stage(dto, values, query.Holders[index]))
            {
                return (null, new TargetStep(null,
                    false,
                    TabularError.Create(TabularErrorCodes.Key.Incomplete,
                        TabularErrorKind.Data,
                        $"A part of the match of {EntityType.Name} is empty.",
                        Located(location, TabularPropertyPath.Of(Parts[index].DtoProperty.Name))),
                    null));
            }
        }

        var filtered = set.Where((Expression<Func<TEntity, bool>>)query.Predicate);
        var candidates = await (Kind == TargetKind.Lookup
                ? filtered.AsNoTracking()
                : filtered.AsTracking())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        TEntity? match = null;
        var count = 0;
        try
        {
            // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                if (query.Matches(candidate))
                {
                    match = candidate;
                    count++;
                }
            }
        }

        // WHY: Application code receives no token; its cancellation counts as the call's only while the call is
        // canceled.
        catch (Exception exception) when (Callbacks.Failed(exception, cancellationToken))
        {
            return (null, Failed("comparer", location));
        }

        return count > 1
            ? (null, new TargetStep(null,
                false,
                null,
                TabularError.Create(TabularErrorCodes.Lookup.NotUnique,
                    TabularErrorKind.Technical,
                    $"The match of {EntityType.Name} returned several records despite the key.",
                    location)))
            : (match, null);
    }

    private TargetStep Update(
        DbContext context,
        TEntity existing,
        TDto record,
        RowTracking tracking,
        TabularErrorLocation location,
        CancellationToken cancellationToken
    )
    {
        tracking.Protect(context.Entry(existing));
        try
        {
            _update!(existing, record);
        }

        // WHY: Application code receives no token; its cancellation counts as the call's only while the call is
        // canceled.
        catch (Exception exception) when (Callbacks.Failed(exception, cancellationToken))
        {
            return Mapped(exception, "update", location);
        }

        return new TargetStep(existing, false, null, null);
    }

    private TargetStep Create(
        DbSet<TEntity> set,
        object dto,
        TabularTargetValues values,
        TabularErrorLocation location,
        CancellationToken cancellationToken
    )
    {
        TEntity created;
        try
        {
            created = _create is null
                ? (TEntity)dto
                : _create((TDto)dto, values);
        }

        // WHY: Application code receives no token; its cancellation counts as the call's only while the call is
        // canceled.
        catch (Exception exception) when (Callbacks.Failed(exception, cancellationToken))
        {
            return Mapped(exception, "create", location);
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // WHY: The create function belongs to the application; a null result is reported, not trusted.
        if (created is null)
        {
            return new TargetStep(null,
                false,
                null,
                TabularError.Create(TabularErrorCodes.Callback.InvalidResult,
                    TabularErrorKind.Technical,
                    $"The create function of {EntityType.Name} returned null.",
                    location));
        }

        set.Add(created);

        return new TargetStep(created, false, null, null);
    }

    /// <summary>The holders and the predicate of the match, built once per write session.</summary>
    public override MatchQuery CreateQuery()
    {
        var holders = Parts
            .Select(part => part.CreateHolder())
            .ToArray();

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression? body = null;

        // WHY: The conditions are joined in declaration order, each with the holder at the same index.
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Parts.Count; index++)
        {
            // WHY: A field of a holder becomes a query parameter, so every row reuses the cached query plan.
            var equal = Parts[index]
                .Condition(parameter, holders[index]);

            body = body is null
                ? equal
                : Expression.AndAlso(body, equal);
        }

        return new MatchQuery(Parts, holders, Expression.Lambda<Func<TEntity, bool>>(body!, parameter));
    }

    private TargetStep Mapped(
        Exception exception,
        string function,
        TabularErrorLocation location
    ) => Errors.Find(exception) is { } code
        ? new TargetStep(null,
            false,
            TabularError.Create(code,
                TabularErrorKind.Data,
                $"The {function} function of {EntityType.Name} rejected the row.",
                location),
            null)
        : Failed(function, location);

    private TargetStep Failed(
        string function,
        TabularErrorLocation location
    ) => new(null,
        false,
        null,
        TabularError.Create(TabularErrorCodes.Callback.Failed,
            TabularErrorKind.Technical,
            $"The {function} function of {EntityType.Name} threw an unassigned exception.",
            location));

    private static TabularErrorLocation Located(
        TabularErrorLocation location,
        TabularPropertyPath path
    ) => new()
    {
        RowNumber = location.RowNumber,
        RecordNumber = location.RecordNumber,
        SheetName = location.SheetName,
        PropertyPath = path,
    };
}
