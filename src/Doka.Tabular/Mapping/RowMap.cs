using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace Doka.Tabular;

/// <summary>
/// Combination of several independent maps read from one row; each import row yields one <see cref="TabularRow" />.
/// </summary>
[PublicAPI]
public sealed class RowMap
{
    internal RowMap(
        IReadOnlyList<RowRole> roles
    )
    {
        Roles = roles;
    }

    internal IReadOnlyList<RowRole> Roles { get; }

    /// <summary>Starts a row map.</summary>
    /// <returns>A new builder.</returns>
    public static RowMapBuilder Create() => new();

    /// <summary>Starts an export binding of the roles to properties of an application type.</summary>
    /// <typeparam name="TSource">The application type of the exported rows.</typeparam>
    /// <returns>A new binding builder.</returns>
    public RowExportMapBuilder<TSource> From<TSource>() => new(this);
}

internal sealed class RowRole
{
    public RowRole(
        int ordinal,
        MapModel model,
        EmbeddingSettings embedding,
        object identity
    )
    {
        Ordinal = ordinal;
        Model = model;
        Embedding = embedding;
        Identity = identity;
    }

    public int Ordinal { get; }

    public MapModel Model { get; }

    public EmbeddingSettings Embedding { get; }

    /// <summary>Gets the reference object that identifies the role, shared by the typed role handle.</summary>
    public object Identity { get; }
}

/// <summary>Builds a <see cref="RowMap" />.</summary>
[PublicAPI]
public sealed class RowMapBuilder
{
    private readonly List<(MapModel Model, EmbeddingSettings Embedding, object Identity)> _roles = [];

    internal RowMapBuilder() { }

    /// <summary>Adds a built map as a role of the row.</summary>
    /// <typeparam name="T">The type of the map.</typeparam>
    /// <param name="map">The map; it stays unchanged.</param>
    /// <param name="configure">Configures index offset, prefixes, and import-only use of this role.</param>
    /// <returns>A typed handle that belongs to this row map only.</returns>
    public TabularRole<T> Add<T>(
        TabularMap<T> map,
        Action<TabularEmbeddingBuilder<T>>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(map);
        var embedding = new EmbeddingSettings();
        configure?.Invoke(new TabularEmbeddingBuilder<T>(embedding));
        var role = new TabularRole<T>(_roles.Count);
        _roles.Add((map.Model, embedding.Clone(), role));

        return role;
    }

    /// <summary>Validates and builds the row map.</summary>
    /// <returns>The row map.</returns>
    /// <exception cref="TabularConfigurationException">
    /// Keys collide across roles, written fixed headers are equal after trimming and ignoring case, or no role was added.
    /// </exception>
    public RowMap Build()
    {
        var roles = _roles
            .Select((role, index) => new RowRole(index, role.Model, role.Embedding.Clone(), role.Identity))
            .ToList();

        var errors = new List<TabularError>();
        if (roles.Count == 0)
        {
            errors.Add(TabularError.Configuration("A row map needs at least one role."));
        }

        var fields = new List<FlatField>();

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var roleIndex = 0; roleIndex < roles.Count; roleIndex++)
        {
            var role = roles[roleIndex];
            MapStructureValidator.AddUnknownTargets(role.Model, role.Embedding, null, errors);
            MapFlattening.Flatten(role.Model, null, EmbeddingContext.Root.Enter(role.Embedding, string.Empty), fields);
        }

        HeaderText.AddIndistinct(fields
                .Where(field => field.IsExported)
                .Select(field => (field, HeaderText.Fixed(field))),
            errors);

        // WHY: Each duplicate group adds one configuration error; a query would hide that side effect.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var group in fields
            .GroupBy(field => field.Key)
            .Where(group => group.Count() > 1))
        {
            errors.Add(TabularError.Configuration(
                $"The key {group.Key} is used by more than one role or field; keys must be unique across the row map.",
                new TabularErrorLocation { ColumnKey = group.Key }));
        }

        if (errors.Count > 0)
        {
            throw new TabularConfigurationException(errors);
        }

        return new RowMap(roles);
    }
}

/// <summary>Typed handle of one role in a <see cref="RowMap" />.</summary>
/// <typeparam name="T">The type of the role's map.</typeparam>
[PublicAPI]
public sealed class TabularRole<T>
{
    internal TabularRole(
        int ordinal
    )
    {
        Ordinal = ordinal;
    }

    internal int Ordinal { get; }
}

/// <summary>The records of one import row of a <see cref="RowMap" />.</summary>
[PublicAPI]
public sealed class TabularRow
{
    private readonly RowMap _map;

    internal TabularRow(
        RowMap map,
        IReadOnlyList<object> items
    )
    {
        _map = map;

        // WHY: The row owns its list of records; the view keeps a cast back to a list from changing it.
        Items = new ReadOnlyCollection<object>(items as IList<object> ?? [.. items]);
    }

    /// <summary>Gets all records of the row in the order of the <c>Add</c> calls.</summary>
    public IReadOnlyList<object> Items { get; }

    /// <summary>Gets the record of a role; the same instance as in <see cref="Items" />.</summary>
    /// <typeparam name="T">The role type.</typeparam>
    /// <param name="role">A role of the row map that produced this row.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentException">The role belongs to another row map.</exception>
    public T Get<T>(
        TabularRole<T> role
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.Ordinal >= _map.Roles.Count
            || !ReferenceEquals(_map.Roles[role.Ordinal].Identity, role))
        {
            throw new ArgumentException("The role belongs to another row map.", nameof(role));
        }

        return (T)Items[role.Ordinal];
    }
}

/// <summary>Binds the roles of a <see cref="RowMap" /> to properties of an application type for export.</summary>
/// <typeparam name="TSource">The application type.</typeparam>
[PublicAPI]
public sealed class RowExportMapBuilder<TSource>
{
    private readonly Dictionary<int, Func<TSource, object?>> _bindings = [];
    private readonly RowMap _map;

    internal RowExportMapBuilder(
        RowMap map
    )
    {
        _map = map;
    }

    /// <summary>Binds a role to the property that holds its record.</summary>
    /// <typeparam name="T">The role type.</typeparam>
    /// <param name="role">The role.</param>
    /// <param name="property">Selects the record of the role from the source.</param>
    /// <returns>This builder.</returns>
    public RowExportMapBuilder<TSource> Bind<T>(
        TabularRole<T> role,
        Expression<Func<TSource, T>> property
    )
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(property);
        if (role.Ordinal >= _map.Roles.Count
            || !ReferenceEquals(_map.Roles[role.Ordinal].Identity, role))
        {
            throw new ArgumentException("The role belongs to another row map.", nameof(role));
        }

        var compiled = property.Compile();
        _bindings[role.Ordinal] = source => compiled(source);

        return this;
    }

    /// <summary>Builds the export binding.</summary>
    /// <returns>The binding.</returns>
    /// <exception cref="TabularConfigurationException">A role is not bound.</exception>
    public RowExportMap<TSource> Build()
    {
        var missing = _map
            .Roles
            .Where(role => !_bindings.ContainsKey(role.Ordinal) && !role.Embedding.IsImportOnly)
            .ToList();

        if (missing.Count > 0)
        {
            throw new TabularConfigurationException(
                missing
                    .Select(role
                        => TabularError.Configuration(
                            $"The role {role.Ordinal + 1} ({role.Model.Type.Name}) is not bound."))
                    .ToArray());
        }

        return new RowExportMap<TSource>(_map, new Dictionary<int, Func<TSource, object?>>(_bindings));
    }
}

/// <summary>Export binding of a <see cref="RowMap" /> for an application type.</summary>
/// <typeparam name="TSource">The application type.</typeparam>
[PublicAPI]
public sealed class RowExportMap<TSource>
{
    internal RowExportMap(
        RowMap map,
        IReadOnlyDictionary<int, Func<TSource, object?>> bindings
    )
    {
        Map = map;
        Bindings = bindings;
    }

    internal RowMap Map { get; }

    internal IReadOnlyDictionary<int, Func<TSource, object?>> Bindings { get; }
}
