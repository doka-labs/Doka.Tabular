using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Stable error code. Applications react to and translate errors by code, never by message.
/// Codes compare ordinally; there is no implicit conversion from <see cref="string" />. System.Text.Json reads
/// and writes a code as a string through <see cref="TabularErrorCodeJsonConverter" />.
/// </summary>
[JsonConverter(typeof(TabularErrorCodeJsonConverter))]
[PublicAPI]
public sealed class TabularErrorCode : IEquatable<TabularErrorCode>
{
    private TabularErrorCode(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the code text.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        TabularErrorCode? other
    ) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <summary>Creates a code, for example for a field rule or an expected exception.</summary>
    /// <param name="value">Non-empty text without white space.</param>
    /// <returns>The code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="value" /> is empty or contains white space.</exception>
    public static TabularErrorCode Create(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0
            || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("An error code must be non-empty and must not contain white space.",
                nameof(value));
        }

        return new TabularErrorCode(value);
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is TabularErrorCode other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Compares two codes ordinally.</summary>
    public static bool operator ==(
        TabularErrorCode? left,
        TabularErrorCode? right
    ) => Equals(left, right);

    /// <summary>Compares two codes ordinally.</summary>
    public static bool operator !=(
        TabularErrorCode? left,
        TabularErrorCode? right
    ) => !Equals(left, right);
}
