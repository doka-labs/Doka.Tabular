namespace Doka.Tabular;

internal sealed class DateTimeOffsetConverter : TimestampConverterBase<DateTimeOffset>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.DateTime;

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out DateTimeOffset value,
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

        return TryInstantOf(timestamp, settings, out var instant, out failure)
            && TryRoundOnUtcAxis(instant,
                timestamp.Excess,
                settings.TimeRounding,
                timestamp.Offset is null
                    ? settings.FileTimeZone
                    : null,
                out var rounded,
                out failure)
                ? Read(rounded, out value, out failure)
                : Fail(failure, out value, out failure);
    }

    /// <summary>The instant of the text: its own offset, otherwise the local time resolved in the file zone.</summary>
    private static bool TryInstantOf(
        LocalTimestamp timestamp,
        FieldSettings settings,
        out DateTimeOffset instant,
        out ConversionFailure failure
    )
    {
        if (timestamp.Offset is { } offset)
        {
            return TryInstant(timestamp.Local, offset, out instant, out failure);
        }

        if (settings.FileTimeZone is null)
        {
            instant = default;
            failure = ConversionFailure.InvalidFormat("The value has no offset and the field has no file time zone.");

            return false;
        }

        return TryResolveLocal(timestamp.Local, settings, out instant, out failure);
    }

    /// <summary>
    /// Rounds the instant on the UTC axis. An offset of the text stays; an offset of the file zone follows the rounded
    /// instant, which can lie on the other side of a daylight change.
    /// </summary>
    private static bool TryRoundOnUtcAxis(
        DateTimeOffset instant,
        ExactDecimal excess,
        TimeRounding? rounding,
        TimeZoneInfo? zone,
        out DateTimeOffset rounded,
        out ConversionFailure failure
    )
    {
        rounded = instant;
        if (!TryRoundTicks(instant.UtcTicks,
            excess,
            rounding,
            DateTime.MaxValue.Ticks,
            out var utcTicks,
            out failure))
        {
            return false;
        }

        var offset = zone?.GetUtcOffset(new DateTime(utcTicks, DateTimeKind.Utc)) ?? instant.Offset;
        var localTicks = utcTicks + offset.Ticks;
        if (localTicks < 0
            || localTicks > DateTime.MaxValue.Ticks)
        {
            failure = ConversionFailure.OutOfRange("The timestamp is outside the supported range.");

            return false;
        }

        rounded = new DateTimeOffset(localTicks, offset);

        return true;
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out DateTimeOffset value,
        out ConversionFailure failure
    ) => Fail(ConversionFailure.Unsupported("A DateTimeOffset is stored as ISO text, not as a native Excel number."),
        out value,
        out failure);

    protected override bool TryWriteText(
        DateTimeOffset value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;

        // WHY: The rounded instant must also be a valid local time at its offset.
        if (!ExcelDates.TryRoundTicks(value.UtcTicks,
            settings.TimeRounding,
            Math.Max(0, -value.Offset.Ticks),
            Math.Min(DateTime.MaxValue.Ticks, DateTime.MaxValue.Ticks - value.Offset.Ticks),
            out var ticks))
        {
            failure = ConversionFailure.OutOfRange("Rounding exceeds the range of DateTimeOffset.");

            return false;
        }

        var shown = new DateTimeOffset(ticks + value.Offset.Ticks, value.Offset);

        // WHY: A written offset makes the local time unambiguous; only a local time without it needs the overlap rule.
        var writesOffset = settings.TimeFormat is { } format
            && TimeFormatSpec
                .Of(format)
                .Offset;

        if (settings.FileTimeZone is { } zone
            && !(writesOffset
                ? TryLocalIn(zone, ticks, out shown, out failure)
                : TryLocalForOutput(zone, ticks, settings, out shown, out failure)))
        {
            return false;
        }

        if (settings.TimeFormat is not null)
        {
            return TryWriteChecked(shown, settings, context, output, out failure);
        }

        output.AppendFormatted(shown.DateTime, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        TimeText.WriteFraction(output, shown.Ticks % TimeSpan.TicksPerSecond);
        if (settings.FileTimeZone is null)
        {
            output.AppendFormatted(shown, "zzz", CultureInfo.InvariantCulture);
        }

        return true;
    }

    /// <summary>
    /// The field format of the shown value, which must read back to the same local time and, with an offset in the
    /// format, the same offset. Without an offset in the format the value needs a file time zone.
    /// </summary>
    private static bool TryWriteChecked(
        DateTimeOffset shown,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        var hasOffset = TimeFormatSpec
            .Of(settings.TimeFormat!)
            .Offset;

        if (!hasOffset
            && settings.FileTimeZone is null)
        {
            failure = ConversionFailure.InvalidFormat("A format without offset needs a file time zone.");

            return false;
        }

        var culture = context.CultureFor(settings);
        var start = output.Length;
        output.AppendFormatted(shown, settings.TimeFormat, culture);
        if (TryCustomTimestamp(output.Written(start), settings, context, out var back, out _)
            && back.Local == shown.DateTime
            && (!hasOffset || back.Offset == shown.Offset))
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

    protected override IEnumerable<DateTimeOffset> Probes(
        Calendar calendar
    )
    {
        foreach (var date in ProbeDates(calendar))
        {
            // WHY: The hours cover AM and PM; the offsets cover a zero and a non-zero offset.
            yield return new DateTimeOffset(date.AddHours(1), TimeSpan.Zero);
            yield return new DateTimeOffset(date.AddHours(13), TimeSpan.FromHours(2));
        }
    }
}
