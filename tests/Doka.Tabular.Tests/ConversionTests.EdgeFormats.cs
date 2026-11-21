using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// A trimming field removes the white space a time format writes at its edge; such a format is a configuration error of
/// the call, checked with the culture of the call.
/// </summary>
public sealed partial class ConversionTests
{
    public static TheoryData<string, bool> EdgeFormatCases()
        => Directions("time", "date", "duration", "offset", "dateTime");

    [Theory]
    [MemberData(nameof(EdgeFormatCases))]
    public async Task FormatWithWhiteSpaceAtItsEdge_UnderTrim_IsAConfigurationErrorOfImportAndExport(
        string type,
        bool export
    )
    {
        // Act
        var kind = type switch
        {
            "time" => await ConfigurationKindAsync(new TimeOnly(14, 30),
                Map<TimeOnly>(c => c
                    .Format(" HH:mm ")
                    .Trim()),
                export),
            "date" => await ConfigurationKindAsync(new DateOnly(2024, 5, 1),
                Map<DateOnly>(c => c
                    .Format("yyyy-MM-dd' '")
                    .Trim()),
                export),
            "duration" => await ConfigurationKindAsync(TimeSpan.FromMinutes(90),
                Map<TimeSpan>(c => c
                    .Format("\\ hh\\:mm")
                    .Trim()),
                export),
            "offset" => await ConfigurationKindAsync(new DateTimeOffset(2024,
                    5,
                    1,
                    14,
                    30,
                    0,
                    TimeSpan.Zero),
                Map<DateTimeOffset>(c => c
                    .Format(" yyyy-MM-dd HH:mm:ss zzz")
                    .Trim()),
                export),
            _ => await ConfigurationKindAsync(new DateTime(2024, 5, 1, 14, 30, 0),
                Map<DateTime>(c => c
                    .Format("yyyy-MM-dd HH:mm:ss ")
                    .Trim()),
                export),
        };

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, kind);
    }

    public static TheoryData<string, bool> EdgeCultureCases() => Directions("time separator", "date pattern", "kind");

    [Theory]
    [MemberData(nameof(EdgeCultureCases))]
    public async Task EdgeWhiteSpaceFromTheCultureOrTheValue_UnderTrim_IsAConfigurationError(
        string source,
        bool export
    )
    {
        // Arrange
        var spaced = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        spaced.DateTimeFormat.TimeSeparator = " ";
        var padded = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        padded.DateTimeFormat.ShortDatePattern = " yyyy-MM-dd ";
        var utc = new DateTime(2024,
            5,
            1,
            14,
            30,
            0,
            DateTimeKind.Utc);

        // Act
        var kind = source switch
        {
            "time separator" => await ConfigurationKindAsync(new TimeOnly(14, 30),
                Map<TimeOnly>(c => c
                    .Format(":HH:mm")
                    .Culture(spaced)
                    .Trim()),
                export),
            "date pattern" => await ConfigurationKindAsync(new DateOnly(2024, 5, 1),
                Map<DateOnly>(c => c
                    .Format("d")
                    .Culture(padded)
                    .Trim()),
                export),

            // WHY: K writes nothing for a value of unspecified kind, so the format can end with white space.
            _ => await ConfigurationKindAsync(utc,
                Map<DateTime>(c => c
                    .Format("yyyy-MM-dd HH:mm K")
                    .Trim()),
                export),
        };

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, kind);
    }

    public static TheoryData<string, string, bool> NameFormatCases()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var format in new[]
            {
                "MMMM dd yyyy",
                "yyyy dd MMMM",
                "MMM dd yyyy",
                "dddd yyyy-MM-dd",
                "yyyy-MM-dd ddd",
            })
        {
            foreach (var type in new[] { "DateOnly", "DateTime", "DateTimeOffset" })
            {
                data.Add(format, type, false);
                data.Add(format, type, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NameFormatCases))]
    public async Task EdgeWhiteSpaceOfAnyMonthOrDayName_UnderTrim_IsAConfigurationError(
        string format,
        string type,
        bool export
    )
    {
        // Arrange
        // WHY: Only February and Tuesday carry the white space; the check must not depend on one probe date.
        var spaced = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        var names = spaced.DateTimeFormat;
        names.MonthNames = Replace(names.MonthNames, 1, " February ");
        names.MonthGenitiveNames = Replace(names.MonthGenitiveNames, 1, " February ");
        names.AbbreviatedMonthNames = Replace(names.AbbreviatedMonthNames, 1, " Feb ");
        names.AbbreviatedMonthGenitiveNames = Replace(names.AbbreviatedMonthGenitiveNames, 1, " Feb ");
        names.DayNames = Replace(names.DayNames, 2, " Tuesday ");
        names.AbbreviatedDayNames = Replace(names.AbbreviatedDayNames, 2, " Tue ");

        // Act
        var kind = type switch
        {
            "DateOnly" => await ConfigurationKindAsync(new DateOnly(2024, 1, 1),
                Map<DateOnly>(c => c
                    .Format(format)
                    .Culture(spaced)
                    .Trim()),
                export),
            "DateTime" => await ConfigurationKindAsync(new DateTime(2024, 1, 1),
                Map<DateTime>(c => c
                    .Format(format)
                    .Culture(spaced)
                    .Trim()),
                export),
            _ => await ConfigurationKindAsync(new DateTimeOffset(2024,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero),
                Map<DateTimeOffset>(c => c
                    .Format(format)
                    .Culture(spaced)
                    .Trim()),
                export),
        };

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, kind);

        return;

        static string[] Replace(
            string[] values,
            int index,
            string value
        )
        {
            var copy = (string[])values.Clone();
            copy[index] = value;

            return copy;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EdgeWhiteSpaceOfAMonthNameOfACommonHebrewYear_UnderTrim_IsAConfigurationError(
        bool export
    )
    {
        // Arrange
        // WHY: The Hebrew calendar names the months of leap years with its own table; the common-year name of month 2
        // must be checked too, whatever year the probes start in.
        var hebrew = HebrewCulture();
        var names = (string[])hebrew.DateTimeFormat.MonthNames.Clone();
        names[1] = " padded ";
        hebrew.DateTimeFormat.MonthNames = names;
        hebrew.DateTimeFormat.MonthGenitiveNames = names;
        var map = Map<DateOnly>(c => c
            .Format("MMMM dd yyyy")
            .Culture(hebrew)
            .Trim());

        // Act
        var kind = await ConfigurationKindAsync(new DateOnly(2024, 1, 1), map, export);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, kind);
    }

    [Theory]
    [InlineData(5761, false)]
    [InlineData(5763, true)]
    public void HebrewYears_OfTheReadBackCases_AreACommonAndALeapYear(
        int year,
        bool leap
    )
    {
        // Act
        var isLeap = new HebrewCalendar().IsLeapYear(year);

        // Assert
        Assert.Equal(leap, isLeap);
    }

    public static TheoryData<int, int> HebrewMonths()
    {
        // WHY: 5761 is a common year with 12 months, 5763 a leap year with 13; both name tables read back.
        var data = new TheoryData<int, int>();
        var calendar = new HebrewCalendar();
        foreach (var year in new[] { 5761, 5763 })
        {
            for (var month = 1; month <= calendar.GetMonthsInYear(year); month++)
            {
                data.Add(year, month);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HebrewMonths))]
    public async Task HebrewMonthNames_UnderTrim_ReadBackInCommonAndLeapYears(
        int year,
        int month
    )
    {
        // Arrange
        var hebrew = HebrewCulture();
        var value = DateOnly.FromDateTime(hebrew.DateTimeFormat.Calendar.ToDateTime(year,
            month,
            1,
            0,
            0,
            0,
            0));

        var map = Map<DateOnly>(c => c
            .Format("dd MMMM yyyy")
            .Culture(hebrew)
            .Trim());

        // Act
        var report = await ReadBackAsync(value, map);

        // Assert
        Assert.Equal(value, ValueOf(report));
    }

    public static TheoryData<string, int> NameFormatsAndMonths()
    {
        var data = new TheoryData<string, int>();
        foreach (var format in new[] { "MMMM dd yyyy", "dddd yyyy-MM-dd" })
        {
            for (var month = 1; month <= 12; month++)
            {
                data.Add(format, month);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NameFormatsAndMonths))]
    public async Task NamesWithoutEdgeWhiteSpace_UnderTrim_ReadBackInEveryMonthAndOnEveryDay(
        string format,
        int month
    )
    {
        // Arrange
        var value = new DateOnly(2024, month, month);
        var map = Map<DateOnly>(c => c
            .Format(format)
            .Trim());

        // Act
        var report = await ReadBackAsync(value, map);

        // Assert
        Assert.Equal(value, ValueOf(report));
    }

    [Fact]
    public async Task EdgeWhiteSpaceWithTrimOfTheCall_IsAConfigurationError()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("x"),
            Map<TimeOnly>(c => c.Format(" HH:mm")),
            new CsvImportOptions { TrimFields = true },
            Token);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, report.Errors[0].Kind);
    }

    [Fact]
    public async Task EdgeWhiteSpaceWithTrimOnTheBaseType_IsAConfigurationError()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("x"),
            Map<string>(c => c
                .Trim()
                .Convert(value => value.ToString("HH:mm", CultureInfo.InvariantCulture),
                    text => TimeOnly.Parse(text, CultureInfo.InvariantCulture),
                    configure: b => b.Format(" HH:mm"))),
            Token);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, report.Errors[0].Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeFormatWithWhiteSpaceAtItsEdge_WithoutTrim_ReadsBack(
        bool fromTheCulture
    )
    {
        // Arrange
        var spaced = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        spaced.DateTimeFormat.TimeSeparator = " ";
        var map = fromTheCulture
            ? Map<TimeOnly>(c => c
                .Format(":HH:mm")
                .Culture(spaced))
            : Map<TimeOnly>(c => c.Format(" HH:mm "));

        // Act
        var report = await ReadBackAsync(new TimeOnly(14, 30), map);

        // Assert
        Assert.Equal(new TimeOnly(14, 30), ValueOf(report));
    }

    [Fact]
    public async Task DurationFormatWithWhiteSpaceAtItsEdge_WithoutTrim_ReadsBack()
    {
        // Act
        var report = await ReadBackAsync(TimeSpan.FromMinutes(90), Map<TimeSpan>(c => c.Format("\\ hh\\:mm")));

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(90), ValueOf(report));
    }

    [Fact]
    public async Task FormatOfACultureWithAnotherCalendar_UnderTrim_ReadsTheDateOfThatCalendar()
    {
        // Act
        var report = await ReadAsync(TabularType.Csv, "1445-01-01", UmAlQuraMap(""));

        // Assert
        Assert.Equal(UmAlQuraNewYear1445(), ValueOf(report));
    }

    [Fact]
    public async Task FormatOfACultureWithAnotherCalendar_UnderTrim_ReadsBack()
    {
        // Act
        var report = await ReadBackAsync(UmAlQuraNewYear1445(), UmAlQuraMap(""));

        // Assert
        Assert.Equal(UmAlQuraNewYear1445(), ValueOf(report));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormatOfACultureWithAnotherCalendar_WithEdgeWhiteSpaceUnderTrim_IsAConfigurationError(
        bool export
    )
    {
        // Act
        var kind = await ConfigurationKindAsync(UmAlQuraNewYear1445(), UmAlQuraMap(" "), export);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, kind);
    }

    private static TabularMap<Value<DateOnly>> UmAlQuraMap(
        string edge
    )
    {
        var culture = (CultureInfo)CultureInfo
            .GetCultureInfo("ar-SA")
            .Clone();

        culture.DateTimeFormat.Calendar = new UmAlQuraCalendar();

        return Map<DateOnly>(c => c
            .Format(edge + "yyyy-MM-dd")
            .Culture(culture)
            .Trim());
    }

    private static DateOnly UmAlQuraNewYear1445() => DateOnly.FromDateTime(new UmAlQuraCalendar().ToDateTime(1445,
        1,
        1,
        0,
        0,
        0,
        0));

    private static CultureInfo HebrewCulture()
    {
        var hebrew = (CultureInfo)CultureInfo
            .GetCultureInfo("he-IL")
            .Clone();

        hebrew.DateTimeFormat.Calendar = new HebrewCalendar();

        return hebrew;
    }

    /// <summary>Every case once for the import and once for the export.</summary>
    private static TheoryData<string, bool> Directions(
        params string[] cases
    )
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in cases)
        {
            data.Add(name, false);
            data.Add(name, true);
        }

        return data;
    }

    /// <summary>Exports one value and reads the written file back.</summary>
    private static async Task<ImportReport<Value<T>>> ReadBackAsync<T>(
        T value,
        TabularMap<Value<T>> map
    )
    {
        var file = new MemoryStream();
        await CsvInstance.ExportAsync(new[] { new Value<T>(value) }, map, file, Token);
        file.Position = 0;

        return await CsvInstance.ImportAsync(file, map, Token);
    }

    /// <summary>The kind of the first error of an import or of the fatal error of an export with the map.</summary>
    private static async Task<TabularErrorKind?> ConfigurationKindAsync<T>(
        T value,
        TabularMap<Value<T>> map,
        bool export
    ) => export
        ? (await CsvInstance.ExportAsync(new[] { new Value<T>(value) }, map, new MemoryStream(), Token)).FatalError
        ?.Kind
        : (await CsvInstance.ImportAsync(TestInput.Utf8("x"), map, Token)).Errors is [var first, ..]
            ? first.Kind
            : null;
}
