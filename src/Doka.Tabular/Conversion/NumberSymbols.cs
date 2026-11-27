namespace Doka.Tabular;

/// <summary>The decimal and group symbols of a culture for one number style, with its currency or percent patterns.</summary>
internal readonly record struct NumberSymbols(
    string Decimal,
    string Group,
    int[] GroupSizes,
    NumberAffixes? Affixes
)
{
    public static NumberSymbols Of(
        NumberFormatInfo format,
        NumberTextStyle style
    ) => style switch
    {
        NumberTextStyle.Currency => new NumberSymbols(
            format.CurrencyDecimalSeparator,
            format.CurrencyGroupSeparator,
            format.CurrencyGroupSizes,
            NumberAffixes.Of(amount => amount.ToString("C0", format))),

        // WHY: The P format writes with the percent separators, which can differ from the number ones.
        NumberTextStyle.Percent => new NumberSymbols(
            format.PercentDecimalSeparator,
            format.PercentGroupSeparator,
            format.PercentGroupSizes,
            NumberAffixes.Of(amount => (amount / 100m).ToString("P0", format))),
        _ => new NumberSymbols(
            format.NumberDecimalSeparator,
            format.NumberGroupSeparator,
            format.NumberGroupSizes,
            null),
    };

    /// <summary>Gets whether a group separator, distinct from the decimal separator, starts at the position.</summary>
    public bool IsGroupSeparatorAt(
        ReadOnlySpan<char> body,
        int position
    ) => Group.Length > 0
        && !string.Equals(Group, Decimal, StringComparison.Ordinal)
        && body[position..]
            .StartsWith(Group, StringComparison.Ordinal);
}

/// <summary>
/// The texts a culture puts before and after a positive and a negative currency or percent amount, without their spaces,
/// as .NET writes them for the patterns of the culture, such as <c>($1)</c> or <c>-1 %</c>.
/// </summary>
internal sealed record NumberAffixes(
    string PositivePrefix,
    string PositiveSuffix,
    string NegativePrefix,
    string NegativeSuffix
)
{
    /// <summary>The affixes of the positive and the negative pattern of a writer of whole amounts.</summary>
    public static NumberAffixes Of(
        Func<decimal, string> write
    )
    {
        var (positivePrefix, positiveSuffix) = Split(write(1m), write(2m));
        var (negativePrefix, negativeSuffix) = Split(write(-1m), write(-2m));

        return new NumberAffixes(positivePrefix, positiveSuffix, negativePrefix, negativeSuffix);
    }

    /// <summary>
    /// Splits the amounts 1 and 2 written in one pattern into the texts they share before and after the amount.
    /// </summary>
    /// <remarks>
    /// WHY: A currency symbol may contain digits, such as <c>1$</c>; the amount is the one character in which both texts
    /// differ, so the shared prefix and suffix are exactly the affixes, whatever the symbol contains.
    /// </remarks>
    private static (string Prefix, string Suffix) Split(
        string one,
        string two
    )
    {
        var prefix = one
            .AsSpan()
            .CommonPrefixLength(two);

        var suffix = 0;
        while (suffix < one.Length - prefix
            && suffix < two.Length - prefix
            && one[^(suffix + 1)] == two[^(suffix + 1)])
        {
            suffix++;
        }

        return (WithoutSpaces(one[..prefix]), WithoutSpaces(one[^suffix..]));
    }

    private static string WithoutSpaces(
        string text
    ) => string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
}
