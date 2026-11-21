namespace Doka.Tabular;

internal sealed class TimeOnlyConverter : TimeConverterBase<TimeOnly>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.Time;

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out TimeOnly value,
        out ConversionFailure failure
    )
    {
        long ticks;
        ExactDecimal excess = default;
        if (settings.TimeFormat is not null)
        {
            if (FromCustomFormat(text, settings, context, Parse, out var custom, out failure) == ReadStatus.Failed)
            {
                value = default;

                return ReadStatus.Failed;
            }

            ticks = custom.Ticks;
        }
        else if (TimeText.TryMatchTime(text, out _, out _, out _, out _))
        {
            if (!TryIsoTime(text, settings.TimeRounding is not null, out ticks, out excess, out failure))
            {
                value = default;

                return ReadStatus.Failed;
            }
        }
        else
        {
            var culture = context.CultureFor(settings);
            if (!TryCulture(text,
                culture,
                [culture.DateTimeFormat.LongTimePattern, culture.DateTimeFormat.ShortTimePattern],
                Parse,
                out var local))
            {
                return Fail(Mismatch, out value, out failure);
            }

            ticks = local.Ticks;
        }

        return TryRoundTicks(ticks, excess, settings.TimeRounding, long.MaxValue, out var rounded, out failure)
            ? FromTicks(rounded, out value, out failure)
            : Fail(failure, out value, out failure);
    }

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    )
    {
        foreach (var problem in base.ValidateSettings(settings))
        {
            yield return problem;
        }

        if (settings.TimeRounding is { Step.Ticks: > 0 } rounding
            && TimeSpan.TicksPerDay % rounding.Step.Ticks != 0)
        {
            yield return "A time-of-day rounding step must divide a day without remainder.";
        }
    }

    protected override string? FormatProblem(
        TimeFormatSpec spec
    ) => spec switch
    {
        { UnsupportedStandard: true } => "This standard format does not describe a time of day.",
        { Hour: false } => "The format must contain the hour.",
        { Year: true } or { ShortYear: true } or { Month: true } or { Day: true }
            => "A time-of-day format contains no date.",
        { LacksDesignator: true } => "A 12-hour format needs the AM/PM designator tt.",
        _ => null,
    };

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out TimeOnly value,
        out ConversionFailure failure
    )
    {
        if (cell.Kind != CellKind.Number)
        {
            return base.TryReadNative(cell, settings, context, out value, out failure);
        }

        if (!IsDateFormatted(cell)
            && !settings.AllowExcelSerialNumbers)
        {
            return Fail(ConversionFailure.InvalidFormat("An unformatted number is not a time of day for this field."),
                out value,
                out failure);
        }

        if (cell.Number is < 0 or >= 1)
        {
            return Fail(ConversionFailure.PrecisionLoss("The cell contains a date part, which TimeOnly cannot hold."),
                out value,
                out failure);
        }

        return ExcelDates.TryTicks(cell.Number, settings.TimeRounding, out var ticks)
            ? FromTicks(ticks, out value, out failure)
            : Fail(ConversionFailure.OutOfRange("The value is not a valid time of day."), out value, out failure);
    }

    protected override bool TryWriteText(
        TimeOnly value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        if (!TryRound(value, settings, out var typed, out failure))
        {
            return false;
        }

        if (settings.TimeFormat is not null)
        {
            return TryWriteCheckedCustomText(typed, settings, context, Parse, output, out failure);
        }

        output.AppendFormatted(typed, "HH:mm:ss", CultureInfo.InvariantCulture);
        TimeText.WriteFraction(output, typed.Ticks % TimeSpan.TicksPerSecond);

        return true;
    }

    public override bool TryWriteExcel(
        TimeOnly value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        cell = OutputCell.Empty;
        if (!TryRound(value, settings, out var typed, out failure))
        {
            return false;
        }

        var serial = (double)typed.Ticks / TimeSpan.TicksPerDay;
        var format = typed.Ticks % TimeSpan.TicksPerSecond == 0
            ? "hh:mm:ss"
            : "hh:mm:ss.000";

        var numberFormat = settings.ExcelNumberFormat ?? format;

        return TryExcelSerial(serial,
            numberFormat,
            TryReadBackNative(serial, numberFormat, settings, context, out var back) && back == typed,
            out cell,
            out failure);
    }

    private static bool TryRound(
        TimeOnly value,
        FieldSettings settings,
        out TimeOnly rounded,
        out ConversionFailure failure
    )
    {
        failure = default;
        rounded = value;
        if (!ExcelDates.TryRoundTicks(value.Ticks, settings.TimeRounding, 0, TimeSpan.TicksPerDay - 1, out var ticks))
        {
            failure = ConversionFailure.OutOfRange("Rounding would move the time of day past midnight.");

            return false;
        }

        rounded = new TimeOnly(ticks);

        return true;
    }

    private static ReadStatus FromTicks(
        long ticks,
        out TimeOnly value,
        out ConversionFailure failure
    ) => ticks is >= 0 and < TimeSpan.TicksPerDay
        ? Read(new TimeOnly(ticks), out value, out failure)
        : Fail(ConversionFailure.OutOfRange("Rounding moves the time of day past midnight."), out value, out failure);

    private static bool Parse(
        ReadOnlySpan<char> text,
        ReadOnlySpan<char> format,
        CultureInfo culture,
        out TimeOnly value
    ) => TimeOnly.TryParseExact(text, format, culture, DateTimeStyles.None, out value);

    protected override IEnumerable<TimeOnly> Probes(
        Calendar calendar
    )
    {
        // WHY: The hours cover AM and PM; a time of day needs no calendar.
        yield return new TimeOnly(1, 0);
        yield return new TimeOnly(13, 0);
    }
}
