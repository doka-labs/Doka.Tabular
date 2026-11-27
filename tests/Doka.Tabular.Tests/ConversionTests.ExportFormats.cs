using System.Numerics;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// An export format is checked for keeping the value, independent of the input profile of the field, and must be a
/// valid .NET format of the type at Build.
/// </summary>
public sealed partial class ConversionTests
{
    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("C2", "12.5", "$12.50")]
    [InlineData("P1", "0.125", "12.5%")]
    [InlineData("N2", "1234.5", "1,234.50")]
    [InlineData("F3", "12.5", "12.500")]
    public async Task ExportFormatThatKeepsTheValue_IsWrittenWhateverTheInputProfile(
        string format,
        string value,
        string expected
    )
    {
        // Act
        var (report, file) = await Export.ExportAsync(
            new[] { new Value<decimal>(decimal.Parse(value, CultureInfo.InvariantCulture)) },
            Map<decimal>(c => c
                .Culture(s_english)
                .ExportFormat(format)),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(expected, Cells(TabularType.Csv, file!)[0][0]);
    }

    [Theory]
    [InlineData("C2", "12.5")]
    [InlineData("P1", "0.125")]
    [InlineData("N2", "1234.5")]
    [InlineData("F3", "12.5")]
    public async Task ExportFormatThatKeepsTheValue_IsWrittenForADouble(
        string format,
        string value
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<double>(double.Parse(value, CultureInfo.InvariantCulture)) },
            Map<double>(c => c
                .Culture(s_english)
                .ExportFormat(format)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Theory]
    [InlineData(0, "($12.50)")]
    [InlineData(3, "$12.50-")]
    [InlineData(15, "(12.50 $)")]
    public async Task NegativeCurrencyInAnyPatternOfTheCulture_KeepsTheValue(
        int pattern,
        string expected
    )
    {
        // Act
        var (report, file) = await Export.ExportAsync(
            new[] { new Value<decimal>(-12.5m) },
            Map<decimal>(c => c
                .Culture(NegativeCurrency(pattern))
                .ExportFormat("C2")),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(expected, Cells(TabularType.Csv, file!)[0][0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(15)]
    public async Task NegativeCurrencyInAnyPatternOfTheCulture_KeepsAnInteger(
        int pattern
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<int>(-12) },
            Map<int>(c => c
                .Culture(NegativeCurrency(pattern))
                .ExportFormat("C0")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(15)]
    public async Task NegativeCurrencyInAnyPatternOfTheCulture_KeepsADouble(
        int pattern
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<double>(-12.5) },
            Map<double>(c => c
                .Culture(NegativeCurrency(pattern))
                .ExportFormat("C2")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(15)]
    public async Task NegativeCurrencyInAnyPatternOfTheCulture_ThatChangesTheValue_IsAPrecisionLoss(
        int pattern
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>(-12.5m) },
            Map<decimal>(c => c
                .Culture(NegativeCurrency(pattern))
                .ExportFormat("C0")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, report.FatalError?.Code);
    }

    private static CultureInfo NegativeCurrency(
        int pattern
    )
    {
        var culture = (CultureInfo)s_english.Clone();
        culture.NumberFormat.CurrencyNegativePattern = pattern;

        return culture;
    }

    public static TheoryData<int, int> CurrencyPatterns()
    {
        var data = new TheoryData<int, int>();
        for (var negative = 0; negative <= 16; negative++)
        {
            data.Add(negative, negative % 4);
        }

        return data;
    }

    public static TheoryData<int, int> PercentPatterns()
    {
        var data = new TheoryData<int, int>();
        for (var negative = 0; negative <= 11; negative++)
        {
            data.Add(negative, negative % 4);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CurrencyPatterns))]
    public async Task CurrencyTextInEveryPatternOfTheCulture_ReadsBackUnderTheCurrencyStyle(
        int negativePattern,
        int positivePattern
    )
    {
        // Arrange
        var culture = (CultureInfo)s_english.Clone();
        culture.NumberFormat.CurrencyNegativePattern = negativePattern;
        culture.NumberFormat.CurrencyPositivePattern = positivePattern;
        var map = Map<decimal>(c => c
            .Culture(culture)
            .TextStyle(NumberTextStyle.Currency)
            .ExportFormat("C2"));

        var values = new[] { -1234.5m, 12.25m, 0m };

        // Act
        var report = await ReadBackAllAsync(values, map);

        // Assert
        Assert.Equal(values, report.Items!.Select(item => item.Item));
    }

    [Theory]
    [MemberData(nameof(PercentPatterns))]
    public async Task PercentTextInEveryPatternOfTheCulture_ReadsBackUnderThePercentStyle(
        int negativePattern,
        int positivePattern
    )
    {
        // Arrange
        var culture = (CultureInfo)s_english.Clone();
        culture.NumberFormat.PercentNegativePattern = negativePattern;
        culture.NumberFormat.PercentPositivePattern = positivePattern;
        var map = Map<decimal>(c => c
            .Culture(culture)
            .TextStyle(NumberTextStyle.Percent)
            .ExportFormat("P1"));

        var values = new[] { -0.125m, 0.5m, 0m };

        // Act
        var report = await ReadBackAllAsync(values, map);

        // Assert
        Assert.Equal(values, report.Items!.Select(item => item.Item));
    }

    [Fact]
    public async Task ExportFormatThatChangesTheValue_StaysAPrecisionLoss()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>(12.5m) },
            Map<decimal>(c => c
                .Culture(s_english)
                .ExportFormat("C0")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss, report.FatalError?.Code);
    }

    [Theory]
    [InlineData("int", "Q")]
    [InlineData("decimal", "X")]
    [InlineData("double?", "D2")]
    [InlineData("BigInteger", "Q")]
    [InlineData("Half", "X")]
    public void InvalidExportFormatOfTheType_IsAConfigurationErrorAtBuild(
        string type,
        string format
    )
    {
        // Act
        var exception = Record.Exception(() => MapWithExportFormat(type, format));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData("int", "X")]
    [InlineData("long?", "D5")]
    [InlineData("decimal", "0.00")]
    public void ValidExportFormatOfTheType_Builds(
        string type,
        string format
    )
    {
        // Act
        var exception = Record.Exception(() => MapWithExportFormat(type, format));

        // Assert
        Assert.Null(exception);
    }

    private static object MapWithExportFormat(
        string type,
        string format
    ) => type switch
    {
        "int" => Map<int>(c => c.ExportFormat(format)),
        "long?" => Map<long?>(c => c.ExportFormat(format)),
        "decimal" => Map<decimal>(c => c.ExportFormat(format)),
        "double?" => Map<double?>(c => c.ExportFormat(format)),
        "BigInteger" => Map<BigInteger>(c => c.ExportFormat(format)),
        _ => Map<Half>(c => c.ExportFormat(format)),
    };

    /// <summary>Exports the values as one file and reads it back.</summary>
    private static async Task<ImportReport<Value<T>>> ReadBackAllAsync<T>(
        IEnumerable<T> values,
        TabularMap<Value<T>> map
    )
    {
        var file = new MemoryStream();
        await CsvInstance.ExportAsync(values.Select(value => new Value<T>(value)), map, file, Token);
        file.Position = 0;

        return await CsvInstance.ImportAsync(file, map, Token);
    }
}
