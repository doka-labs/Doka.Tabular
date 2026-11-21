namespace Doka.Tabular;

internal sealed class DateOnlyConverter : TimeConverterBase<DateOnly>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.Date;

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    )
    {
        foreach (var problem in base.ValidateSettings(settings))
        {
            yield return problem;
        }

        if (settings.TimeRounding is { Step.Ticks: > 0 } rounding
            && rounding.Step.Ticks % TimeSpan.TicksPerDay != 0)
        {
            yield return "A date rounding step must be a whole number of days.";
        }
    }

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out DateOnly value,
        out ConversionFailure failure
    )
    {
        if (settings.TimeFormat is not null)
        {
            var status = FromCustomFormat(text, settings, context, Parse, out value, out failure);

            return status == ReadStatus.Value
                ? Rounded(value, settings, out value, out failure)
                : status;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return Rounded(iso, settings, out value, out failure);
        }

        var culture = context.CultureFor(settings);

        // WHY: A short date pattern without year, month, and day would take the missing parts from the clock.
        return CulturePatterns.For(culture.DateTimeFormat)
                .DateIsComplete
            && TryCulture(text, culture, [culture.DateTimeFormat.ShortDatePattern], Parse, out var local)
                ? Rounded(local, settings, out value, out failure)
                : Fail(Mismatch, out value, out failure);
    }

    /// <summary>Rounds a date to the step of the field, counted in days from the calendar origin.</summary>
    private static ReadStatus Rounded(
        DateOnly date,
        FieldSettings settings,
        out DateOnly value,
        out ConversionFailure failure
    ) => TryRound(date, settings, out value, out failure)
        ? ReadStatus.Value
        : ReadStatus.Failed;

    private static bool TryRound(
        DateOnly date,
        FieldSettings settings,
        out DateOnly rounded,
        out ConversionFailure failure
    )
    {
        rounded = date;
        if (!TryRoundTicks(date.DayNumber * TimeSpan.TicksPerDay,
            default,
            settings.TimeRounding,
            DateOnly.MaxValue.DayNumber * TimeSpan.TicksPerDay,
            out var ticks,
            out failure))
        {
            return false;
        }

        rounded = DateOnly.FromDayNumber((int)(ticks / TimeSpan.TicksPerDay));

        return true;
    }

    protected override string? FormatProblem(
        TimeFormatSpec spec
    ) => spec switch
    {
        { UnsupportedStandard: true } => "This standard format does not describe a complete date.",
        { HasDate: false } => "The format must contain year, month, and day; nothing is taken from the clock.",
        _ => null,
    };

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out DateOnly value,
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
            return Fail(ConversionFailure.InvalidFormat("An unformatted number is not a date for this field."),
                out value,
                out failure);
        }

        if (!ExcelDates.TryToDateTime(cell.Number, context.Uses1904DateSystem, null, out var date))
        {
            return Fail(ConversionFailure.OutOfRange("The serial number is not a valid Excel date."),
                out value,
                out failure);
        }

        return date.TimeOfDay != TimeSpan.Zero
            ? Fail(ConversionFailure.PrecisionLoss("The cell contains a time of day, which DateOnly cannot hold."),
                out value,
                out failure)
            : Rounded(DateOnly.FromDateTime(date), settings, out value, out failure);
    }

    protected override bool TryWriteText(
        DateOnly value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        if (!TryRound(value, settings, out value, out failure))
        {
            return false;
        }

        if (settings.TimeFormat is not null)
        {
            return TryWriteCheckedCustomText(value, settings, context, Parse, output, out failure);
        }

        output.AppendFormatted(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return true;
    }

    public override bool TryWriteExcel(
        DateOnly value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        if (!TryRound(value, settings, out value, out failure))
        {
            cell = OutputCell.Empty;

            return false;
        }

        var date = value.ToDateTime(TimeOnly.MinValue);
        if (date < new DateTime(1900, 1, 1))
        {
            cell = OutputCell.Empty;
            failure = ConversionFailure.OutOfRange(
                "Dates before 1900-01-01 cannot be native Excel dates; use a text column.");

            return false;
        }

        var serial = ExcelDates.ToSerial(date);
        var format = settings.ExcelNumberFormat ?? "yyyy-mm-dd";

        return TryExcelSerial(serial,
            format,
            TryReadBackNative(serial, format, settings, context, out var back) && back == value,
            out cell,
            out failure);
    }

    private static bool Parse(
        ReadOnlySpan<char> text,
        ReadOnlySpan<char> format,
        CultureInfo culture,
        out DateOnly value
    ) => DateOnly.TryParseExact(text, format, culture, DateTimeStyles.None, out value);

    protected override IEnumerable<DateOnly> Probes(
        Calendar calendar
    )
    {
        foreach (var date in ProbeDates(calendar))
        {
            yield return DateOnly.FromDateTime(date);
        }
    }
}
