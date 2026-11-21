using System.Linq.Expressions;

namespace Doka.Tabular;

/// <summary>Entry point for building column maps.</summary>
[PublicAPI]
public static class TabularMap
{
    /// <summary>Starts a map for <typeparamref name="T" />.</summary>
    /// <typeparam name="T">The mapped type.</typeparam>
    /// <returns>A new builder.</returns>
    public static TabularMapBuilder<T> For<T>() => new();
}

/// <summary>
/// Immutable column map of <typeparamref name="T" /> for import and export. A built map can be used concurrently by
/// several calls; everything that belongs to a run lives in the call.
/// </summary>
/// <typeparam name="T">The mapped type.</typeparam>
[PublicAPI]
public sealed class TabularMap<T>
{
    internal TabularMap(
        MapModel model
    )
    {
        Model = model;
    }

    internal MapModel Model { get; }

    /// <summary>Declares a source key for duplicate detection within one call, passed as import option <c>SourceKey</c>.</summary>
    /// <typeparam name="TKey">Type of the mapped key property.</typeparam>
    /// <param name="property">The mapped property.</param>
    /// <param name="comparer">Comparer of converted values; the default is type equality, for text ordinal.</param>
    /// <returns>The key; the map itself is not changed.</returns>
    /// <exception cref="ArgumentException">The property is not mapped by this map.</exception>
    public TabularSourceKey<T> Key<TKey>(
        Expression<Func<T, TKey>> property,
        IEqualityComparer<TKey>? comparer = null
    )
    {
        var info = PropertyExpressions.Direct(property);
        if (Model.Members.All(member => member.Name != info.Name))
        {
            throw new ArgumentException($"The property {info.Name} is not mapped.", nameof(property));
        }

        var effectiveComparer = comparer ?? DefaultComparer<TKey>();

        return new TabularSourceKey<T>(
        [
            new SourceKeyPart<TKey>(TabularPropertyPath.Of(info.Name), effectiveComparer),
        ]);
    }

    private static IEqualityComparer<TKey> DefaultComparer<TKey>() => typeof(TKey) == typeof(string)
        ? (IEqualityComparer<TKey>)StringComparer.Ordinal
        : EqualityComparer<TKey>.Default;
}

/// <summary>One property of a source key with its typed comparer.</summary>
internal abstract class SourceKeyPart
{
    protected SourceKeyPart(
        TabularPropertyPath path
    )
    {
        Path = path;
    }

    public TabularPropertyPath Path { get; }

    /// <summary>The typed key values of the rows of one table, read from the records of <paramref name="itemType" />.</summary>
    [RequiresDynamicCode("The key reader is compiled from an expression tree at run time.")]
    [RequiresUnreferencedCode("The key reader binds the key property by reflection.")]
    public abstract KeyColumn CreateColumn(
        Type itemType
    );
}

internal sealed class SourceKeyPart<TKey> : SourceKeyPart
{
    private readonly IEqualityComparer<TKey> _comparer;

    public SourceKeyPart(
        TabularPropertyPath path,
        IEqualityComparer<TKey> comparer
    )
        : base(path)
    {
        _comparer = comparer;
    }

    [RequiresDynamicCode("The key reader is compiled from an expression tree at run time.")]
    [RequiresUnreferencedCode("The key reader binds the key property by reflection.")]
    public override KeyColumn CreateColumn(
        Type itemType
    )
    {
        var property = itemType.GetProperty(Path.Value)
            ?? throw new InvalidOperationException($"The property {Path.Value} does not exist on {itemType.Name}.");

        return new KeyColumn<TKey>(Accessors.Path<TKey>([property]), _comparer);
    }
}

/// <summary>The key values of one key part for the rows of a table, kept typed for comparison.</summary>
internal abstract class KeyColumn
{
    /// <summary>Reads the value of a record; <see langword="false" /> when it is empty.</summary>
    public abstract bool Stage(
        object item
    );

    /// <summary>Keeps the staged value as the value of the next row.</summary>
    public abstract void Commit();

    public abstract bool Equal(
        int left,
        int right
    );

    public abstract int Hash(
        int row
    );
}

internal sealed class KeyColumn<TKey> : KeyColumn
{
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly PathReader<TKey> _read;
    private readonly List<TKey> _values = [];
    private TKey _staged = default!;

    public KeyColumn(
        PathReader<TKey> read,
        IEqualityComparer<TKey> comparer
    )
    {
        _read = read;
        _comparer = comparer;
    }

    public override bool Stage(
        object item
    )
    {
        _staged = _read(item, out var missing);

        return !missing && Stages.IsPresent(_staged);
    }

    public override void Commit() => _values.Add(_staged);

    public override bool Equal(
        int left,
        int right
    ) => _comparer.Equals(_values[left], _values[right]);

    public override int Hash(
        int row
    ) => Stages.IsPresent(_values[row])
        ? _comparer.GetHashCode(_values[row]!)
        : 0;
}

/// <summary>Source key for duplicate detection; created with <see cref="TabularMap{T}.Key{TKey}" />.</summary>
[PublicAPI]
public abstract class TabularSourceKey
{
    private protected TabularSourceKey(
        Type itemType,
        IReadOnlyList<SourceKeyPart> parts,
        object? role
    )
    {
        ItemType = itemType;
        Parts = parts;
        Role = role;
    }

    internal Type ItemType { get; }

    internal IReadOnlyList<SourceKeyPart> Parts { get; }

    /// <summary>Gets the role of a row map the key applies to; <see langword="null" /> for the first role of the type.</summary>
    internal object? Role { get; }
}

/// <summary>Source key of a map: rows with equal converted key values in one sheet are all rejected.</summary>
/// <typeparam name="T">The mapped type.</typeparam>
[PublicAPI]
public sealed class TabularSourceKey<T> : TabularSourceKey
{
    internal TabularSourceKey(
        IReadOnlyList<SourceKeyPart> parts,
        object? role = null
    )
        : base(typeof(T), parts, role) { }

    internal TabularSourceKey<T> ForRole(
        TabularRole<T> role
    ) => new(Parts, role);
}
