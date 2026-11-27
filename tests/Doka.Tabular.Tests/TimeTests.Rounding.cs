using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Rounding and range of native Excel durations.</summary>
public sealed partial class TimeTests
{
    private static readonly TabularMap<Value<DateOnly>> s_twoDays =
        Map<DateOnly>(c => c.Rounding(TimeSpan.FromDays(2), MidpointRounding.ToZero));

    [Fact]
    public async Task NativeDuration_BeyondTheRangeWithRounding_IsARangeError()
    {
        // Arrange
        var map = Map<TimeSpan>(c => c.Rounding(TimeSpan.FromTicks(1)));

        // Act
        var report = await ReadAsync(null, map, null, Serial(1e20));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task NativeDuration_IsRoundedOnceToTheStep()
    {
        // Arrange
        // WHY: 2.6 ticks rounded first to whole ticks (3) and then to the step would give 4; the step alone gives 2.
        var map = Map<TimeSpan>(c => c.Rounding(TimeSpan.FromTicks(2)));

        // Act
        var report = await ReadAsync(null, map, null, Serial(2.6 / TimeSpan.TicksPerDay));

        // Assert
        Assert.Equal(TimeSpan.FromTicks(2), ValueOf(report));
    }

    [Theory]
    [InlineData(5_000_000L)]
    [InlineData(2_500_000L)]
    [InlineData(1_230_000L)]
    [InlineData(10_000_000L)]
    public async Task NativeExcelTimestampWithRounding_IsWrittenOnlyWhenTheImportReadsTheSameValue(
        long fraction
    )
    {
        // Arrange
        // WHY: The import reads a native number with the rounding of the field; the export check must read it the
        // same way, so a successful export always reads back unchanged.
        var stamp = new DateTime(2024, 1, 1).AddTicks(fraction);
        var map = Map<DateTime>(c => c.Rounding(TimeSpan.FromTicks(1)));

        // Act
        // WHY: A refused export returns no file; the empty input then reads nothing, which the assertion allows.
        var (export, file) =
            await ExcelInstance.Format.ExportAsync(new[] { new Value<DateTime>(1, stamp) }, map, Token);

        var report = await ExcelInstance.ImportAsync(file ?? new MemoryStream(), map, Token);

        // Assert
        Assert.True(
            export.Status != ReportStatus.Succeeded || (report.Items is [{ Item: var read }] && read == stamp),
            $"{stamp:O} was written but did not read back");
    }

    [Theory]
    [InlineData(5_000_000L)]
    [InlineData(2_500_000L)]
    [InlineData(1_230_000L)]
    [InlineData(10_000_000L)]
    public async Task NativeExcelTimeWithRounding_IsWrittenOnlyWhenTheImportReadsTheSameValue(
        long fraction
    )
    {
        // Arrange
        var time = new TimeOnly(fraction);
        var map = Map<TimeOnly>(c => c.Rounding(TimeSpan.FromTicks(1)));

        // Act
        // WHY: A refused export returns no file; the empty input then reads nothing, which the assertion allows.
        var (export, file) = await ExcelInstance.Format.ExportAsync(new[] { new Value<TimeOnly>(1, time) }, map, Token);
        var report = await ExcelInstance.ImportAsync(file ?? new MemoryStream(), map, Token);

        // Assert
        Assert.True(
            export.Status != ReportStatus.Succeeded || (report.Items is [{ Item: var read }] && read == time),
            $"{time:O} was written but did not read back");
    }

    [Fact]
    public async Task NativeDuration_BeyondTheRange_StaysARangeErrorWhenRoundingWouldBringItInside()
    {
        // Arrange
        // WHY: 2^63 ticks lie one tick above TimeSpan.MaxValue; a step of three toward zero would give 2^63 - 2.
        var map = Map<TimeSpan>(c => c
            .ExcelDuration(ExcelDurationUnit.Ticks)
            .Rounding(TimeSpan.FromTicks(3), MidpointRounding.ToZero));

        // Act
        var report = await ReadAsync(null, map, null, Serial(9223372036854775808d));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData("10675199.02:48:05.47758079")]
    [InlineData("-10675199.02:48:05.47758081")]
    public async Task DurationTextJustBeyondTheRange_StaysARangeErrorWithRounding(
        string text
    )
    {
        // Act
        var report = await ReadAsync(
            text,
            Map<TimeSpan>(c => c.Rounding(TimeSpan.FromTicks(1), MidpointRounding.ToZero)),
            null,
            null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task DateTimeTextJustBeyondTheRange_StaysARangeErrorWithRounding()
    {
        // Act
        var report = await ReadAsync(
            "9999-12-31T23:59:59.99999999",
            Map<DateTime>(c => c.Rounding(TimeSpan.FromTicks(1), MidpointRounding.ToZero)),
            null,
            null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task DateTimeOffsetTextJustBeyondTheRange_StaysARangeErrorWithRounding()
    {
        // Act
        var report = await ReadAsync(
            "9999-12-31T23:59:59.99999999Z",
            Map<DateTimeOffset>(c => c.Rounding(TimeSpan.FromTicks(1), MidpointRounding.ToZero)),
            null,
            null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task NativeTimestampOfAFileZone_IsRoundedOnceToTheStep()
    {
        // Arrange
        // WHY: 7 * 2^-42 days are 1.375 ticks; the nearest tick 1 rounded again to two ticks gives 0, the step alone 2.
        var map = Map<DateTime>(c => c
            .FileTimeZone(TimeZoneInfo.Utc)
            .Rounding(TimeSpan.FromTicks(2)));

        // Act
        var report = await ReadAsync(null, map, null, Serial(1 + 7 * Math.Pow(2, -42)));

        // Assert
        Assert.Equal(
            new DateTime(1900, 1, 1).Ticks + 2,
            ValueOf(report)
                .Ticks);
    }

    [Fact]
    public async Task ExportRoundingOfADurationBeyondTheRange_IsARangeErrorOfTheField()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<TimeSpan>(1, TimeSpan.MaxValue) },
            Map<TimeSpan>(c => c.Rounding(TimeSpan.FromTicks(2))),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Conversion.OutOfRange, "Item"),
            (report.FatalError?.Code, report.FatalError?.Location.PropertyPath?.ToString()));
    }

    [Fact]
    public async Task ExportRoundingOfAnInstantBeyondTheRange_IsARangeErrorOfTheField()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<DateTimeOffset>(1, DateTimeOffset.MaxValue) },
            Map<DateTimeOffset>(c => c.Rounding(TimeSpan.FromDays(1))),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Conversion.OutOfRange, "Item"),
            (report.FatalError?.Code, report.FatalError?.Location.PropertyPath?.ToString()));
    }

    [Fact]
    public async Task DateRounding_OfText_AppliesFromTheCalendarOrigin()
    {
        // Act
        var report = await ReadAsync("0001-01-02", s_twoDays, null, null);

        // Assert
        Assert.Equal(DateOnly.MinValue, ValueOf(report));
    }

    [Fact]
    public async Task DateRounding_OfANativeDate_AppliesFromTheCalendarOrigin()
    {
        // Act
        var report = await ReadAsync(null, s_twoDays, null, Serial(1));

        // Assert
        Assert.Equal(new DateOnly(1899, 12, 31), ValueOf(report));
    }

    [Fact]
    public async Task DateRounding_OnExport_AppliesFromTheCalendarOrigin()
    {
        // Act
        var (_, text) = await ExportTextAsync(new DateOnly(1, 1, 2), s_twoDays);

        // Assert
        Assert.Equal("0001-01-01", text);
    }

    [Fact]
    public void DateRounding_OfPartOfADay_IsAConfigurationError()
    {
        // Act
        var exception = Record.Exception(() => Map<DateOnly>(c => c.Rounding(TimeSpan.FromHours(36))));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }
}
