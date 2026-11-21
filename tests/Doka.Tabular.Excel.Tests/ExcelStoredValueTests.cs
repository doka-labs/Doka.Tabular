using System.IO.Compression;

namespace Doka.Tabular.Excel.Tests;

/// <summary>Stored cell forms that Excel writes: ISO 8601 date cells, empty value elements, extension elements.</summary>
public sealed class ExcelStoredValueTests
{
    private static readonly ExcelFormat s_format = new(new ExcelFormatOptions());

    private static readonly TabularMap<FormulaValues> s_escapedFormat = TabularMap
        .For<FormulaValues>()
        .Column(x => x.Id, index: 0, configure: c => c.ExcelNumberFormat("0\" _x0041_\""))
        .Column(x => x.Number, index: 1)
        .Column(x => x.Flag, index: 2)
        .Column(x => x.Date, index: 3)
        .Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ExcelImportOptions Partial => new() { Strategy = ImportStrategy.Partial };

    [Fact]
    public async Task Date1904_IsKeptWhenAnExtensionRepeatsWorkbookPr()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""
            <row r="1"><c r="A1"><v>43921</v></c><c r="B1" t="d"><v>2024-04-01</v></c></row>
            <row r="2"><c r="A2"><v>0</v></c><c r="B2" t="d"><v>1904-01-01</v></c></row>
            """,
            workbookBeforeSheets: """<workbookPr date1904="1"/>""",
            workbookAfterSheets: """
            <extLst><ext uri="{B58B0392-4F1F-4190-BB64-5DF3571DCE5F}" xmlns:x15="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"><x15:workbookPr chartTrackingRefBase="1"/></ext></extLst>
            """);

        var map = TabularMap
            .For<TwoDates>()
            .Column(x => x.Serial, index: 0, configure: c => c.AllowExcelSerialNumbers())
            .Column(x => x.Iso, index: 1)
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Empty(report.Errors);
        Assert.Equal(
            [
                new TwoDates(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1)),
                new TwoDates(new DateOnly(1904, 1, 1), new DateOnly(1904, 1, 1)),
            ],
            report.Items);
    }

    [Fact]
    public async Task EmptyValueElement_IsAStoredEmptyValueNotAMissingResult()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""
            <row r="1"><c r="A1"><v>1</v></c><c r="B1" t="str"><f>""</f><v/></c><c r="C1"><v/></c><c r="D1" t="b"><v/></c></row>
            """);

        var map = TabularMap
            .For<EmptyValues>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Text, index: 1)
            .Column(x => x.Number, index: 2)
            .Column(x => x.Flag, index: 3)
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Empty(report.Errors);
        Assert.Equal([new EmptyValues(1, "", null, null)], report.Items);
    }

    [Fact]
    public async Task EmptyStoredFormulaResult_IsAnEmptyValueOfEveryCellType()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""
            <row r="1"><c r="A1"><v>1</v></c><c r="B1"><f>IF(1,"",1)</f><v/></c><c r="C1" t="b"><f>IF(1,"",TRUE)</f><v/></c><c r="D1" t="d"><f>IF(1,"",TODAY())</f><v/></c></row>
            """);

        // Act
        var report = await s_format.ImportAsync(workbook, FormulaMap(), Partial, Token);

        // Assert
        Assert.Empty(report.Errors);
        Assert.Equal([new FormulaValues(1, null, null, null)], report.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" t=\"b\"")]
    [InlineData(" t=\"d\"")]
    public async Task RowWithOnlyAFormulaWithAnEmptyResult_IsADataRow(
        string type
    )
    {
        // Arrange
        var map = TabularMap
            .For<OneDate>()
            .Column(x => x.Value, index: 0)
            .Build();

        // Act
        var report = await s_format.ImportAsync(EmptyFormulaRow(type), map, Partial, Token);

        // Assert
        Assert.Equal([new OneDate(null)], report.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" t=\"b\"")]
    [InlineData(" t=\"d\"")]
    public async Task RowWithOnlyAFormulaWithAnEmptyResult_IsRejectedByAFieldThatRejectsFormulas(
        string type
    )
    {
        // Arrange
        var map = TabularMap
            .For<OneDate>()
            .Column(x => x.Value, index: 0, configure: c => c.RejectFormulas())
            .Build();

        // Act
        var report = await s_format.ImportAsync(EmptyFormulaRow(type), map, Partial, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.UnsupportedValue,
            Assert.Single(report.Errors)
                .Code);
    }

    private static MemoryStream EmptyFormulaRow(
        string type
    ) => RawWorkbook.Create($"""
        <row r="1"><c r="A1"{type}><f>IF(1,"",1)</f><v/></c></row>
        """);

    [Fact]
    public async Task FormulaWithoutStoredResult_StaysAnErrorOfEveryCellType()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""
            <row r="1"><c r="A1"><v>1</v></c><c r="B1"><f>1+1</f></c><c r="C1" t="b"><f>TRUE</f></c><c r="D1" t="d"><f>TODAY()</f></c></row>
            """);

        // Act
        var report = await s_format.ImportAsync(workbook, FormulaMap(), Partial, Token);

        // Assert
        Assert.Equal([
                TabularErrorCodes.Conversion.UnsupportedValue, TabularErrorCodes.Conversion.UnsupportedValue,
                TabularErrorCodes.Conversion.UnsupportedValue,
            ],
            report.Errors.Select(error => error.Code));
    }

    [Theory]
    [InlineData("_x0079__x0079__x0079__x0079_", true)]
    [InlineData("yyyy", true)]
    [InlineData("_x0064_._x006D_.", true)]
    [InlineData("0.00", false)]
    [InlineData("_x0030_.00", false)]
    public async Task EscapedNumberFormatCode_IsReadAsTheFormatItEncodes(
        string code,
        bool date
    )
    {
        // Arrange
        // WHY: _x0079_ is y and _x0064_ is d; such escaped codes are date formats and mark the number as a date.
        var workbook = RawWorkbook.Create("""
            <row r="1"><c r="A1" s="1"><v>45383</v></c></row>
            """,
            styles: $"""
            <numFmts count="1"><numFmt numFmtId="164" formatCode="{code}"/></numFmts><cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="164" applyNumberFormat="1"/></cellXfs>
            """);

        var map = TabularMap
            .For<OneDate>()
            .Column(x => x.Value, index: 0)
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Equal(date, report.Errors.Count == 0);
        Assert.Equal(date
                ? [new OneDate(new DateOnly(2024, 4, 1))]
                : [],
            report.Items);
    }

    [Fact]
    public async Task LiteralEscapeTextInAnExportedNumberFormat_IsEscaped()
    {
        // Arrange
        var file = new MemoryStream();

        // Act
        var report = await s_format.ExportAsync(new[] { new FormulaValues(7, null, null, null) },
            s_escapedFormat,
            file,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        file.Position = 0;
        await using var archive = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read, true, null, Token);
        var entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("xl/styles.xml"));
        using var reader = new StreamReader(await entry.OpenAsync(Token));
        Assert.Contains("_x005F_x0041_", await reader.ReadToEndAsync(Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiteralEscapeTextInAnExportedNumberFormat_ReadsBack()
    {
        // Arrange
        var file = new MemoryStream();

        // Act
        // WHY: The reimport of the export is the checked contract, so both form one action.
        await s_format.ExportAsync(new[] { new FormulaValues(7, null, null, null) }, s_escapedFormat, file, Token);
        file.Position = 0;
        var report = await s_format.ImportAsync(file, s_escapedFormat, Partial, Token);

        // Assert
        Assert.Empty(report.Errors);
        Assert.Equal(7,
            Assert.Single(report.Items!)
                .Id);
    }

    private static TabularMap<FormulaValues> FormulaMap() => TabularMap
        .For<FormulaValues>()
        .Column(x => x.Id, index: 0)
        .Column(x => x.Number, index: 1)
        .Column(x => x.Flag, index: 2)
        .Column(x => x.Date, index: 3)
        .Build();

    [Fact]
    public async Task IsoDateCellsWithAZone_AreInstantsInAUtcColumn()
    {
        // Arrange
        var utc = TabularMap
            .For<ZonedValues>()
            .Column(x => x.First, index: 0, configure: c => c.ExcelAsUtc())
            .Column(x => x.Second, index: 1, configure: c => c.ExcelAsUtc())
            .Column(x => x.Third, index: 2, configure: c => c.ExcelAsUtc())
            .Column(x => x.Zoneless, index: 3)
            .Build();

        // Act
        var report = await s_format.ImportAsync(ZonedWorkbook(), utc, Partial, Token);

        // Assert
        Assert.Equal(new ZonedValues(new DateTime(1976,
                    11,
                    22,
                    8,
                    30,
                    0,
                    DateTimeKind.Utc),
                new DateTime(2024,
                    4,
                    1,
                    12,
                    30,
                    0,
                    DateTimeKind.Utc),
                new DateTime(2024,
                    3,
                    31,
                    23,
                    0,
                    30,
                    DateTimeKind.Utc),
                new DateTime(2024, 4, 1, 14, 30, 0)),
            Assert.Single(report.Items!));

        Assert.Equal(DateTimeKind.Utc, report.Items![0].First.Kind);
    }

    [Fact]
    public async Task IsoDateCellWithAZone_InAColumnWithoutUtc_IsAnInvalidFormat()
    {
        // Arrange
        var zoneless = TabularMap
            .For<ZonedValues>()
            .Column(x => x.First, index: 0)
            .Column(x => x.Second, index: 1, configure: c => c.ExcelAsUtc())
            .Column(x => x.Third, index: 2, configure: c => c.ExcelAsUtc())
            .Column(x => x.Zoneless, index: 3)
            .Build();

        // Act
        var report = await s_format.ImportAsync(ZonedWorkbook(), zoneless, Partial, Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal((TabularErrorCodes.Conversion.InvalidFormat, 0, ReportStatus.CompletedWithErrors),
            (error.Code, error.Location.ColumnIndex!.Value, report.Status));
    }

    private static MemoryStream ZonedWorkbook() => RawWorkbook.Create("""
        <row r="1">
          <c r="A1" t="d"><v>1976-11-22T08:30Z</v></c>
          <c r="B1" t="d"><v>2024-04-01T14:30+02:00</v></c>
          <c r="C1" t="d"><v>2024-04-01T01:00:30+02:00</v></c>
          <c r="D1" t="d"><v>2024-04-01T14:30</v></c>
        </row>
        """);

    [Fact]
    public async Task IsoDateCellWithAZoneBeyondTheLastDate_IsARangeErrorOfTheField()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""<row r="1"><c r="A1" t="d"><v>9999-12-31T23:30-02:00</v></c></row>""");
        var map = TabularMap
            .For<OneMoment>()
            .Column(x => x.Value, index: 0, configure: c => c.ExcelAsUtc())
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Conversion.OutOfRange, 0, null),
            (Assert.Single(report.Errors)
                .Code, report.Errors[0].Location.ColumnIndex!.Value, report.FatalError));
    }

    [Fact]
    public async Task IsoDateCellWithAZoneBeforeTheFirstDate_IsARangeErrorOfTheField()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""
            <row r="1"><c r="A1" t="d"><v>1904-01-01T01:00+02:00</v></c></row>
            <row r="2"><c r="A2" t="d"><v>1904-01-02T00:00Z</v></c></row>
            """,
            workbookBeforeSheets: """<workbookPr date1904="1"/>""");

        var map = TabularMap
            .For<OneMoment>()
            .Column(x => x.Value, index: 0, configure: c => c.ExcelAsUtc())
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Conversion.OutOfRange, 1L, null),
            (Assert.Single(report.Errors)
                .Code, report.Errors[0].Location.RecordNumber!.Value, report.FatalError));

        Assert.Equal(new DateTime(1904, 1, 2),
            Assert.Single(report.Items!)
                .Value);
    }

    [Fact]
    public async Task IsoDateCellWithAZone_WhoseLocalDateLiesBeforeTheFirstDate_IsReadAsItsInstant()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""<row r="1"><c r="A1" t="d"><v>1903-12-31T23:00-02:00</v></c></row>""",
            workbookBeforeSheets: """<workbookPr date1904="1"/>""");

        var map = TabularMap
            .For<OneMoment>()
            .Column(x => x.Value, index: 0, configure: c => c.ExcelAsUtc())
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Empty(report.Errors);
        Assert.Equal(new DateTime(1904,
                1,
                1,
                1,
                0,
                0,
                DateTimeKind.Utc),
            Assert.Single(report.Items!)
                .Value);
    }

    [Theory]
    [InlineData("1899-12-29T23:00-02:00", true)]
    [InlineData("1899-12-30T10:00Z", false)]
    [InlineData("1899-12-30T23:00-02:00", false)]
    public async Task IsoDateCellWithAZoneOnTheTimeOnlyDate_IsAnInstantAndNoTimeOfDay(
        string text,
        bool date1904
    )
    {
        // Arrange
        var workbook = RawWorkbook.Create($"""<row r="1"><c r="A1" t="d"><v>{text}</v></c></row>""",
            workbookBeforeSheets: date1904
                ? """<workbookPr date1904="1"/>"""
                : "");

        var map = TabularMap
            .For<OneMoment>()
            .Column(x => x.Value, index: 0, configure: c => c.ExcelAsUtc())
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Conversion.OutOfRange, null),
            (Assert.Single(report.Errors)
                .Code, report.FatalError));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("garbage")]
    public async Task BooleanCellWithAnotherValue_IsASyntaxError(
        string value
    )
    {
        // Arrange
        var workbook = RawWorkbook.Create($"""<row r="1"><c r="A1" t="b"><v>{value}</v></c></row>""");
        var map = TabularMap
            .For<OneFlag>()
            .Column(x => x.Value, index: 0)
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, report.FatalError?.Code);
        Assert.Empty(report.Items!);
    }

    [Fact]
    public async Task IsoDateCells_ReadLikeTheSerialNumbersOfATransitionalWorkbook()
    {
        // Arrange
        var workbook = RawWorkbook.Create("""
            <row r="1">
              <c r="A1" t="d"><v>1980-04-01</v></c>
              <c r="B1" t="d"><v>14:29:59.9999999999968050</v></c>
              <c r="C1" t="d"><v>2024-04-01T08:29:59.99999979045242400</v></c>
              <c r="D1" t="d"><v>PT27H0M0.000S</v></c>
              <c r="E1" t="d"><v>1899-12-30</v></c>
              <c r="F1" t="d"><v>-P1DT12H</v></c>
              <c r="G1" t="d"><v>1900-01-01</v></c>
              <c r="H1" t="d"><v>1900-03-01</v></c>
            </row>
            """);

        var map = TabularMap
            .For<IsoValues>()
            .Column(x => x.Date, index: 0)
            .Column(x => x.Time, index: 1)
            .Column(x => x.Moment, index: 2)
            .Column(x => x.Duration, index: 3)
            .Column(x => x.Midnight, index: 4)
            .Column(x => x.Negative, index: 5)
            .Column(x => x.FirstDay, index: 6)
            .Column(x => x.AfterLeapDay, index: 7)
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        Assert.Empty(report.Errors);
        Assert.Equal(
            [
                new IsoValues(new DateOnly(1980, 4, 1),
                    new TimeOnly(14, 30),
                    new DateTime(2024, 4, 1, 8, 30, 0),
                    TimeSpan.FromHours(27),
                    TimeOnly.MinValue,
                    TimeSpan.FromHours(-36),
                    new DateOnly(1900, 1, 1),
                    new DateOnly(1900, 3, 1)),
            ],
            report.Items);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2024-04-01T08:30:00+24:00")]
    [InlineData("P1Y")]
    [InlineData("25:00:00")]
    public async Task InvalidIsoDateCell_IsASyntaxErrorAndNotADamagedContainer(
        string text
    )
    {
        // Arrange
        var workbook = RawWorkbook.Create($"""<row r="1"><c r="A1" t="d"><v>{text}</v></c></row>""");
        var map = TabularMap
            .For<OneDate>()
            .Column(x => x.Value, index: 0)
            .Build();

        // Act
        var report = await s_format.ImportAsync(workbook, map, Partial, Token);

        // Assert
        var error = report.FatalError ?? Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, error.Code);
        Assert.Contains("invalid cell", error.Message, StringComparison.Ordinal);
    }

    private sealed record TwoDates(
        DateOnly Serial,
        DateOnly? Iso
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record OneDate(
        DateOnly? Value
    );

    private sealed record FormulaValues(
        int Id,
        int? Number,
        bool? Flag,
        DateOnly? Date
    );

    private sealed record EmptyValues(
        int Id,
        string Text,
        decimal? Number,
        bool? Flag
    );

    // WHY: Only the import creates instances, through the constructor the map binds by reflection.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed record OneMoment(DateTime Value);

    // WHY: Only the import creates instances, through the constructor the map binds by reflection.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed record OneFlag(bool Value);

    // WHY: Only the import creates instances, through the constructor the map binds by reflection.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed record ZonedValues(
        DateTime First,
        DateTime Second,
        DateTime Third,
        DateTime Zoneless
    );

    private sealed record IsoValues(
        DateOnly Date,
        TimeOnly Time,
        DateTime Moment,
        TimeSpan Duration,
        TimeOnly Midnight,
        TimeSpan Negative,
        DateOnly FirstDay,
        DateOnly AfterLeapDay
    );
}
