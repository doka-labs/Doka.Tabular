using System.Numerics;

namespace Doka.Tabular;

internal static class NumberRules
{
    private const int ExcelSignificantDigits = 15;

    /// <summary>The smallest positive number of an Excel worksheet.</summary>
    private const double ExcelSmallestNumber = 2.22507385850721E-308;

    private static readonly NumberTextStyle[] s_writtenStyles =
        [NumberTextStyle.Plain, NumberTextStyle.Currency, NumberTextStyle.Percent];

    public static IEnumerable<string> CommonProblems<TValue>(
        FieldSettings settings,
        bool isInteger,
        bool isFloating
    )
        where TValue : struct, IFormattable
    {
        if (settings.ExportFormat is { } exportFormat
            && !CanFormat<TValue>(exportFormat))
        {
            yield return $"The export format {exportFormat} is no valid .NET format of {typeof(TValue).Name}.";
        }

        if (isInteger && settings.NumericRounding is not null)
        {
            yield return "Integers do not accept rounding.";
        }

        if (settings.NumericRounding is { Decimals: not null, SignificantDigits: not null })
        {
            yield return "Rounding uses either decimal places or significant digits, not both.";
        }

        if (!isFloating
            && settings.AllowNonFiniteValues)
        {
            yield return "Only floating-point fields have NaN and infinity; NonFiniteValues has no effect here.";
        }
    }

    private static bool CanFormat<TValue>(
        string format
    )
        where TValue : struct, IFormattable
    {
        try
        {
            _ = default(TValue).ToString(format, CultureInfo.InvariantCulture);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static ExactDecimal ApplyRounding(
        ExactDecimal value,
        NumericRounding? rounding
    ) => rounding switch
    {
        { Decimals: { } decimals } => value.RoundToDecimals(decimals, rounding.Mode),
        { SignificantDigits: { } digits } => value.RoundToSignificantDigits(digits, rounding.Mode),
        _ => value,
    };

    public static bool TryParse(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        bool exponentByDefault,
        out ParsedNumberText parsed,
        out ConversionFailure failure
    )
    {
        failure = default;
        var format = context
            .CultureFor(settings)
            .NumberFormat;

        if (!DecimalText.TryParse(text,
            format,
            context.SymbolsFor(format, settings.TextStyle),
            settings.TextStyle,
            out parsed,
            out var problem))
        {
            failure = ConversionFailure.InvalidFormat(problem ?? "The text is not a number.");

            return false;
        }

        if (parsed.HadExponent
            && !exponentByDefault
            && !settings.AllowExponent)
        {
            failure = ConversionFailure.InvalidFormat("An exponent is not allowed for this field.");

            return false;
        }

        return true;
    }

    /// <summary>Appends the plain text of a value in the text style of the field.</summary>
    public static void WritePlain(
        ExactDecimal value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output
    )
    {
        var culture = context.CultureFor(settings);
        switch (settings.TextStyle)
        {
            case NumberTextStyle.Percent:
                // WHY: The import reads the percent separators of the culture, which can differ from the number ones.
                (value with { Exponent = value.Exponent + 2 }).WritePlain(output,
                    culture.NumberFormat,
                    context
                        .SymbolsFor(culture.NumberFormat, NumberTextStyle.Percent)
                        .Decimal);

                output.Append(culture.NumberFormat.PercentSymbol);
                break;
            case NumberTextStyle.Currency:
                // WHY: The import reads the currency separators of the culture, which can differ from the number ones.
                output.Append(culture.NumberFormat.CurrencySymbol);
                value.WritePlain(output,
                    culture.NumberFormat,
                    context
                        .SymbolsFor(culture.NumberFormat, NumberTextStyle.Currency)
                        .Decimal);

                break;
            case NumberTextStyle.Plain:
            default:
                value.WritePlain(output, culture);
                break;
        }
    }

    /// <summary>
    /// Appends the value in the export format and checks that the text still denotes <paramref name="expected" />.
    /// </summary>
    public static bool TryWriteWithExportFormat<TValue>(
        TValue value,
        ExactDecimal expected,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
        where TValue : ISpanFormattable, INumberBase<TValue>
    {
        var start = output.Length;
        var parsed = TryFormatted(value, settings, context, output, start);

        return Accepted(parsed is { } exact
                ? Equal(exact, expected)
                : DenotesExactly(expected, settings, context, output, start),
            output,
            start,
            out failure);
    }

    /// <summary>
    /// Writes a binary floating point value with the export format; the text must read back into the same value of
    /// the type, as for native numbers, not into its shortest decimal.
    /// </summary>
    public static bool TryWriteFloatWithExportFormat<TFloat>(
        TFloat value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
        where TFloat : IFloatingPointIeee754<TFloat>
    {
        var start = output.Length;
        var parsed = TryFormatted(value, settings, context, output, start);

        return Accepted(parsed is { } exact
                ? exact.ToFloat<TFloat>() == value
                : ReadsBackAsFloat(value, settings, context, output, start),
            output,
            start,
            out failure);
    }

    /// <summary>
    /// Whether a written text the plain, currency, and percent forms do not cover, such as a negative amount in
    /// parentheses or with a trailing sign, denotes exactly the value. The number parser of the culture only confirms
    /// the form and the sign: it rounds digits beyond its type, and a format can write such digits as literals, so the
    /// number is taken from the digits of the text itself.
    /// </summary>
    private static bool DenotesExactly(
        ExactDecimal expected,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        int start
    )
    {
        var culture = context.CultureFor(settings);
        var text = output.Written(start);

        // WHY: double accepts every length of digits, so integers beyond the decimal range keep their check.
        return double.TryParse(text, NumberStyles.Any, culture, out var parsed)
            && DigitsOf(text.ToString(), culture.NumberFormat, negative: double.IsNegative(parsed)) is { } written
            && Equal(written, expected);
    }

    /// <summary>
    /// The number a text accepted by the parser denotes, read from its own digits along the grammar of the parser:
    /// leading white space, sign, parenthesis, and currency symbol; then digits with the decimal and group separators;
    /// then an exponent after <c>E</c> or <c>e</c> with its sign. The currency symbol is only taken where the parser
    /// takes it, before or after the digits, never out of them. A text without a digit, such as the symbol of infinity
    /// or NaN, and an exponent beyond the range of a scale denote no number.
    /// </summary>
    /// <remarks>
    /// WHY: The parser of the culture confirmed the text but rounds it; this reads the same digits exactly. The order of
    /// the checks follows <c>Number.TryParseNumber</c> of .NET with <see cref="NumberStyles.Any" />: the currency
    /// separators always apply, the number separators only before a currency symbol.
    /// https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/Common/src/System/Number.Parsing.Common.cs
    /// </remarks>
    internal static ExactDecimal? DigitsOf(
        string text,
        NumberFormatInfo format,
        bool negative
    )
    {
        var position = 0;
        var signSeen = false;
        var currencySeen = false;

        // WHY: The states decide what a character is: after a sign or a parenthesis a sign character can only be the
        // currency symbol, and white space after a sign without a currency symbol ends the leading part, as in the
        // parser.
        while (position < text.Length)
        {
            if (IsParserWhite(text[position])
                && !(signSeen && !currencySeen && format.NumberNegativePattern != 2))
            {
                position++;
            }
            else if (!signSeen
                && Matches(text, position, format.PositiveSign) is var positive and > 0)
            {
                position += positive;
                signSeen = true;
            }
            else if (!signSeen
                && MatchesNegativeSign(text, position, format) is var negativeSign and > 0)
            {
                position += negativeSign;
                signSeen = true;
            }
            else if (!signSeen
                && text[position] == '(')
            {
                position++;
                signSeen = true;
            }
            else if (!currencySeen
                && Matches(text, position, format.CurrencySymbol) is var symbol and > 0)
            {
                position += symbol;
                currencySeen = true;
            }
            else
            {
                break;
            }
        }

        var mantissa = BigInteger.Zero;
        var digits = 0;
        var fraction = 0;
        var inFraction = false;
        while (position < text.Length)
        {
            if (text[position] is >= '0' and <= '9')
            {
                mantissa = mantissa * 10 + (text[position] - '0');
                digits++;
                fraction += inFraction
                    ? 1
                    : 0;

                position++;
            }
            else if (!inFraction
                && Separator(text,
                    position,
                    format.CurrencyDecimalSeparator,
                    format.NumberDecimalSeparator,
                    currencySeen) is var decimalSeparator and > 0)
            {
                inFraction = true;
                position += decimalSeparator;
            }
            else if (digits > 0
                && !inFraction
                && Separator(text,
                    position,
                    format.CurrencyGroupSeparator,
                    format.NumberGroupSeparator,
                    currencySeen) is var groupSeparator and > 0)
            {
                position += groupSeparator;
            }
            else
            {
                break;
            }
        }

        if (digits == 0)
        {
            return null;
        }

        var exponent = Exponent(text, position, format);
        var scale = exponent - fraction;

        return scale < int.MinValue || scale > int.MaxValue
            ? null
            : new ExactDecimal(negative, mantissa, (int)scale);
    }

    /// <summary>The exponent after the digits; zero when no <c>E</c> with digits follows, as for a currency symbol.</summary>
    private static BigInteger Exponent(
        string text,
        int position,
        NumberFormatInfo format
    )
    {
        if (position >= text.Length
            || text[position] is not ('E' or 'e'))
        {
            return BigInteger.Zero;
        }

        position++;
        var negative = false;
        if (Matches(text, position, format.PositiveSign) is var positive and > 0)
        {
            position += positive;
        }
        else if (MatchesNegativeSign(text, position, format) is var negativeSign and > 0)
        {
            position += negativeSign;
            negative = true;
        }

        var exponent = BigInteger.Zero;
        var digits = 0;
        for (; position < text.Length && text[position] is >= '0' and <= '9'; position++)
        {
            exponent = exponent * 10 + (text[position] - '0');
            digits++;
        }

        if (digits == 0)
        {
            return BigInteger.Zero;
        }

        return negative
            ? -exponent
            : exponent;
    }

    /// <summary>The length of the currency separator at the position, or of the number one before a currency symbol.</summary>
    private static int Separator(
        string text,
        int position,
        string currency,
        string number,
        bool currencySeen
    ) => Matches(text, position, currency) is var length and > 0
        ? length
        : currencySeen
            ? 0
            : Matches(text, position, number);

    /// <summary>
    /// The length of the negative sign at the position; the parser also takes a hyphen for a culture whose negative
    /// sign is another minus character.
    /// </summary>
    private static int MatchesNegativeSign(
        string text,
        int position,
        NumberFormatInfo format
    ) => Matches(text, position, format.NegativeSign) is var length and > 0
        ? length
        : position < text.Length
        && text[position] == '-'
        && format.NegativeSign is
        [
            (char)0x2012
            or (char)0x207B
            or (char)0x208B
            or (char)0x2212
            or (char)0x2796
            or (char)0xFE63
            or (char)0xFF0D,
        ]
            ? 1
            : 0;

    /// <summary>
    /// The length of the symbol at the position, or zero; like the parser, a space in the text stands for a no-break or
    /// narrow no-break space of the symbol.
    /// </summary>
    private static int Matches(
        string text,
        int position,
        string symbol
    )
    {
        if (symbol.Length == 0
            || position + symbol.Length > text.Length)
        {
            return 0;
        }

        for (var index = 0; index < symbol.Length; index++)
        {
            var actual = text[position + index];
            var expected = symbol[index];
            if (actual != expected
                && !(expected is (char)0x00A0 or (char)0x202F && actual == ' '))
            {
                return 0;
            }
        }

        return symbol.Length;
    }

    /// <summary>White space as the parser of .NET takes it: the space and the control characters tab to carriage return.</summary>
    private static bool IsParserWhite(
        char character
    ) => character is ' ' or >= (char)0x09 and <= (char)0x0D;

    /// <summary>
    /// Whether a written text the plain, currency, and percent forms do not cover reads back as the same binary
    /// floating point value with the culture's number parser; for these types the parser rounding to the same value is
    /// the contract.
    /// </summary>
    private static bool ReadsBackAsFloat<TValue>(
        TValue value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        int start
    )
        where TValue : INumberBase<TValue> => TValue.TryParse(output.Written(start),
            NumberStyles.Any,
            context.CultureFor(settings),
            out var parsed)
        && parsed == value;

    /// <summary>
    /// Appends the value with the export format and parses it back as plain, currency or percent text of the culture;
    /// <see langword="null" /> when the text is no number in any of them. The input profile of the field plays no
    /// part, since an export format is no template for reading.
    /// </summary>
    private static ExactDecimal? TryFormatted<TValue>(
        TValue value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        int start
    )
        where TValue : ISpanFormattable
    {
        var culture = context.CultureFor(settings);
        output.AppendFormatted(value, settings.ExportFormat, culture);
        var format = culture.NumberFormat;
        foreach (var style in s_writtenStyles)
        {
            if (DecimalText.TryParse(output.Written(start),
                format,
                context.SymbolsFor(format, style),
                style,
                out var parsed,
                out _))
            {
                return parsed.Value;
            }
        }

        return null;
    }

    private static bool Accepted(
        bool accepted,
        TextBuffer output,
        int start,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (accepted)
        {
            return true;
        }

        output.Truncate(start);
        failure = ConversionFailure.PrecisionLoss("The export format changes the value; rounding is required.");

        return false;
    }

    private static bool Equal(
        ExactDecimal left,
        ExactDecimal right
    )
    {
        var a = left.Normalize();
        var b = right.Normalize();

        return a.Mantissa == b.Mantissa && a.Exponent == b.Exponent && (a.Negative == b.Negative || a.Mantissa.IsZero);
    }

    /// <summary>A native Excel number; the value must keep at most 15 significant digits and round-trip.</summary>
    public static bool TryExcelNumber(
        double number,
        ExactDecimal exact,
        FieldSettings settings,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        cell = OutputCell.Empty;
        failure = default;

        // WHY: Excel worksheets do not hold IEEE subnormal numbers; values below the smallest worksheet number
        // become zero.
        // https://learn.microsoft.com/office/client-developer/excel/excel-worksheet-and-expression-evaluation
        if (number != 0
            && Math.Abs(number) < ExcelSmallestNumber)
        {
            failure = ConversionFailure.OutOfRange(
                "The value is below the smallest native Excel number; use ExcelAsText() or rounding.");

            return false;
        }

        if (exact.SignificantDigits > ExcelSignificantDigits
            || double.IsInfinity(number))
        {
            failure = ConversionFailure.PrecisionLoss(
                "The value has more than 15 significant digits and cannot be written as a native Excel number; use ExcelAsText() or rounding.");

            return false;
        }

        // WHY: Reading a native number back uses its shortest round-trip decimal text, so 0.1m survives as 0.1.
        if (!Equal(ShortestOf(number), exact))
        {
            failure = ConversionFailure.PrecisionLoss("The value cannot be stored exactly as a native Excel number.");

            return false;
        }

        cell = OutputCell.FromNumber(number, settings.ExcelNumberFormat);

        return true;
    }

    /// <summary>Shortest decimal expansion that round-trips the value in its own type.</summary>
    public static ExactDecimal ShortestOf<TFloat>(
        TFloat value
    )
        where TFloat : ISpanFormattable
    {
        Span<char> text = stackalloc char[32];
        value.TryFormat(text, out var written, "R", CultureInfo.InvariantCulture);
        return DecimalText.TryParseInvariant(text[..written], out var parsed)
            ? parsed.Value
            : throw new UnreachableException("The round-trip text of a finite double is a number.");
    }
}
