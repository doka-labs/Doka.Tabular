using System.Security.Cryptography;

namespace Doka.Tabular;

/// <summary>SHA-256 hash of file bytes, represented as 64 lowercase hexadecimal characters.</summary>
[PublicAPI]
public sealed class Sha256Hash : IEquatable<Sha256Hash>
{
    private Sha256Hash(
        string value
    )
    {
        Value = value;
    }

    /// <summary>Gets the hash as 64 lowercase hexadecimal characters.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(
        Sha256Hash? other
    ) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <summary>Parses a hash from 64 hexadecimal characters.</summary>
    /// <param name="value">The hexadecimal text; upper-case digits are normalized to lower case.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="value" /> is not 64 hexadecimal characters.</exception>
    public static Sha256Hash Parse(
        string value
    )
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != 64
            || !value.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("A SHA-256 hash consists of 64 hexadecimal characters.", nameof(value));
        }

        return new Sha256Hash(value.ToLowerInvariant());
    }

    /// <summary>Creates a hash from the 32 digest bytes.</summary>
    /// <param name="digest">The digest.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="ArgumentException"><paramref name="digest" /> does not contain 32 bytes.</exception>
    public static Sha256Hash FromBytes(
        ReadOnlySpan<byte> digest
    )
    {
        if (digest.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException("A SHA-256 digest contains 32 bytes.", nameof(digest));
        }

        return new Sha256Hash(Convert.ToHexStringLower(digest));
    }

    /// <inheritdoc />
    public override bool Equals(
        object? obj
    ) => obj is Sha256Hash other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
