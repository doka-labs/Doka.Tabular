namespace Doka.Tabular;

/// <summary>Settings for enum and flags fields.</summary>
[PublicAPI]
public static class TabularEnumColumnExtensions
{
    /// <param name="builder">The field builder.</param>
    extension<TEnum, TBuilder>(
        TabularValueBuilder<TEnum, TBuilder> builder
    )
        where TEnum : struct, Enum
        where TBuilder : TabularValueBuilder<TEnum, TBuilder>
    {
        /// <summary>Sets the translation key of each value, resolved per call from <c>Translations</c>.</summary>
        /// <param name="keySelector">Returns the translation key of a value.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TranslationKeys(
            Func<TEnum, string> keySelector
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(keySelector);
            builder.Settings.EnumTranslationKey = value => keySelector((TEnum)value);

            return builder.This;
        }

        /// <summary>Accepts whole decimal numbers of declared values in addition to names.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowNumericInput()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowNumericInput = true;

            return builder.This;
        }

        /// <summary>Writes the numeric value instead of a name.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder ExportAsNumber()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ExportAsNumber = true;

            return builder.This;
        }

        /// <summary>Writes the C# member name instead of the attribute text.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder ExportMemberName()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ExportMemberName = true;

            return builder.This;
        }

        /// <summary>
        /// Chooses the member whose name is written for a value that has several aliases; once per value. Aliases share
        /// one value, so the member is named, best with <c>nameof</c>; <c>Build()</c> rejects a name that is no member.
        /// </summary>
        /// <param name="memberName">The C# name of the member, for example <c>nameof(Status.Enabled)</c>.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ExportAlias(
            string memberName
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(memberName);
            builder.Settings.ExportAliases.Add(memberName);

            return builder.This;
        }

        /// <summary>Sets the inner separator of a flags list; the default is the comma.</summary>
        /// <param name="separator">One character that is neither quote, white space, control character, surrogate, digit, plus, nor minus.</param>
        /// <returns>The field builder.</returns>
        public TBuilder FlagsSeparator(
            char separator
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.FlagsSeparator = separator;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TEnum, TBuilder>(
        TabularValueBuilder<TEnum?, TBuilder> builder
    )
        where TEnum : struct, Enum
        where TBuilder : TabularValueBuilder<TEnum?, TBuilder>
    {
        /// <summary>Sets the translation key of each value, resolved per call from <c>Translations</c>.</summary>
        /// <param name="keySelector">Returns the translation key of a value.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TranslationKeys(
            Func<TEnum, string> keySelector
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(keySelector);
            builder.Settings.EnumTranslationKey = value => keySelector((TEnum)value);

            return builder.This;
        }

        /// <summary>Accepts whole decimal numbers of declared values in addition to names.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowNumericInput()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowNumericInput = true;

            return builder.This;
        }

        /// <summary>Writes the numeric value instead of a name.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder ExportAsNumber()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ExportAsNumber = true;

            return builder.This;
        }

        /// <summary>Writes the C# member name instead of the attribute text.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder ExportMemberName()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ExportMemberName = true;

            return builder.This;
        }

        /// <summary>
        /// Chooses the member whose name is written for a value that has several aliases; once per value. Aliases share
        /// one value, so the member is named, best with <c>nameof</c>; <c>Build()</c> rejects a name that is no member.
        /// </summary>
        /// <param name="memberName">The C# name of the member, for example <c>nameof(Status.Enabled)</c>.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ExportAlias(
            string memberName
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(memberName);
            builder.Settings.ExportAliases.Add(memberName);

            return builder.This;
        }

        /// <summary>Sets the inner separator of a flags list; the default is the comma.</summary>
        /// <param name="separator">One character that is neither quote, white space, control character, surrogate, digit, plus, nor minus.</param>
        /// <returns>The field builder.</returns>
        public TBuilder FlagsSeparator(
            char separator
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.FlagsSeparator = separator;

            return builder.This;
        }
    }
}
