using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Language-independent identifier of a map column, used for column selection, layout settings, and
/// error locations. Keys compare with <see cref="StringComparer.OrdinalIgnoreCase" />. System.Text.Json
/// reads and writes a key as a string through <see cref="ColumnKeyJsonConverter" />.
/// </summary>
[JsonConverter(typeof(ColumnKeyJsonConverter))]
[PublicAPI]
public sealed class ColumnKey : IEquatable<ColumnKey>
{
    private ColumnKey(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the key text as given at creation.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        ColumnKey? other
    ) => other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a key.</summary>
    /// <param name="value">Non-empty text without leading or trailing white space.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value" /> is empty or has surrounding white space.
    /// </exception>
    public static ColumnKey Create(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        TextRules.RequireTrimmedNonEmpty(value, nameof(value));

        return new ColumnKey(value);
    }

    /// <summary>Combines a prefix and a key with a dot, as used for embedded maps.</summary>
    internal static ColumnKey Combine(
        ColumnKey prefix,
        ColumnKey key
    ) => new($"{prefix.Value}.{key.Value}");

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is ColumnKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Compares two keys with <see cref="StringComparer.OrdinalIgnoreCase" />.</summary>
    public static bool operator ==(
        ColumnKey? left,
        ColumnKey? right
    ) => Equals(left, right);

    /// <summary>Compares two keys with <see cref="StringComparer.OrdinalIgnoreCase" />.</summary>
    public static bool operator !=(
        ColumnKey? left,
        ColumnKey? right
    ) => !Equals(left, right);
}
