using System.Numerics;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// Number texts in the export format, the percent style, and the currency style: a written text must denote exactly
/// the value, with the symbols of the style, whatever digits the culture puts into its own symbols.
/// </summary>
public sealed partial class ConversionTests
{
    private static readonly CultureInfo s_enUs = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("0;(0.0000000000000000000000000000'1')")]
    [InlineData("0;0.0000000000000000000000000000'1'-")]
    [InlineData("0.00;(0.00'000000000000000000000000001')")]
    public async Task ExportFormatWithALiteralDigit_ThatChangesTheDecimal_IsAPrecisionLoss(
        string format
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>(-1m) },
            Map<decimal>(c => c
                .Culture(s_enUs)
                .ExportFormat(format)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, report.FatalError?.Code);
    }

    [Theory]
    [InlineData("0;(0)", "(1)")]
    [InlineData("0.00;0.00-", "1.00-")]
    [InlineData("#,##0.00;(#,##0.00)", "(1.00)")]
    public async Task ExportFormatWithParenthesesOrATrailingSign_ThatKeepsTheDecimal_IsWritten(
        string format,
        string expected
    )
    {
        // Act
        var (report, file) = await Export.ExportAsync(
            new[] { new Value<decimal>(-1m) },
            Map<decimal>(c => c
                .Culture(s_enUs)
                .ExportFormat(format)),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(expected, Cells(TabularType.Csv, file!)[0][0]);
    }

    [Theory]
    [InlineData(-13, "0;('1E+3')")]
    [InlineData(-1, "0;(0'E+3')")]
    [InlineData(-1, "0;(0'E-3')")]
    [InlineData(-0.00125, "0;(0.0E+0)")]
    [InlineData(0, "0;0;'Infinity'")]
    [InlineData(0, "0;0;'NaN'")]
    [InlineData(-1, "0;('1E+99999999999')")]
    public async Task ExportFormatWhoseExponentChangesTheValue_IsAPrecisionLoss(
        double value,
        string format
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>((decimal)value) },
            Map<decimal>(c => c
                .Culture(s_enUs)
                .ExportFormat(format)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, report.FatalError?.Code);
    }

    [Theory]
    [InlineData(-1000, "0;(0E+0)", "(1E+3)")]
    [InlineData(-0.0015, "0.0E+0;(0.0E+0)", "(1.5E-3)")]
    [InlineData(-1500, "0;(0.0e0)", "(1.5e3)")]
    [InlineData(-12, "0;0.0E+00-", "1.2E+01-")]
    public async Task ExportFormatWithAnExponentThatKeepsTheValue_IsWritten(
        double value,
        string format,
        string expected
    )
    {
        // Act
        var (report, file) = await Export.ExportAsync(
            new[] { new Value<decimal>((decimal)value) },
            Map<decimal>(c => c
                .Culture(s_enUs)
                .ExportFormat(format)),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(expected, Cells(TabularType.Csv, file!)[0][0]);
    }

    [Theory]
    [InlineData(-1, "0;('123')", false)]
    [InlineData(-123, "0;(0)", true)]
    [InlineData(-231, "0;(0)", false)]
    [InlineData(-1, "0;('23'0)", true)]
    public async Task ExportFormatUnderANumericCurrencySymbol_IsReadAsTheCultureParserReadsIt(
        int value,
        string format,
        bool written
    )
    {
        // Arrange
        // WHY: The parser takes the symbol 23 only before or after the digits, never out of them: (123) is -123, and
        // (231) is the symbol before the digit 1.
        var culture = (CultureInfo)s_enUs.Clone();
        culture.NumberFormat.CurrencySymbol = "23";
        culture.NumberFormat.CurrencyNegativePattern = 0;

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>(value) },
            Map<decimal>(c => c
                .Culture(culture)
                .ExportFormat(format)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(
            written
                ? null
                : TabularErrorCodes.Conversion.PrecisionLoss,
            report.FatalError?.Code);
    }

    [Theory]
    [InlineData(-15, "0;('1,5')", false)]
    [InlineData(-1.5, "0.0;(0.0)", true)]
    [InlineData(-1500, "#,##0;(#,##0)", false)]
    public async Task ExportFormatWithTheCurrencySeparators_IsReadAsTheCultureParserReadsIt(
        double value,
        string format,
        bool written
    )
    {
        // Arrange
        // WHY: The parser accepts the currency separators also without a symbol, so (1,5) and the grouped (1,500) are
        // -1.5 here; the number separators apply only while no currency separator matches.
        var culture = (CultureInfo)s_enUs.Clone();
        culture.NumberFormat.CurrencyDecimalSeparator = ",";
        culture.NumberFormat.CurrencyGroupSeparator = ".";

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>((decimal)value) },
            Map<decimal>(c => c
                .Culture(culture)
                .ExportFormat(format)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(
            written
                ? null
                : TabularErrorCodes.Conversion.PrecisionLoss,
            report.FatalError?.Code);
    }

    [Theory]
    [InlineData("-", -1.5, "0;('-1.5')", false)]
    [InlineData("-", -15, "0;('-1.5')", true)]
    [InlineData("+", -1, "0;('+1')", true)]
    [InlineData("$", -1, "0;('+1')", false)]
    [InlineData("$", -1, "0;('-1')", false)]
    public async Task ExportFormatWithASignAfterTheParenthesis_IsReadAsTheCultureParserReadsIt(
        string symbol,
        double value,
        string format,
        bool written
    )
    {
        // Arrange
        // WHY: The parenthesis is the sign of the text, so the parser takes a following sign character only as the
        // currency symbol: with the symbol - and the currency separators , and ., (-1.5) is -15; with another symbol
        // the text is no number.
        var culture = (CultureInfo)s_enUs.Clone();
        culture.NumberFormat.CurrencySymbol = symbol;
        culture.NumberFormat.CurrencyDecimalSeparator = ",";
        culture.NumberFormat.CurrencyGroupSeparator = ".";

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>((decimal)value) },
            Map<decimal>(c => c
                .Culture(culture)
                .ExportFormat(format)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(
            written
                ? null
                : TabularErrorCodes.Conversion.PrecisionLoss,
            report.FatalError?.Code);
    }

    [Fact]
    public async Task ExportFormatWithALiteralDigit_OfAnInteger_ThatChangesTheValue_IsAPrecisionLoss()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<long>(-1) },
            Map<long>(c => c
                .Culture(s_enUs)
                .ExportFormat("0;(0'.5')")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, report.FatalError?.Code);
    }

    [Fact]
    public async Task ExportFormatWithParentheses_OfAnIntegerBeyondTheDecimalRange_IsWritten()
    {
        // Arrange
        var value = -BigInteger.Pow(10, 40);

        // Act
        var (report, file) = await Export.ExportAsync(
            new[] { new Value<BigInteger>(value) },
            Map<BigInteger>(c => c
                .Culture(s_enUs)
                .ExportFormat("0;(0)")),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal($"({BigInteger.Pow(10, 40)})", Cells(TabularType.Csv, file!)[0][0]);
    }

    [Fact]
    public async Task PercentExportFormat_UsesThePercentSeparatorsOfTheCulture()
    {
        // Act
        var (report, file) = await Export.ExportAsync(
            new[] { new Value<decimal>(0.125m) },
            Map<decimal>(c => c
                .Culture(PercentCommaCulture())
                .ExportFormat("P1")),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal("12,5%", Cells(TabularType.Csv, file!)[0][0]);
    }

    [Fact]
    public async Task PercentStyle_WritesThePercentDecimalSeparatorOfTheCulture()
    {
        // Act
        var (_, file) = await Export.ExportAsync(
            new[] { new Value<decimal>(0.125m) },
            Map<decimal>(c => c
                .Culture(PercentCommaCulture())
                .TextStyle(NumberTextStyle.Percent)),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal("12,5%", Cells(TabularType.Csv, file!)[0][0]);
    }

    [Theory]
    [InlineData("12,5 %")]
    [InlineData("12,5%")]
    [InlineData("1.212,5 %")]
    public async Task PercentStyle_ReadsThePercentSeparatorsOfTheCulture(
        string text
    )
    {
        // Act
        var report = await ReadAsync(
            TabularType.Csv,
            text,
            Map<decimal>(c => c
                .Culture(PercentCommaCulture())
                .TextStyle(NumberTextStyle.Percent)));

        // Assert
        Assert.Equal(
            decimal.Parse(
                text
                    .Replace(".", "", StringComparison.Ordinal)
                    .Replace(",", ".", StringComparison.Ordinal)
                    .TrimEnd('%', ' '),
                CultureInfo.InvariantCulture)
            / 100m,
            ValueOf(report));
    }

    [Theory]
    [InlineData("1$", 0, "(1$12.50)", -12.5)]
    [InlineData("1$", 0, "1$12.50", 12.5)]
    [InlineData("$1", 3, "$112.50-", -12.5)]
    [InlineData("2", 8, "-12.50 2", -12.5)]
    public async Task CurrencySymbolWithDigits_IsReadUnderTheCurrencyStyle(
        string symbol,
        int negativePattern,
        string text,
        double expected
    )
    {
        // Arrange
        var culture = (CultureInfo)s_enUs.Clone();
        culture.NumberFormat.CurrencySymbol = symbol;
        culture.NumberFormat.CurrencyNegativePattern = negativePattern;

        // Act
        var report = await ReadAsync(
            TabularType.Csv,
            text,
            Map<decimal>(c => c
                .Culture(culture)
                .TextStyle(NumberTextStyle.Currency)));

        // Assert
        Assert.Equal((decimal)expected, ValueOf(report));
    }

    [Theory]
    [InlineData("1$", 0)]
    [InlineData("$1", 3)]
    public async Task CurrencySymbolWithDigits_ExportFormatC2_IsWrittenAndReadsBack(
        string symbol,
        int negativePattern
    )
    {
        // Arrange
        var culture = (CultureInfo)s_enUs.Clone();
        culture.NumberFormat.CurrencySymbol = symbol;
        culture.NumberFormat.CurrencyNegativePattern = negativePattern;
        var map = Map<decimal>(c => c
            .Culture(culture)
            .TextStyle(NumberTextStyle.Currency)
            .ExportFormat("C2"));

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var report = await ReadBackAllAsync([-12.5m, 12.5m], map);

        // Assert
        Assert.Equal([-12.5m, 12.5m], report.Items!.Select(item => item.Item));
    }

    [Fact]
    public async Task CurrencySymbolOfOnlyDigitsBeforeTheAmount_IsAmbiguousAndAPrecisionLoss()
    {
        // Arrange
        // WHY: With the symbol 2 before the amount, 12.50 is written as 212.50, which a reader takes for 212.5.
        var culture = (CultureInfo)s_enUs.Clone();
        culture.NumberFormat.CurrencySymbol = "2";

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>(12.5m) },
            Map<decimal>(c => c
                .Culture(culture)
                .TextStyle(NumberTextStyle.Currency)
                .ExportFormat("C2")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, report.FatalError?.Code);
    }

    /// <summary>A copy of en-US whose percent values use a comma as decimal and a period as group separator.</summary>
    private static CultureInfo PercentCommaCulture()
    {
        var culture = (CultureInfo)s_enUs.Clone();
        culture.NumberFormat.PercentDecimalSeparator = ",";
        culture.NumberFormat.PercentGroupSeparator = ".";

        return culture;
    }
}
