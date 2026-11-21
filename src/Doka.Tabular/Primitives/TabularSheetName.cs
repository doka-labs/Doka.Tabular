using System.Buffers;
using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Worksheet name. Names follow the Excel rules: 1 to 31 characters, none of <c>: \ / ? * [ ]</c>, no
/// leading or trailing apostrophe, and not the reserved name <c>History</c>. Names compare with
/// <see cref="StringComparer.OrdinalIgnoreCase" />, as Excel does. System.Text.Json reads and writes a name as
/// a string through <see cref="TabularSheetNameJsonConverter" />.
/// </summary>
[JsonConverter(typeof(TabularSheetNameJsonConverter))]
[PublicAPI]
public sealed class TabularSheetName : IEquatable<TabularSheetName>
{
    private static readonly SearchValues<char> s_invalidCharacters = SearchValues.Create(":\\/?*[]");

    private TabularSheetName(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the sheet name.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        TabularSheetName? other
    ) => other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a sheet name.</summary>
    /// <param name="value">The name.</param>
    /// <returns>The validated name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="value" /> violates the Excel naming rules.</exception>
    public static TabularSheetName Create(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        var problem = Validate(value);
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(value));
        }

        return new TabularSheetName(value);
    }

    /// <summary>Wraps a name read from an existing workbook; such names are not re-validated.</summary>
    internal static TabularSheetName FromWorkbook(
        string value
    ) => new(value);

    private static string? Validate(
        string value
    )
    {
        if (value.Length is 0 or > 31)
        {
            return "A sheet name must contain 1 to 31 characters.";
        }

        if (value
            .AsSpan()
            .ContainsAny(s_invalidCharacters))
        {
            return "A sheet name must not contain any of : \\ / ? * [ ].";
        }

        if (value is ['\'', ..] or [.., '\''])
        {
            return "A sheet name must not begin or end with an apostrophe.";
        }

        return string.Equals(value, "History", StringComparison.OrdinalIgnoreCase)
            ? "History is a reserved sheet name."
            : null;
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is TabularSheetName other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Compares two names with <see cref="StringComparer.OrdinalIgnoreCase" />.</summary>
    public static bool operator ==(
        TabularSheetName? left,
        TabularSheetName? right
    ) => Equals(left, right);

    /// <summary>Compares two names with <see cref="StringComparer.OrdinalIgnoreCase" />.</summary>
    public static bool operator !=(
        TabularSheetName? left,
        TabularSheetName? right
    ) => !Equals(left, right);
}
