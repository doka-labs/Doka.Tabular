using System.Reflection;

namespace Doka.Tabular.Tests;

/// <summary>Every setting has one place: the column, or the builder of the base type of a custom conversion.</summary>
public sealed partial class ConversionTests
{
    [Fact]
    public void BuilderOfTheBaseType_OffersNoSettingOfTheColumn()
    {
        // Arrange
        string[] columnOnly =
        [
            "Header", "HeaderTranslationKey", "IncludeSourceValue", "ImportOnly", "ExportOnly", "OptionalHeader",
            "RejectFormulas", "ExcelAsText",
        ];

        // Act
        var methods = typeof(TabularBaseTypeBuilder<string>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToHashSet();

        // Assert
        Assert.All(columnOnly, name => Assert.DoesNotContain(name, methods));
        Assert.All(columnOnly,
            name => Assert.NotNull(typeof(TabularColumnBuilder<string>).GetMethod(name,
                BindingFlags.Public | BindingFlags.Instance)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConversionSettingOfAColumnWithACustomConversion_IsAConfigurationError(
        bool boolText
    )
    {
        // Act
        var exception = Record.Exception(() => boolText
            ? KeyedMap<bool>(c => c
                .TrueText("ja")
                .Convert<string, bool>(text => text == "1",
                    flag => flag
                        ? "1"
                        : "0"))
            : KeyedMap<Wrapped>(c => c
                .Culture(CultureInfo.GetCultureInfo("de-DE"))
                .Convert(number => new Wrapped($"{number}"), _ => 0m)));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public void ConversionSettingOfTheBaseTypeOfACustomConversion_Builds()
    {
        // Act
        var exception = Record.Exception(() => KeyedMap<Wrapped>(c => c.Convert(number => new Wrapped($"{number}"),
            _ => 0m,
            configure: b => b
                .Culture(CultureInfo.GetCultureInfo("de-DE"))
                .Rounding(2)
                .Required())));

        // Assert
        Assert.Null(exception);
    }
}
