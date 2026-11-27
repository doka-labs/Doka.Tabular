using System.IO.Compression;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

/// <summary>Formats, sheet selection, read range, empty rows, formulas, escaping, and package safety.</summary>
public sealed class ExcelReadTests
{
    private static readonly object?[][] s_reportSheet =
        [["Bericht"], [], [], ["Id", "Name"], ["x", "Erster"], [2, "Zweiter"], [], ["Summe"]];

    private static readonly object?[][] s_sparseRows =
        [["Ada", null, null, null, null, null, null, null, null, null, null, "L"], ["Bob"]];

    private static readonly TabularMap<Row> s_rows = TabularMap
        .For<Row>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Column(x => x.Name, index: 1, configure: c => c.Header("Name"))
        .Build();

    private static ExcelImportOptions Partial => new() { Strategy = ImportStrategy.Partial };

    [Fact]
    public async Task CsvContent_AsExcel_IsAFormatMismatch()
    {
        // Act
        var report = await ExcelInstance.ImportAsync(TestInput.Utf8("1,a"), s_rows, Partial, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.FormatMismatch,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public void EmptyZip_StartsWithTheEndOfTheCentralDirectory()
    {
        // Act
        // WHY: An archive without entries starts with the end of the central directory, PK 05 06.
        var bytes = RawWorkbook.Bytes(RawWorkbook.Zip(new Dictionary<string, string>()));

        // Assert
        Assert.Equal([0x50, 0x4B, 0x05, 0x06], bytes[..4]);
    }

    [Fact]
    public async Task EmptyZip_IsAZipWithoutWorkbookForOneSheet()
    {
        // Act
        var report = await ExcelInstance.ImportAsync(
            RawWorkbook.Zip(new Dictionary<string, string>()),
            s_rows,
            Partial,
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.UnsupportedFormat,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task EmptyZip_IsAZipWithoutWorkbookForSeveralSheets()
    {
        // Arrange
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("Sheet1"), s_rows);

        // Act
        var report = await ExcelInstance.Format.ImportAsync(
            RawWorkbook.Zip(new Dictionary<string, string>()),
            sheets.Build(),
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.UnsupportedFormat,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task EmptyZip_IsAContainerForCsv()
    {
        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            RawWorkbook.Zip(new Dictionary<string, string>()),
            s_rows,
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Schema.FormatMismatch, error.Code);
        Assert.Contains("ZIP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZipWithoutWorkbook_IsAnUnsupportedFormat()
    {
        // Arrange
        var plainZip = RawWorkbook.Zip(new Dictionary<string, string> { ["readme.txt"] = "no workbook" });

        // Act
        var report = await ExcelInstance.ImportAsync(plainZip, s_rows, Partial, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.UnsupportedFormat,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task DamagedZip_IsASyntaxErrorBeforeTheFirstRecord()
    {
        // Arrange
        var damaged = new MemoryStream(
        [
            .. RawWorkbook
                .Bytes(Create(TabularType.Excel, [1, "a"]))
                .Take(200),
        ]);

        // Act
        var report = await ExcelInstance.ImportAsync(damaged, s_rows, Partial, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, (report.FatalError ?? Assert.Single(report.Errors)).Code);
        Assert.Empty(report.Items!);
    }

    [Fact]
    public async Task ExpectedVariantXlsm_ForAnXlsx_IsAFormatMismatch()
    {
        // Act
        var report = await ExcelInstance.ImportAsync(
            Create(TabularType.Excel, [1, "a"]),
            s_rows,
            Partial with { ExpectedVariant = ExcelVariant.Xlsm },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.FormatMismatch,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task SheetSelection_ChoosesTheOnlySheet()
    {
        // Arrange
        var single = Workbook(("Benutzer", [[1, "Ada"]]));

        // Act
        var report = await ExcelInstance.ImportAsync(single, s_rows, Partial, Token);

        // Assert
        Assert.Equal(new Row(1, "Ada"), Assert.Single(report.Items!));
    }

    [Fact]
    public async Task SheetSelection_OfSeveralSheetsWithoutName_IsAmbiguousAndNamesTheSheets()
    {
        // Arrange
        var two = Workbook(("Hinweise", [["Text"]]), ("Benutzer", [[1, "Ada"]]));

        // Act
        var report = await ExcelInstance.ImportAsync(two, s_rows, Partial, Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Schema.AmbiguousSheet, error.Code);
        Assert.Contains("Hinweise", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SheetSelection_OfSeveralSheetsByName_ReadsTheNamedSheet()
    {
        // Arrange
        var two = Workbook(("Hinweise", [["Text"]]), ("Benutzer", [[1, "Ada"]]));

        // Act
        var report = await ExcelInstance.ImportAsync(
            two,
            s_rows,
            Partial with { SheetName = TabularSheetName.Create("Benutzer") },
            Token);

        // Assert
        Assert.Equal(new Row(1, "Ada"), Assert.Single(report.Items!));
    }

    [Theory]
    [InlineData(ColumnBinding.ByHeaderName)]
    [InlineData(ColumnBinding.ByIndexWithHeader)]
    public async Task StartAndEndRow_SelectTheTableAndKeepSheetRowNumbers(
        ColumnBinding binding
    )
    {
        // Act
        var report = await ExcelInstance.ImportAsync(
            Workbook(("Sheet1", s_reportSheet)),
            s_rows,
            Partial with
            {
                StartRow = 4,
                Binding = binding,
                EndRow = 6,
            },
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal((5L, 1L), (error.Location.RowNumber!.Value, error.Location.RecordNumber!.Value));
        Assert.Equal("Sheet1", error.Location.SheetName?.Value);
    }

    [Fact]
    public async Task StartRowWithoutEndRow_ReadsToTheLastRow()
    {
        // Act
        var report = await ExcelInstance.ImportAsync(
            Workbook(("Sheet1", s_reportSheet)),
            s_rows,
            Partial with
            {
                StartRow = 4,
                Binding = ColumnBinding.ByIndexWithHeader,
            },
            Token);

        // Assert
        Assert.Contains(report.Errors, error => error.Location.RowNumber == 8);
    }

    [Fact]
    public async Task StartAndEndRow_WithoutHeader_StartAtTheFirstDataRow()
    {
        // Act
        var report = await ExcelInstance.ImportAsync(
            Workbook(("Sheet1", s_reportSheet)),
            s_rows,
            Partial with
            {
                StartRow = 5,
                EndRow = 6,
            },
            Token);

        // Assert
        Assert.Equal(5L, report.Errors[0].Location.RowNumber);
    }

    [Fact]
    public async Task EmptyAndMissingCells_AreNullForANullableField()
    {
        // Arrange
        var map = TabularMap
            .For<Dated>()
            .Column(x => x.Name, index: 0)
            .Column(x => x.DateOfHire, index: 10)
            .Build();

        // Act
        var report = await ExcelInstance.ImportAsync(Workbook(("Sheet1", s_sparseRows)), map, Token);

        // Assert
        Assert.All(report.Items!, item => Assert.Null(item.DateOfHire));
    }

    [Fact]
    public async Task EmptyAndMissingCells_ViolateARequiredField()
    {
        // Arrange
        var required = TabularMap
            .For<Dated>()
            .Column(x => x.Name, index: 0)
            .Column(x => x.DateOfHire, index: 10, configure: c => c.Required())
            .Build();

        // Act
        var report = await ExcelInstance.ImportAsync(Workbook(("Sheet1", s_sparseRows)), required, Partial, Token);

        // Assert
        Assert.Equal(
            [(1L, 10), (2L, 10)],
            report.Errors.Select(error => (error.Location.RowNumber!.Value, error.Location.ColumnIndex!.Value)));
    }

    [Fact]
    public async Task FormattedOnlyRow_IsSkippedAndAFormulaWithEmptyResultIsARecord()
    {
        // Arrange
        const string sheet = """
            <row r="1"><c r="A1"><v>1</v></c><c r="B1" t="inlineStr"><is><t>Ada</t></is></c></row>
            <row r="2"><c r="A2" s="0"/></row>
            <row r="3"><c r="F3" t="str"><f>""</f><v></v></c></row>
            <row r="4"><c r="A4" t="inlineStr"><is><t>x</t></is></c></row>
            """;

        var map = TabularMap
            .For<Row>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Name, index: 1, configure: c => c.Required())
            .Build();

        // Act
        var report = await ExcelInstance.ImportAsync(RawWorkbook.Create(sheet), map, Partial, Token);

        // Assert
        Assert.Equal(3, report.RowsRead);
        Assert.Contains(report.Errors, error => error.Location.RowNumber == 3);
        Assert.Contains(report.Errors, error => error.Location.RowNumber == 4);
        Assert.DoesNotContain(report.Errors, error => error.Location.RowNumber == 2);
    }

    [Fact]
    public async Task Formulas_UseStoredResultsAndRejectMissingResultsAndErrors()
    {
        // Arrange
        const string sheet = """
            <row r="1"><c r="A1"><v>1</v></c><c r="B1"><f>B2*C2</f><v>42</v></c></row>
            <row r="2"><c r="A2"><v>2</v></c><c r="B2"><f>B2*C2</f></c></row>
            <row r="3"><c r="A3"><v>3</v></c><c r="B3" t="e"><f>1/0</f><v>#DIV/0!</v></c></row>
            """;

        var map = TabularMap
            .For<Amount>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Value, index: 1)
            .Build();

        // Act
        var report = await ExcelInstance.ImportAsync(RawWorkbook.Create(sheet), map, Partial, Token);

        // Assert
        Assert.Equal(
            42m,
            Assert.Single(report.Items!)
                .Value);

        Assert.Equal([2L, 3L], report.Errors.Select(error => error.Location.RowNumber!.Value));
        Assert.All(report.Errors, error => Assert.Equal(TabularErrorCodes.Conversion.UnsupportedValue, error.Code));
    }

    [Fact]
    public async Task Formulas_InAFieldThatRejectsThem_AreRejectedInEveryRow()
    {
        // Arrange
        const string sheet = """
            <row r="1"><c r="A1"><v>1</v></c><c r="B1"><f>B2*C2</f><v>42</v></c></row>
            <row r="2"><c r="A2"><v>2</v></c><c r="B2"><f>B2*C2</f></c></row>
            <row r="3"><c r="A3"><v>3</v></c><c r="B3" t="e"><f>1/0</f><v>#DIV/0!</v></c></row>
            """;

        var banned = TabularMap
            .For<Amount>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Value, index: 1, configure: c => c.RejectFormulas())
            .Build();

        // Act
        var report = await ExcelInstance.ImportAsync(RawWorkbook.Create(sheet), banned, Partial, Token);

        // Assert
        Assert.Equal(3, report.Errors.Count);
    }

    [Fact]
    public async Task HiddenRows_AreReadByDefault()
    {
        // Arrange
        const string sheet = """
            <row r="1"><c r="A1"><v>1</v></c><c r="B1" t="inlineStr"><is><t>a</t></is></c></row>
            <row r="2" hidden="1"><c r="A2" t="inlineStr"><is><t>x</t></is></c><c r="B2" t="inlineStr"><is><t>b</t></is></c></row>
            <row r="3"><c r="A3"><v>3</v></c><c r="B3" t="inlineStr"><is><t>c</t></is></c></row>
            """;

        // Act
        var report = await ExcelInstance.ImportAsync(RawWorkbook.Create(sheet), s_rows, Partial, Token);

        // Assert
        Assert.Equal((3L, 1), (report.RowsRead, report.Errors.Count));
    }

    [Fact]
    public async Task HiddenRows_AreSkippedForVisibleRowsOnly()
    {
        // Arrange
        const string sheet = """
            <row r="1"><c r="A1"><v>1</v></c><c r="B1" t="inlineStr"><is><t>a</t></is></c></row>
            <row r="2" hidden="1"><c r="A2" t="inlineStr"><is><t>x</t></is></c><c r="B2" t="inlineStr"><is><t>b</t></is></c></row>
            <row r="3"><c r="A3"><v>3</v></c><c r="B3" t="inlineStr"><is><t>c</t></is></c></row>
            """;

        // Act
        var report = await ExcelInstance.ImportAsync(
            RawWorkbook.Create(sheet),
            s_rows,
            Partial with { VisibleRowsOnly = true },
            Token);

        // Assert
        Assert.Equal((2L, 0), (report.RowsRead, report.Errors.Count));
        Assert.Equal([1, 3], report.Items!.Select(item => item.Id));
    }

    [Fact]
    public async Task EscapedStrings_AreWrittenWithEscapes()
    {
        // Act
        var (_, file) = await ExcelInstance.ExportAsync(EscapedRows(), s_rows, Token);

        // Assert
        var xml = SheetXml(file!);
        Assert.Contains("_x0001_", xml, StringComparison.Ordinal);
        Assert.Contains("_x000D_", xml, StringComparison.Ordinal);
        Assert.Contains("_x005F_x0041_", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EscapedStrings_ReadBackUnchanged()
    {
        // Arrange
        var rows = EscapedRows();

        // Act
        // WHY: The reimport of the export is the checked contract, so both form one action.
        var (_, file) = await ExcelInstance.ExportAsync(rows, s_rows, Token);
        var report = await ExcelInstance.ImportAsync(file!, s_rows, Token);

        // Assert
        Assert.Equal(rows, report.Items);
    }

    [Fact]
    public async Task ForeignEscapesPhoneticRunsAndRichText_AreDecoded()
    {
        // Arrange
        const string foreign = """
            <row r="1"><c r="A1"><v>1</v></c><c r="B1" t="s"><v>0</v></c></row>
            <row r="2"><c r="A2"><v>2</v></c><c r="B2" t="inlineStr"><is><t>_x005F_x0041_</t></is></c></row>
            <row r="3"><c r="A3"><v>3</v></c><c r="B3" t="inlineStr"><is><t>東京</t><rPh sb="0" eb="2"><t>トウキョウ</t></rPh></is></c></row>
            <row r="4"><c r="A4"><v>4</v></c><c r="B4" t="s"><v>1</v></c></row>
            """;

        var file = RawWorkbook.Create(
            foreign,
            "<si><t>Zeile1_x000D_</t></si><si><r><t>Rich</t></r><r><t> text</t></r></si>");

        // Act
        var report = await ExcelInstance.ImportAsync(file, s_rows, Token);

        // Assert
        Assert.True(
            report.Status == ReportStatus.Succeeded,
            string.Join(" | ", report.Errors.Select(error => $"{error.Code} {error.Message}"))
            + report.FatalError?.Message);

        Assert.Equal(["Zeile1\r", "_x0041_", "東京", "Rich text"], report.Items!.Select(item => item.Name));
    }

    private static Row[] EscapedRows() =>
    [
        new(0, "Ada\u0001Lovelace"), new(1, "Zeile1\r\nZeile2"), new(2, "Ada\tLovelace"), new(3, "_x0041_"),
    ];

    [Fact]
    public async Task CellTexts_StayCorrectAcrossRowsRunsAndBufferGrowth()
    {
        // Arrange
        var map = TabularMap
            .For<Row>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Name, index: 1)
            .Build();

        var longText = new string('x', 6000);
        var rows = $"""
            <row r="1"><c r="A1"><v>1</v></c><c r="B1" t="inlineStr"><is><r><t>Ri</t></r><r><t>ch</t></r><r><t> text</t></r></is></c></row>
            <row r="2"><c r="A2"><v>2</v></c><c r="B2" t="inlineStr"><is><t>{longText}</t></is></c></row>
            <row r="3"><c r="A3"><v>3</v></c><c r="B3" t="inlineStr"><is><t>a &amp; b &lt;c&gt;</t></is></c></row>
            <row r="4"><c r="A4"><v>4</v></c><c r="B4" t="inlineStr"><is><t>short</t></is></c></row>
            """;

        // Act
        var read = await ExcelInstance.ImportAsync(RawWorkbook.Create(rows), map, Token);

        // Assert
        Assert.True(
            read.Status == ReportStatus.Succeeded,
            string.Join(" | ", read.Errors.Select(error => $"{error.Code} {error.Message}"))
            + read.FatalError?.Message);

        Assert.Equal(["Rich text", longText, "a & b <c>", "short"], read.Items!.Select(item => item.Name));
        Assert.Equal([1, 2, 3, 4], read.Items!.Select(item => item.Id));
    }

    [Fact]
    public async Task Dtd_InPackageXml_IsRejected()
    {
        // Arrange
        var withDtd = RawWorkbook.Create(
            """<row r="1"><c r="A1"><v>1</v></c></row>""",
            sheetPrefix: "<?xml version=\"1.0\"?><!DOCTYPE worksheet [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>");

        // Act
        var report = await ExcelInstance.ImportAsync(withDtd, s_rows, Partial, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Empty(report.Items!);
    }

    [Fact]
    public async Task ExpandedBytes_AreLimitedWhileUnpacking()
    {
        // Arrange
        var format = new ExcelFormat(new ExcelFormatOptions { MaxExpandedBytes = 1024 * 1024 });

        // Act
        var report = await format.ImportAsync(RawWorkbook.Expanding(4), s_rows, Partial, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Resource.LimitExceeded, report.FatalError?.Code);
    }

    [Fact]
    public void ExpandedBytes_DefaultIsOneGibibyte()
    {
        // Act
        var options = new ExcelFormatOptions();

        // Assert
        Assert.Equal(1L << 30, options.MaxExpandedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeekableAndForwardOnlyInput_GiveTheSameResult(
        bool forwardOnly
    )
    {
        // Arrange
        var bytes = RawWorkbook.Bytes(Create(TabularType.Excel, [1, "a"], [2, "b"]));
        Stream input = forwardOnly
            ? new ForwardOnlyStream(new MemoryStream(bytes))
            : new MemoryStream(bytes);

        // Act
        var report = await ExcelInstance.ImportAsync(input, s_rows, Token);

        // Assert
        Assert.Equal([new Row(1, "a"), new Row(2, "b")], report.Items);
    }

    private static string SheetXml(
        Stream file
    )
    {
        file.Position = 0;
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.Entries.First(item => item.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());

        return reader.ReadToEnd();
    }

    private sealed record Row(
        int Id,
        string Name
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Amount(
        int Id,
        decimal? Value
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Dated(
        string Name,
        DateTime? DateOfHire
    );
}
