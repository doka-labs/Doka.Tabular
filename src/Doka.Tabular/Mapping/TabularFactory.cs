using System.Linq.Expressions;

namespace Doka.Tabular;

/// <summary>
/// Prepares the mapped properties a factory reads. It is used once while the map is built; the prepared fields are
/// then read for every record through <see cref="TabularValues{T}" />.
/// </summary>
/// <typeparam name="T">The mapped type.</typeparam>
[PublicAPI]
public sealed class TabularFields<T>
{
    private readonly List<TabularError> _errors = [];
    private readonly IReadOnlyList<MapMember> _members;
    private readonly List<int> _reads = [];
    private bool _completed;

    internal TabularFields(
        IReadOnlyList<MapMember> members
    )
    {
        _members = members;
    }

    /// <summary>Gets the configuration errors found while the fields were prepared.</summary>
    internal IReadOnlyList<TabularError> Errors => _errors;

    /// <summary>Ends the preparation and returns a copy of the members read, which the map keeps.</summary>
    internal int[] Complete()
    {
        _completed = true;

        return [.. _reads];
    }

    /// <summary>Prepares a mapped property for reading its converted value in the factory.</summary>
    /// <typeparam name="TValue">The property type.</typeparam>
    /// <param name="property">The mapped property, for example <c>x => x.Email</c>.</param>
    /// <returns>The prepared field. A property that is not mapped becomes a configuration error of the map.</returns>
    /// <exception cref="ArgumentException">The expression does not access one property directly.</exception>
    /// <exception cref="InvalidOperationException">The map is already built.</exception>
    public TabularField<T, TValue> Of<TValue>(
        Expression<Func<T, TValue>> property
    )
    {
        // WHY: The built map keeps the fields read at Build; a later call would change an immutable map.
        if (_completed)
        {
            throw new InvalidOperationException("The fields are prepared only while the map is built.");
        }

        var info = PropertyExpressions.Direct(property);
        for (var ordinal = 0; ordinal < _members.Count; ordinal++)
        {
            if (_members[ordinal].Name == info.Name)
            {
                _reads.Add(ordinal);

                return new TabularField<T, TValue>(ordinal);
            }
        }

        _errors.Add(TabularError.Configuration($"The factory reads the property {info.Name}, which is not mapped.",
            new TabularErrorLocation { PropertyPath = TabularPropertyPath.Of(info.Name) }));

        return new TabularField<T, TValue>(-1);
    }
}

/// <summary>A mapped property prepared by <see cref="TabularFields{T}.Of{TValue}" />.</summary>
/// <typeparam name="T">The mapped type.</typeparam>
/// <typeparam name="TValue">The property type.</typeparam>
[PublicAPI]
public sealed class TabularField<T, TValue>
{
    internal TabularField(
        int ordinal
    )
    {
        Ordinal = ordinal;
    }

    internal int Ordinal { get; }
}

/// <summary>
/// Values of the mapped properties of the record being created, passed to a factory. The values are valid during the
/// factory call.
/// </summary>
/// <typeparam name="T">The mapped type.</typeparam>
[PublicAPI]
public sealed class TabularValues<T>
{
    private readonly RecordValues _values;

    internal TabularValues(
        RecordValues values
    )
    {
        _values = values;
    }

    /// <summary>Gets the converted value of a prepared field.</summary>
    /// <typeparam name="TValue">The property type.</typeparam>
    /// <param name="field">The field prepared by <see cref="TabularFields{T}.Of{TValue}" /> of this map.</param>
    /// <returns>The value.</returns>
    public TValue Get<TValue>(
        TabularField<T, TValue> field
    ) => _values.Get<TValue>(field.Ordinal);
}
