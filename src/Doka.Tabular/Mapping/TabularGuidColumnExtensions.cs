namespace Doka.Tabular;

/// <summary>Settings for <see cref="Guid" /> fields.</summary>

// WHY: CA1708 reads the extension blocks for T and T? as members differing only by case
// (https://github.com/dotnet/sdk/issues/51716); the fix (https://github.com/dotnet/sdk/pull/51838) is not in
// SDK 10.0.404. Remove the suppression once the pinned SDK contains it.
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "Analyzer defect for extension blocks.")]
[PublicAPI]
public static class TabularGuidColumnExtensions
{
    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<Guid, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<Guid, TBuilder>
    {
        /// <summary>Restricts the accepted input formats, for example <c>"D"</c> or <c>"DN"</c>; the default accepts D, N, B, P, and X.</summary>
        /// <param name="formats">One or more of the format letters D, N, B, P, X.</param>
        /// <returns>The field builder.</returns>
        public TBuilder AcceptGuidFormats(
            string formats
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(formats);
            builder.Settings.GuidInputFormats = formats;

            return builder.This;
        }

        /// <summary>Sets the output format; the default is lowercase D.</summary>
        /// <param name="format">One of the format letters D, N, B, P, X.</param>
        /// <returns>The field builder.</returns>
        public TBuilder GuidExportFormat(
            string format
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(format);
            builder.Settings.GuidOutputFormat = format;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<Guid?, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<Guid?, TBuilder>
    {
        /// <summary>Restricts the accepted input formats, for example <c>"D"</c> or <c>"DN"</c>; the default accepts D, N, B, P, and X.</summary>
        /// <param name="formats">One or more of the format letters D, N, B, P, X.</param>
        /// <returns>The field builder.</returns>
        public TBuilder AcceptGuidFormats(
            string formats
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(formats);
            builder.Settings.GuidInputFormats = formats;

            return builder.This;
        }

        /// <summary>Sets the output format; the default is lowercase D.</summary>
        /// <param name="format">One of the format letters D, N, B, P, X.</param>
        /// <returns>The field builder.</returns>
        public TBuilder GuidExportFormat(
            string format
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(format);
            builder.Settings.GuidOutputFormat = format;

            return builder.This;
        }
    }
}
