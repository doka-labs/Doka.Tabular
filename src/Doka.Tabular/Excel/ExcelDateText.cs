using System.Text.RegularExpressions;

namespace Doka.Tabular.Excel;

/// <summary>
/// Converts the ISO 8601 text of a date cell (<c>t="d"</c>, ECMA-376 Part 1, 18.18.11) into the serial number of the
/// workbook's date system, so that it takes the same conversion path as a numeric cell.
/// </summary>
/// <remarks>
/// Excel writes these cells in Strict workbooks with more fractional digits than a tick, for example
/// <c>14:29:59.9999999999968050</c>, <c>2024-04-01T08:29:59.99999979045242400</c> or <c>PT27H0M0.000S</c>. The
/// text is converted exactly with <see langword="decimal" /> and rounded only once, to the same double a
/// Transitional workbook stores.
/// </remarks>
internal static partial class ExcelDateText
{
    private const decimal SecondsPerDay = 86_400m;

    // WHY: Excel writes the zero serial number, a pure time of day, with this date in both date systems.
    private static readonly DateOnly s_timeOnlyDate = new(1899, 12, 30);

    private static readonly DateOnly s_base1900 = new(1899, 12, 31);

    private static readonly DateOnly s_base1904 = new(1904, 1, 1);

    private static readonly DateOnly s_firstAfterLeapBug = new(1900, 3, 1);

    /// <summary>
    /// The serial number of a date cell; <paramref name="utc" /> tells that the text stated a time zone, whose
    /// instant the serial number then holds as UTC time.
    /// </summary>
    public static bool TryToSerial(
        string text,
        bool is1904,
        out double serial,
        out NumberFormatKind shape,
        out bool utc
    )
    {
        serial = 0;
        shape = NumberFormatKind.DateTime;
        utc = false;
        if (DateAndTime()
                .Match(text) is { Success: true } date)
        {
            utc = date.Groups["zone"].Success;

            return TryDateAndTime(date, is1904, out serial, out shape);
        }

        decimal seconds;
        if (TimeOfDay()
                .Match(text) is { Success: true } clock)
        {
            if (!TrySeconds(
                clock.Groups["hour"].Value,
                clock.Groups["minute"].Value,
                clock.Groups["second"].Value,
                out seconds))
            {
                return false;
            }
        }
        else if (Duration()
                .Match(text) is not { Success: true } duration
            || !TryDurationSeconds(duration, out seconds))
        {
            return false;
        }

        shape = NumberFormatKind.Time;
        serial = (double)(seconds / SecondsPerDay);

        return true;
    }

    private static bool TryDateAndTime(
        Match date,
        bool is1904,
        out double serial,
        out NumberFormatKind shape
    )
    {
        serial = 0;
        shape = NumberFormatKind.DateTime;
        if (!DateOnly.TryParseExact(
            date.Groups["date"].Value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var day))
        {
            return false;
        }

        // WHY: With a stated zone, the range applies to the UTC date, which is checked after the normalization.
        // WHY: Only a zoneless cell on 1899-12-30 is a time of day; a stated zone makes every cell an instant.
        var zone = date.Groups["zone"];
        var timeOnly = !zone.Success && day == s_timeOnlyDate;
        var days = 0;
        if (!zone.Success
            && !timeOnly
            && !TryDayNumber(day, is1904, out days))
        {
            return false;
        }

        var time = 0m;
        var hasTime = date.Groups["time"].Success;
        if (hasTime
            && !TrySeconds(
                date.Groups["hour"].Value,
                date.Groups["minute"].Value,
                date.Groups["second"].Value,
                out time))
        {
            return false;
        }

        if (zone.Success)
        {
            // WHY: A stated zone denotes an instant; the cell holds its UTC time, which may lie on another day.
            time -= OffsetSeconds(zone.Value);
            var dayNumber = day.DayNumber;
            for (; time < 0; time += SecondsPerDay)
            {
                dayNumber--;
            }

            for (; time >= SecondsPerDay; time -= SecondsPerDay)
            {
                dayNumber++;
            }

            if (dayNumber > DateOnly.MaxValue.DayNumber)
            {
                // WHY: The instant lies after the last date; a number past it lets the column report the range.
                TryDayNumber(DateOnly.MaxValue, is1904, out days);
                serial = (double)(days + 1 + time / SecondsPerDay);

                return true;
            }

            // WHY: An instant before the first date of the date system gets a number below it, so that the column
            // reports the range instead of the cell syntax.
            if (dayNumber < DateOnly.MinValue.DayNumber
                || !TryDayNumber(DateOnly.FromDayNumber(dayNumber), is1904, out days))
            {
                serial = -1 + (double)(time / SecondsPerDay);

                return true;
            }
        }

        shape = (timeOnly, hasTime) switch
        {
            (true, _) => NumberFormatKind.Time,
            (false, true) => NumberFormatKind.DateTime,
            (false, false) => NumberFormatKind.Date,
        };

        serial = (double)(days + time / SecondsPerDay);

        return true;
    }

    /// <summary>The offset of <c>Z</c> or <c>+HH:mm</c>/<c>-HH:mm</c> in seconds.</summary>
    private static decimal OffsetSeconds(
        string zone
    )
    {
        if (zone == "Z")
        {
            return 0;
        }

        var hours = int.Parse(zone.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture);
        var minutes = int.Parse(zone.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture);
        var seconds = (hours * 60m + minutes) * 60m;

        return zone[0] == '-'
            ? -seconds
            : seconds;
    }

    /// <summary>The serial day of a calendar date; a date before the first day of the date system has none.</summary>
    private static bool TryDayNumber(
        DateOnly day,
        bool is1904,
        out int days
    )
    {
        if (is1904)
        {
            days = day.DayNumber - s_base1904.DayNumber;
        }
        else
        {
            // WHY: Serial number 60 is the fictitious 1900-02-29 that Excel keeps for Lotus compatibility; every
            // later date is one serial number further from the base.
            days = day >= s_firstAfterLeapBug
                ? day.DayNumber - s_base1900.DayNumber + 1
                : day.DayNumber - s_base1900.DayNumber;
        }

        return days >= 0;
    }

    private static bool TrySeconds(
        string hour,
        string minute,
        string second,
        out decimal seconds
    )
    {
        seconds = 0;
        var hours = int.Parse(hour, NumberStyles.None, CultureInfo.InvariantCulture);
        var minutes = int.Parse(minute, NumberStyles.None, CultureInfo.InvariantCulture);
        if (hours > 24
            || minutes > 59
            || !TrySecond(second, out var rest)
            || rest >= 60)
        {
            return false;
        }

        seconds = hours * 3_600m + minutes * 60m + rest;

        return seconds <= SecondsPerDay;
    }

    /// <summary>The seconds with their fraction; a time without seconds has none.</summary>
    private static bool TrySecond(
        string second,
        out decimal value
    )
    {
        value = 0;

        return second.Length == 0
            || decimal.TryParse(second, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDurationSeconds(
        Match duration,
        out decimal seconds
    )
    {
        seconds = 0;
        if (!TryPart(duration.Groups["days"], out var days)
            || !TryPart(duration.Groups["hours"], out var hours)
            || !TryPart(duration.Groups["minutes"], out var minutes)
            || !TryPart(duration.Groups["seconds"], out var rest))
        {
            return false;
        }

        seconds = days * SecondsPerDay + hours * 3_600m + minutes * 60m + rest;
        if (duration.Groups["sign"].Success)
        {
            seconds = -seconds;
        }

        return true;
    }

    private static bool TryPart(
        Group group,
        out decimal value
    )
    {
        value = 0;

        return !group.Success
            || decimal.TryParse(group.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(
        @"\A(?<date>\d{4}-\d{2}-\d{2})(?:(?<time>T(?<hour>\d{2}):(?<minute>\d{2})(?::(?<second>\d{2}(?:\.\d+)?))?)(?<zone>Z|[+-](?:[01]\d|2[0-3]):[0-5]\d)?)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex DateAndTime();

    [GeneratedRegex(@"\A(?<hour>\d{2}):(?<minute>\d{2}):(?<second>\d{2}(?:\.\d+)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimeOfDay();

    // WHY: Excel writes durations without years and months; both would need a reference date to become days. The
    // digit limits keep every part far inside the decimal range.
    [GeneratedRegex(
        @"\A(?<sign>-)?P(?=\d|T\d)(?:(?<days>\d{1,9})D)?(?:T(?=\d)(?:(?<hours>\d{1,9})H)?(?:(?<minutes>\d{1,9})M)?(?:(?<seconds>\d{1,9}(?:\.\d+)?)S)?)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex Duration();
}
