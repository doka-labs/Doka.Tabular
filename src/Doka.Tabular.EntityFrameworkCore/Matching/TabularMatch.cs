using System.Reflection;
using System.Runtime.CompilerServices;

namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Entry point for matches between import records and entities.</summary>
[PublicAPI]
public static class TabularMatch
{
    /// <summary>Starts a match between records of <paramref name="map" /> and entities of <typeparamref name="TEntity" />.</summary>
    /// <typeparam name="TDto">The record type of the map.</typeparam>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="map">The map whose records are matched; every DTO field of a pair must be mapped by it.</param>
    /// <returns>A new builder.</returns>
    public static TabularMatchBuilder<TDto, TEntity> For<TDto, TEntity>(
        TabularMap<TDto> map
    )
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(map);

        return new TabularMatchBuilder<TDto, TEntity>(map);
    }
}

/// <summary>Collects the pairs of a match. Every pair assigns one DTO field to one entity property; nothing is derived from names.</summary>
/// <typeparam name="TDto">The record type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
[PublicAPI]
public sealed class TabularMatchBuilder<TDto, TEntity>
    where TEntity : class
{
    private readonly TabularMap<TDto> _map;
    private readonly List<MatchPart> _parts = [];
    private bool _uniqueKey;

    internal TabularMatchBuilder(
        TabularMap<TDto> map
    )
    {
        _map = map;
    }

    /// <summary>Adds a pair compared with <paramref name="comparer" />.</summary>
    /// <typeparam name="TValue">The common value type.</typeparam>
    /// <param name="dto">The mapped DTO property.</param>
    /// <param name="entity">The entity property.</param>
    /// <param name="comparer">
    /// Comparer of the values; the default is type equality, for text ordinal. It checks the candidates the database
    /// found with the column collation, so it can narrow the match but not widen it.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A property is not accessed directly, the DTO property is not mapped, or the entity property is already paired.</exception>
    public TabularMatchBuilder<TDto, TEntity> Part<TValue>(
        Expression<Func<TDto, TValue>> dto,
        Expression<Func<TEntity, TValue>> entity,
        IEqualityComparer<TValue>? comparer = null
    ) => Add(dto, entity, null, comparer);

    /// <summary>
    /// Adds a pair whose value comes from another target of the same row, for example a database-generated id. The DTO
    /// field is part of the source key; the entity property is compared with the value of <paramref name="valueFrom" />.
    /// </summary>
    /// <typeparam name="TDtoValue">Type of the DTO property.</typeparam>
    /// <typeparam name="TValue">Type of the entity property and the value.</typeparam>
    /// <param name="dto">The mapped DTO property that references the other record in the file.</param>
    /// <param name="entity">The entity property.</param>
    /// <param name="valueFrom">The value of another target, created with <c>Value</c>.</param>
    /// <param name="comparer">
    /// Comparer of the values; the default is type equality, for text ordinal. It checks the candidates the database
    /// found with the column collation, so it can narrow the match but not widen it.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A property is not accessed directly, the DTO property is not mapped, or the entity property is already paired.</exception>
    public TabularMatchBuilder<TDto, TEntity> Part<TDtoValue, TValue>(
        Expression<Func<TDto, TDtoValue>> dto,
        Expression<Func<TEntity, TValue>> entity,
        TabularTargetValue<TValue> valueFrom,
        IEqualityComparer<TValue>? comparer = null
    )
    {
        ArgumentNullException.ThrowIfNull(valueFrom);

        return Add(dto, entity, valueFrom, comparer);
    }

    /// <summary>Declares that the pairs cover a unique key or unique index instead of the primary key.</summary>
    /// <returns>This builder.</returns>
    public TabularMatchBuilder<TDto, TEntity> UseUniqueKey()
    {
        _uniqueKey = true;

        return this;
    }

    /// <summary>Builds the immutable match.</summary>
    /// <returns>The match.</returns>
    /// <exception cref="InvalidOperationException">No pair was added.</exception>
    public TabularMatch<TDto, TEntity> Build()
    {
        if (_parts.Count == 0)
        {
            throw new InvalidOperationException("A match needs at least one pair.");
        }

        return new TabularMatch<TDto, TEntity>([.. _parts], _uniqueKey);
    }

    private TabularMatchBuilder<TDto, TEntity> Add<TDtoValue, TValue>(
        Expression<Func<TDto, TDtoValue>> dto,
        Expression<Func<TEntity, TValue>> entity,
        TabularTargetValue<TValue>? valueFrom,
        IEqualityComparer<TValue>? comparer
    )
    {
        var dtoProperty = PropertyExpressions.Direct(dto);
        var entityProperty = PropertyExpressions.Direct(entity);
        if (_map.Model.Members.All(member => member.Name != dtoProperty.Name))
        {
            throw new ArgumentException($"The property {dtoProperty.Name} is not mapped.", nameof(dto));
        }

        if (_parts.Any(part => part.EntityProperty.Name == entityProperty.Name))
        {
            throw new ArgumentException($"The entity property {entityProperty.Name} is already paired.",
                nameof(entity));
        }

        var effective = comparer
            ?? (typeof(TValue) == typeof(string)
                ? (IEqualityComparer<TValue>)StringComparer.Ordinal
                : EqualityComparer<TValue>.Default);

        var dtoComparer = typeof(TDtoValue) == typeof(string)
            ? (IEqualityComparer<TDtoValue>)StringComparer.Ordinal
            : EqualityComparer<TDtoValue>.Default;

        if (valueFrom is null
            && comparer is not null)
        {
            dtoComparer = (IEqualityComparer<TDtoValue>)comparer;
        }

        _parts.Add(new MatchPart<TDto, TEntity, TDtoValue, TValue>(dtoProperty,
            entityProperty,
            valueFrom,
            dto.Compile(),
            entity.Compile(),
            effective,
            new SourceKeyPart<TDtoValue>(TabularPropertyPath.Of(dtoProperty.Name), dtoComparer)));

        return this;
    }
}

/// <summary>
/// Immutable match between import records and entities. It also supplies the source key: equal keys in several records
/// of one sheet reject all of them.
/// </summary>
/// <typeparam name="TDto">The record type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
[PublicAPI]
public sealed class TabularMatch<TDto, TEntity>
    where TEntity : class
{
    internal TabularMatch(
        IReadOnlyList<MatchPart> parts,
        bool uniqueKey
    )
    {
        Parts = parts;
        UniqueKey = uniqueKey;
        SourceKey = new TabularSourceKey<TDto>(parts
            .Select(part => part.SourceKeyPart)
            .ToArray());
    }

    /// <summary>Gets the source key of the DTO fields of all pairs.</summary>
    public TabularSourceKey<TDto> SourceKey { get; }

    internal IReadOnlyList<MatchPart> Parts { get; }

    internal bool UniqueKey { get; }
}

/// <summary>One pair of a match; its key value is kept typed in a holder that a cached query reads.</summary>
internal abstract class MatchPart
{
    protected MatchPart(
        PropertyInfo dtoProperty,
        PropertyInfo entityProperty,
        TabularTargetValue? valueFrom,
        SourceKeyPart sourceKeyPart
    )
    {
        DtoProperty = dtoProperty;
        EntityProperty = entityProperty;
        ValueFrom = valueFrom;
        SourceKeyPart = sourceKeyPart;
    }

    public PropertyInfo DtoProperty { get; }

    public PropertyInfo EntityProperty { get; }

    public TabularTargetValue? ValueFrom { get; }

    public SourceKeyPart SourceKeyPart { get; }

    /// <summary>A new holder of the key value, read by a query as a parameter.</summary>
    public abstract IStrongBox CreateHolder();

    /// <summary>The comparison of the entity property with the holder value, for the query.</summary>
    public abstract Expression Condition(
        ParameterExpression entity,
        IStrongBox holder
    );

    /// <summary>Puts the key value of the row into the holder; <see langword="false" /> when it is empty.</summary>
    public abstract bool Stage(
        object dto,
        TabularTargetValues values,
        IStrongBox holder
    );

    /// <summary>Whether a found entity carries the key value of the holder under the comparer of the pair.</summary>
    public abstract bool Matches(
        object entity,
        IStrongBox holder
    );
}

internal sealed class MatchPart<TDto, TEntity, TDtoValue, TValue> : MatchPart
{
    // WHY: The field is resolved once per closed type; the typed lookup keeps it visible to trimming.
    // ReSharper disable once StaticMemberInGenericType
    private static readonly FieldInfo s_value = typeof(StrongBox<TValue>).GetField(nameof(StrongBox<>.Value))!;

    private readonly IEqualityComparer<TValue> _comparer;
    private readonly Func<TDto, TDtoValue> _dto;
    private readonly Func<TEntity, TValue> _entity;
    private readonly Func<TDtoValue, TValue>? _toValue;

    public MatchPart(
        PropertyInfo dtoProperty,
        PropertyInfo entityProperty,
        TabularTargetValue? valueFrom,
        Func<TDto, TDtoValue> dto,
        Func<TEntity, TValue> entity,
        IEqualityComparer<TValue> comparer,
        SourceKeyPart sourceKeyPart
    )
        : base(dtoProperty, entityProperty, valueFrom, sourceKeyPart)
    {
        _dto = dto;
        _entity = entity;
        _comparer = comparer;

        // WHY: A pair that takes its value from an earlier target never reads the record value.
        if (valueFrom is null)
        {
            var value = Expression.Parameter(typeof(TDtoValue), "value");
            _toValue = Expression
                .Lambda<Func<TDtoValue, TValue>>(Expression.Convert(value, typeof(TValue)), value)
                .Compile();
        }
    }

    public override IStrongBox CreateHolder() => new StrongBox<TValue>();

    public override Expression Condition(
        ParameterExpression entity,
        IStrongBox holder
    ) => Expression.Equal(Expression.Property(entity, EntityProperty),
        Expression.Field(Expression.Constant(holder), s_value));

    public override bool Stage(
        object dto,
        TabularTargetValues values,
        IStrongBox holder
    )
    {
        var typed = (StrongBox<TValue>)holder;

        // WHY: The pattern binds the checked non-null value; inlining would read the field or property again.
        // ReSharper disable once InlineTemporaryVariable
        if (ValueFrom is { } from)
        {
            var shared = values.Get(from);
            typed.Value = (TValue)shared!;

            return shared is not null;
        }

        var value = _dto((TDto)dto);
        if (value is null)
        {
            return false;
        }

        typed.Value = _toValue!(value);

        return true;
    }

    public override bool Matches(
        object entity,
        IStrongBox holder
    ) => _comparer.Equals(_entity((TEntity)entity), ((StrongBox<TValue>)holder).Value!);
}
