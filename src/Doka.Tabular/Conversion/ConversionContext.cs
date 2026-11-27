using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Doka.Tabular;

internal enum TabularFormatKind
{
    Csv,
    Excel,
}

/// <summary>Call-specific settings that conversion needs in addition to the field settings.</summary>
internal sealed class ConversionContext
{
    private readonly Dictionary<CultureInfo, CultureInfo> _fieldCultures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(NumberFormatInfo, NumberTextStyle), NumberSymbols> _symbols = [];

    public ConversionContext(
        TabularFormatKind format,
        CultureInfo callCulture,
        bool trimFields,
        IReadOnlyDictionary<string, string>? translations,
        MissingTranslationBehavior missingTranslation
    )
    {
        Format = format;

        // WHY: Translations and cultures belong to the application and may change between calls; a call keeps the
        // values of its start, the next call sees the current ones.
        CallCulture = callCulture.IsReadOnly
            ? callCulture
            : CultureInfo.ReadOnly(callCulture);

        TrimFields = trimFields;
        Translations = Snapshot(translations);
        MissingTranslation = missingTranslation;
    }

    /// <summary>
    /// Gets a buffer for intermediate text that a converter turns into its result before it returns. A context
    /// serves one call on one thread, so the buffer is never shared; its content is undefined between conversions.
    /// </summary>
    public TextBuffer Scratch { get; } = new(64);

    public TabularFormatKind Format { get; }

    private CultureInfo CallCulture { get; }

    private bool TrimFields { get; }

    public IReadOnlyDictionary<string, string>? Translations { get; }

    public MissingTranslationBehavior MissingTranslation { get; }

    /// <summary>Gets or sets whether the source workbook uses the 1904 date system.</summary>
    public bool Uses1904DateSystem { get; set; }

    /// <summary>
    /// The translations of one call: immutable dictionaries as they are, a <see cref="Dictionary{TKey,TValue}" /> with
    /// its comparer, any other dictionary with ordinal keys.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? Snapshot(
        IReadOnlyDictionary<string, string>? translations
    ) => translations switch
    {
        null or FrozenDictionary<string, string> or ImmutableDictionary<string, string> => translations,
        Dictionary<string, string> dictionary => dictionary.ToFrozenDictionary(dictionary.Comparer),
        _ => translations.ToFrozenDictionary(StringComparer.Ordinal),
    };

    /// <summary>The culture of a field: its own, as frozen for this call, otherwise the culture of the call.</summary>
    public CultureInfo CultureFor(
        FieldSettings settings
    ) => settings.Culture is { } culture
        ? Frozen(culture)
        : CallCulture;

    /// <summary>Freezes the cultures of a field and of its conversion base type when the call prepares it.</summary>
    public void FreezeCultures(
        FieldSettings settings
    )
    {
        if (settings.Culture is { } culture)
        {
            Frozen(culture);
        }

        if (settings.Conversion?.BaseSettings is { } baseSettings)
        {
            FreezeCultures(baseSettings);
        }
    }

    /// <summary>
    /// The read-only copy of a field culture for this call; like the call culture, it keeps the values of the start.
    /// </summary>
    private CultureInfo Frozen(
        CultureInfo culture
    )
    {
        if (!_fieldCultures.TryGetValue(culture, out var frozen))
        {
            frozen = culture.IsReadOnly
                ? culture
                : CultureInfo.ReadOnly(culture);

            _fieldCultures.Add(culture, frozen);
        }

        return frozen;
    }

    public bool TrimFor(
        FieldSettings settings
    ) => settings.Trim ?? TrimFields;

    /// <summary>
    /// The separators of a culture for one style, read once per call.
    /// </summary>
    /// <remarks>
    /// WHY: <see cref="NumberFormatInfo.NumberGroupSizes" /> returns a new copy on every read; a call reads them once
    /// per culture and style instead of once per cell.
    /// </remarks>
    public NumberSymbols SymbolsFor(
        NumberFormatInfo format,
        NumberTextStyle style
    )
    {
        if (!_symbols.TryGetValue((format, style), out var symbols))
        {
            symbols = NumberSymbols.Of(format, style);
            _symbols[(format, style)] = symbols;
        }

        return symbols;
    }
}

/// <summary>Why a value could not be converted; the message is a fixed text without source values.</summary>
internal readonly record struct ConversionFailure(
    TabularErrorCode Code,
    string Message
)
{
    public static ConversionFailure InvalidFormat(
        string message
    ) => new(TabularErrorCodes.Conversion.InvalidFormat, message);

    public static ConversionFailure OutOfRange(
        string message
    ) => new(TabularErrorCodes.Conversion.OutOfRange, message);

    public static ConversionFailure PrecisionLoss(
        string message
    ) => new(TabularErrorCodes.Conversion.PrecisionLoss, message);

    public static ConversionFailure Unsupported(
        string message
    ) => new(TabularErrorCodes.Conversion.UnsupportedValue, message);
}

/// <summary>The outcome of reading one cell.</summary>
internal enum ReadStatus
{
    Value,

    /// <summary>The text is valid but denotes no value, for example a flags list without parts.</summary>
    Empty,

    Failed,
}

/// <summary>The type-independent part of a converter: registry identity, width class, and setting checks.</summary>
internal abstract class ValueConverter
{
    /// <summary>Gets whether the Excel column is numeric by default, which influences the default width.</summary>
    public virtual ExcelWidthClass WidthClass => ExcelWidthClass.Text;

    /// <summary>Returns configuration problems of the settings for this type, checked by <c>Build()</c>.</summary>
    public virtual IEnumerable<string> ValidateSettings(
        FieldSettings settings
    ) => [];

    /// <summary>Returns configuration problems that depend on the call, such as colliding translations.</summary>
    public virtual IEnumerable<string> ValidateCall(
        FieldSettings settings,
        ConversionContext context
    ) => [];
}

/// <summary>
/// Converts one built-in type between cells and values without boxing. Instances are immutable and shared, except
/// for enum converters, which are built per call.
/// </summary>
/// <typeparam name="T">The converted type.</typeparam>
internal abstract class ValueConverter<T> : ValueConverter
{
    /// <summary>Converts trimmed, non-empty text.</summary>
    protected abstract ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out T value,
        out ConversionFailure failure
    );

    /// <summary>Converts a native Excel number or Boolean cell.</summary>
    /// <summary>
    /// Converts the text of a text cell; the default reads its span. A converter whose result is the text itself
    /// overrides this to keep a string that the cell already holds.
    /// </summary>
    public virtual ReadStatus TryReadText(
        ReadOnlyMemory<char> text,
        FieldSettings settings,
        ConversionContext context,
        out T value,
        out ConversionFailure failure
    ) => TryRead(text.Span, settings, context, out value, out failure);

    public virtual ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out T value,
        out ConversionFailure failure
    )
    {
        value = default!;
        failure = ConversionFailure.Unsupported("The cell type is not allowed for this field.");

        return ReadStatus.Failed;
    }

    /// <summary>
    /// Reads a native number cell the export is about to write through the import of this converter, so the check of
    /// the export cannot differ from what the import reads: zone, rounding, unit, and date system alike.
    /// </summary>
    protected bool TryReadBackNative(
        double number,
        string? numberFormat,
        FieldSettings settings,
        ConversionContext context,
        out T value
    ) => TryReadNative(
            SourceCell.FromNumber(number, NumberFormats.Classify(0, numberFormat ?? "General")),
            settings,
            context,
            out value,
            out _)
        == ReadStatus.Value;

    /// <summary>Appends the text of a non-null value to <paramref name="output" />.</summary>
    protected abstract bool TryWriteText(
        T value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    );

    /// <summary>Formats a non-null value for Excel; the default is a text cell.</summary>
    public virtual bool TryWriteExcel(
        T value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    ) => TryWriteCell(value, settings, context, output, out cell, out failure);

    /// <summary>Appends the text of a value and returns it as a text cell.</summary>
    public bool TryWriteCell(
        T value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        var start = output.Length;
        if (!TryWriteText(value, settings, context, output, out failure))
        {
            output.Truncate(start);
            cell = OutputCell.Empty;

            return false;
        }

        cell = OutputCell.FromText(output.Slice(start));

        return true;
    }

    protected static ReadStatus Read(
        T result,
        out T value,
        out ConversionFailure failure
    )
    {
        value = result;
        failure = default;

        return ReadStatus.Value;
    }

    protected static ReadStatus Fail(
        ConversionFailure reason,
        out T value,
        out ConversionFailure failure
    )
    {
        value = default!;
        failure = reason;

        return ReadStatus.Failed;
    }
}

internal enum ExcelWidthClass
{
    Text,
    Guid,
    Integer,
    Decimal,
    Date,
    Time,
    DateTime,
    Duration,
    Boolean,
}
