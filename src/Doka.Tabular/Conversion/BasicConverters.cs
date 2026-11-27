using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Doka.Tabular;

internal sealed class StringConverter : ValueConverter<string>
{
    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out string value,
        out ConversionFailure failure
    ) => Read(Pad(text, settings), out value, out failure);

    public override ReadStatus TryReadText(
        ReadOnlyMemory<char> text,
        FieldSettings settings,
        ConversionContext context,
        out string value,
        out ConversionFailure failure
    )
    {
        // WHY: A shared string of a workbook is already a string; a cell that spans all of it is taken without a copy.
        if ((settings.Padding is null || text.Length >= settings.Padding.TotalWidth)
            && MemoryMarshal.TryGetString(text, out var whole, out var start, out var length)
            && start == 0
            && length == whole.Length)
        {
            return Read(whole, out value, out failure);
        }

        return TryRead(text.Span, settings, context, out value, out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out string value,
        out ConversionFailure failure
    )
    {
        if (cell.Kind != CellKind.Number
            || !double.IsFinite(cell.Number))
        {
            return base.TryReadNative(cell, settings, context, out value, out failure);
        }

        var output = context.Scratch;
        output.Clear();
        NumberRules
            .ShortestOf(cell.Number)
            .WritePlain(output, CultureInfo.InvariantCulture);

        return Read(Pad(output.Written(0), settings), out value, out failure);
    }

    protected override bool TryWriteText(
        string value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (settings.Padding is { } padding
            && value.Length < padding.TotalWidth)
        {
            output.Append(padding.PaddingCharacter, padding.TotalWidth - value.Length);
        }

        output.Append(value);

        return true;
    }

    public override bool TryWriteExcel(
        string value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        // WHY: Unpadded text is the value itself, so the cell refers to the string instead of copying it.
        if (settings.Padding is null)
        {
            failure = default;
            cell = OutputCell.FromText(value.AsMemory());

            return true;
        }

        return TryWriteCell(value, settings, context, output, out cell, out failure);
    }

    /// <summary>The text padded to the width of the field; text at or above the width is unchanged.</summary>
    internal static string Pad(
        ReadOnlySpan<char> text,
        FieldSettings settings
    )
    {
        if (settings.Padding is not var (width, paddingCharacter)
            || text.Length >= width)
        {
            return text.ToString();
        }

        var buffer = width <= 256
            ? stackalloc char[width]
            : new char[width];

        buffer[..(width - text.Length)]
            .Fill(paddingCharacter);

        text.CopyTo(buffer[(width - text.Length)..]);

        return new string(buffer);
    }
}

internal sealed class CharConverter : ValueConverter<char>
{
    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out char value,
        out ConversionFailure failure
    ) => text is [var single] && !char.IsSurrogate(single)
        ? Read(single, out value, out failure)
        : Fail(
            ConversionFailure.InvalidFormat("The text must be exactly one UTF-16 code unit that is not a surrogate."),
            out value,
            out failure);

    protected override bool TryWriteText(
        char value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (char.IsSurrogate(value))
        {
            failure = ConversionFailure.InvalidFormat("An isolated surrogate cannot be written.");

            return false;
        }

        output.Append(value);

        return true;
    }
}

internal sealed class RuneConverter : ValueConverter<Rune>
{
    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out Rune value,
        out ConversionFailure failure
    ) => Rune.DecodeFromUtf16(text, out var rune, out var consumed) == OperationStatus.Done && consumed == text.Length
        ? Read(rune, out value, out failure)
        : Fail(
            ConversionFailure.InvalidFormat("The text must be exactly one Unicode scalar value."),
            out value,
            out failure);

    protected override bool TryWriteText(
        Rune value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        output.Advance(value.EncodeToUtf16(output.GetSpan(2)));

        return true;
    }
}

internal sealed class GuidConverter : ValueConverter<Guid>
{
    private const string AllFormats = "DNBPX";

    public override ExcelWidthClass WidthClass => ExcelWidthClass.Guid;

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    )
    {
        if (settings.GuidInputFormats is { } input
            && !input.All(format => AllFormats.Contains(char.ToUpperInvariant(format), StringComparison.Ordinal)))
        {
            yield return "Guid input formats are limited to the letters D, N, B, P, and X.";
        }

        if (settings.GuidOutputFormat is { } output
            && (output.Length != 1 || !AllFormats.Contains(char.ToUpperInvariant(output[0]), StringComparison.Ordinal)))
        {
            yield return "The Guid output format is one of the letters D, N, B, P, and X.";
        }
    }

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out Guid value,
        out ConversionFailure failure
    )
    {
        // WHY: Guid.TryParseExact trims by itself; text that still has surrounding spaces comes from a field whose
        // trimming is off and must not be accepted.
        if (!text.IsEmpty
            && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
        {
            return Fail(
                ConversionFailure.InvalidFormat("The text is not a Guid in an accepted format."),
                out value,
                out failure);
        }

        var formats = (settings.GuidInputFormats ?? AllFormats).AsSpan();
        for (var index = 0; index < formats.Length; index++)
        {
            if (Guid.TryParseExact(text, formats.Slice(index, 1), out var parsed))
            {
                return Read(parsed, out value, out failure);
            }
        }

        return Fail(
            ConversionFailure.InvalidFormat("The text is not a Guid in an accepted format."),
            out value,
            out failure);
    }

    protected override bool TryWriteText(
        Guid value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        output.AppendFormatted(value, settings.GuidOutputFormat ?? "D", CultureInfo.InvariantCulture);

        return true;
    }
}

internal sealed class BooleanConverter : ValueConverter<bool>
{
    private const string BlankText =
        "A text of true or false is empty after trimming; a trimming field reads it as an empty value.";

    public override ExcelWidthClass WidthClass => ExcelWidthClass.Boolean;

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    )
    {
        if (Collides(Texts(true, settings, null), Texts(false, settings, null), settings.Trim == true))
        {
            yield return "A text of true equals a text of false; TrueText and FalseText must differ from each other "
                + "and from the standard texts of the other value.";
        }

        if (settings.Trim == true
            && HasBlankText(settings, null))
        {
            yield return BlankText;
        }
    }

    public override IEnumerable<string> ValidateCall(
        FieldSettings settings,
        ConversionContext context
    )
    {
        // WHY: The call decides the trimming; texts that collide only after trimming are found here.
        if ((context.Translations is not null || context.TrimFor(settings))
            && Collides(Texts(true, settings, context), Texts(false, settings, context), context.TrimFor(settings)))
        {
            yield return "A text of true equals a text of false as this call reads them.";
        }

        // WHY: An empty translation names no value with or without trimming; it is found before the first access, not
        // at the first record that needs it.
        if (context.Translations is { } translations
            && new[] { settings.TrueTranslationKey, settings.FalseTranslationKey }.Any(key
                => key is not null && translations.TryGetValue(key, out var text) && text.Length == 0))
        {
            yield return "A translation of the true or false text is empty.";
        }

        // WHY: Build checked the fixed texts for Trim(); the call adds its own trimming and its translations.
        if (context.TrimFor(settings)
            && (settings.Trim != true || context.Translations is not null)
            && HasBlankText(settings, context))
        {
            yield return BlankText;
        }
    }

    /// <summary>Whether a fixed or translated text consists of white space only.</summary>
    private static bool HasBlankText(
        FieldSettings settings,
        ConversionContext? context
    ) => Texts(true, settings, context)
        .Concat(Texts(false, settings, context))
        .Any(string.IsNullOrWhiteSpace);

    /// <summary>Every text that reads as <paramref name="value" />; the translation only with a context.</summary>
    private static IEnumerable<string> Texts(
        bool value,
        FieldSettings settings,
        ConversionContext? context
    )
    {
        yield return value
            ? "true"
            : "false";

        yield return value
            ? "1"
            : "0";

        if ((value
                ? settings.TrueText
                : settings.FalseText) is { } fixedText)
        {
            yield return fixedText;
        }

        if (context?.Translations is { } translations
            && (value
                ? settings.TrueTranslationKey
                : settings.FalseTranslationKey) is { } key
            && translations.TryGetValue(key, out var translated))
        {
            yield return translated;
        }
    }

    private static bool Collides(
        IEnumerable<string> trueTexts,
        IEnumerable<string> falseTexts,
        bool trims
    ) => trueTexts
        .Select(text => trims
            ? text.Trim()
            : text)
        .Intersect(
            falseTexts.Select(text => trims
                ? text.Trim()
                : text),
            StringComparer.OrdinalIgnoreCase)
        .Any();

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out bool value,
        out ConversionFailure failure
    )
    {
        var trims = context.TrimFor(settings);
        if (Matches(text, "true", settings.TrueText, settings.TrueTranslationKey, context, trims)
            || text is "1")
        {
            return Read(true, out value, out failure);
        }

        if (Matches(text, "false", settings.FalseText, settings.FalseTranslationKey, context, trims)
            || text is "0")
        {
            return Read(false, out value, out failure);
        }

        return Fail(
            ConversionFailure.InvalidFormat("The text is not a Boolean value of this field."),
            out value,
            out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out bool value,
        out ConversionFailure failure
    )
    {
        // WHY: Excel stores TRUE as exactly 1 and FALSE as exactly 0; any other number is not a Boolean.
        // ReSharper disable CompareOfFloatsByEqualityOperator
        return cell switch
        {
            { Kind: CellKind.Boolean } => Read(cell.Boolean, out value, out failure),
            { Kind: CellKind.Number, Number: 1 } => Read(true, out value, out failure),
            { Kind: CellKind.Number, Number: 0 } => Read(false, out value, out failure),
            { Kind: CellKind.Number } => Fail(
                ConversionFailure.InvalidFormat("Only the numbers 1 and 0 are Boolean values."),
                out value,
                out failure),
            _ => base.TryReadNative(cell, settings, context, out value, out failure),
        };

        // ReSharper restore CompareOfFloatsByEqualityOperator
    }

    protected override bool TryWriteText(
        bool value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        if (!Translations.Resolve(
            value
                ? settings.TrueTranslationKey
                : settings.FalseTranslationKey,
            value
                ? settings.TrueText ?? "true"
                : settings.FalseText ?? "false",
            context,
            out var text,
            out failure))
        {
            return false;
        }

        output.Append(text);

        return true;
    }

    public override bool TryWriteExcel(
        bool value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        var custom = settings.TrueText is not null
            || settings.FalseText is not null
            || (settings.TrueTranslationKey is not null && context.Translations is not null);

        if (custom)
        {
            return TryWriteCell(value, settings, context, output, out cell, out failure);
        }

        failure = default;
        cell = OutputCell.FromBoolean(value);

        return true;
    }

    private static bool Matches(
        ReadOnlySpan<char> text,
        string standard,
        string? fixedText,
        string? translationKey,
        ConversionContext context,
        bool trims
    ) => text.Equals(standard, StringComparison.OrdinalIgnoreCase)
        || (fixedText is not null && text.Equals(Label(fixedText, trims), StringComparison.OrdinalIgnoreCase))
        || (translationKey is not null
            && context.Translations is not null
            && context.Translations.TryGetValue(translationKey, out var translated)
            && text.Equals(Label(translated, trims), StringComparison.OrdinalIgnoreCase));

    /// <summary>A label as a trimmed input sees it, so a padded label written by the export reads back.</summary>
    private static ReadOnlySpan<char> Label(
        string label,
        bool trims
    ) => trims
        ? label
            .AsSpan()
            .Trim()
        : label;
}
