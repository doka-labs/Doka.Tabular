using System.Numerics;

namespace Doka.Tabular;

/// <summary>
/// ISO 8601 shapes of the time types, parsed from spans: <c>yyyy-MM-dd</c>, optionally followed by
/// <c>T</c> or a space, <c>HH:mm[:ss[.f+]]</c>, and an offset <c>Z</c> or <c>+hh:mm</c>; digits are ASCII digits.
/// </summary>
internal static class TimeText
{
    /// <summary>Splits an ISO timestamp; <paramref name="time" /> and <paramref name="zone" /> are empty when absent.</summary>
    public static bool TryMatchTimestamp(
        ReadOnlySpan<char> text,
        out ReadOnlySpan<char> date,
        out ReadOnlySpan<char> time,
        out ReadOnlySpan<char> zone
    )
    {
        date = [];
        time = [];
        zone = [];
        if (text.Length < 10
            || !IsDate(text[..10]))
        {
            return false;
        }

        date = text[..10];
        if (text.Length == 10)
        {
            return true;
        }

        if (text[10] is not ('T' or ' '))
        {
            return false;
        }

        var rest = text[11..];
        var timeLength = TimeLength(rest);
        if (timeLength == 0)
        {
            return false;
        }

        time = rest[..timeLength];
        zone = rest[timeLength..];

        return zone.IsEmpty || zone is "Z" || IsOffset(zone);
    }

    /// <summary>Splits an ISO time of day; seconds and fraction are 0 and empty when the text has none.</summary>
    public static bool TryMatchTime(
        ReadOnlySpan<char> text,
        out int hour,
        out int minute,
        out int second,
        out ReadOnlySpan<char> fraction
    )
    {
        hour = 0;
        minute = 0;
        second = 0;
        fraction = [];
        if (TimeLength(text) != text.Length
            || text.Length == 0)
        {
            return false;
        }

        hour = Number(text[..2]);
        minute = Number(text[3..5]);
        if (text.Length > 5)
        {
            second = Number(text[6..8]);
            fraction = text.Length > 8
                ? text[9..]
                : [];
        }

        return true;
    }

    /// <summary>Appends the fraction of a second without trailing zeros; nothing for a whole second.</summary>
    public static void WriteFraction(
        TextBuffer output,
        long ticksOfSecond
    )
    {
        if (ticksOfSecond == 0)
        {
            return;
        }

        Span<char> digits = stackalloc char[7];
        ticksOfSecond.TryFormat(digits, out _, "0000000", CultureInfo.InvariantCulture);
        output.Append('.');
        output.Append(digits.TrimEnd('0'));
    }

    /// <summary>
    /// The ticks of a fraction of a second. Up to seven digits are whole ticks; further zeros change nothing. Other
    /// further digits stay exact in <paramref name="excess" />, a part of one tick, when
    /// <paramref name="allowExcess" /> is set, so that rounding happens once on the exact value.
    /// </summary>
    public static bool TryFraction(
        ReadOnlySpan<char> digits,
        bool allowExcess,
        out long ticks,
        out ExactDecimal excess,
        out bool needsRounding
    )
    {
        ticks = 0;
        excess = default;
        needsRounding = false;
        var whole = digits.Length <= 7
            ? digits
            : digits[..7];

        foreach (var digit in whole)
        {
            ticks = ticks * 10 + (digit - '0');
        }

        for (var missing = whole.Length; missing < 7; missing++)
        {
            ticks *= 10;
        }

        // WHY: Only digits that change the value count; trailing zeros after the seventh digit need no rounding.
        if (digits.Length <= 7
            || !digits[7..]
                .ContainsAnyExcept('0'))
        {
            return true;
        }

        if (!allowExcess)
        {
            needsRounding = true;

            return false;
        }

        var rest = digits[7..];
        excess = new ExactDecimal(
            false,
            BigInteger.Parse(rest, NumberStyles.None, CultureInfo.InvariantCulture),
            -rest.Length);

        return true;
    }

    public static int Number(
        ReadOnlySpan<char> digits
    )
    {
        var value = 0;
        foreach (var digit in digits)
        {
            value = value * 10 + (digit - '0');
        }

        return value;
    }

    /// <summary>The length of a leading <c>HH:mm[:ss[.f+]]</c>; 0 when the text does not start with one.</summary>
    private static int TimeLength(
        ReadOnlySpan<char> text
    )
    {
        if (text.Length < 5
            || !IsDigits(text[..2])
            || text[2] != ':'
            || !IsDigits(text[3..5]))
        {
            return 0;
        }

        if (text.Length < 8
            || text[5] != ':'
            || !IsDigits(text[6..8]))
        {
            return 5;
        }

        if (text.Length < 10
            || text[8] != '.'
            || !char.IsAsciiDigit(text[9]))
        {
            return 8;
        }

        var end = 10;
        while (end < text.Length
            && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return end;
    }

    private static bool IsDate(
        ReadOnlySpan<char> text
    ) => IsDigits(text[..4]) && text[4] == '-' && IsDigits(text[5..7]) && text[7] == '-' && IsDigits(text[8..10]);

    private static bool IsOffset(
        ReadOnlySpan<char> text
    ) => text.Length == 6 && text[0] is '+' or '-' && IsDigits(text[1..3]) && text[3] == ':' && IsDigits(text[4..6]);

    private static bool IsDigits(
        ReadOnlySpan<char> text
    ) => !text.ContainsAnyExceptInRange('0', '9');
}
