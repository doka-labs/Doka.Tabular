using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Rounding of binary floating point values, float precision, decimal overflow, and untrimmed Guid text.</summary>
public sealed partial class ConversionTests
{
    private static readonly TabularMap<Value<decimal>> s_oneDigitDecimal = Map<decimal>(c => c
        .AllowExponent()
        .SignificantDigitsRounding(1, MidpointRounding.ToZero));

    [Fact]
    public async Task BinaryUnderflowToZero_IsAnErrorAlsoWithRounding()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "1e-400", Map<double>(c => c.Rounding(2)));

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, ErrorOf(report));
    }

    [Fact]
    public async Task NativeUnderflowToZero_IsAnErrorAlsoWithRounding()
    {
        // Act
        var report = await ReadAsync(TabularType.Excel, 1e-100, Map<float>(c => c.Rounding(2)));

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, ErrorOf(report));
    }

    [Fact]
    public async Task RoundingToZero_OfARepresentableValue_IsZero()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "0.001", Map<double>(c => c.Rounding(2)));

        // Assert
        Assert.Equal(0d, ValueOf(report));
    }

    [Fact]
    public async Task NativeOverflow_IsAnErrorAlsoWithRounding()
    {
        // Arrange
        // WHY: 66000 is above Half.MaxValue 65504; rounding the source to one digit would give a representable 60000.
        var rounded = Map<Half>(c => c.SignificantDigitsRounding(1, MidpointRounding.ToZero));

        // Act
        var report = await ReadAsync(TabularType.Excel, 66000d, rounded);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, ErrorOf(report));
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ExportRoundingBeyondTheRange_IsARangeError(
        TabularType type
    )
    {
        // Act
        // WHY: Half.MaxValue 65504 rounded to one significant digit is 70000, above the range of Half.
        var (report, _) = await Export.ExportAsync(
            new[] { new Value<Half>(Half.MaxValue) },
            Map<Half>(c => c.SignificantDigitsRounding(1)),
            type,
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, report.FatalError?.Code);
    }

    [Fact]
    public async Task FieldCulture_IsFrozenForTheCallAndCurrentForTheNext()
    {
        // Arrange
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        var map = Map<decimal>(c => c.Culture(culture));

        // Act
        // WHY: The change takes effect only between calls, so the changing call and the next one form one action.
        var (_, first) = await CsvInstance.ExportAsync(ChangingSeparator(culture), map, Token);
        var (_, second) = await CsvInstance.ExportAsync(new[] { new Value<decimal>(1.5m) }, map, Token);

        // Assert
        Assert.Equal([["1.5"], ["1.5"]], Cells(TabularType.Csv, first!));
        Assert.Equal([["1,5"]], Cells(TabularType.Csv, second!));
    }

    private static IEnumerable<Value<decimal>> ChangingSeparator(
        CultureInfo culture
    )
    {
        yield return new Value<decimal>(1.5m);

        culture.NumberFormat.NumberDecimalSeparator = ",";

        yield return new Value<decimal>(1.5m);
    }

    [Fact]
    public async Task SubnormalDouble_IsNoNativeExcelNumber()
    {
        // Act
        var (report, _) = await ExcelInstance.ExportAsync(new[] { new Value<double>(1e-320) }, Map<double>(), Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, report.FatalError?.Code);
    }

    [Fact]
    public async Task SmallestNormalRangeDouble_ReadsBackFromExcel()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) =
            await ExcelInstance.ExportAsync(new[] { new Value<double>(2.3e-308) }, Map<double>(), Token);

        var report = await ExcelInstance.ImportAsync(file!, Map<double>(), Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        Assert.Equal(
            2.3e-308,
            Assert.Single(report.Items!)
                .Item);
    }

    [Fact]
    public async Task DoubleRounding_StartsAtTheBinaryValue()
    {
        // Act
        // WHY: The double nearest to 2.675 is slightly below it, so rounding to two decimals gives 2.67 as on export.
        var report = await ReadAsync(TabularType.Csv, "2.675", Map<double>(c => c.Rounding(2)));

        // Assert
        Assert.Equal(2.67, ValueOf(report));
    }

    [Theory]
    [InlineData("F9", true)]
    [InlineData("F0", false)]
    public async Task FloatExportFormat_IsAcceptedWhenItReadsBackToTheSameFloat(
        string format,
        bool accepted
    )
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<float>(0.1f) },
            Map<float>(c => c.ExportFormat(format)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(accepted, report.Status == ReportStatus.Succeeded);
    }

    [Fact]
    public async Task Float_WithAnExportFormat_IsFormattedWithItsOwnPrecision()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<float>(0.1f) },
            Map<float>(c => c.ExportFormat("F1")),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task Float_ReadsBackFromExcelWithItsOwnPrecision()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) = await ExcelInstance.ExportAsync(
            new[] { new Value<float>(0.1f), new Value<float>(1.2f) },
            Map<float>(),
            Token);

        var report = await ExcelInstance.ImportAsync(file!, Map<float>(), Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        Assert.Equal([0.1f, 1.2f], report.Items!.Select(item => item.Item));
    }

    [Theory]
    [InlineData("79228162514264337593543950336")]
    [InlineData("-79228162514264337593543950335.5")]
    [InlineData("1e29")]
    public async Task DecimalAboveTheRange_StaysARangeErrorWithRounding(
        string text
    )
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, text, s_oneDigitDecimal);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, ErrorOf(report));
    }

    [Fact]
    public async Task DecimalMaximum_IsRoundedToOneSignificantDigit()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "79228162514264337593543950335", s_oneDigitDecimal);

        // Assert
        Assert.Equal(7e28m, ValueOf(report));
    }

    [Fact]
    public async Task DecimalRoundingBeyondTheRange_IsAnError()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<decimal>(decimal.MaxValue) },
            Map<decimal>(c => c.SignificantDigitsRounding(1)),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task Guid_WithTrimDisabled_RejectsSurroundingSpaces()
    {
        // Act
        var report = await ReadAsync(
            TabularType.Csv,
            " 6f9619ff-8b86-d011-b42d-00c04fc964ff ",
            Map<Guid>(c => c.Trim(false)));

        // Assert
        Assert.NotEmpty(Codes(report));
    }
}
