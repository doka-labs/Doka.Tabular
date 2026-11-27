using System.Numerics;

namespace Doka.Tabular;

/// <summary>Conversion between Excel serial numbers and ticks for the 1900 and 1904 date systems.</summary>
internal static class ExcelDates
{
    private static readonly DateTime s_base1900 = new(
        1899,
        12,
        31,
        0,
        0,
        0,
        DateTimeKind.Unspecified);

    private static readonly DateTime s_base1904 = new(
        1904,
        1,
        1,
        0,
        0,
        0,
        DateTimeKind.Unspecified);

    private static readonly long[] s_steps = [TimeSpan.TicksPerSecond, TimeSpan.TicksPerMillisecond, 1_000, 100, 10, 1];

    /// <summary>Converts a serial number to a date and time; <see langword="false" /> outside the valid range.</summary>
    public static bool TryToDateTime(
        double serial,
        bool is1904,
        TimeRounding? rounding,
        out DateTime value
    )
    {
        value = default;
        if (!TryDays(serial, is1904, out var days, out var origin))
        {
            return false;
        }

        if (rounding is not null)
        {
            return TryRoundAtCalendarOrigin(days, origin, rounding, out value);
        }

        if (!TryTicks(days, null, out var ticks))
        {
            return false;
        }

        if (ticks > DateTime.MaxValue.Ticks - origin.Ticks)
        {
            return false;
        }

        value = new DateTime(origin.Ticks + ticks, DateTimeKind.Unspecified);

        return true;
    }

    /// <summary>
    /// The exact local time of a serial number as whole ticks and the exact rest of a tick, for a field that rounds
    /// only after a time zone is applied; <see langword="false" /> outside the valid range.
    /// </summary>
    public static bool TryToExactDateTime(
        double serial,
        bool is1904,
        out DateTime value,
        out ExactDecimal excess
    )
    {
        value = default;
        excess = default;
        if (!TryDays(serial, is1904, out var days, out var origin))
        {
            return false;
        }

        var absolute = ExactTicks(days, origin);
        var unit = BigInteger.Pow(10, -absolute.Exponent);
        if (absolute.Mantissa > DateTime.MaxValue.Ticks * unit)
        {
            return false;
        }

        var whole = BigInteger.DivRem(absolute.Mantissa, unit, out var rest);
        value = new DateTime((long)whole, DateTimeKind.Unspecified);
        excess = new ExactDecimal(false, rest, absolute.Exponent);

        return true;
    }

    /// <summary>The days since the origin of the date system; <see langword="false" /> for an invalid serial number.</summary>
    private static bool TryDays(
        double serial,
        bool is1904,
        out double days,
        out DateTime origin
    )
    {
        days = 0;
        origin = default;
        if (!double.IsFinite(serial)
            || serial < 0)
        {
            return false;
        }

        if (is1904)
        {
            origin = s_base1904;
            days = serial;

            return true;
        }

        // WHY: Sequential range guards on a double read clearer as ifs than as relational switch cases.
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (serial < 1)
        {
            return false;
        }

        if (serial is >= 60 and < 61)
        {
            return false;
        }

        origin = s_base1900;
        days = serial >= 61
            ? serial - 1
            : serial;

        return true;
    }

    /// <summary>The exact ticks since 0001-01-01 of a day count from an origin; the exponent is never positive.</summary>
    private static ExactDecimal ExactTicks(
        double days,
        DateTime origin
    )
    {
        var exact = ExactDecimal.FromDouble(days);
        var scale = BigInteger.Pow(10, Math.Max(0, -exact.Exponent));

        return new ExactDecimal(
            false,
            exact.Mantissa * TimeSpan.TicksPerDay * BigInteger.Pow(10, Math.Max(0, exact.Exponent))
            + origin.Ticks * scale,
            Math.Min(0, exact.Exponent));
    }

    /// <summary>
    /// Rounds the exact point in time once to the step, counted from the calendar origin 0001-01-01 and not from the
    /// Excel origin; a step that does not divide a day would otherwise land on another grid.
    /// </summary>
    private static bool TryRoundAtCalendarOrigin(
        double days,
        DateTime origin,
        TimeRounding rounding,
        out DateTime value
    )
    {
        value = default;
        var absolute = ExactTicks(days, origin);

        // WHY: The range is checked before rounding; rounding down must not turn 10000-01-01 into 9999-12-31.
        if (absolute.Mantissa > DateTime.MaxValue.Ticks * BigInteger.Pow(10, -absolute.Exponent))
        {
            return false;
        }

        var step = rounding.Step.Ticks;
        var rounded = DivideExact(absolute, step)
                .RoundToDecimals(0, rounding.Mode)
                .ToBigIntegerExact()
            * step;

        if (rounded < 0
            || rounded > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        value = new DateTime((long)rounded, DateTimeKind.Unspecified);

        return true;
    }

    /// <summary>Converts a date and time to its serial number in the 1900 system.</summary>
    public static double ToSerial(
        DateTime value
    )
    {
        var ticks = value.Ticks - s_base1900.Ticks;
        var days = (double)ticks / TimeSpan.TicksPerDay;

        return value >= new DateTime(1900, 3, 1)
            ? days + 1
            : days;
    }

    /// <summary>Converts a day fraction or day count to ticks, rounding to the nearest tick or to the step.</summary>
    public static bool TryTicks(
        double days,
        TimeRounding? rounding,
        out long ticks
    )
    {
        var step = rounding is null
            ? 1
            : rounding.Step.Ticks;

        var mode = rounding?.Mode ?? MidpointRounding.ToEven;
        if (!TryTicksBinary(days, step, mode, out ticks)
            && !TryTicksExact(days, step, mode, out ticks))
        {
            return false;
        }

        if (rounding is null)
        {
            ticks = Shortest(ticks, days);
        }

        return true;
    }

    /// <summary>
    /// The exact conversion in 128-bit integers: a double is a 53-bit mantissa times a power of two, so the exact tick
    /// value is a fraction whose numerator and denominator both fit. <see langword="false" /> when the value lies
    /// outside that range; the decimal path then decides.
    /// </summary>
    /// <remarks>
    /// WHY: The decimal path allocates several big integers per cell; every date and time read from Excel takes this
    /// path, so it decides without allocation for all serial numbers in the range of dates.
    /// </remarks>
    internal static bool TryTicksBinary(
        double days,
        long step,
        MidpointRounding mode,
        out long ticks
    )
    {
        ticks = 0;
        var bits = BitConverter.DoubleToInt64Bits(days);
        var negative = bits < 0;
        var biased = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFFL;
        if (biased == 0)
        {
            // WHY: Zero and subnormal numbers are below one tick except zero itself; only zero is decided here.
            return mantissa == 0;
        }

        mantissa |= 1L << 52;

        // WHY: The numerator stays below 2^93; the denominator, step times 2^shift, must stay below 2^126 so that
        // twice the remainder cannot overflow.
        var shift = 1075 - biased;
        if (shift < 0
            || shift + 64 - (int)long.LeadingZeroCount(step) > 125)
        {
            return false;
        }

        var numerator = (Int128)mantissa * TimeSpan.TicksPerDay;
        var denominator = (Int128)step << shift;
        var quotient = Int128.DivRem(numerator, denominator);
        var steps = quotient.Quotient;
        if (quotient.Remainder != 0)
        {
            var comparison = (quotient.Remainder * 2).CompareTo(denominator);
            var up = mode switch
            {
                MidpointRounding.ToEven => comparison > 0 || (comparison == 0 && !Int128.IsEvenInteger(steps)),
                MidpointRounding.AwayFromZero => comparison >= 0,
                MidpointRounding.ToZero => false,
                MidpointRounding.ToPositiveInfinity => !negative,
                MidpointRounding.ToNegativeInfinity => negative,
                _ => comparison > 0,
            };

            if (up)
            {
                steps++;
            }
        }

        var magnitude = steps * step;
        if (magnitude > long.MaxValue)
        {
            return false;
        }

        ticks = negative
            ? -(long)magnitude
            : (long)magnitude;

        return true;
    }

    /// <summary>The exact conversion in decimal arithmetic for values outside the 128-bit range.</summary>
    internal static bool TryTicksExact(
        double days,
        long step,
        MidpointRounding mode,
        out long ticks
    )
    {
        ticks = 0;
        var exact = ExactDecimal.FromDouble(days);
        var scaled = exact with { Mantissa = exact.Mantissa * TimeSpan.TicksPerDay };
        var inSteps = DivideExact(scaled, step);
        var rounded = inSteps
                .RoundToDecimals(0, mode)
                .ToBigIntegerExact()
            * step;

        if (rounded > long.MaxValue
            || rounded < long.MinValue)
        {
            return false;
        }

        ticks = (long)rounded;

        return true;
    }

    /// <summary>
    /// Returns the coarsest tick value (second, millisecond, down to one tick) whose day count is the same double, so
    /// a written value reads back unchanged although the double cannot hold every tick.
    /// </summary>
    private static long Shortest(
        long nearest,
        double days
    )
    {
        // WHY: Around the year 2024 a serial number resolves about one microsecond, coarser than a tick; the
        // nearest tick of the stored double is then not the written value, the coarsest equal candidate is.
        foreach (var step in s_steps)
        {
            var candidate = (long)Math.Round((decimal)nearest / step, MidpointRounding.ToEven) * step;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            // WHY: The candidate is accepted only if it reproduces the stored serial exactly.
            if ((double)candidate / TimeSpan.TicksPerDay == days)
            {
                return candidate;
            }
        }

        return nearest;
    }

    /// <summary>Rounds ticks to the step; <see langword="false" /> when the result leaves the given range.</summary>
    public static bool TryRoundTicks(
        long ticks,
        TimeRounding? rounding,
        long minimum,
        long maximum,
        out long rounded
    )
    {
        rounded = ticks;
        if (rounding is null)
        {
            return true;
        }

        var value = new ExactDecimal(ticks < 0, BigInteger.Abs(ticks), 0);
        var result = DivideExact(value, rounding.Step.Ticks)
                .RoundToDecimals(0, rounding.Mode)
                .ToBigIntegerExact()
            * rounding.Step.Ticks;

        if (result < minimum
            || result > maximum)
        {
            return false;
        }

        rounded = (long)result;

        return true;
    }

    /// <summary>Whether an exact value lies outside the inclusive range; checked before rounding can move it inside.</summary>
    internal static bool IsOutside(
        ExactDecimal value,
        long minimum,
        long maximum
    )
    {
        var unit = BigInteger.Pow(10, Math.Max(0, -value.Exponent));
        var scaled = value.Mantissa * BigInteger.Pow(10, Math.Max(0, value.Exponent));
        if (value.Negative)
        {
            scaled = -scaled;
        }

        return scaled < minimum * unit || scaled > maximum * unit;
    }

    internal static ExactDecimal DivideExact(
        ExactDecimal value,
        long divisor
    )
    {
        if (divisor == 1)
        {
            return value;
        }

        // Scale up so that the integer division keeps enough digits for exact midpoint decisions.
        const int extra = 40;
        var numerator = value.Mantissa * BigInteger.Pow(10, extra);
        var quotient = BigInteger.DivRem(numerator, divisor, out var remainder);
        if (!remainder.IsZero)
        {
            // A remainder below the scaled precision must still break midpoint ties away from exact halves.
            quotient = quotient * 10 + 1;

            return new ExactDecimal(value.Negative, quotient, value.Exponent - extra - 1);
        }

        return new ExactDecimal(value.Negative, quotient, value.Exponent - extra);
    }
}
