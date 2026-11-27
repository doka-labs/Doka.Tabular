namespace Doka.Tabular.Tests;

/// <summary>
/// A column that Excel writes as text has no use for a number format: ExcelNumberFormat together with ExcelAsText or
/// NonFiniteValues is a configuration error at <c>Build()</c>.
/// </summary>
public sealed partial class ConversionTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("decimal")]
    [InlineData("DateOnly")]
    [InlineData("TimeSpan")]
    public void ExcelNumberFormat_WithExcelAsText_IsAConfigurationError(
        string type
    )
    {
        // Act
        var exception = Record.Exception(() => type switch
        {
            "int" => Map<int>(c => c
                .ExcelNumberFormat("0")
                .ExcelAsText()),
            "decimal" => Map<decimal>(c => c
                .ExcelNumberFormat("0.00")
                .ExcelAsText()),
            "DateOnly" => Map<DateOnly>(c => c
                .ExcelNumberFormat("dd.mm.yyyy")
                .ExcelAsText()),
            _ => Map<TimeSpan>(c => c
                .ExcelNumberFormat("[h]:mm")
                .ExcelAsText()),
        });

        // Assert
        Assert.Contains(
            Assert
                .IsType<TabularConfigurationException>(exception)
                .Errors,
            error => error.Message.Contains("ExcelNumberFormat", StringComparison.Ordinal)
                && error.Message.Contains("ExcelAsText", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcelNumberFormatOfTheBaseType_WithExcelAsTextOfTheColumn_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => Map<CustomerNumber>(c => c
            .ExcelAsText()
            .Convert(
                read: number => new CustomerNumber($"{number}"),
                write: _ => 0m,
                configure: b => b.ExcelNumberFormat("0.00"))));

        // Assert
        Assert.Contains(
            Assert
                .IsType<TabularConfigurationException>(exception)
                .Errors,
            error => error.Message.Contains("ExcelAsText", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcelNumberFormat_WithNonFiniteValues_IsAConfigurationError(
        bool nullable
    )
    {
        // Act
        var exception = Record.Exception(() => nullable
            ? Map<double?>(c => c
                .ExcelNumberFormat("0.00")
                .NonFiniteValues())
            : Map<double>(c => c
                .NonFiniteValues()
                .ExcelNumberFormat("0.00")));

        // Assert
        Assert.Contains(
            Assert
                .IsType<TabularConfigurationException>(exception)
                .Errors,
            error => error.Message.Contains("ExcelNumberFormat", StringComparison.Ordinal)
                && error.Message.Contains("NonFiniteValues", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("format only")]
    [InlineData("text only")]
    [InlineData("non-finite only")]
    public void ExcelNumberFormat_ExcelAsText_AndNonFiniteValues_EachBuildAlone(
        string kind
    )
    {
        // Act
        var exception = Record.Exception(() => kind switch
        {
            "format only" => Map<double>(c => c.ExcelNumberFormat("0.00")),
            "text only" => Map<double>(c => c.ExcelAsText()),
            _ => (object)Map<double>(c => c.NonFiniteValues()),
        });

        // Assert
        Assert.Null(exception);
    }
}
