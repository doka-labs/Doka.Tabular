namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Name of a journal; compared with <see cref="StringComparison.OrdinalIgnoreCase" />. <c>default</c> is reserved.</summary>
[PublicAPI]
public sealed class JournalName : IEquatable<JournalName>
{
    private JournalName(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the name of the standard journal.</summary>
    public static JournalName Default { get; } = new("default");

    /// <summary>Gets the name.</summary>
    public string Value { get; }

    internal string Key => Value.ToLowerInvariant();

    /// <inheritdoc />
    public bool Equals(
        JournalName? other
    ) => other is not null && Value.Equals(other.Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a journal name of 1 to 64 ASCII letters, digits, hyphens, or underscores.</summary>
    /// <param name="value">The name.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentException">The name is empty, too long, contains other characters, or is <c>default</c>.</exception>
    public static JournalName Create(
        string value
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > 64
            || !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            throw new ArgumentException(
                "A journal name has 1 to 64 ASCII letters, digits, hyphens, or underscores.",
                nameof(value));
        }

        if (value.Equals(Default.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The name default is reserved for the standard journal.", nameof(value));
        }

        return new JournalName(value);
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is JournalName other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>The business scope of journaled imports and exports, for example <c>users</c> or <c>tenant-42/users/v1</c>.</summary>
[PublicAPI]
public sealed class JournalScope : IEquatable<JournalScope>
{
    private JournalScope(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the scope text.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        JournalScope? other
    ) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <summary>Creates a scope of 1 to 256 characters without control characters.</summary>
    /// <param name="value">The scope.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentException">The scope is empty, too long, or contains control characters.</exception>
    public static JournalScope Create(
        string value
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > 256
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("A scope has 1 to 256 characters without control characters.", nameof(value));
        }

        return new JournalScope(value);
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is JournalScope other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Compares two scopes ordinally.</summary>
    /// <param name="left">The first scope.</param>
    /// <param name="right">The second scope.</param>
    /// <returns><see langword="true" /> when both are equal.</returns>
    public static bool operator ==(
        JournalScope? left,
        JournalScope? right
    ) => Equals(left, right);

    /// <summary>Compares two scopes ordinally.</summary>
    /// <param name="left">The first scope.</param>
    /// <param name="right">The second scope.</param>
    /// <returns><see langword="true" /> when both differ.</returns>
    public static bool operator !=(
        JournalScope? left,
        JournalScope? right
    ) => !Equals(left, right);
}
