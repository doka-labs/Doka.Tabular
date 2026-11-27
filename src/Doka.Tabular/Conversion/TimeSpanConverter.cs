using System.Numerics;

namespace Doka.Tabular;

internal sealed class TimeSpanConverter : TimeConverterBase<TimeSpan>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.Duration;

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out TimeSpan value,
        out ConversionFailure failure
    )
    {
        if (settings.TimeFormat is not null)
        {
            var status = FromCustomFormat(text, settings, context, Parse, out value, out failure);

            return status == ReadStatus.Value
                ? Rounded(value.Ticks, default, settings, out value, out failure)
                : status;
        }

        if (!text.Contains(':'))
        {
            return Fail(
                ConversionFailure.InvalidFormat(
                    "A duration needs at least hours and minutes; a bare number has no unit."),
                out value,
                out failure);
        }

        // WHY: .NET rounds fraction digits beyond the seventh to whole ticks itself; they are cut off here and
        // either rejected or rounded once together with the value to the step of the field.
        var culture = context.CultureFor(settings);

        // WHY: The .NET parsers ignore surrounding white space, also when the field does not trim.
        var core = text.Trim();
        var digits = FractionDigits(core, culture);
        var excess = default(ExactDecimal);
        if (digits.Length > 7)
        {
            if (!TimeText.TryFraction(
                digits,
                settings.TimeRounding is not null,
                out _,
                out excess,
                out var needsRounding))
            {
                return Fail(
                    needsRounding
                        ? ConversionFailure.PrecisionLoss("More than seven fraction digits require rounding.")
                        : Mismatch,
                    out value,
                    out failure);
            }

            text = core[..(core.Length - digits.Length + 7)];
        }

        if (TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var parsed)
            || TimeSpan.TryParseExact(text, "g", culture, out parsed)
            || TimeSpan.TryParseExact(text, "G", culture, out parsed))
        {
            return Rounded(
                parsed.Ticks,
                excess.IsZero
                    ? default
                    : excess with
                    {
                        Negative = text
                            .TrimStart()
                            .StartsWith('-'),
                    },
                settings,
                out value,
                out failure);
        }

        return Fail(Mismatch, out value, out failure);
    }

    /// <summary>The fraction digits of the seconds at the end of a duration text; empty without a fraction.</summary>
    private static ReadOnlySpan<char> FractionDigits(
        ReadOnlySpan<char> text,
        CultureInfo culture
    )
    {
        var start = text.LastIndexOfAnyExceptInRange('0', '9') + 1;
        if (start <= 1
            || text.Length == start)
        {
            return [];
        }

        var separator = text[start - 1];

        return separator == '.'
            || culture
                .NumberFormat
                .NumberDecimalSeparator
                .AsSpan()
                .SequenceEqual([separator])
                ? text[start..]
                : [];
    }

    /// <summary>
    /// The duration rounded once to the step of the field; <paramref name="excess" /> is a signed part of one tick
    /// beyond the seventh fraction digit. Leaving the range is an error.
    /// </summary>
    private static ReadStatus Rounded(
        long ticks,
        ExactDecimal excess,
        FieldSettings settings,
        out TimeSpan value,
        out ConversionFailure failure
    )
    {
        if (settings.TimeRounding is not { } rounding)
        {
            return Read(new TimeSpan(ticks), out value, out failure);
        }

        // WHY: The text carries the sign for the whole duration, also when its whole ticks are zero.
        var negative = ticks < 0 || excess.Negative;
        var scale = BigInteger.Pow(10, -excess.Exponent);
        var exact = new ExactDecimal(negative, BigInteger.Abs(ticks) * scale + excess.Mantissa, excess.Exponent);
        if (ExcelDates.IsOutside(exact, TimeSpan.MinValue.Ticks, TimeSpan.MaxValue.Ticks))
        {
            return Fail(
                ConversionFailure.OutOfRange("The duration is outside the supported range."),
                out value,
                out failure);
        }

        var step = rounding.Step.Ticks;
        var rounded = ExcelDates
                .DivideExact(exact, step)
                .RoundToDecimals(0, rounding.Mode)
                .ToBigIntegerExact()
            * step;

        return rounded > TimeSpan.MaxValue.Ticks || rounded < TimeSpan.MinValue.Ticks
            ? Fail(ConversionFailure.OutOfRange("The duration is outside the supported range."), out value, out failure)
            : Read(new TimeSpan((long)rounded), out value, out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out TimeSpan value,
        out ConversionFailure failure
    )
    {
        if (cell.Kind != CellKind.Number
            || !double.IsFinite(cell.Number))
        {
            return base.TryReadNative(cell, settings, context, out value, out failure);
        }

        var exact = ExactDecimal.FromDouble(cell.Number);
        var factor = TicksPerUnit(settings.ExcelDurationUnit);
        var scaled = exact with { Mantissa = exact.Mantissa * factor };
        var rounding = settings.TimeRounding;

        // WHY: The range is checked on the exact value; rounding must not turn a duration beyond it into a valid one.
        if (rounding is not null
            && ExcelDates.IsOutside(scaled, TimeSpan.MinValue.Ticks, TimeSpan.MaxValue.Ticks))
        {
            return Fail(
                ConversionFailure.OutOfRange("The duration is outside the supported range."),
                out value,
                out failure);
        }

        BigInteger ticks;
        if (rounding is null)
        {
            ticks = scaled
                .RoundToDecimals(0, MidpointRounding.ToEven)
                .ToBigIntegerExact();
        }
        else
        {
            // WHY: The exact value is rounded once to the step; rounding to whole ticks first would round twice, and
            // limiting to the tick range before the range check would hide an overflow.
            var step = rounding.Step.Ticks;
            ticks = ExcelDates
                    .DivideExact(scaled, step)
                    .RoundToDecimals(0, rounding.Mode)
                    .ToBigIntegerExact()
                * step;
        }

        return ticks > TimeSpan.MaxValue.Ticks || ticks < TimeSpan.MinValue.Ticks
            ? Fail(ConversionFailure.OutOfRange("The duration is outside the supported range."), out value, out failure)
            : Read(new TimeSpan((long)ticks), out value, out failure);
    }

    /// <summary>Rounds a duration for output; a result beyond the range of TimeSpan is a range error.</summary>
    private static bool TryRoundForOutput(
        TimeSpan value,
        FieldSettings settings,
        out TimeSpan rounded,
        out ConversionFailure failure
    )
    {
        failure = default;
        rounded = value;
        if (ExcelDates.TryRoundTicks(
            value.Ticks,
            settings.TimeRounding,
            TimeSpan.MinValue.Ticks,
            TimeSpan.MaxValue.Ticks,
            out var ticks))
        {
            rounded = new TimeSpan(ticks);

            return true;
        }

        failure = ConversionFailure.OutOfRange("Rounding exceeds the range of TimeSpan.");

        return false;
    }

    protected override bool TryWriteText(
        TimeSpan value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        if (!TryRoundForOutput(value, settings, out var typed, out failure))
        {
            return false;
        }

        if (settings.TimeFormat is not null)
        {
            return TryWriteCheckedCustomText(typed, settings, context, Parse, output, out failure);
        }

        output.AppendFormatted(typed, "c", CultureInfo.InvariantCulture);

        return true;
    }

    public override bool TryWriteExcel(
        TimeSpan value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        cell = OutputCell.Empty;
        if (!TryRoundForOutput(value, settings, out var typed, out failure))
        {
            return false;
        }

        var unit = settings.ExcelDurationUnit;
        var factor = TicksPerUnit(unit);
        var number = (double)typed.Ticks / factor;
        var defaultFormat = unit switch
        {
            ExcelDurationUnit.Days when typed.Ticks % TimeSpan.TicksPerSecond == 0 => "[h]:mm:ss",
            ExcelDurationUnit.Days => "[h]:mm:ss.000",
            ExcelDurationUnit.Hours
                or ExcelDurationUnit.Minutes
                or ExcelDurationUnit.Seconds
                or ExcelDurationUnit.Milliseconds
                or ExcelDurationUnit.Ticks => null,
            _ => throw new UnreachableException($"The duration unit {unit} is not defined."),
        };

        // WHY: Excel shows no negative time in the 1900 date system, whether the date or time format is the default
        // [h]:mm:ss or set explicitly; a numeric section for negative values shows it.
        var format = settings.ExcelNumberFormat ?? defaultFormat;
        if (typed < TimeSpan.Zero
            && format is not null
            && !NumberFormats.ShowsNegative(format))
        {
            failure = ConversionFailure.OutOfRange(
                $"The display format {format} cannot show a negative duration; use a numeric unit such as hours or a numeric format.");

            return false;
        }

        if (!TryReadBackNative(number, format, settings, context, out var back)
            || back != typed)
        {
            failure = ConversionFailure.PrecisionLoss(
                "The duration cannot be stored exactly as a native Excel number.");

            return false;
        }

        failure = default;
        cell = OutputCell.FromNumber(number, format);

        return true;
    }

    private static long TicksPerUnit(
        ExcelDurationUnit unit
    ) => unit switch
    {
        ExcelDurationUnit.Hours => TimeSpan.TicksPerHour,
        ExcelDurationUnit.Minutes => TimeSpan.TicksPerMinute,
        ExcelDurationUnit.Seconds => TimeSpan.TicksPerSecond,
        ExcelDurationUnit.Milliseconds => TimeSpan.TicksPerMillisecond,
        ExcelDurationUnit.Ticks => 1,
        _ => TimeSpan.TicksPerDay,
    };

    private static bool Parse(
        ReadOnlySpan<char> text,
        ReadOnlySpan<char> format,
        CultureInfo culture,
        out TimeSpan value
    ) => TimeSpan.TryParseExact(text, format, culture, out value);

    protected override IEnumerable<TimeSpan> Probes(
        Calendar calendar
    )
    {
        // WHY: A duration needs no calendar; one positive value covers its custom formats.
        yield return new TimeSpan(1, 1, 1, 1);
    }
}
