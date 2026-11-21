namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Non-generic part of <see cref="TabularTargetValue{TValue}" />.</summary>
[PublicAPI]
public abstract class TabularTargetValue
{
    private protected TabularTargetValue(
        TargetDefinition target,
        object role,
        Func<object, object?> read
    )
    {
        Target = target;
        Role = role;
        Read = read;
    }

    internal TargetDefinition Target { get; }

    internal object Role { get; }

    internal Func<object, object?> Read { get; }
}

/// <summary>
/// The actual value of an entity property of another target in the same row, also a database-generated value. Created
/// with <c>Value</c> on a write or lookup target.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
[PublicAPI]
public sealed class TabularTargetValue<TValue> : TabularTargetValue
{
    internal TabularTargetValue(
        TargetDefinition target,
        object role,
        Func<object, object?> read
    )
        : base(target, role, read) { }
}

/// <summary>Values of other targets of the current row, passed to <c>create</c>.</summary>
[PublicAPI]
public sealed class TabularTargetValues
{
    private readonly Dictionary<TabularTargetValue, object?> _values = [];

    internal TabularTargetValues() { }

    /// <summary>Gets a value of a target that was written before in this row.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="value">The value handle.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The value belongs to a target that is not bound before this one.</exception>
    public TValue Get<TValue>(
        TabularTargetValue<TValue> value
    )
    {
        ArgumentNullException.ThrowIfNull(value);

        return _values.TryGetValue(value, out var result)
            ? (TValue)result!
            : throw new ArgumentException("The value belongs to a target that is not bound before this one.",
                nameof(value));
    }

    internal object? Get(
        TabularTargetValue value
    ) => _values[value];

    internal void Set(
        TabularTargetValue value,
        object? result
    ) => _values[value] = result;
}
