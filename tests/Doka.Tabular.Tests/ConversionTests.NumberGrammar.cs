namespace Doka.Tabular.Tests;

/// <summary>
/// The exact reading of written number texts follows the culture parser of .NET: every text up to five characters
/// over signs, parentheses, digits, separators, an exponent, white space, and symbol characters that the parser
/// accepts within the decimal range reads as the same number.
/// </summary>
public sealed partial class ConversionTests
{
    private const string GrammarAlphabet = "()-+15.,E $";

    [Theory]
    [InlineData("en-US")]
    [InlineData("symbol -")]
    [InlineData("symbol +")]
    [InlineData("symbol E")]
    [InlineData("symbol 1$")]
    [InlineData("symbol .")]
    [InlineData("minus sign")]
    [InlineData("negative pattern 2")]
    public void EveryTextTheCultureParserAccepts_ReadsAsTheSameNumber(
        string kind
    )
    {
        // Arrange
        var format = GrammarCulture(kind);

        // Act
        // WHY: The comparison with the parser over all texts is the checked contract, so reading them is one action.
        var differences = GrammarTexts()
            .Where(text => decimal.TryParse(text, NumberStyles.Any, format, out _))
            .Where(text => !ReadsAsTheParser(text, format))
            .Take(5)
            .ToList();

        // Assert
        Assert.Empty(differences);
    }

    private static bool ReadsAsTheParser(
        string text,
        NumberFormatInfo format
    )
    {
        // WHY: The decimal parser runs the same grammar as the double one and keeps the few digits of these texts
        // exactly; the sign of a zero is no difference.
        var parsed = decimal.Parse(text, NumberStyles.Any, format);
        if (NumberRules.DigitsOf(text, format, double.IsNegative(double.Parse(text, NumberStyles.Any, format))) is not
            { } read)
        {
            return false;
        }

        var magnitude = decimal.Parse(
            $"{read.Mantissa}E{read.Exponent}",
            NumberStyles.Float,
            CultureInfo.InvariantCulture);

        return (read.Negative
                ? -magnitude
                : magnitude)
            == parsed;
    }

    private static IEnumerable<string> GrammarTexts()
    {
        var texts = new List<string> { "" };
        for (var length = 1; length <= 5; length++)
        {
            texts = [.. texts.SelectMany(text => GrammarAlphabet.Select(character => text + character))];
            foreach (var text in texts)
            {
                yield return text;
            }
        }
    }

    private static NumberFormatInfo GrammarCulture(
        string kind
    )
    {
        var format = (NumberFormatInfo)CultureInfo
            .GetCultureInfo("en-US")
            .NumberFormat
            .Clone();

        switch (kind)
        {
            case "symbol -":
                format.CurrencySymbol = "-";
                format.CurrencyDecimalSeparator = ",";
                format.CurrencyGroupSeparator = ".";
                break;
            case "symbol +":
                format.CurrencySymbol = "+";
                break;
            case "symbol E":
                format.CurrencySymbol = "E";
                break;
            case "symbol 1$":
                format.CurrencySymbol = "1$";
                break;
            case "symbol .":
                format.CurrencySymbol = ".";
                format.CurrencyDecimalSeparator = ",";
                break;
            case "minus sign":
                // WHY: The parser also takes a hyphen for a culture whose negative sign is the minus character.
                format.NegativeSign = $"{(char)0x2212}";
                break;
            case "negative pattern 2":
                format.NumberNegativePattern = 2;
                format.CurrencySymbol = "";
                break;
        }

        return format;
    }
}
