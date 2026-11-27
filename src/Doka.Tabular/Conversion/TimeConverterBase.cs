using System.Numerics;

namespace Doka.Tabular;

/// <summary>Shared rules for the five time types.</summary>
/// <typeparam name="TValue">The time type.</typeparam>
internal abstract class TimeConverterBase<TValue> : ValueConverter<TValue>
    where TValue : struct, ISpanFormattable, IEquatable<TValue>
{
    protected static ConversionFailure Mismatch
        => ConversionFailure.InvalidFormat("The text does not match the format of this field.");

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    )
    {
        if (settings.TimeFormat is { } format)
        {
            // WHY: .NET checks a format only when it formats; an unclosed literal or a pattern the type cannot write
            // would otherwise fail at the first exported value. The invariant culture checks the syntax only.
            if (!CanFormat(format))
            {
                yield return $"The format is not a valid .NET format for {typeof(TValue).Name}.";

                yield break;
            }

            if (UsesTwoDigitYear(format))
            {
                yield return "A two-digit year format is not allowed; years have four digits.";
            }

            if (FormatProblem(TimeFormatSpec.Of(format)) is { } problem)
            {
                yield return problem;
            }
        }

        if (settings.TimeRounding is { Step.Ticks: <= 0 })
        {
            yield return "The rounding step must be positive.";
        }
    }

    /// <summary>Why the field format cannot describe a complete value of this type; nothing is added from the clock.</summary>
    protected virtual string? FormatProblem(
        TimeFormatSpec spec
    ) => null;

    /// <summary>The problem of a format for a date, optionally with a time of day.</summary>
    protected static string? DateFormatProblem(
        TimeFormatSpec spec
    ) => spec switch
    {
        { UnsupportedStandard: true } => "This standard format does not describe a complete date.",
        { HasDate: false } => "The format must contain year, month, and day; nothing is taken from the clock.",
        { LacksDesignator: true } => "A 12-hour format needs the AM/PM designator tt.",
        _ => null,
    };

    /// <summary>
    /// A trimming field removes the white space a format writes at its edge, so its own output would not read back.
    /// The format is probed with the culture of the call, because separators, designators, and offsets can write it.
    /// </summary>
    public override IEnumerable<string> ValidateCall(
        FieldSettings settings,
        ConversionContext context
    )
    {
        if (settings.TimeFormat is { } format
            && context.TrimFor(settings)
            && WritesEdgeWhiteSpace(format, context.CultureFor(settings)))
        {
            yield return "The format writes white space at its edge, which the trimming of the field removes; "
                + "remove the white space or turn trimming off with Trim(false).";
        }
    }

    /// <summary>
    /// Values that cover the parts of a format whose text depends on the value, such as AM and PM or the kind of a
    /// date and time; their date lies inside the range of <paramref name="calendar" />.
    /// </summary>
    protected abstract IEnumerable<TValue> Probes(
        Calendar calendar
    );

    /// <summary>
    /// Dates inside the range of the calendar that cover every text a date format takes from the culture: each month
    /// of a common and of a leap year (names and genitive names, full and abbreviated), each day of the week, and each
    /// era.
    /// </summary>
    protected static IEnumerable<DateTime> ProbeDates(
        Calendar calendar
    )
    {
        var start = StartDate(calendar);
        var dates = new HashSet<DateTime>();
        for (var day = 0; day < 7; day++)
        {
            AddDate(start.AddDays(day));
        }

        // WHY: A calendar may name the months of a leap year differently, as the Hebrew calendar does with its leap
        // month; a common and a leap year near the start cover both sets of names.
        var era = calendar.GetEra(start);
        var startYear = calendar.GetYear(start);
        bool? covered = null;
        for (var offset = 0; offset <= 16; offset++)
        {
            var year = startYear
                + (offset % 2 == 0
                    ? offset / 2
                    : -(offset + 1) / 2);

            if (LeapYear(year) is not { } leap
                || leap == covered)
            {
                continue;
            }

            var months = calendar.GetMonthsInYear(year, era);
            for (var month = 1; month <= months; month++)
            {
                AddFirstDay(year, month, era);
            }

            if (covered is not null)
            {
                break;
            }

            covered = leap;
        }

        // WHY: Indexed access reads the array without an enumerator; each era has its own name.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var eraIndex = 0; eraIndex < calendar.Eras.Length; eraIndex++)
        {
            AddFirstDay(2, 1, calendar.Eras[eraIndex]);
        }

        return dates;

        bool? LeapYear(
            int year
        )
        {
            try
            {
                return calendar.IsLeapYear(year, era);
            }
            catch (ArgumentOutOfRangeException)
            {
                // WHY: A year outside the supported range of the calendar is no probe.
                return null;
            }
        }

        void AddFirstDay(
            int dateYear,
            int month,
            int dateEra
        )
        {
            try
            {
                AddDate(
                    calendar.ToDateTime(
                        dateYear,
                        month,
                        1,
                        0,
                        0,
                        0,
                        0,
                        dateEra));
            }
            catch (ArgumentOutOfRangeException)
            {
                // WHY: A calendar need not have every month or era in its supported range; such a date is no probe.
            }
        }

        void AddDate(
            DateTime date
        )
        {
            if (date >= calendar.MinSupportedDateTime
                && date.AddDays(1) <= calendar.MaxSupportedDateTime)
            {
                dates.Add(date.Date);
            }
        }
    }

    private static DateTime StartDate(
        Calendar calendar
    )
    {
        var probe = new DateTime(2000, 1, 1);
        if (probe < calendar.MinSupportedDateTime)
        {
            return calendar.MinSupportedDateTime.Date.AddDays(1);
        }

        return probe.AddDays(7) > calendar.MaxSupportedDateTime
            ? calendar.MaxSupportedDateTime.Date.AddDays(-8)
            : probe;
    }

    private bool WritesEdgeWhiteSpace(
        string format,
        CultureInfo culture
    )
    {
        foreach (var probe in Probes(culture.DateTimeFormat.Calendar))
        {
            string text;
            try
            {
                text = probe.ToString(format, culture);
            }
            catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
            {
                // WHY: Build checked the format; a probe the culture cannot write says nothing about the edges.
                continue;
            }

            if (text.Length > 0
                && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanFormat(
        string format
    )
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

    /// <summary>Whether the pattern has a year of fewer than four digits; literals and escapes do not count.</summary>
    protected static bool UsesTwoDigitYear(
        string format
    ) => TimeFormatSpec
        .OfPattern(format)
        .ShortYear;

    protected ReadStatus FromCustomFormat(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        TryParseExact parse,
        out TValue value,
        out ConversionFailure failure
    )
    {
        var culture = context.CultureFor(settings);
        var format = settings.TimeFormat!;

        // WHY: A standard format stands for a pattern of the culture, which may lack parts the checked format has.
        if (format.Length == 1
            && FormatProblem(
                CulturePatterns
                    .For(culture.DateTimeFormat)
                    .SpecOf(format[0])) is { } problem)
        {
            return Fail(ConversionFailure.InvalidFormat(problem), out value, out failure);
        }

        // WHY: ParseExact accepts single digits for two-digit specifiers; reformatting enforces the exact text.
        return parse(text, format, culture, out value)
            && FormatsAs(value, format, culture, text)
            && !UsesTwoDigitYear(format)
                ? Read(value, out value, out failure)
                : Fail(Mismatch, out value, out failure);
    }

    /// <summary>The first culture pattern that parses the text and formats back to exactly the same text.</summary>
    protected static bool TryCulture(
        ReadOnlySpan<char> text,
        CultureInfo culture,
        ReadOnlySpan<string> patterns,
        TryParseExact parse,
        out TValue value
    )
    {
        foreach (var pattern in patterns)
        {
            if (!UsesTwoDigitYear(pattern)
                && parse(text, pattern, culture, out value)
                && FormatsAs(value, pattern, culture, text))
            {
                return true;
            }
        }

        value = default;

        return false;
    }

    /// <summary>Appends the custom text and checks that it parses back to the same value.</summary>
    protected static bool TryWriteCheckedCustomText(
        TValue value,
        FieldSettings settings,
        ConversionContext context,
        TryParseExact parse,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        var culture = context.CultureFor(settings);
        var start = output.Length;
        output.AppendFormatted(value, settings.TimeFormat, culture);
        if (parse(output.Written(start), settings.TimeFormat!, culture, out var back)
            && back.Equals(value))
        {
            return true;
        }

        output.Truncate(start);
        failure = ConversionFailure.PrecisionLoss("The text format loses information; rounding is required.");

        return false;
    }

    protected static bool TryExcelSerial(
        double serial,
        string? numberFormat,
        bool roundTrips,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        failure = default;
        cell = OutputCell.Empty;
        if (!roundTrips)
        {
            failure = ConversionFailure.PrecisionLoss(
                "The value cannot be stored exactly as a native Excel number; use rounding or a text column.");

            return false;
        }

        cell = OutputCell.FromNumber(serial, numberFormat);

        return true;
    }

    /// <summary>
    /// The ticks of a matched ISO time of day; digits beyond seven stay exact in <paramref name="excess" /> when the
    /// field rounds, otherwise they are an error.
    /// </summary>
    protected static bool TryIsoTime(
        ReadOnlySpan<char> time,
        bool rounds,
        out long ticks,
        out ExactDecimal excess,
        out ConversionFailure failure
    )
    {
        ticks = 0;
        excess = default;
        failure = Mismatch;
        if (!TimeText.TryMatchTime(time, out var hour, out var minute, out var second, out var fraction))
        {
            return false;
        }

        if (hour > 23
            || minute > 59
            || second > 59)
        {
            failure = ConversionFailure.InvalidFormat("The time of day is not valid.");

            return false;
        }

        if (!TimeText.TryFraction(fraction, rounds, out var fractionTicks, out excess, out var needsRounding))
        {
            failure = needsRounding
                ? ConversionFailure.PrecisionLoss("More than seven fraction digits require rounding.")
                : Mismatch;

            return false;
        }

        ticks = ((hour * 60L + minute) * 60 + second) * TimeSpan.TicksPerSecond + fractionTicks;

        return true;
    }

    /// <summary>
    /// Rounds the exact value of <paramref name="ticks" /> plus <paramref name="excess" /> once to the step of the field;
    /// a result above <paramref name="maxTicks" /> is a range error.
    /// </summary>
    protected static bool TryRoundTicks(
        long ticks,
        ExactDecimal excess,
        TimeRounding? rounding,
        long maxTicks,
        out long rounded,
        out ConversionFailure failure
    )
    {
        failure = default;
        rounded = ticks;
        if (rounding is null)
        {
            return true;
        }

        var exact = excess.IsZero
            ? new ExactDecimal(ticks < 0, BigInteger.Abs(ticks), 0)
            : new ExactDecimal(false, ticks * BigInteger.Pow(10, -excess.Exponent) + excess.Mantissa, excess.Exponent);

        // WHY: The range is checked on the exact value; rounding must not turn a time beyond it into a valid one.
        if (ExcelDates.IsOutside(exact, 0, maxTicks))
        {
            failure = ConversionFailure.OutOfRange("The value is outside the supported range.");

            return false;
        }

        var step = rounding.Step.Ticks;
        var result = ExcelDates
                .DivideExact(exact, step)
                .RoundToDecimals(0, rounding.Mode)
                .ToBigIntegerExact()
            * step;

        if (result > maxTicks
            || result < 0)
        {
            failure = ConversionFailure.OutOfRange("Rounding exceeds the supported range.");

            return false;
        }

        rounded = (long)result;

        return true;
    }

    protected static bool IsDateFormatted(
        in SourceCell cell
    ) => cell.NumberFormat is NumberFormatKind.Date or NumberFormatKind.DateTime or NumberFormatKind.Time;

    /// <summary>Whether the value formats to exactly <paramref name="text" />, without an intermediate string.</summary>
    protected static bool FormatsAs<TFormatted>(
        TFormatted value,
        string format,
        CultureInfo culture,
        ReadOnlySpan<char> text
    )
        where TFormatted : ISpanFormattable
    {
        var size = Math.Max(128, text.Length + 1);
        var buffer = size <= 512
            ? stackalloc char[size]
            : new char[size];

        return value.TryFormat(buffer, out var written, format, culture)
            && buffer[..written]
                .SequenceEqual(text);
    }

    protected delegate bool TryParseExact(
        ReadOnlySpan<char> text,
        ReadOnlySpan<char> format,
        CultureInfo culture,
        out TValue value
    );
}
