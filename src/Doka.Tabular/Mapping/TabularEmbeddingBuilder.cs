using System.Linq.Expressions;

namespace Doka.Tabular;

/// <summary>Configures one inclusion of a built map, as a nested map or as a <see cref="RowMap" /> role.</summary>
/// <typeparam name="TNested">Type of the included map.</typeparam>
[PublicAPI]
public sealed class TabularEmbeddingBuilder<TNested>
{
    internal TabularEmbeddingBuilder(
        EmbeddingSettings settings
    )
    {
        Settings = settings;
    }

    private EmbeddingSettings Settings { get; }

    /// <summary>Offsets every declared index: effective index = <paramref name="startIndex" /> + declared index.</summary>
    /// <param name="startIndex">The offset; not negative.</param>
    /// <returns>This builder.</returns>
    public TabularEmbeddingBuilder<TNested> StartIndex(
        int startIndex
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        Settings.StartIndex = startIndex;

        return this;
    }

    /// <summary>Prefixes every key of the inclusion, for example <c>address.city</c>.</summary>
    /// <param name="prefix">The prefix.</param>
    /// <returns>This builder.</returns>
    public TabularEmbeddingBuilder<TNested> KeyPrefix(
        ColumnKey prefix
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Settings.KeyPrefix = prefix;

        return this;
    }

    /// <summary>Sets the full key of one field of the inclusion; the key prefix does not apply to it.</summary>
    /// <typeparam name="TValue">The field type.</typeparam>
    /// <param name="field">The field of the included map.</param>
    /// <param name="key">The full key.</param>
    /// <returns>This builder.</returns>
    public TabularEmbeddingBuilder<TNested> ExportKey<TValue>(
        Expression<Func<TNested, TValue>> field,
        ColumnKey key
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        Settings.ExportKeys[PropertyExpressions.ChainName(field)] = key;

        return this;
    }

    /// <summary>Puts a fixed text, including any separator, in front of every header of the inclusion.</summary>
    /// <param name="prefix">The prefix text.</param>
    /// <returns>This builder.</returns>
    public TabularEmbeddingBuilder<TNested> HeaderPrefix(
        string prefix
    )
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Settings.HeaderPrefix = prefix;

        return this;
    }

    /// <summary>Sets the translation key of the header prefix, resolved per call from <c>Translations</c>.</summary>
    /// <param name="key">The translation key.</param>
    /// <returns>This builder.</returns>
    public TabularEmbeddingBuilder<TNested> HeaderPrefixTranslationKey(
        string key
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Settings.HeaderPrefixTranslationKey = key;

        return this;
    }

    /// <summary>Sets the full header of one field of the inclusion; the prefix does not apply to it.</summary>
    /// <typeparam name="TValue">The field type.</typeparam>
    /// <param name="field">The field of the included map.</param>
    /// <param name="header">The full header.</param>
    /// <returns>This builder.</returns>
    public TabularEmbeddingBuilder<TNested> Header<TValue>(
        Expression<Func<TNested, TValue>> field,
        string header
    )
    {
        ArgumentNullException.ThrowIfNull(header);
        Settings.Headers[PropertyExpressions.ChainName(field)] = header;

        return this;
    }

    /// <summary>Marks a <see cref="RowMap" /> role as import only; it writes nothing on export.</summary>
    /// <returns>This builder.</returns>
    public TabularEmbeddingBuilder<TNested> ImportOnly()
    {
        Settings.IsImportOnly = true;

        return this;
    }
}
