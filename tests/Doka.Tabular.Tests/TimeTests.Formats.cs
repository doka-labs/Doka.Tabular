using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Field formats, offsets, zones, and rounding of text timestamps.</summary>
public sealed partial class TimeTests
{
    private static readonly CultureInfo s_german = CultureInfo.GetCultureInfo("de-DE");

    private static readonly TabularMap<Value<DateTime>> s_escapedQuote =
        Map<DateTime>(c => c.Format("yyyy-MM-dd HH:mm 'a\\'y'"));

    private static readonly DateTime s_lastUtcHour = new(9999,
        12,
        31,
        23,
        0,
        0,
        DateTimeKind.Utc);

    private static readonly DateTime s_utcNoon = new(2024,
        4,
        1,
        12,
        0,
        0,
        DateTimeKind.Utc);

    // WHY: A property, because the field it uses is declared in another part of the class.
    private static TabularMap<Value<DateTimeOffset>> BerlinMinutes => Map<DateTimeOffset>(c => c
        .Format("yyyy-MM-dd HH:mm")
        .FileTimeZone(s_berlin));

    // WHY: A property, because the field it uses is declared in another part of the class.
    private static TabularMap<Value<DateTime>> BerlinThreeHours => Map<DateTime>(c => c
        .FileTimeZone(s_berlin)
        .Rounding(TimeSpan.FromHours(3)));

    // WHY: A property, because the field it uses is declared in another part of the class.
    private static TabularMap<Value<DateTime>> BerlinUtcColumn => Map<DateTime>(c => c
        .FileTimeZone(s_berlin)
        .ExcelAsUtc());

    [Fact]
    public async Task OffsetFormatWithoutOffset_WithoutAFileZone_IsRejected()
    {
        // Act
        var report = await ReadAsync("2024-04-01 08:30",
            Map<DateTimeOffset>(c => c.Format("yyyy-MM-dd HH:mm")),
            null,
            null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task OffsetFormatWithoutOffset_InADaylightGapOfTheFileZone_IsRejected()
    {
        // Act
        var report = await ReadAsync("2024-03-31 02:30", BerlinMinutes, null, null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task OffsetFormatWithoutOffset_TakesTheOffsetOfTheFileZone()
    {
        // Act
        var report = await ReadAsync("2024-04-01 08:30", BerlinMinutes, null, null);

        // Assert
        Assert.Equal(new DateTimeOffset(2024,
                4,
                1,
                8,
                30,
                0,
                TimeSpan.FromHours(2)),
            ValueOf(report));
    }

    [Fact]
    public async Task OffsetFormatWithOffset_ReadsTheOffsetFromTheText()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c.Format("yyyy-MM-dd HH:mm zzz"));

        // Act
        var report = await ReadAsync("2024-04-01 08:30 +05:00", map, null, null);

        // Assert
        var value = ValueOf(report);
        Assert.Equal((new DateTime(2024, 4, 1, 8, 30, 0), TimeSpan.FromHours(5)), (value.DateTime, value.Offset));
    }

    [Fact]
    public async Task ZerosBeyondTheSeventhDigit_NeedNoRounding()
    {
        // Act
        var report = await ReadAsync("10:00:00.1234567000", Map<TimeOnly>(), null, null);

        // Assert
        Assert.Equal(new TimeOnly(10, 0, 0).Add(TimeSpan.FromTicks(1_234_567)), ValueOf(report));
    }

    [Fact]
    public async Task ADigitBeyondTheSeventh_NeedsRounding()
    {
        // Act
        var report = await ReadAsync("10:00:00.12345670001", Map<TimeOnly>(), null, null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData("DateOnly", "d", "04-01")]
    [InlineData("DateOnly", null, "04-01")]
    [InlineData("DateTime", null, "04-01 08:30")]
    [InlineData("DateTime", "d", "04-01")]
    [InlineData("DateTimeOffset", "d", "04-01")]
    public async Task CulturePatternWithoutYear_TakesNothingFromTheClock(
        string type,
        string? format,
        string text
    )
    {
        // Arrange
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = "MM-dd";

        // Act
        var status = type switch
        {
            "DateOnly" => (await ReadAsync(text,
                format is null
                    ? Map<DateOnly>()
                    : Map<DateOnly>(c => c.Format(format)),
                culture,
                null)).Status,
            "DateTime" => (await ReadAsync(text,
                format is null
                    ? Map<DateTime>()
                    : Map<DateTime>(c => c.Format(format)),
                culture,
                null)).Status,
            _ => (await ReadAsync(text,
                Map<DateTimeOffset>(c => c
                    .Format(format!)
                    .FileTimeZone(s_berlin)),
                culture,
                null)).Status,
        };

        // Assert
        Assert.Equal(ReportStatus.Failed, status);
    }

    [Theory]
    [InlineData("DateTime", "HH:mm")]
    [InlineData("DateOnly", "MM-dd")]
    [InlineData("TimeOnly", "h:mm")]
    [InlineData("DateTime", "t")]
    public void IncompleteFormats_AreConfigurationErrors(
        string type,
        string format
    )
    {
        // Act
        var exception = Record.Exception(() => type switch
        {
            "DateTime" => Map<DateTime>(c => c.Format(format)),
            "DateOnly" => Map<DateOnly>(c => c.Format(format)),
            _ => Map<TimeOnly>(c => c.Format(format)),
        });

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData(false, "+14:59")]
    [InlineData(true, "+14:59")]
    [InlineData(false, "-14:59")]
    [InlineData(true, "-14:59")]
    public async Task OffsetBeyondFourteenHours_IsAFieldError(
        bool withOffset,
        string offset
    )
    {
        // Act
        var status = withOffset
            ? (await ReadAsync<DateTimeOffset>($"2024-04-01T14:30:00{offset}", null, null, null)).Status
            : (await ReadAsync<DateTime>($"2024-04-01T14:30:00{offset}", null, null, null)).Status;

        // Assert
        Assert.Equal(ReportStatus.Failed, status);
    }

    [Fact]
    public async Task TimestampWithOffset_IsRoundedOnTheUtcAxis()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c.Rounding(TimeSpan.FromHours(3)));

        // Act
        var report = await ReadAsync("2024-04-01T14:30:00+02:00", map, null, null);

        // Assert
        Assert.Equal(new DateTimeOffset(2024,
                4,
                1,
                14,
                0,
                0,
                TimeSpan.FromHours(2)),
            ValueOf(report));
    }

    [Fact]
    public async Task DurationWithFormat_AppliesTheRounding()
    {
        // Arrange
        var map = Map<TimeSpan>(c => c
            .Format("c")
            .Rounding(TimeSpan.FromMilliseconds(1)));

        // Act
        var report = await ReadAsync("10:00:00.1235000", map, null, null);

        // Assert
        Assert.Equal(new TimeSpan(0, 10, 0, 0, 124), ValueOf(report));
    }

    [Fact]
    public async Task DurationWithoutFormat_WithAnEighthDigit_IsRejectedWithoutRounding()
    {
        // Act
        var report = await ReadAsync<TimeSpan>("00:00:00.00000005", null, null, null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData("00:00:00.00000010", 0L, null, 1L)]
    [InlineData("00:00:00.00000014", 2L, null, 2L)]
    [InlineData("-00:00:00.00000015", 1L, null, -2L)]
    [InlineData("0:00:00,00000014", 2L, "de-DE", 2L)]
    public async Task DurationWithoutFormat_RoundsAnEighthDigitOnce(
        string text,
        long roundingTicks,
        string? culture,
        long expectedTicks
    )
    {
        // Arrange
        var map = roundingTicks == 0
            ? Map<TimeSpan>()
            : Map<TimeSpan>(c => c.Rounding(TimeSpan.FromTicks(roundingTicks)));

        // Act
        var report = await ReadAsync(text,
            map,
            culture is null
                ? null
                : CultureInfo.GetCultureInfo(culture),
            null);

        // Assert
        Assert.Equal(TimeSpan.FromTicks(expectedTicks), ValueOf(report));
    }

    public static TheoryData<string, AmbiguousTimeResolution?, bool, bool> AmbiguousExports()
    {
        var data = new TheoryData<string, AmbiguousTimeResolution?, bool, bool>();
        foreach (var withOffset in new[] { false, true })
        {
            data.Add("2024-10-27T00:30:00Z", null, false, withOffset);
            data.Add("2024-10-27T00:30:00Z", AmbiguousTimeResolution.Earlier, true, withOffset);
            data.Add("2024-10-27T00:30:00Z", AmbiguousTimeResolution.Later, false, withOffset);
            data.Add("2024-10-27T01:30:00Z", AmbiguousTimeResolution.Earlier, false, withOffset);
            data.Add("2024-10-27T01:30:00Z", AmbiguousTimeResolution.Later, true, withOffset);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AmbiguousExports))]
    public async Task AmbiguousLocalTimeOnExport_MustReadBackToTheSameInstant(
        string instant,
        AmbiguousTimeResolution? resolution,
        bool written,
        bool withOffset
    )
    {
        // Arrange
        var utc = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);

        // Act
        var report = withOffset
            ? await CsvInstance.ExportAsync(new[] { new Value<DateTimeOffset>(1, utc) },
                Map<DateTimeOffset>(c => BerlinWith(c, resolution)),
                new MemoryStream(),
                Token)
            : await CsvInstance.ExportAsync(new[] { new Value<DateTime>(1, utc.UtcDateTime) },
                Map<DateTime>(c => BerlinWith(c, resolution)),
                new MemoryStream(),
                Token);

        // Assert
        Assert.Equal(written
                ? null
                : TabularErrorCodes.Conversion.InvalidFormat,
            report.FatalError?.Code);
    }

    private static void BerlinWith(
        TabularColumnBuilder<DateTime> column,
        AmbiguousTimeResolution? resolution
    )
    {
        column.FileTimeZone(s_berlin);
        if (resolution is { } rule)
        {
            column.ResolveAmbiguousTime(rule);
        }
    }

    private static void BerlinWith(
        TabularColumnBuilder<DateTimeOffset> column,
        AmbiguousTimeResolution? resolution
    )
    {
        column.FileTimeZone(s_berlin);
        if (resolution is { } rule)
        {
            column.ResolveAmbiguousTime(rule);
        }
    }

    [Fact]
    public async Task AmbiguousLocalTimeWithItsOffsetInTheFormat_IsWrittenWithoutARule()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c
            .FileTimeZone(s_berlin)
            .Format("yyyy-MM-dd HH:mm zzz"));

        // Act
        var (_, text) = await ExportTextAsync(new DateTimeOffset(2024,
                10,
                27,
                0,
                30,
                0,
                TimeSpan.Zero),
            map);

        // Assert
        Assert.Equal("2024-10-27 02:30 +02:00", text);
    }

    [Theory]
    [InlineData("HH:mm 'day'", "14:30 day")]
    [InlineData("HH:mm \"by\"", "14:30 by")]
    [InlineData("HH:mm \\y", "14:30 y")]
    public async Task LetterYInALiteral_IsWrittenAsLiteralText(
        string format,
        string text
    )
    {
        // Act
        var (_, written) = await ExportTextAsync(new TimeOnly(14, 30), Map<TimeOnly>(c => c.Format(format)));

        // Assert
        Assert.Equal(text, written);
    }

    [Theory]
    [InlineData("HH:mm 'day'", "14:30 day")]
    [InlineData("HH:mm \"by\"", "14:30 by")]
    [InlineData("HH:mm \\y", "14:30 y")]
    public async Task LetterYInALiteral_IsReadAsLiteralText(
        string format,
        string text
    )
    {
        // Act
        var report = await ReadAsync(text, Map<TimeOnly>(c => c.Format(format)), null, null);

        // Assert
        Assert.Equal(new TimeOnly(14, 30), ValueOf(report));
    }

    [Fact]
    public async Task EscapedQuoteInALiteral_KeepsTheLiteralOpenOnExport()
    {
        // Act
        var (_, text) = await ExportTextAsync(new DateTime(2024, 5, 1, 14, 30, 0), s_escapedQuote);

        // Assert
        Assert.Equal("2024-05-01 14:30 a'y", text);
    }

    [Fact]
    public async Task EscapedQuoteInALiteral_KeepsTheLiteralOpenOnImport()
    {
        // Act
        var report = await ReadAsync("2024-05-01 14:30 a'y", s_escapedQuote, null, null);

        // Assert
        Assert.Equal(new DateTime(2024, 5, 1, 14, 30, 0), ValueOf(report));
    }

    [Theory]
    [InlineData("HH:mm 'a\\'y'")]
    [InlineData("HH:mm 'abc")]
    [InlineData("d")]
    public void FormatThatTimeOnlyCannotWrite_IsAConfigurationError(
        string format
    )
    {
        // Act
        var exception = Record.Exception(() => Map<TimeOnly>(c => c.Format(format)));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData("dd.MM.yy HH:mm")]
    [InlineData("'a\\'' yy-MM-dd")]
    public void TwoDigitYearOutsideALiteral_IsAConfigurationError(
        string format
    )
    {
        // Act
        var exception = Record.Exception(() => Map<DateTime>(c => c.Format(format)));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task UntrimmedDurationWithoutFormat_WithAnEighthDigit_IsRejectedWithoutRounding()
    {
        // Act
        var report = await ReadAsync(" 00:00:00.00000005 ", Map<TimeSpan>(c => c.Trim(false)), null, null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task UntrimmedDurationWithoutFormat_RoundsAnEighthDigitOnce()
    {
        // Arrange
        var twoTicks = Map<TimeSpan>(c => c
            .Trim(false)
            .Rounding(TimeSpan.FromTicks(2)));

        // Act
        var report = await ReadAsync("00:00:00.00000014 ", twoTicks, null, null);

        // Assert
        Assert.Equal(TimeSpan.FromTicks(2), ValueOf(report));
    }

    [Fact]
    public async Task NativeDateBeyondTheRange_IsAnErrorBeforeRounding()
    {
        // Arrange
        // WHY: 2958466 is 10000-01-01 in the 1900 system; rounding down on a two-day grid would give 9999-12-31.
        var map = Map<DateTime>(c => c.Rounding(TimeSpan.FromDays(2), MidpointRounding.ToZero));

        // Act
        var report = await ReadAsync(null, map, null, Serial(2958466));

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task NativeZonelessDateTime_IsRoundedAtTheCalendarOrigin()
    {
        // Arrange
        // WHY: 7 hours do not divide a day; 1900-01-01 lies exactly on the 7-hour grid of 0001-01-01.
        var map = Map<DateTime>(c => c.Rounding(TimeSpan.FromHours(7)));

        // Act
        var report = await ReadAsync(null, map, null, Serial(1));

        // Assert
        Assert.Equal(new DateTime(1900, 1, 1), ValueOf(report));
    }

    [Fact]
    public async Task DateTimeWithFileZone_IsRoundedOnTheUtcAxisOnExport()
    {
        // Act
        var (_, text) = await ExportTextAsync(new DateTime(2024,
                4,
                1,
                12,
                30,
                0,
                DateTimeKind.Utc),
            BerlinThreeHours);

        // Assert
        Assert.Equal("2024-04-01T14:00:00", text);
    }

    [Fact]
    public async Task DateTimeWithFileZone_IsRoundedOnTheUtcAxisOnImport()
    {
        // Arrange
        var local = new DateTime(2024, 4, 1, 14, 30, 0);

        // Act
        var report = await ReadAsync(null, BerlinThreeHours, null, Serial(local.ToOADate()));

        // Assert
        Assert.Equal(new DateTime(2024,
                4,
                1,
                12,
                0,
                0,
                DateTimeKind.Utc),
            ValueOf(report));
    }

    [Theory]
    [InlineData(5, "0001-01-01T01:00:00", false)]
    [InlineData(-5, "9999-12-31T23:00:00", false)]
    [InlineData(5, "0001-01-01T01:00:00", true)]
    [InlineData(-5, "9999-12-31T23:00:00", true)]
    public async Task LocalTimeOfAFileZoneOutsideTheUtcRange_IsARangeError(
        int hours,
        string text,
        bool withOffset
    )
    {
        // Arrange
        var zone = FixedZone(hours);

        // Act
        var errors = withOffset
            ? (await ReadAsync(text, Map<DateTimeOffset>(c => c.FileTimeZone(zone)), null, null)).Errors
            : (await ReadAsync(text, Map<DateTime>(c => c.FileTimeZone(zone)), null, null)).Errors;

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, errors[0].Code);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task LateInstantBeyondTheRangeOfTheFileZone_IsARangeErrorOnExport(
        TabularType type
    )
    {
        // Act
        var (report, _) = await Export.ExportAsync(new[] { new Value<DateTime>(1, s_lastUtcHour) },
            Map<DateTime>(c => c.FileTimeZone(FixedZone(5))),
            type,
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, report.FatalError?.Code);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task EarlyInstantBeyondTheRangeOfTheFileZone_IsARangeErrorOnExport(
        TabularType type
    )
    {
        // Arrange
        var first = new DateTime(1,
            1,
            1,
            1,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        var (report, _) = await Export.ExportAsync(new[] { new Value<DateTime>(1, first) },
            Map<DateTime>(c => c.FileTimeZone(FixedZone(-5))),
            type,
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, report.FatalError?.Code);
    }

    [Fact]
    public async Task LateOffsetBeyondTheRangeOfTheFileZone_IsARangeErrorOnCsvExport()
    {
        // Act
        var (report, _) = await Export.ExportAsync(
            new[] { new Value<DateTimeOffset>(1, new DateTimeOffset(s_lastUtcHour)) },
            Map<DateTimeOffset>(c => c.FileTimeZone(FixedZone(5))),
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, report.FatalError?.Code);
    }

    private static TimeZoneInfo FixedZone(
        int hours
    ) => TimeZoneInfo.CreateCustomTimeZone($"Fixed{hours}",
        TimeSpan.FromHours(hours),
        $"Fixed{hours}",
        $"Fixed{hours}");

    [Fact]
    public async Task UtcExcelColumnWithFileZone_KeepsTheInstant()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(new[] { new Value<DateTime>(1, s_utcNoon) },
            BerlinUtcColumn,
            TabularType.Excel,
            Token);

        var report = await Import.ImportAsync(file!, TabularType.Excel, BerlinUtcColumn, Token);

        // Assert
        var value = ValueOf(report);
        Assert.Equal((s_utcNoon, DateTimeKind.Utc), (value, value.Kind));
    }

    [Fact]
    public async Task UtcExcelColumnWithFileZone_RejectsALocalValue()
    {
        // Act
        var (report, _) = await Export.ExportAsync(new[] { new Value<DateTime>(1, s_utcNoon.ToLocalTime()) },
            BerlinUtcColumn,
            TabularType.Excel,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task UtcExcelColumnWithFileZone_WritesLocalTimeIntoCsv()
    {
        // Act
        var (_, text) = await ExportTextAsync(s_utcNoon, BerlinUtcColumn);

        // Assert
        Assert.Equal("2024-04-01T14:00:00", text);
    }

    [Fact]
    public async Task FormatPath_AppliesTheRounding()
    {
        // Arrange
        var time = Map<TimeOnly>(c => c
            .Format("HH:mm:ss.ffff")
            .Rounding(TimeSpan.FromMilliseconds(1)));

        // Act
        var report = await ReadAsync("10:00:00.1235", time, null, null);

        // Assert
        Assert.Equal(new TimeOnly(10, 0, 0, 124), ValueOf(report));
    }

    [Fact]
    public async Task CulturePath_AppliesTheRounding()
    {
        // Arrange
        var dateTime = Map<DateTime>(c => c.Rounding(TimeSpan.FromSeconds(2)));

        // Act
        var report = await ReadAsync("01.04.2024 10:00:01", dateTime, s_german, null);

        // Assert
        Assert.Equal(new DateTime(2024, 4, 1, 10, 0, 0), ValueOf(report));
    }

    [Fact]
    public async Task OffsetExportWithFileZone_UsesTheFieldFormat()
    {
        // Arrange
        var map = Map<DateTimeOffset>(c => c
            .Format("yyyy/MM/dd HH:mm")
            .FileTimeZone(s_berlin));

        // Act
        var (_, text) = await ExportTextAsync(new DateTimeOffset(2024,
                4,
                1,
                14,
                30,
                0,
                TimeSpan.FromHours(2)),
            map);

        // Assert
        Assert.Equal("2024/04/01 14:30", text);
    }

    [Fact]
    public async Task UtcExportWithFormatWithoutZone_IsAnError()
    {
        // Arrange
        var map = Map<DateTime>(c => c.Format("yyyy-MM-dd HH:mm"));

        // Act
        var (report, file) = await CsvInstance.ExportAsync(
            new[]
            {
                new Value<DateTime>(1,
                    new DateTime(2024,
                        4,
                        1,
                        12,
                        30,
                        0,
                        DateTimeKind.Utc)),
            },
            map,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Null(file);
    }

    [Fact]
    public void TimeOfDayStep_MustDivideADay()
    {
        // Act
        var exception = Record.Exception(() => Map<TimeOnly>(c => c.Rounding(TimeSpan.FromHours(7))));

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Fact]
    public async Task NativeDateTimeWithFileZone_IsCheckedOnTheUtcAxisOfTheImport()
    {
        // Arrange
        var map = Map<DateTime>(c => c
            .FileTimeZone(FixedZone(2))
            .Rounding(TimeSpan.FromHours(3)));

        var utc = new DateTime(2024,
            4,
            1,
            12,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        // WHY: A refused export returns no file; the empty input then reads nothing, and the status shows the cause.
        var (export, file) = await ExcelInstance.ExportAsync(new[] { new Value<DateTime>(1, utc) }, map, Token);
        var report = await ExcelInstance.ImportAsync(file ?? new MemoryStream(), map, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        var value = ValueOf(report);
        Assert.Equal((utc, DateTimeKind.Utc), (value, value.Kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZonelessDateTimeWithFileZone_IsRoundedOnTheUtcAxisOnExport(
        bool excel
    )
    {
        // Arrange
        // WHY: 14:00 in UTC+02 is 12:00 UTC and lies on the three-hour grid of the UTC axis; rounding at the calendar
        // origin would move it to 15:00, which the import reads as 13:00 UTC and rounds to another instant.
        var map = Map<DateTime>(c => c
            .FileTimeZone(FixedZone(2))
            .Rounding(TimeSpan.FromHours(3)));

        var local = new DateTime(2024, 4, 1, 14, 0, 0);

        // Act
        var (_, file) = await Export.ExportAsync(new[] { new Value<DateTime>(1, local) },
            map,
            excel
                ? TabularType.Excel
                : TabularType.Csv,
            Token);

        // Assert
        var written = Cells(excel
                ? TabularType.Excel
                : TabularType.Csv,
            file!)[0][1]!;

        Assert.Equal(local,
            excel
                ? DateTime.FromOADate(double.Parse(written, CultureInfo.InvariantCulture))
                : DateTime.Parse(written, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ZonelessDateTimeWithFileZone_ReadsBackAsTheSameInstant()
    {
        // Arrange
        var map = Map<DateTime>(c => c
            .FileTimeZone(FixedZone(2))
            .Rounding(TimeSpan.FromHours(3)));

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await ExcelInstance.ExportAsync(
            new[] { new Value<DateTime>(1, new DateTime(2024, 4, 1, 14, 0, 0)) },
            map,
            Token);

        var report = await ExcelInstance.ImportAsync(file ?? new MemoryStream(), map, Token);

        // Assert
        Assert.Equal(new DateTime(2024,
                4,
                1,
                12,
                0,
                0,
                DateTimeKind.Utc),
            ValueOf(report));
    }
}
