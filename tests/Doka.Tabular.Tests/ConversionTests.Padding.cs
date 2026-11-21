using System.Text.Json.Serialization;

namespace Doka.Tabular.Tests;

/// <summary>Own output reads back: padded labels and formats under trim, and the separators of a number style.</summary>
public sealed partial class ConversionTests
{
    public enum Padded
    {
        [JsonStringEnumMemberName(" caption ")]
        Ready = 1,
    }

    [Fact]
    public async Task PaddedBoolText_ReadsBackUnderTrim()
    {
        // Act
        var report = await ReadBackAsync(true,
            Map<bool>(c => c
                .TrueText(" yes ")
                .Trim()));

        // Assert
        Assert.True(ValueOf(report));
    }

    [Fact]
    public async Task PaddedBoolText_IsReadUnderTrim()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv,
            " yes ",
            Map<bool>(c => c
                .TrueText(" yes ")
                .Trim()));

        // Assert
        Assert.True(ValueOf(report));
    }

    [Fact]
    public async Task PaddedEnumLabel_ReadsBackUnderTrim()
    {
        // Act
        var report = await ReadBackAsync(Padded.Ready, Map<Padded>(c => c.Trim()));

        // Assert
        Assert.Equal(Padded.Ready, ValueOf(report));
    }

    [Fact]
    public async Task CurrencyText_ReadsBackWithTheCurrencyDecimalSeparator()
    {
        // Act
        var report = await ReadBackAsync(12.5m, UsdCurrencyText());

        // Assert
        Assert.Equal(12.5m, ValueOf(report));
    }

    [Fact]
    public async Task CurrencyText_IsReadWithTheCurrencyDecimalSeparator()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "USD12,5", UsdCurrencyText());

        // Assert
        Assert.Equal(12.5m, ValueOf(report));
    }

    private static TabularMap<Value<decimal>> UsdCurrencyText()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.CurrencySymbol = "USD";
        culture.NumberFormat.CurrencyDecimalSeparator = ",";
        culture.NumberFormat.CurrencyGroupSeparator = " ";
        culture.NumberFormat.NumberDecimalSeparator = ".";

        return Map<decimal>(c => c
            .TextStyle(NumberTextStyle.Currency)
            .Culture(culture));
    }
}
