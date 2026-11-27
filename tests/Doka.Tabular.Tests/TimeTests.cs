using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>DateOnly, TimeOnly, DateTime, DateTimeOffset, and TimeSpan in text and Excel cells.</summary>
public sealed partial class TimeTests
{
    private static readonly TimeZoneInfo s_berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly DateTimeOffset s_offsetValue = new(
        2024,
        4,
        1,
        14,
        30,
        0,
        TimeSpan.FromHours(2));

    private static readonly DateTime s_utcTen = new(
        2024,
        1,
        1,
        10,
        0,
        0,
        DateTimeKind.Utc);

    private static readonly Value<TimeSpan>[] s_durations =
        [new(1, TimeSpan.FromHours(27)), new(2, TimeSpan.FromMinutes(-90))];

    private static readonly TabularMap<Value<TimeSpan>> s_hours =
        Map<TimeSpan>(c => c.ExcelDuration(ExcelDurationUnit.Hours));

    private static TabularMap<Value<T>> Map<T>(
        Action<TabularColumnBuilder<T>>? configure = null
    ) => TabularMap
        .For<Value<T>>()
        .Column(x => x.Key, index: 0)
        .Column(x => x.Item, index: 1, configure: configure)
        .Build();

    private static async Task<ImportReport<Value<T>>> ReadAsync<T>(
        object? cell,
        TabularMap<Value<T>>? map,
        CultureInfo? culture,
        Stream? file
    )
    {
        var options = new TabularImportOptions { Culture = culture };
        var type = file is null
            ? TabularType.Csv
            : TabularType.Excel;

        return await Import.ImportAsync(file ?? CsvText([1, cell]), type, map ?? Map<T>(), options, Token);
    }

    /// <summary>The value of the single record of a successful read.</summary>
    private static T ValueOf<T>(
        ImportReport<Value<T>> report
    )
    {
        Assert.True(
            report.Status == ReportStatus.Succeeded,
            string.Join(" | ", report.Errors.Select(error => error.Code.Value)));

        return Assert.Single(report.Items!)
            .Item;
    }

    /// <summary>Exports one value to CSV and returns the report and the written text of the value.</summary>
    private static async Task<(ExportReport Report, string? Text)> ExportTextAsync<T>(
        T value,
        TabularMap<Value<T>>? map = null
    )
    {
        var (report, file) = await CsvInstance.ExportAsync(new[] { new Value<T>(1, value) }, map ?? Map<T>(), Token);

        return (report, file is null
            ? null
            : Cells(TabularType.Csv, file)[0][1]);
    }

    private static MemoryStream Serial(
        double value,
        bool dateFormat = true,
        bool date1904 = false
    ) => Workbook(
        date1904,
        dateFormat
            ? new HashSet<int> { 1 }
            : null,
        ("Sheet1", [[1, value]]));

    [Theory]
    [InlineData("2024-04-01", null)]
    [InlineData("01.04.2024", null)]
    [InlineData("01.04.2024", "dd.MM.yyyy")]
    public async Task DateOnly_UnderGermanCulture_ReadsIsoCultureAndFixedFormatTexts(
        string text,
        string? format
    )
    {
        // Arrange
        var map = format is null
            ? Map<DateOnly>()
            : Map<DateOnly>(c => c.Format(format));

        // Act
        var report = await ReadAsync(text, map, CultureInfo.GetCultureInfo("de-DE"), null);

        // Assert
        Assert.Equal(new DateOnly(2024, 4, 1), ValueOf(report));
    }

    [Theory]
    [InlineData("1.4.2024", "dd.MM.yyyy")]
    [InlineData("2024-04-01", "dd.MM.yyyy")]
    [InlineData("31.04.2024", "dd.MM.yyyy")]
    [InlineData("03/04/2024", null)]
    [InlineData("01.04.24", null)]
    public async Task DateOnly_UnderGermanCulture_RejectsOtherTexts(
        string text,
        string? format
    )
    {
        // Arrange
        var map = format is null
            ? Map<DateOnly>()
            : Map<DateOnly>(c => c.Format(format));

        // Act
        var report = await ReadAsync(text, map, CultureInfo.GetCultureInfo("de-DE"), null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task DateOnly_UnderBritishCulture_ReadsDayBeforeMonth()
    {
        // Act
        var report = await ReadAsync<DateOnly>("03/04/2024", null, CultureInfo.GetCultureInfo("en-GB"), null);

        // Assert
        Assert.Equal(new DateOnly(2024, 4, 3), ValueOf(report));
    }

    [Fact]
    public void DateOnly_WithATwoDigitYearFormat_FailsAtBuild()
    {
        // Act
        var exception = Record.Exception(() => Map<DateOnly>(c => c.Format("dd.MM.yy")));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task DateTime_WithAnOffset_IsReadAsUtc()
    {
        // Act
        var report = await ReadAsync<DateTime>("2024-04-01T14:30:00+02:00", null, null, null);

        // Assert
        var value = ValueOf(report);
        Assert.Equal((new DateTime(2024, 4, 1, 12, 30, 0), DateTimeKind.Utc), (value, value.Kind));
    }

    [Fact]
    public async Task DateTime_WithANegativeOffset_IsReadAsUtc()
    {
        // Act
        var report = await ReadAsync<DateTime>("2024-04-01T14:30:00-05:30", null, null, null);

        // Assert
        var value = ValueOf(report);
        Assert.Equal((new DateTime(2024, 4, 1, 20, 0, 0), DateTimeKind.Utc), (value, value.Kind));
    }

    [Fact]
    public async Task DateTimeOffset_WithANegativeOffset_KeepsTheOffsetOfTheText()
    {
        // Act
        var report = await ReadAsync<DateTimeOffset>("2024-04-01T14:30:00-05:30", null, null, null);

        // Assert
        Assert.Equal(
            new DateTimeOffset(
                2024,
                4,
                1,
                14,
                30,
                0,
                new TimeSpan(-5, -30, 0)),
            ValueOf(report));
    }

    [Fact]
    public async Task DateTimeOffset_KeepsTheOffsetOfTheText()
    {
        // Act
        var report = await ReadAsync<DateTimeOffset>("2024-04-01T14:30:00+02:00", null, null, null);

        // Assert
        var value = ValueOf(report);
        Assert.Equal(
            new DateTimeOffset(
                2024,
                4,
                1,
                14,
                30,
                0,
                TimeSpan.FromHours(2)),
            value);

        Assert.Equal(TimeSpan.FromHours(2), value.Offset);
    }

    [Fact]
    public async Task DateTime_WithoutAnOffset_StaysUnspecified()
    {
        // Act
        var report = await ReadAsync<DateTime>("2024-04-01T08:30:00", null, null, null);

        // Assert
        Assert.Equal(
            DateTimeKind.Unspecified,
            ValueOf(report)
                .Kind);
    }

    [Fact]
    public async Task DateTimeOffset_WithoutAnOffsetOrFileTimeZone_IsRejected()
    {
        // Act
        var report = await ReadAsync<DateTimeOffset>("2024-04-01T08:30:00", null, null, null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task FileTimeZone_ResolvesLocalText()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c.FileTimeZone(s_berlin));

        // Act
        var report = await ReadAsync("2024-04-01T08:30:00", map, null, null);

        // Assert
        Assert.Equal(
            new DateTimeOffset(
                2024,
                4,
                1,
                8,
                30,
                0,
                TimeSpan.FromHours(2)),
            ValueOf(report));
    }

    [Fact]
    public async Task FileTimeZone_WritesLocalTime()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c.FileTimeZone(s_berlin));

        // Act
        var (_, text) = await ExportTextAsync(
            new DateTimeOffset(
                2024,
                4,
                1,
                23,
                30,
                0,
                TimeSpan.Zero),
            map);

        // Assert
        Assert.Equal("2024-04-02T01:30:00", text);
    }

    [Fact]
    public async Task FileTimeZone_ReadsItsOwnTextBack()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c.FileTimeZone(s_berlin));

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, text) = await ExportTextAsync(
            new DateTimeOffset(
                2024,
                4,
                1,
                23,
                30,
                0,
                TimeSpan.Zero),
            map);

        var report = await ReadAsync(text, map, null, null);

        // Assert
        Assert.Equal(
            new DateTimeOffset(
                2024,
                4,
                1,
                23,
                30,
                0,
                TimeSpan.Zero),
            ValueOf(report));
    }

    [Fact]
    public async Task ShiftInvalidTime_WithANegativeDaylightDelta_MovesForwardByTheGap()
    {
        // Arrange
        // WHY: With a negative delta the gap lies at the end of daylight time: 02:00 to 03:00 on 1 November.
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31),
            TimeSpan.FromHours(-1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1));

        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Negative",
            TimeSpan.Zero,
            "Negative",
            "Standard",
            "Daylight",
            [rule]);

        var map = Map<DateTimeOffset>(c => c
            .FileTimeZone(zone)
            .ShiftInvalidTime());

        // Act
        var report = await ReadAsync("2024-11-01T02:30:00", map, null, null);

        // Assert
        Assert.True(zone.IsInvalidTime(new DateTime(2024, 11, 1, 2, 30, 0)));
        Assert.Equal(
            new DateTimeOffset(
                2024,
                11,
                1,
                3,
                30,
                0,
                TimeSpan.Zero),
            ValueOf(report));
    }

    [Theory]
    [InlineData("2024-03-31T02:30:00")]
    [InlineData("2024-10-27T02:30:00")]
    public async Task DaylightSavingGapOrOverlap_WithoutARule_IsRejected(
        string text
    )
    {
        // Act
        var report = await ReadAsync(text, Map<DateTimeOffset>(c => c.FileTimeZone(s_berlin)), null, null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task DaylightSavingGap_WithShiftInvalidTime_MovesForwardByTheGap()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c
            .FileTimeZone(s_berlin)
            .ShiftInvalidTime());

        // Act
        var report = await ReadAsync("2024-03-31T02:30:00", map, null, null);

        // Assert
        Assert.Equal(
            new DateTimeOffset(
                2024,
                3,
                31,
                3,
                30,
                0,
                TimeSpan.FromHours(2)),
            ValueOf(report));
    }

    [Theory]
    [InlineData(AmbiguousTimeResolution.Earlier, 2)]
    [InlineData(AmbiguousTimeResolution.Later, 1)]
    public async Task DaylightSavingOverlap_WithAResolution_TakesTheChosenOffset(
        AmbiguousTimeResolution resolution,
        int offsetHours
    )
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c
            .FileTimeZone(s_berlin)
            .ResolveAmbiguousTime(resolution));

        // Act
        var report = await ReadAsync("2024-10-27T02:30:00", map, null, null);

        // Assert
        Assert.Equal(
            new DateTimeOffset(
                2024,
                10,
                27,
                2,
                30,
                0,
                TimeSpan.FromHours(offsetHours)),
            ValueOf(report));
    }

    [Fact]
    public async Task LocalDateTime_FailsTheExport()
    {
        // Arrange
        var local = new DateTime(
            2024,
            4,
            1,
            8,
            0,
            0,
            DateTimeKind.Local);

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<DateTime>(1, local) },
            Map<DateTime>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task Csv_KeepsUtcAndUnspecifiedDateTimes()
    {
        // Arrange
        var rows = new[]
        {
            new Value<DateTime>(
                1,
                new DateTime(
                    2024,
                    4,
                    1,
                    8,
                    0,
                    0,
                    DateTimeKind.Utc)),
            new Value<DateTime>(2, new DateTime(2024, 4, 1, 8, 0, 0)),
        };

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await CsvInstance.ExportAsync(rows, Map<DateTime>(), Token);
        var report = await CsvInstance.ImportAsync(file!, Map<DateTime>(), Token);

        // Assert
        Assert.Equal([DateTimeKind.Utc, DateTimeKind.Unspecified], report.Items!.Select(item => item.Item.Kind));
    }

    [Fact]
    public async Task UtcDateTime_InExcelWithoutUtcColumn_FailsTheExport()
    {
        // Arrange
        var utc = new DateTime(
            2024,
            4,
            1,
            8,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        var report = await ExcelInstance.ExportAsync(
            new[] { new Value<DateTime>(1, utc) },
            Map<DateTime>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData("10:00:00.1235")]
    [InlineData("10:00:00.1245")]
    public async Task TimeRounding_ToMilliseconds_UsesTheMidpointRule(
        string text
    )
    {
        // Act
        var report = await ReadAsync(text, Map<TimeOnly>(c => c.Rounding(TimeSpan.FromMilliseconds(1))), null, null);

        // Assert
        Assert.Equal(new TimeOnly(10, 0, 0, 124), ValueOf(report));
    }

    [Fact]
    public async Task DateTimeRounding_CarriesIntoTheNextDay()
    {
        // Act
        var report = await ReadAsync(
            "2024-04-01T23:59:59.9999",
            Map<DateTime>(c => c.Rounding(TimeSpan.FromMilliseconds(1))),
            null,
            null);

        // Assert
        Assert.Equal(new DateTime(2024, 4, 2), ValueOf(report));
    }

    [Fact]
    public async Task TimeRounding_PastMidnight_IsRejected()
    {
        // Act
        var report = await ReadAsync(
            "23:59:59.9999",
            Map<TimeOnly>(c => c.Rounding(TimeSpan.FromMilliseconds(1))),
            null,
            null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData(59d, false, "1900-02-28")]
    [InlineData(61d, false, "1900-03-01")]
    [InlineData(0d, true, "1904-01-01")]
    public async Task ExcelSerials_Follow1900And1904Systems(
        double serial,
        bool date1904,
        string expected
    )
    {
        // Act
        var report = await ReadAsync<DateOnly>(null, null, null, Serial(serial, date1904: date1904));

        // Assert
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), ValueOf(report));
    }

    [Fact]
    public async Task ExcelSerial60_TheNonexistent29February1900_IsRejectedAsADate()
    {
        // Act
        var report = await ReadAsync<DateOnly>(null, null, null, Serial(60));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task ExcelSerial60_TheNonexistent29February1900_IsRejectedAsADateTime()
    {
        // Act
        var report = await ReadAsync<DateTime>(null, null, null, Serial(60.5));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task ExcelSerialZero_IsMidnight()
    {
        // Act
        var report = await ReadAsync<TimeOnly>(null, null, null, Serial(0));

        // Assert
        Assert.Equal(TimeOnly.MinValue, ValueOf(report));
    }

    [Fact]
    public async Task ExcelSerialZero_IsAnEmptyDuration()
    {
        // Act
        var report = await ReadAsync<TimeSpan>(null, null, null, Serial(0));

        // Assert
        Assert.Equal(TimeSpan.Zero, ValueOf(report));
    }

    [Fact]
    public async Task SerialWithoutDateFormat_IsRejectedByDefault()
    {
        // Act
        var report = await ReadAsync<DateOnly>(null, null, null, Serial(45292, dateFormat: false));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task SerialWithoutDateFormat_IsReadWhenSerialNumbersAreAllowed()
    {
        // Act
        var report = await ReadAsync(
            null,
            Map<DateOnly>(c => c.AllowExcelSerialNumbers()),
            null,
            Serial(45292, dateFormat: false));

        // Assert
        Assert.Equal(new DateOnly(2024, 1, 1), ValueOf(report));
    }

    [Fact]
    public async Task SerialWithDateFormat_IsADate()
    {
        // Act
        var report = await ReadAsync<DateOnly>(null, null, null, Serial(45292));

        // Assert
        Assert.Equal(new DateOnly(2024, 1, 1), ValueOf(report));
    }

    [Fact]
    public async Task SerialWithATime_IsRejectedAsADate()
    {
        // Act
        var report = await ReadAsync<DateOnly>(null, null, null, Serial(45292.5));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task SerialWithATime_IsADateThroughAConversionOfDateTime()
    {
        // Arrange
        var converted = Map<DateOnly>(c
            => c.Convert(read: DateOnly.FromDateTime, write: date => date.ToDateTime(TimeOnly.MinValue)));

        // Act
        var report = await ReadAsync(null, converted, null, Serial(45292.5));

        // Assert
        Assert.Equal(new DateOnly(2024, 1, 1), ValueOf(report));
    }

    [Fact]
    public async Task SerialWithADate_IsRejectedAsATime()
    {
        // Act
        var report = await ReadAsync<TimeOnly>(null, null, null, Serial(45292.5));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task DateTimeOffset_InExcel_IsAnIsoTextCell()
    {
        // Act
        var (_, file) = await ExcelInstance.ExportAsync(
            new[] { new Value<DateTimeOffset>(1, s_offsetValue) },
            Map<DateTimeOffset>(),
            Token);

        // Assert
        Assert.Equal("2024-04-01T14:30:00+02:00", Cells(TabularType.Excel, file!)[0][1]);
    }

    [Fact]
    public async Task DateTimeOffset_InExcel_ReadsBackWithItsOffset()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await ExcelInstance.ExportAsync(
            new[] { new Value<DateTimeOffset>(1, s_offsetValue) },
            Map<DateTimeOffset>(),
            Token);

        var report = await ExcelInstance.ImportAsync(file!, Map<DateTimeOffset>(), Token);

        // Assert
        var value = Assert.Single(report.Items!)
            .Item;

        Assert.Equal((s_offsetValue, s_offsetValue.Offset), (value, value.Offset));
    }

    [Theory]
    [InlineData("yyyy-MM-dd'T'HH:mm:ssK")]
    [InlineData("o")]
    public async Task UtcDateTimeWithKOrO_WritesZ(
        string format
    )
    {
        // Act
        var (report, text) = await ExportTextAsync(s_utcTen, Map<DateTime>(c => c.Format(format)));

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.EndsWith("Z", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("yyyy-MM-dd'T'HH:mm:ssK")]
    [InlineData("o")]
    public async Task UtcDateTimeWithKOrO_ReadsBackAsUtc(
        string format
    )
    {
        // Arrange
        var map = Map<DateTime>(c => c.Format(format));

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, text) = await ExportTextAsync(s_utcTen, map);
        var report = await ReadAsync(text, map, null, null);

        // Assert
        var value = ValueOf(report);
        Assert.Equal((s_utcTen, DateTimeKind.Utc), (value, value.Kind));
    }

    [Theory]
    [InlineData("yyyy-MM-dd'T'HH:mm:ssK")]
    [InlineData("o")]
    public async Task UtcTextWithKOrO_IsAnOffsetOfZero(
        string format
    )
    {
        // Act
        var report = await ReadAsync(
            s_utcTen.ToString(format, CultureInfo.InvariantCulture),
            Map<DateTimeOffset>(c => c.Format(format)),
            null,
            null);

        // Assert
        Assert.Equal(new DateTimeOffset(s_utcTen), ValueOf(report));
    }

    [Theory]
    [InlineData(ExcelDurationUnit.Days, "[h]:mm:ss", false)]
    [InlineData(ExcelDurationUnit.Days, "[mm]:ss", false)]
    [InlineData(ExcelDurationUnit.Hours, "[h]:mm", false)]
    [InlineData(ExcelDurationUnit.Days, "0.0000", true)]
    [InlineData(ExcelDurationUnit.Hours, "0.00", true)]
    [InlineData(ExcelDurationUnit.Days, "[h]:mm:ss;-0.0000", true)]
    [InlineData(ExcelDurationUnit.Days, "[h]:mm:ss;\"minus \"0.0000;0", true)]
    [InlineData(ExcelDurationUnit.Days, "0.0000;[h]:mm:ss", false)]
    [InlineData(ExcelDurationUnit.Days, "0.0000;;0", false)]
    [InlineData(ExcelDurationUnit.Days, "0.0000;\"\"", false)]
    [InlineData(ExcelDurationUnit.Days, "0.0000;\" \"", false)]
    [InlineData(ExcelDurationUnit.Days, "0.0000;\"minus\"", false)]
    [InlineData(ExcelDurationUnit.Days, "0.0000;_-", false)]
    [InlineData(ExcelDurationUnit.Days, "[h]:mm:ss\"[<0]\";-0.0000", true)]
    [InlineData(ExcelDurationUnit.Days, "[h]:mm:ss\\[<0];-0.0000", true)]
    [InlineData(ExcelDurationUnit.Days, "[h]:mm:ss;[Red]-0.0000", true)]
    [InlineData(ExcelDurationUnit.Days, "[h]:mm:ss;\\-0.0000", true)]
    [InlineData(ExcelDurationUnit.Days, "[>=0][h]:mm:ss;[<0]-0.0000", false)]
    [InlineData(ExcelDurationUnit.Days, "\"a;b\"[h]:mm", false)]
    public async Task NegativeDurationWithAnExplicitExcelFormat_IsAnErrorOnlyForADateOrTimeFormat(
        ExcelDurationUnit unit,
        string format,
        bool written
    )
    {
        // Arrange
        var map = Map<TimeSpan>(c => c
            .ExcelDuration(unit)
            .ExcelNumberFormat(format));

        // Act
        var report = await ExcelInstance.ExportAsync(
            new[] { new Value<TimeSpan>(1, TimeSpan.FromMinutes(-90)) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(
            written
                ? null
                : TabularErrorCodes.Conversion.OutOfRange,
            report.FatalError?.Code);
    }

    [Fact]
    public async Task Duration_InExcel_IsWrittenInDaysByDefault()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(
            new[] { new Value<TimeSpan>(1, TimeSpan.FromHours(27)) },
            Map<TimeSpan>(),
            Token);

        // Assert
        Assert.Equal("1.125", Cells(TabularType.Excel, report.File!)[0][1]);
    }

    [Fact]
    public async Task Duration_InExcel_IsWrittenInTheChosenUnit()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(s_durations, s_hours, Token);

        // Assert
        Assert.Equal(
            ["27", "-1.5"],
            Cells(TabularType.Excel, report.File!)
                .Select(row => row[1]));
    }

    [Fact]
    public async Task Duration_InExcel_ReadsBackInTheChosenUnit()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var export = await ExcelInstance.ExportAsync(s_durations, s_hours, Token);
        var report = await ExcelInstance.ImportAsync(export.File!, s_hours, Token);

        // Assert
        Assert.Equal([TimeSpan.FromHours(27), TimeSpan.FromMinutes(-90)], report.Items!.Select(item => item.Item));
    }

    [Fact]
    public async Task NegativeDuration_InExcelDays_FailsTheExportAtItsRow()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(
            new[] { new Value<TimeSpan>(1, TimeSpan.FromMinutes(-90)) },
            Map<TimeSpan>(),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(1, report.FatalError?.Location.RowNumber);
    }

    [Fact]
    public async Task SubMillisecondTime_ReadsBackFromText()
    {
        // Arrange
        var precise = new TimeOnly(10, 0, 0).Add(TimeSpan.FromTicks(1234));

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, text) = await ExportTextAsync(precise);
        var report = await ReadAsync(text, Map<TimeOnly>(), null, null);

        // Assert
        Assert.Equal(precise, ValueOf(report));
    }

    [Fact]
    public async Task SecondsHiddenByTheExcelFormat_ReadBack()
    {
        // Arrange
        var halfSecond = new DateTime(
            2024,
            4,
            1,
            10,
            0,
            0,
            500);

        var map = Map<DateTime>(c => c.ExcelNumberFormat("dd.mm.yyyy hh:mm"));

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await ExcelInstance.ExportAsync(new[] { new Value<DateTime>(1, halfSecond) }, map, Token);
        var report = await ExcelInstance.ImportAsync(file!, map, Token);

        // Assert
        Assert.Equal(halfSecond, ValueOf(report));
    }

    private sealed record Value<T>(
        int Key,
        T Item
    );
}
