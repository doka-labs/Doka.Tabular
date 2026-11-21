using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Dotted path from the mapped type to a target property, for example <c>Address.PostalCode</c>.
/// System.Text.Json reads and writes a path as a string through <see cref="TabularPropertyPathJsonConverter" />.
/// </summary>
[JsonConverter(typeof(TabularPropertyPathJsonConverter))]
[PublicAPI]
public sealed class TabularPropertyPath : IEquatable<TabularPropertyPath>
{
    private TabularPropertyPath(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the dotted path.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        TabularPropertyPath? other
    ) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    internal static TabularPropertyPath Of(
        string name
    ) => new(name);

    /// <summary>A path read from a transported report; it must be non-empty without surrounding white space.</summary>
    internal static TabularPropertyPath FromText(
        string value
    )
    {
        TextRules.RequireTrimmedNonEmpty(value, nameof(value));

        return new TabularPropertyPath(value);
    }

    internal TabularPropertyPath Append(
        string name
    ) => new($"{Value}.{name}");

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is TabularPropertyPath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
