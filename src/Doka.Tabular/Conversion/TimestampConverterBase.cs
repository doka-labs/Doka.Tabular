namespace Doka.Tabular;

/// <summary>Shared parsing of timestamps for <see cref="DateTime" /> and <see cref="DateTimeOffset" />.</summary>
/// <typeparam name="TValue">The timestamp type.</typeparam>
internal abstract class TimestampConverterBase<TValue> : TimeConverterBase<TValue>
    where TValue : struct, ISpanFormattable, IEquatable<TValue>
{
    /// <summary>An ISO timestamp, unrounded; digits beyond seven stay exact when the field rounds.</summary>
    protected static bool TryParseIso(
        ReadOnlySpan<char> text,
        bool rounds,
        out LocalTimestamp timestamp,
        out ConversionFailure failure
    )
    {
        timestamp = default;
        failure = Mismatch;
        if (!TimeText.TryMatchTimestamp(text, out var dateText, out var time, out var zone))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(dateText,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            failure = ConversionFailure.InvalidFormat("The date is not a valid calendar day.");

            return false;
        }

        long timeTicks = 0;
        ExactDecimal excess = default;
        TimeSpan? offset = null;
        if ((!time.IsEmpty && !TryIsoTime(time, rounds, out timeTicks, out excess, out failure))
            || (!zone.IsEmpty && !TryIsoOffset(zone, out offset, out failure)))
        {
            return false;
        }

        var ticks = date.ToDateTime(TimeOnly.MinValue)
                .Ticks
            + timeTicks;

        timestamp = new LocalTimestamp(new DateTime(ticks, DateTimeKind.Unspecified), offset, excess);

        return true;
    }

    /// <summary>The offset of <c>Z</c> or <c>+hh:mm</c>; offsets beyond 14 hours are invalid.</summary>
    private static bool TryIsoOffset(
        ReadOnlySpan<char> zone,
        out TimeSpan? offset,
        out ConversionFailure failure
    )
    {
        failure = Mismatch;
        offset = null;
        if (zone is "Z")
        {
            offset = TimeSpan.Zero;

            return true;
        }

        var hours = TimeText.Number(zone[1..3]);
        var minutes = TimeText.Number(zone[4..6]);

        // WHY: An offset reaches at most 14:00; +14:59 has valid parts but is not a valid offset.
        if (minutes > 59
            || hours * 60 + minutes > 14 * 60)
        {
            failure = ConversionFailure.InvalidFormat("The offset is not valid.");

            return false;
        }

        var span = new TimeSpan(hours, minutes, 0);
        offset = zone[0] == '-'
            ? -span
            : span;

        return true;
    }

    protected static bool TryParseCulture(
        ReadOnlySpan<char> text,
        CultureInfo culture,
        out DateTime value
    )
    {
        var patterns = CulturePatterns.For(culture.DateTimeFormat);

        // WHY: A short date pattern without year, month, and day would take the missing parts from the clock.
        if (!patterns.DateIsComplete)
        {
            value = default;

            return false;
        }

        foreach (var pattern in (ReadOnlySpan<string>)[patterns.DateLongTime, patterns.DateShortTime, patterns.Date])
        {
            if (!UsesTwoDigitYear(pattern)
                && DateTime.TryParseExact(text, pattern, culture, DateTimeStyles.None, out value)
                && FormatsAs(value, pattern, culture, text))
            {
                return true;
            }
        }

        value = default;

        return false;
    }

    /// <summary>Resolves a local time of the file zone to UTC, applying the gap and overlap rules.</summary>
    protected static bool TryResolveLocal(
        DateTime local,
        FieldSettings settings,
        out DateTimeOffset instant,
        out ConversionFailure failure
    )
    {
        var zone = settings.FileTimeZone!;
        failure = default;
        if (zone.IsInvalidTime(local))
        {
            return TryShiftInvalid(zone, local, settings, out instant, out failure);
        }

        if (zone.IsAmbiguousTime(local))
        {
            return TryResolveAmbiguous(zone, local, settings, out instant, out failure);
        }

        return TryInstant(local, zone.GetUtcOffset(local), out instant, out failure);
    }

    /// <summary>Moves a local time inside a gap of the zone forward by the gap, if the field allows it.</summary>
    private static bool TryShiftInvalid(
        TimeZoneInfo zone,
        DateTime local,
        FieldSettings settings,
        out DateTimeOffset instant,
        out ConversionFailure failure
    )
    {
        failure = default;
        instant = default;
        if (!settings.ShiftInvalidTime)
        {
            failure = ConversionFailure.InvalidFormat("The local time does not exist in the file time zone.");

            return false;
        }

        // WHY: The gap lies between the smaller offset before and the larger offset after the transition, whatever
        // the sign of the daylight delta. Read with the offset before, the local time is an instant after the
        // transition, which is the local time moved forward by the gap.
        var first = OffsetAt(zone, local.Ticks - zone.BaseUtcOffset.Ticks);
        var second = OffsetAt(zone, local.Ticks - first.Ticks);
        var before = first < second
            ? first
            : second;

        return TryInstant(local, before, out var shifted, out failure)
            && TryLocalIn(zone, shifted.UtcTicks, out instant, out failure);
    }

    /// <summary>The offset of the zone at UTC ticks; ticks beyond the range probe the nearest valid instant.</summary>
    private static TimeSpan OffsetAt(
        TimeZoneInfo zone,
        long utcTicks
    ) => zone.GetUtcOffset(new DateTime(Math.Clamp(utcTicks, 0, DateTime.MaxValue.Ticks), DateTimeKind.Utc));

    /// <summary>The local time of a UTC instant in a zone; a local time beyond the range is a range error.</summary>
    protected static bool TryLocalIn(
        TimeZoneInfo zone,
        long utcTicks,
        out DateTimeOffset local,
        out ConversionFailure failure
    )
    {
        failure = default;
        local = default;
        var offset = zone.GetUtcOffset(new DateTime(utcTicks, DateTimeKind.Utc));
        var localTicks = utcTicks + offset.Ticks;
        if (localTicks < 0
            || localTicks > DateTime.MaxValue.Ticks)
        {
            failure = ConversionFailure.OutOfRange("The time in the file time zone is outside the supported range.");

            return false;
        }

        local = new DateTimeOffset(localTicks, offset);

        return true;
    }

    /// <summary>
    /// The local time of the file zone that the export writes for an instant. An ambiguous local time must read back
    /// to the same instant under the overlap rule of the field, as the zone rules apply in both directions.
    /// </summary>
    protected static bool TryLocalForOutput(
        TimeZoneInfo zone,
        long utcTicks,
        FieldSettings settings,
        out DateTimeOffset local,
        out ConversionFailure failure
    )
    {
        if (!TryLocalIn(zone, utcTicks, out local, out failure))
        {
            return false;
        }

        if (!zone.IsAmbiguousTime(local.DateTime)
            || (TryResolveAmbiguous(zone, local.DateTime, settings, out var back, out _) && back.UtcTicks == utcTicks))
        {
            return true;
        }

        failure = ConversionFailure.InvalidFormat(
            "The local time is ambiguous in the file time zone and the overlap rule of the field reads another instant.");

        return false;
    }

    /// <summary>Chooses the earlier or later instant of an ambiguous local time, as the field configures.</summary>
    private static bool TryResolveAmbiguous(
        TimeZoneInfo zone,
        DateTime local,
        FieldSettings settings,
        out DateTimeOffset instant,
        out ConversionFailure failure
    )
    {
        failure = default;
        instant = default;
        if (settings.AmbiguousTimeResolution is not { } resolution)
        {
            failure = ConversionFailure.InvalidFormat("The local time is ambiguous in the file time zone.");

            return false;
        }

        var offsets = zone.GetAmbiguousTimeOffsets(local);
        var chosen = resolution == AmbiguousTimeResolution.Earlier
            ? offsets.Max()
            : offsets.Min();

        return TryInstant(local, chosen, out instant, out failure);
    }

    /// <summary>
    /// The field format as a local time, with the offset of the text when the format has one. A format without offset
    /// yields a local time that the zone rules resolve; the time zone of the computer is never used.
    /// </summary>
    protected static bool TryCustomTimestamp(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out LocalTimestamp timestamp,
        out ConversionFailure failure
    )
    {
        failure = default;
        var culture = context.CultureFor(settings);
        var format = settings.TimeFormat!;

        // WHY: A standard format stands for a pattern of the culture, which may lack parts the checked format has.
        var spec = format.Length == 1
            ? CulturePatterns
                .For(culture.DateTimeFormat)
                .SpecOf(format[0])
            : TimeFormatSpec.Of(format);

        if (DateFormatProblem(spec) is { } problem)
        {
            timestamp = default;
            failure = ConversionFailure.InvalidFormat(problem);

            return false;
        }

        if (spec.Offset)
        {
            // WHY: A UTC DateTime writes K and o as Z, a DateTimeOffset as +00:00; both texts state the same instant, so
            // a zero offset also counts when the text is the UTC form.
            if (DateTimeOffset.TryParseExact(text, format, culture, DateTimeStyles.None, out var withOffset)
                && (FormatsAs(withOffset, format, culture, text)
                    || (withOffset.Offset == TimeSpan.Zero
                        && FormatsAs(withOffset.UtcDateTime, format, culture, text))))
            {
                timestamp = new LocalTimestamp(withOffset.DateTime, withOffset.Offset, default);

                return true;
            }
        }
        else if (DateTime.TryParseExact(text, format, culture, DateTimeStyles.None, out var local)
            && FormatsAs(local, format, culture, text))
        {
            timestamp = new LocalTimestamp(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), null, default);

            return true;
        }

        timestamp = default;
        failure = Mismatch;

        return false;
    }

    /// <summary>The instant of a local time with a known offset; a result outside the range is a field error.</summary>
    protected static bool TryInstant(
        DateTime local,
        TimeSpan offset,
        out DateTimeOffset instant,
        out ConversionFailure failure
    )
    {
        failure = default;
        instant = default;
        var utcTicks = local.Ticks - offset.Ticks;
        if (utcTicks < 0
            || utcTicks > DateTime.MaxValue.Ticks)
        {
            failure = ConversionFailure.OutOfRange("The timestamp is outside the supported range.");

            return false;
        }

        instant = new DateTimeOffset(local, offset);

        return true;
    }

    /// <param name="Local">The local date and time, whole ticks.</param>
    /// <param name="Offset">The offset of the text, if it has one.</param>
    /// <param name="Excess">The exact part of one tick beyond seven fraction digits; zero without rounding.</param>
    protected readonly record struct LocalTimestamp(
        DateTime Local,
        TimeSpan? Offset,
        ExactDecimal Excess
    );
}
