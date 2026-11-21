using System.Numerics;

namespace Doka.Tabular;

internal sealed class DecimalConverter : ValueConverter<decimal>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.Decimal;

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    ) => NumberRules.CommonProblems<decimal>(settings, isInteger: false, isFloating: false);

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out decimal value,
        out ConversionFailure failure
    )
    {
        if (!NumberRules.TryParse(text, settings, context, exponentByDefault: false, out var parsed, out failure))
        {
            value = 0;

            return ReadStatus.Failed;
        }

        return FromParsed(parsed, settings, out value, out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out decimal value,
        out ConversionFailure failure
    )
    {
        if (cell.Kind != CellKind.Number
            || !double.IsFinite(cell.Number))
        {
            return base.TryReadNative(cell, settings, context, out value, out failure);
        }

        Span<char> text = stackalloc char[32];
        cell.Number.TryFormat(text, out var written, "R", CultureInfo.InvariantCulture);
        return DecimalText.TryParseInvariant(text[..written], out var parsed)
            ? FromParsed(parsed, settings, out value, out failure)
            : throw new UnreachableException("The round-trip text of a finite double is a number.");
    }

    protected override bool TryWriteText(
        decimal value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (settings is { NumericRounding: null, ExportFormat: null, TextStyle: NumberTextStyle.Plain })
        {
            WriteNormalized(value, context.CultureFor(settings), output);

            return true;
        }

        if (!TryRound(value, settings, out var typed, out failure))
        {
            return false;
        }

        var exact = Exact(typed);
        if (settings.ExportFormat is null)
        {
            NumberRules.WritePlain(exact, settings, context, output);

            return true;
        }

        return NumberRules.TryWriteWithExportFormat(typed, exact, settings, context, output, out failure);
    }

    public override bool TryWriteExcel(
        decimal value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        if (!TryRound(value, settings, out var typed, out failure))
        {
            cell = OutputCell.Empty;

            return false;
        }

        return NumberRules.TryExcelNumber((double)typed, Exact(typed), settings, out cell, out failure);
    }

    private static ExactDecimal Exact(
        decimal value
    )
    {
        var (negative, mantissa, scale) = Parts(value);

        return new ExactDecimal(negative && mantissa != 0, mantissa, -scale);
    }

    /// <summary>Writes the value without trailing fraction zeros, as the exact path normalizes it.</summary>
    private static void WriteNormalized(
        decimal value,
        CultureInfo culture,
        TextBuffer output
    )
    {
        var (negative, mantissa, scale) = Parts(value);
        while (scale > 0
            && mantissa % 10 == 0)
        {
            mantissa /= 10;
            scale--;
        }

        var format = culture.NumberFormat;
        if (negative && mantissa != 0)
        {
            output.Append(format.NegativeSign);
        }

        Span<char> digits = stackalloc char[40];
        mantissa.TryFormat(digits, out var written, default, CultureInfo.InvariantCulture);
        ExactDecimal.WritePlainDigits(output, digits[..written], -scale, format.NumberDecimalSeparator);
    }

    private static (bool Negative, UInt128 Mantissa, int Scale) Parts(
        decimal value
    )
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var mantissa = ((UInt128)(uint)bits[2] << 64) | ((UInt128)(uint)bits[1] << 32) | (uint)bits[0];

        return (bits[3] < 0, mantissa, (bits[3] >> 16) & 0xFF);
    }

    /// <summary>The rounded value; a result outside the range of decimal is an error, never the unrounded value.</summary>
    private static bool TryRound(
        decimal value,
        FieldSettings settings,
        out decimal rounded,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (TryToDecimal(NumberRules.ApplyRounding(Exact(value), settings.NumericRounding), out rounded))
        {
            return true;
        }

        failure = ConversionFailure.OutOfRange("The rounded value is outside the range of decimal.");

        return false;
    }

    private static ReadStatus FromParsed(
        in ParsedNumberText parsed,
        FieldSettings settings,
        out decimal value,
        out ConversionFailure failure
    )
    {
        if (settings.NumericRounding is null
            && parsed.IsSmall
            && TrySmall(parsed.Negative, parsed.Small, parsed.Exponent, out value))
        {
            failure = default;

            return ReadStatus.Value;
        }

        return FromExact(parsed.Value, settings, out value, out failure);
    }

    /// <summary>The normalized decimal of a mantissa below 2^64; <see langword="false" /> leaves it to the exact path.</summary>
    private static bool TrySmall(
        bool negative,
        ulong mantissa,
        int exponent,
        out decimal value
    )
    {
        value = 0;
        while (mantissa != 0
            && mantissa % 10 == 0)
        {
            mantissa /= 10;
            exponent++;
        }

        if (mantissa == 0)
        {
            return true;
        }

        if (exponent > 0)
        {
            return TryWhole(negative, mantissa, exponent, out value);
        }

        if (-exponent > 28)
        {
            return false;
        }

        value = new decimal((int)(uint)mantissa, (int)(uint)(mantissa >> 32), 0, negative, (byte)-exponent);

        return true;
    }

    /// <summary>A whole number of a mantissa times a power of ten; <see langword="false" /> beyond 96 bits.</summary>
    private static bool TryWhole(
        bool negative,
        ulong mantissa,
        int exponent,
        out decimal value
    )
    {
        value = 0;
        var maximum = ((UInt128)1 << 96) - 1;
        UInt128 scaled = mantissa;
        for (var step = 0; step < exponent; step++)
        {
            scaled *= 10;
            if (scaled > maximum)
            {
                return false;
            }
        }

        value = new decimal((int)(uint)scaled, (int)(uint)(scaled >> 32), (int)(uint)(scaled >> 64), negative, 0);

        return true;
    }

    private static ReadStatus FromExact(
        ExactDecimal exact,
        FieldSettings settings,
        out decimal value,
        out ConversionFailure failure
    )
    {
        // WHY: Rounding must not bring a value back that lies outside the range of decimal already.
        if (ExceedsMaximum(exact.Normalize()))
        {
            value = 0;
            failure = ConversionFailure.OutOfRange("The value is outside the range of decimal.");

            return ReadStatus.Failed;
        }

        var rounded = NumberRules
            .ApplyRounding(exact, settings.NumericRounding)
            .Normalize();

        if (TryToDecimal(rounded, out value))
        {
            failure = default;

            return ReadStatus.Value;
        }

        failure = rounded.Exponent > 0 || ExactDecimal.DigitCount(rounded.Mantissa) + rounded.Exponent > 29
            ? ConversionFailure.OutOfRange("The value is outside the range of decimal.")
            : ConversionFailure.PrecisionLoss(
                "The value has more digits than decimal can represent; rounding is required.");

        return ReadStatus.Failed;
    }

    /// <summary>Whether the magnitude of a normalized value is above <see cref="decimal.MaxValue" />.</summary>
    private static bool ExceedsMaximum(
        ExactDecimal value
    )
    {
        if (value.Mantissa.IsZero)
        {
            return false;
        }

        var integerDigits = ExactDecimal.DigitCount(value.Mantissa) + value.Exponent;
        if (integerDigits != 29)
        {
            return integerDigits > 29;
        }

        var maximum = new BigInteger(decimal.MaxValue);

        return value.Exponent >= 0
            ? value.Mantissa * BigInteger.Pow(10, value.Exponent) > maximum
            : value.Mantissa > maximum * BigInteger.Pow(10, -value.Exponent);
    }

    private static bool TryToDecimal(
        ExactDecimal value,
        out decimal result
    )
    {
        result = 0;
        var normalized = value.Normalize();
        var mantissa = normalized.Mantissa;
        var exponent = normalized.Exponent;
        if (exponent > 0)
        {
            mantissa *= BigInteger.Pow(10, exponent);
            exponent = 0;
        }

        var scale = -exponent;
        if (scale > 28)
        {
            return false;
        }

        if (mantissa > new BigInteger(decimal.MaxValue))
        {
            return false;
        }

        var low = (int)(uint)(mantissa & uint.MaxValue);
        var mid = (int)(uint)((mantissa >> 32) & uint.MaxValue);
        var high = (int)(uint)((mantissa >> 64) & uint.MaxValue);
        result = new decimal(low, mid, high, normalized.Negative && !mantissa.IsZero, (byte)scale);

        return true;
    }
}
