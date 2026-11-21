namespace Doka.Tabular;

/// <summary>Settings for Boolean fields.</summary>

// WHY: CA1708 reads the extension blocks for T and T? as members differing only by case
// (https://github.com/dotnet/sdk/issues/51716); the fix (https://github.com/dotnet/sdk/pull/51838) is not in
// SDK 10.0.404. Remove the suppression once the pinned SDK contains it.
[SuppressMessage("Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "Analyzer defect for extension blocks.")]
[PublicAPI]
public static class TabularBooleanColumnExtensions
{
    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<bool, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<bool, TBuilder>
    {
        /// <summary>Sets the fixed text for <see langword="true" />; it replaces <c>true</c> on export and is accepted on import.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TrueText(
            string text
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(text);
            builder.Settings.TrueText = text;

            return builder.This;
        }

        /// <summary>Sets the fixed text for <see langword="false" />; it replaces <c>false</c> on export and is accepted on import.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The field builder.</returns>
        public TBuilder FalseText(
            string text
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(text);
            builder.Settings.FalseText = text;

            return builder.This;
        }

        /// <summary>Sets the translation keys of both values, resolved per call from <c>Translations</c>.</summary>
        /// <param name="trueKey">Key for <see langword="true" />.</param>
        /// <param name="falseKey">Key for <see langword="false" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TranslationKeys(
            string trueKey,
            string falseKey
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(trueKey);
            ArgumentException.ThrowIfNullOrEmpty(falseKey);
            builder.Settings.TrueTranslationKey = trueKey;
            builder.Settings.FalseTranslationKey = falseKey;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<bool?, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<bool?, TBuilder>
    {
        /// <summary>Sets the fixed text for <see langword="true" />; it replaces <c>true</c> on export and is accepted on import.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TrueText(
            string text
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(text);
            builder.Settings.TrueText = text;

            return builder.This;
        }

        /// <summary>Sets the fixed text for <see langword="false" />; it replaces <c>false</c> on export and is accepted on import.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The field builder.</returns>
        public TBuilder FalseText(
            string text
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(text);
            builder.Settings.FalseText = text;

            return builder.This;
        }

        /// <summary>Sets the translation keys of both values, resolved per call from <c>Translations</c>.</summary>
        /// <param name="trueKey">Key for <see langword="true" />.</param>
        /// <param name="falseKey">Key for <see langword="false" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TranslationKeys(
            string trueKey,
            string falseKey
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(trueKey);
            ArgumentException.ThrowIfNullOrEmpty(falseKey);
            builder.Settings.TrueTranslationKey = trueKey;
            builder.Settings.FalseTranslationKey = falseKey;

            return builder.This;
        }
    }
}
