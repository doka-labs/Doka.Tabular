namespace Doka.Tabular;

/// <summary>
/// Settings of a value and its conversion, shared by the builder of a column and the builder of the base type of a
/// custom conversion. Type-specific settings, such as enum or time options, are extension methods that exist only for
/// matching value types.
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
/// <typeparam name="TBuilder">The concrete builder, which every setting returns for chaining.</typeparam>
[PublicAPI]
public abstract class TabularValueBuilder<TValue, TBuilder>
    where TBuilder : TabularValueBuilder<TValue, TBuilder>
{
    private protected TabularValueBuilder(
        FieldSettings settings
    )
    {
        Settings = settings;
    }

    internal FieldSettings Settings { get; }

    internal TBuilder This => (TBuilder)this;

    /// <summary>Makes an empty value a field error, also for <see cref="string" /> and nullable types.</summary>
    /// <returns>This builder.</returns>
    public TBuilder Required()
    {
        Settings.IsRequired = true;

        return This;
    }

    /// <summary>Adds a synchronous rule that runs after a successful conversion.</summary>
    /// <param name="rule">Returns <see langword="false" /> for an invalid value.</param>
    /// <param name="code">Error code of a violation; <see langword="null" /> uses the general validation code.</param>
    /// <param name="alsoOnExport">Whether the rule also runs on export when the field is selected.</param>
    /// <returns>This builder.</returns>
    public TBuilder Validate(
        Func<TValue, bool> rule,
        TabularErrorCode? code = null,
        bool alsoOnExport = false
    )
    {
        ArgumentNullException.ThrowIfNull(rule);
        Settings.Validators.Add(new FieldValidator<TValue>(rule, code, alsoOnExport));

        return This;
    }

    /// <summary>Turns trimming of the field text on or off, overriding <c>TrimFields</c> of the call.</summary>
    /// <param name="enabled">Whether leading and trailing white space is removed.</param>
    /// <returns>This builder.</returns>
    public TBuilder Trim(
        bool enabled = true
    )
    {
        Settings.Trim = enabled;

        return This;
    }

    /// <summary>Sets the culture for numbers and dates as text; it takes precedence over the call culture.</summary>
    /// <param name="culture">The culture.</param>
    /// <returns>This builder.</returns>
    public TBuilder Culture(
        CultureInfo culture
    )
    {
        ArgumentNullException.ThrowIfNull(culture);
        Settings.Culture = culture;

        return This;
    }

    /// <summary>Sets the Excel number format code for native cells, for example <c>dd.mm.yyyy</c>.</summary>
    /// <param name="formatCode">The Excel format code.</param>
    /// <returns>This builder.</returns>
    public TBuilder ExcelNumberFormat(
        string formatCode
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(formatCode);
        Settings.ExcelNumberFormat = formatCode;

        return This;
    }

    /// <summary>Sets a fixed text that represents <see langword="null" />, distinct from the empty string.</summary>
    /// <param name="text">The text, compared after trimming, exactly and ordinally.</param>
    /// <returns>This builder.</returns>
    public TBuilder NullText(
        string text
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        Settings.NullText = text;

        return This;
    }

    /// <summary>Sets the translation key of the text that represents <see langword="null" />.</summary>
    /// <param name="key">The translation key.</param>
    /// <returns>This builder.</returns>
    public TBuilder EmptyTranslationKey(
        string key
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Settings.EmptyTranslationKey = key;

        return This;
    }

    /// <summary>
    /// Masks all but the last <paramref name="visibleCount" /> characters of the formatted value. Allowed on
    /// export-only fields only.
    /// </summary>
    /// <param name="visibleCount">Number of visible trailing characters.</param>
    /// <param name="maskChar">Replacement character.</param>
    /// <returns>This builder.</returns>
    public TBuilder MaskExceptLast(
        int visibleCount,
        char maskChar
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visibleCount);
        Settings.Mask = new FieldMask(MaskMode.ExceptLast, visibleCount, maskChar);

        return This;
    }

    /// <summary>
    /// Masks all but the first <paramref name="visibleCount" /> characters of the formatted value. Allowed on
    /// export-only fields only.
    /// </summary>
    /// <param name="visibleCount">Number of visible leading characters.</param>
    /// <param name="maskChar">Replacement character.</param>
    /// <returns>This builder.</returns>
    public TBuilder MaskExceptFirst(
        int visibleCount,
        char maskChar
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visibleCount);
        Settings.Mask = new FieldMask(MaskMode.ExceptFirst, visibleCount, maskChar);

        return This;
    }
}
