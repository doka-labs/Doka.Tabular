using System.Numerics;

namespace Doka.Tabular;

internal sealed class FloatingConverter<TFloat> : ValueConverter<TFloat>
    where TFloat : struct, IFloatingPointIeee754<TFloat>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.Decimal;

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    ) => NumberRules.CommonProblems<TFloat>(settings, isInteger: false, isFloating: true);

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out TFloat value,
        out ConversionFailure failure
    )
    {
        if (settings.AllowNonFiniteValues
            && NonFinite(text) is { } special)
        {
            return Read(special, out value, out failure);
        }

        if (!NumberRules.TryParse(text, settings, context, exponentByDefault: true, out var parsed, out failure))
        {
            value = default;

            return ReadStatus.Failed;
        }

        var nearest = parsed.IsSmall
            ? Parse(parsed)
            : parsed.Value.ToFloat<TFloat>();

        var sourceIsZero = parsed.IsSmall
            ? parsed.Small == 0
            : parsed.Value.IsZero;

        // WHY: A non-zero text whose binary value is already zero underflowed; rounding must not hide that.
        if (settings.NumericRounding is null
            || !TFloat.IsFinite(nearest)
            || (TFloat.IsZero(nearest) && !sourceIsZero))
        {
            return Checked(nearest, sourceIsZero, out value, out failure);
        }

        // WHY: Rounding starts at the binary value the text denotes, as on export; 2.675 is slightly below 2.675.
        var rounded = NumberRules.ApplyRounding(
            ExactDecimal.FromDouble(double.CreateChecked(nearest)),
            settings.NumericRounding);

        return Checked(rounded.ToFloat<TFloat>(), rounded.IsZero, out value, out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out TFloat value,
        out ConversionFailure failure
    )
    {
        if (cell.Kind != CellKind.Number
            || !double.IsFinite(cell.Number))
        {
            return base.TryReadNative(cell, settings, context, out value, out failure);
        }

        // WHY: The nearest value of the exact expansion of a double is the correctly rounded conversion itself. A
        // number that overflows or underflows in the target type is out of range; rounding must not hide that.
        var nearest = TFloat.CreateChecked(cell.Number);
        if (settings.NumericRounding is null
            || !TFloat.IsFinite(nearest)
            || (TFloat.IsZero(nearest) && cell.Number != 0))
        {
            return Checked(nearest, cell.Number == 0, out value, out failure);
        }

        var rounded = NumberRules.ApplyRounding(ExactDecimal.FromDouble(cell.Number), settings.NumericRounding);

        return Checked(rounded.ToFloat<TFloat>(), rounded.IsZero, out value, out failure);
    }

    protected override bool TryWriteText(
        TFloat value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (!TFloat.IsFinite(value))
        {
            return TryWriteNonFinite(value, settings, output, out failure);
        }

        if (!TryRound(value, settings, out var typed, out failure))
        {
            return false;
        }

        if (settings.ExportFormat is not null)
        {
            return NumberRules.TryWriteFloatWithExportFormat(typed, settings, context, output, out failure);
        }

        if (settings.TextStyle == NumberTextStyle.Plain)
        {
            output.AppendFormatted(typed, "R", context.CultureFor(settings));

            return true;
        }

        NumberRules.WritePlain(NumberRules.ShortestOf(typed), settings, context, output);

        return true;
    }

    public override bool TryWriteExcel(
        TFloat value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        // WHY: A column has exactly one cell type; with NonFiniteValues it is text, also for finite values, so NaN
        // and Infinity can stand next to them.
        if (settings.AllowNonFiniteValues
            || !TFloat.IsFinite(value))
        {
            return TryWriteCell(value, settings, context, output, out cell, out failure);
        }

        // WHY: A float or Half is written as the double of its own shortest text, 0.1f as 0.1, which reads back to the
        // same float; the double of its binary value would need 17 digits.
        if (!TryRound(value, settings, out var typed, out failure))
        {
            cell = OutputCell.Empty;

            return false;
        }

        var exact = NumberRules.ShortestOf(typed);
        var number = typeof(TFloat) == typeof(double)
            ? double.CreateChecked(typed)
            : exact.ToFloat<double>();

        if (TFloat.CreateChecked(number) != typed)
        {
            cell = OutputCell.Empty;
            failure = ConversionFailure.PrecisionLoss("The value cannot be stored exactly as a native Excel number.");

            return false;
        }

        return NumberRules.TryExcelNumber(number, exact, settings, out cell, out failure);
    }

    private static TFloat? NonFinite(
        ReadOnlySpan<char> text
    )
    {
        if (text.Equals("NaN", StringComparison.OrdinalIgnoreCase))
        {
            return TFloat.NaN;
        }

        if (text.Equals("Infinity", StringComparison.OrdinalIgnoreCase))
        {
            return TFloat.PositiveInfinity;
        }

        return text.Equals("-Infinity", StringComparison.OrdinalIgnoreCase)
            ? TFloat.NegativeInfinity
            : null;
    }

    private static bool TryWriteNonFinite(
        TFloat value,
        FieldSettings settings,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (!settings.AllowNonFiniteValues)
        {
            failure = ConversionFailure.Unsupported("NaN and infinity are not allowed for this field.");

            return false;
        }

        if (TFloat.IsNaN(value))
        {
            output.Append("NaN");
        }
        else
        {
            output.Append(
                TFloat.IsPositiveInfinity(value)
                    ? "Infinity"
                    : "-Infinity");
        }

        return true;
    }

    /// <summary>Parses the invariant scientific form of a small mantissa without an intermediate string.</summary>
    private static TFloat Parse(
        in ParsedNumberText parsed
    )
    {
        Span<char> text = stackalloc char[40];
        var written = 0;
        if (parsed.Negative)
        {
            text[written++] = '-';
        }

        parsed.Small.TryFormat(text[written..], out var digits, default, CultureInfo.InvariantCulture);
        written += digits;
        text[written++] = 'E';
        parsed.Exponent.TryFormat(text[written..], out var exponentDigits, default, CultureInfo.InvariantCulture);
        written += exponentDigits;

        return TFloat.Parse(text[..written], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Rounds a finite value for output; a result beyond the range of the type is a range error.</summary>
    private static bool TryRound(
        TFloat value,
        FieldSettings settings,
        out TFloat rounded,
        out ConversionFailure failure
    )
    {
        failure = default;

        // WHY: Negative zero becomes zero in both directions, so it is never written as -0.
        rounded = TFloat.IsZero(value)
            ? TFloat.Zero
            : value;

        if (settings.NumericRounding is null)
        {
            return true;
        }

        rounded = NumberRules
            .ApplyRounding(ExactDecimal.FromDouble(double.CreateChecked(value)), settings.NumericRounding)
            .ToFloat<TFloat>();

        if (TFloat.IsFinite(rounded))
        {
            return true;
        }

        failure = ConversionFailure.OutOfRange("Rounding exceeds the range of the type.");

        return false;
    }

    private static ReadStatus Checked(
        TFloat result,
        bool sourceIsZero,
        out TFloat value,
        out ConversionFailure failure
    )
    {
        if (TFloat.IsInfinity(result))
        {
            return Fail(
                ConversionFailure.OutOfRange("The value is outside the range of the target type."),
                out value,
                out failure);
        }

        if (TFloat.IsZero(result)
            && !sourceIsZero)
        {
            return Fail(
                ConversionFailure.OutOfRange("The value is too small for the target type and would become zero."),
                out value,
                out failure);
        }

        return Read(
            TFloat.IsZero(result)
                ? TFloat.Zero
                : result,
            out value,
            out failure);
    }
}
