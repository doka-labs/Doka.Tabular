using System.Buffers;
using System.Numerics;

namespace Doka.Tabular;

/// <summary>Strict parser for decimal text under a culture: nothing is guessed.</summary>
internal static class DecimalText
{
    private const int SmallDigits = 19;

    private static readonly NumberSymbols s_invariant =
        NumberSymbols.Of(NumberFormatInfo.InvariantInfo, NumberTextStyle.Plain);

    // WHY: The space, the no-break space, and the narrow no-break space cultures put around signs and symbols; as
    // character codes, so the source stays ASCII.
    private static readonly char[] s_spaces = [' ', (char)0x00A0, (char)0x202F];

    /// <summary>Parses invariant plain text such as the round-trip form of a double.</summary>
    public static bool TryParseInvariant(
        ReadOnlySpan<char> text,
        out ParsedNumberText parsed
    ) => TryParse(text, NumberFormatInfo.InvariantInfo, s_invariant, NumberTextStyle.Plain, out parsed, out _);

    public static bool TryParse(
        ReadOnlySpan<char> text,
        NumberFormatInfo format,
        NumberSymbols symbols,
        NumberTextStyle style,
        out ParsedNumberText parsed,
        out string? problem
    )
    {
        parsed = default;
        var body = text;
        var patterned = symbols.Affixes is { } affixes
            ? TryStripPattern(ref body, affixes)
            : Pattern.None;

        problem = null;
        if (patterned == Pattern.None
            && !TryStripStyle(ref body, style, format, out problem))
        {
            return false;
        }

        // WHY: The negative pattern of the culture carries the sign itself; a positive pattern still allows a sign
        // after the symbol, as before.
        var position = 0;
        var negative = patterned == Pattern.Negative || ReadSign(body, ref position, format);
        var integerStart = position;
        if (!TryReadInteger(body, ref position, symbols, out var integerDigits, out problem))
        {
            return false;
        }

        var integerPart = body[integerStart..position];
        var fraction = ReadFraction(body, ref position, symbols.Decimal, out var hadDecimalPoint);
        if (integerDigits == 0
            && fraction.Length == 0)
        {
            problem = "The text is not a number.";

            return false;
        }

        if (!TryReadExponent(body, ref position, format, out var exponent, out var hadExponent, out problem))
        {
            return false;
        }

        if (position != body.Length)
        {
            problem = "The text contains characters that are not part of a number.";

            return false;
        }

        var decimalExponent = exponent
            - fraction.Length
            - (style == NumberTextStyle.Percent
                ? 2
                : 0);

        parsed = integerDigits + fraction.Length <= SmallDigits
            ? new ParsedNumberText(negative,
                true,
                SmallMantissa(integerPart, fraction),
                BigInteger.Zero,
                decimalExponent,
                hadDecimalPoint,
                hadExponent)
            : new ParsedNumberText(negative,
                false,
                0,
                LargeMantissa(integerPart, integerDigits, fraction),
                decimalExponent,
                hadDecimalPoint,
                hadExponent);

        return true;
    }

    /// <summary>The mantissa of at most 19 digits; group separators between the integer digits are skipped.</summary>
    private static ulong SmallMantissa(
        ReadOnlySpan<char> integerPart,
        ReadOnlySpan<char> fraction
    )
    {
        var mantissa = 0UL;
        foreach (var character in integerPart)
        {
            if (char.IsAsciiDigit(character))
            {
                mantissa = mantissa * 10 + (ulong)(character - '0');
            }
        }

        foreach (var character in fraction)
        {
            mantissa = mantissa * 10 + (ulong)(character - '0');
        }

        return mantissa;
    }

    private static BigInteger LargeMantissa(
        ReadOnlySpan<char> integerPart,
        int integerDigits,
        ReadOnlySpan<char> fraction
    )
    {
        var length = integerDigits + fraction.Length;
        var rented = ArrayPool<char>.Shared.Rent(length);
        try
        {
            var digits = rented.AsSpan(0, length);
            var written = 0;
            foreach (var character in integerPart)
            {
                if (char.IsAsciiDigit(character))
                {
                    digits[written++] = character;
                }
            }

            fraction.CopyTo(digits[written..]);

            return BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Removes the affixes of the negative or the positive pattern of the culture; spaces around the affix characters
    /// may differ from those the culture writes.
    /// </summary>
    private static Pattern TryStripPattern(
        ref ReadOnlySpan<char> body,
        NumberAffixes affixes
    )
    {
        if (TryStripAffixes(ref body, affixes.NegativePrefix, affixes.NegativeSuffix))
        {
            return Pattern.Negative;
        }

        return TryStripAffixes(ref body, affixes.PositivePrefix, affixes.PositiveSuffix)
            ? Pattern.Positive
            : Pattern.None;
    }

    private static bool TryStripAffixes(
        ref ReadOnlySpan<char> body,
        string prefix,
        string suffix
    )
    {
        var start = 0;
        var end = body.Length;
        if (!TryMatchPrefix(body, prefix, ref start)
            || !TryMatchSuffix(body[start..], suffix, ref end, start))
        {
            return false;
        }

        body = body[start..end];

        return true;
    }

    private static bool TryMatchPrefix(
        ReadOnlySpan<char> body,
        string prefix,
        ref int position
    )
    {
        foreach (var character in prefix)
        {
            while (position < body.Length
                && Array.IndexOf(s_spaces, body[position]) >= 0)
            {
                position++;
            }

            if (position >= body.Length
                || body[position] != character)
            {
                return false;
            }

            position++;
        }

        while (prefix.Length > 0
            && position < body.Length
            && Array.IndexOf(s_spaces, body[position]) >= 0)
        {
            position++;
        }

        return true;
    }

    private static bool TryMatchSuffix(
        ReadOnlySpan<char> rest,
        string suffix,
        ref int end,
        int offset
    )
    {
        var position = rest.Length;
        for (var index = suffix.Length - 1; index >= 0; index--)
        {
            while (position > 0
                && Array.IndexOf(s_spaces, rest[position - 1]) >= 0)
            {
                position--;
            }

            if (position == 0
                || rest[position - 1] != suffix[index])
            {
                return false;
            }

            position--;
        }

        while (suffix.Length > 0
            && position > 0
            && Array.IndexOf(s_spaces, rest[position - 1]) >= 0)
        {
            position--;
        }

        end = offset + position;

        return true;
    }

    /// <summary>Removes the percent sign or currency symbol the style requires.</summary>
    private static bool TryStripStyle(
        ref ReadOnlySpan<char> body,
        NumberTextStyle style,
        NumberFormatInfo format,
        out string? problem
    )
    {
        problem = null;

        // WHY: The branches test the style together with an affix check that changes the body; a switch would hide that.
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (style == NumberTextStyle.Percent
            && !TryStripAffix(ref body, format.PercentSymbol))
        {
            problem = "The text does not contain the percent sign of the culture.";
        }
        else if (style == NumberTextStyle.Currency
            && !TryStripAffix(ref body, format.CurrencySymbol))
        {
            problem = "The text does not contain the currency symbol of the culture.";
        }

        return problem is null;
    }

    /// <summary>Reads a leading sign of the culture; returns whether it is negative.</summary>
    private static bool ReadSign(
        ReadOnlySpan<char> body,
        ref int position,
        NumberFormatInfo format
    )
    {
        if (format.NegativeSign.Length > 0
            && body.StartsWith(format.NegativeSign, StringComparison.Ordinal))
        {
            position = format.NegativeSign.Length;

            return true;
        }

        if (format.PositiveSign.Length > 0
            && body.StartsWith(format.PositiveSign, StringComparison.Ordinal))
        {
            position = format.PositiveSign.Length;
        }

        return false;
    }

    /// <summary>Reads the integer digits; group separators must follow the group sizes of the culture.</summary>
    private static bool TryReadInteger(
        ReadOnlySpan<char> body,
        ref int position,
        NumberSymbols symbols,
        out int digits,
        out string? problem
    )
    {
        // WHY: A group holds at least one digit, so a body of n characters has at most n + 1 groups.
        var groups = body.Length < 128
            ? stackalloc int[body.Length + 1]
            : new int[body.Length + 1];

        var groupCount = 0;
        var currentGroup = 0;
        digits = 0;
        while (position < body.Length)
        {
            if (char.IsAsciiDigit(body[position]))
            {
                digits++;
                currentGroup++;
                position++;
                continue;
            }

            // WHY: A separator without digits before it ends the loop; the check below reports the grouping.
            if (currentGroup == 0
                || !symbols.IsGroupSeparatorAt(body, position))
            {
                break;
            }

            groups[groupCount++] = currentGroup;
            currentGroup = 0;
            position += symbols.Group.Length;
        }

        var separatorWithoutDigits = position < body.Length && symbols.IsGroupSeparatorAt(body, position);
        var invalid = separatorWithoutDigits;
        if (!invalid
            && groupCount > 0)
        {
            groups[groupCount++] = currentGroup;
            invalid = !IsValidGrouping(groups[..groupCount], symbols.GroupSizes);
        }

        problem = invalid
            ? "The grouping of the number is invalid."
            : null;

        return !invalid;
    }

    private static ReadOnlySpan<char> ReadFraction(
        ReadOnlySpan<char> body,
        ref int position,
        string decimalSeparator,
        out bool hadDecimalPoint
    )
    {
        hadDecimalPoint = position < body.Length
            && body[position..]
                .StartsWith(decimalSeparator, StringComparison.Ordinal);

        if (!hadDecimalPoint)
        {
            return [];
        }

        position += decimalSeparator.Length;
        var start = position;
        while (position < body.Length
            && char.IsAsciiDigit(body[position]))
        {
            position++;
        }

        return body[start..position];
    }

    /// <summary>
    /// Reads the exponent. Its sign is the sign of the culture, as .NET writes it (for example U+2212 or a sign with
    /// a direction mark); the ASCII signs stay accepted.
    /// </summary>
    private static bool TryReadExponent(
        ReadOnlySpan<char> body,
        ref int position,
        NumberFormatInfo format,
        out int exponent,
        out bool hadExponent,
        out string? problem
    )
    {
        exponent = 0;
        problem = null;
        hadExponent = position < body.Length && body[position] is 'e' or 'E';
        if (!hadExponent)
        {
            return true;
        }

        position++;
        var rest = body[position..];
        var negative = false;
        if (format.NegativeSign.Length > 0
            && rest.StartsWith(format.NegativeSign, StringComparison.Ordinal))
        {
            negative = true;
            position += format.NegativeSign.Length;
        }
        else if (format.PositiveSign.Length > 0
            && rest.StartsWith(format.PositiveSign, StringComparison.Ordinal))
        {
            position += format.PositiveSign.Length;
        }
        else if (position < body.Length
            && body[position] is '+' or '-')
        {
            negative = body[position] == '-';
            position++;
        }

        var start = position;
        while (position < body.Length
            && char.IsAsciiDigit(body[position]))
        {
            position++;
        }

        if (position == start
            || position - start > 9)
        {
            problem = "The exponent of the number is invalid.";

            return false;
        }

        exponent = int.Parse(body[start..position], NumberStyles.None, CultureInfo.InvariantCulture);
        if (negative)
        {
            exponent = -exponent;
        }

        return true;
    }

    private static bool IsValidGrouping(
        ReadOnlySpan<int> groups,
        int[] groupSizes
    )
    {
        // WHY: The groups are ordered left to right; they are validated right to left against the group sizes.
        var sizeIndex = 0;
        for (var index = groups.Length - 1; index >= 1; index--)
        {
            var expected = groupSizes.Length == 0
                ? 3
                : groupSizes[Math.Min(sizeIndex, groupSizes.Length - 1)];

            if (expected == 0
                || groups[index] != expected)
            {
                return false;
            }

            sizeIndex++;
        }

        var leading = groupSizes.Length == 0
            ? 3
            : groupSizes[Math.Min(sizeIndex, groupSizes.Length - 1)];

        return groups[0] >= 1 && (leading == 0 || groups[0] <= leading);
    }

    private static bool TryStripAffix(
        ref ReadOnlySpan<char> body,
        string symbol
    )
    {
        if (symbol.Length == 0)
        {
            return false;
        }

        if (body.StartsWith(symbol, StringComparison.Ordinal))
        {
            body = TrimSpaces(body[symbol.Length..]);

            return true;
        }

        if (body.EndsWith(symbol, StringComparison.Ordinal))
        {
            body = TrimSpaces(body[..^symbol.Length]);

            return true;
        }

        return false;
    }

    private static ReadOnlySpan<char> TrimSpaces(
        ReadOnlySpan<char> text
    ) => text.Trim(s_spaces);

    private enum Pattern
    {
        None,
        Positive,
        Negative,
    }
}
