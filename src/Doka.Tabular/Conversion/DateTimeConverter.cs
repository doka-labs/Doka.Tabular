namespace Doka.Tabular;

internal sealed class DateTimeConverter : TimestampConverterBase<DateTime>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.DateTime;

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out DateTime value,
        out ConversionFailure failure
    )
    {
        LocalTimestamp timestamp;
        if (settings.TimeFormat is not null)
        {
            if (!TryCustomTimestamp(text, settings, context, out timestamp, out failure))
            {
                value = default;

                return ReadStatus.Failed;
            }
        }
        else if (!TryParseIso(text, settings.TimeRounding is not null, out timestamp, out failure))
        {
            if (failure.Code != TabularErrorCodes.Conversion.InvalidFormat
                || !TryParseCulture(text, context.CultureFor(settings), out var local))
            {
                value = default;

                return ReadStatus.Failed;
            }

            timestamp = new LocalTimestamp(local, null, default);
        }

        return ResolveAndRound(timestamp.Local, timestamp.Offset, timestamp.Excess, settings, out value, out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out DateTime value,
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
            return Fail(
                ConversionFailure.InvalidFormat("An unformatted number is not a date for this field."),
                out value,
                out failure);
        }

        // WHY: A local time of a file zone is an instant; it is rounded once on the UTC axis after the zone is
        // applied, from the exact value of the cell and not from a tick rounded before.
        if (settings is { FileTimeZone: not null, ExcelAsUtc: false, TimeRounding: not null })
        {
            return ExcelDates.TryToExactDateTime(cell.Number, context.Uses1904DateSystem, out var exact, out var excess)
                ? ResolveAndRound(exact, null, excess, settings, out value, out failure)
                : Fail(
                    ConversionFailure.OutOfRange("The serial number is not a valid Excel date."),
                    out value,
                    out failure);
        }

        if (!ExcelDates.TryToDateTime(cell.Number, context.Uses1904DateSystem, settings.TimeRounding, out var local))
        {
            return Fail(
                ConversionFailure.OutOfRange("The serial number is not a valid Excel date."),
                out value,
                out failure);
        }

        if (settings.ExcelAsUtc)
        {
            return Read(DateTime.SpecifyKind(local, DateTimeKind.Utc), out value, out failure);
        }

        return Resolve(local, null, settings, out var resolved, out failure) == ReadStatus.Failed
            ? Fail(failure, out value, out failure)
            : Read(resolved, out value, out failure);
    }

    /// <summary>Applies the zone or offset, then rounds the resolved value once together with the exact rest of a tick.</summary>
    private static ReadStatus ResolveAndRound(
        DateTime local,
        TimeSpan? offset,
        ExactDecimal excess,
        FieldSettings settings,
        out DateTime value,
        out ConversionFailure failure
    )
    {
        if (Resolve(local, offset, settings, out var resolved, out failure) == ReadStatus.Failed)
        {
            value = default;

            return ReadStatus.Failed;
        }

        // WHY: The resolved value is rounded once: a UTC value on the UTC axis, a zoneless one at the calendar origin.
        return TryRoundTicks(
            resolved.Ticks,
            excess,
            settings.TimeRounding,
            DateTime.MaxValue.Ticks,
            out var ticks,
            out failure)
            ? Read(new DateTime(ticks, resolved.Kind), out value, out failure)
            : Fail(failure, out value, out failure);
    }

    protected override bool TryWriteText(
        DateTime value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        if (!TryForOutput(value, settings, utcColumn: false, out var typed, out _, out failure))
        {
            return false;
        }

        if (settings.TimeFormat is not null)
        {
            return TryWriteChecked(typed, settings, context, output, out failure);
        }

        output.AppendFormatted(typed, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        TimeText.WriteFraction(output, typed.Ticks % TimeSpan.TicksPerSecond);
        if (typed.Kind == DateTimeKind.Utc)
        {
            output.Append('Z');
        }

        return true;
    }

    public override bool TryWriteExcel(
        DateTime value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        cell = OutputCell.Empty;
        if (!TryForOutput(
            value,
            settings,
            utcColumn: settings.ExcelAsUtc,
            out var typed,
            out var expected,
            out failure))
        {
            return false;
        }

        if (!settings.ExcelAsUtc
            && typed.Kind == DateTimeKind.Utc)
        {
            failure = ConversionFailure.InvalidFormat(
                "A zoneless Excel column does not accept UTC values; use ExcelAsUtc().");

            return false;
        }

        if (typed < new DateTime(1900, 1, 1))
        {
            failure = ConversionFailure.OutOfRange(
                "Dates before 1900-01-01 cannot be native Excel dates; use a text column.");

            return false;
        }

        var serial = ExcelDates.ToSerial(DateTime.SpecifyKind(typed, DateTimeKind.Unspecified));
        var format = settings.ExcelNumberFormat ?? DefaultFormat(typed, settings.ExcelAsUtc);

        return TryExcelSerial(
            serial,
            format,
            TryReadBackNative(serial, format, settings, context, out var back)
            && back.Ticks == expected.Ticks
            && back.Kind == expected.Kind,
            out cell,
            out failure);
    }

    private static string DefaultFormat(
        DateTime value,
        bool utc
    ) => (value.Ticks % TimeSpan.TicksPerSecond != 0, utc) switch
    {
        (true, true) => "yyyy-mm-dd hh:mm:ss.000 \"UTC\"",
        (true, false) => "yyyy-mm-dd hh:mm:ss.000",
        (false, true) => "yyyy-mm-dd hh:mm:ss \"UTC\"",
        (false, false) => "yyyy-mm-dd hh:mm:ss",
    };

    private static ReadStatus Resolve(
        DateTime local,
        TimeSpan? offset,
        FieldSettings settings,
        out DateTime value,
        out ConversionFailure failure
    )
    {
        if (offset is { } known)
        {
            return TryInstant(local, known, out var withOffset, out failure)
                ? Read(withOffset.UtcDateTime, out value, out failure)
                : Fail(failure, out value, out failure);
        }

        if (settings.FileTimeZone is null)
        {
            return Read(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), out value, out failure);
        }

        return TryResolveLocal(local, settings, out var instant, out failure)
            ? Read(instant.UtcDateTime, out value, out failure)
            : Fail(failure, out value, out failure);
    }

    private static bool TryForOutput(
        DateTime value,
        FieldSettings settings,
        bool utcColumn,
        out DateTime output,
        out DateTime expected,
        out ConversionFailure failure
    )
    {
        failure = default;
        output = value;
        expected = value;
        if (value.Kind == DateTimeKind.Local
            && (settings.FileTimeZone is null || utcColumn))
        {
            failure = ConversionFailure.InvalidFormat(
                utcColumn
                    ? "A UTC Excel column does not accept local DateTime values."
                    : "A local DateTime requires an explicit file time zone.");

            return false;
        }

        // WHY: A UTC Excel column holds UTC values, as the import reads them; the file zone applies to text only.
        if (settings.FileTimeZone is { } zone
            && !utcColumn)
        {
            // WHY: The import resolves every local time of the file zone to its instant and rounds that on the UTC
            // axis; a zoneless value is such a local time and is resolved by the same rules first.
            DateTime utc;
            if (value.Kind == DateTimeKind.Unspecified)
            {
                if (!TryResolveLocal(value, settings, out var resolved, out failure))
                {
                    return false;
                }

                utc = resolved.UtcDateTime;
            }
            else
            {
                utc = value.Kind == DateTimeKind.Local
                    ? value.ToUniversalTime()
                    : value;
            }

            if (!TryRoundTicks(
                utc.Ticks,
                default,
                settings.TimeRounding,
                DateTime.MaxValue.Ticks,
                out var instant,
                out failure))
            {
                return false;
            }

            if (!TryLocalForOutput(zone, instant, settings, out var local, out failure))
            {
                return false;
            }

            output = local.DateTime;
            expected = new DateTime(instant, DateTimeKind.Utc);

            return true;
        }

        if (utcColumn && value.Kind == DateTimeKind.Unspecified)
        {
            output = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        if (!TryRoundTicks(
            output.Ticks,
            default,
            settings.TimeRounding,
            DateTime.MaxValue.Ticks,
            out var ticks,
            out failure))
        {
            return false;
        }

        output = new DateTime(ticks, output.Kind);
        expected = output;

        return true;
    }

    /// <summary>
    /// The field format of the value, which must read back to the same local time. A UTC value needs an offset in the
    /// format to keep its kind; a zoneless value has no offset to write.
    /// </summary>
    private static bool TryWriteChecked(
        DateTime typed,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        var hasOffset = TimeFormatSpec
            .Of(settings.TimeFormat!)
            .Offset;

        if (hasOffset != (typed.Kind == DateTimeKind.Utc))
        {
            failure = ConversionFailure.InvalidFormat(
                hasOffset
                    ? "A zoneless value has no offset for this format."
                    : "The format has no offset and would lose the UTC kind; use K or zzz, or a file time zone.");

            return false;
        }

        var culture = context.CultureFor(settings);
        var start = output.Length;
        output.AppendFormatted(typed, settings.TimeFormat, culture);
        if (TryCustomTimestamp(output.Written(start), settings, context, out var back, out _)
            && back.Local.Ticks == typed.Ticks
            && (back.Offset ?? TimeSpan.Zero) == TimeSpan.Zero)
        {
            failure = default;

            return true;
        }

        output.Truncate(start);
        failure = ConversionFailure.PrecisionLoss("The text format loses information; rounding is required.");

        return false;
    }

    protected override string? FormatProblem(
        TimeFormatSpec spec
    ) => DateFormatProblem(spec);

    protected override IEnumerable<DateTime> Probes(
        Calendar calendar
    )
    {
        foreach (var date in ProbeDates(calendar))
        {
            // WHY: K writes nothing for an unspecified kind and Z for UTC; the hours cover AM and PM.
            yield return DateTime.SpecifyKind(date.AddHours(1), DateTimeKind.Unspecified);
            yield return DateTime.SpecifyKind(date.AddHours(13), DateTimeKind.Utc);
        }
    }
}
