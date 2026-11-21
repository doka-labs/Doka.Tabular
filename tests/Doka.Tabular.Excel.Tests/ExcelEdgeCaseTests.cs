using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

/// <summary>Rows without references, header cell limits, decoded field limits, and control characters in names.</summary>
public sealed class ExcelEdgeCaseTests
{
    private static readonly TabularMap<Number> s_numbers = TabularMap
        .For<Number>()
        .Column(x => x.Value, index: 0)
        .Build();

    private static readonly TabularMap<Text> s_texts = TabularMap
        .For<Text>()
        .Column(x => x.Value, index: 0)
        .Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<int, int?, bool> OutsideTheSelection => new()
    {
        { 1, 1, false },
        { 2, null, false },
        { 1, null, true },
    };

    [Fact]
    public async Task RowsWithoutReferences_FollowThePreviousRow()
    {
        // Arrange
        var file = RawWorkbook.Create("<row><c><v>7</v></c></row><row><c><v>8</v></c></row>");

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(file, s_numbers, Token);

        // Assert
        Assert.Equal([7, 8], report.Items!.Select(item => item.Value));
    }

    [Theory]
    [MemberData(nameof(OutsideTheSelection))]
    public async Task InvalidCellValueOutsideTheSelection_IsNotRead(
        int startRow,
        int? endRow,
        bool visibleRowsOnly
    )
    {
        // Arrange
        const string invalid = """<c r="A{0}" t="d"><v>yesterday</v></c>""";
        var rows = (startRow, endRow, visibleRowsOnly) switch
        {
            (2, _, _) =>
                $"""<row r="1">{string.Format(CultureInfo.InvariantCulture, invalid, 1)}</row><row r="2"><c r="A2"><v>7</v></c></row>""",
            (_, _, true) =>
                $"""<row r="1"><c r="A1"><v>7</v></c></row><row r="2" hidden="1">{string.Format(CultureInfo.InvariantCulture, invalid, 2)}</row>""",
            _ =>
                $"""<row r="1"><c r="A1"><v>7</v></c></row><row r="2">{string.Format(CultureInfo.InvariantCulture, invalid, 2)}</row>""",
        };

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(RawWorkbook.Create(rows),
            s_numbers,
            new ExcelImportOptions
            {
                StartRow = startRow,
                EndRow = endRow,
                VisibleRowsOnly = visibleRowsOnly,
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(7,
            Assert.Single(report.Items!)
                .Value);
    }

    [Fact]
    public async Task HiddenFirstDataRowWithAnInvalidValue_IsNotRead()
    {
        // Arrange
        var file = RawWorkbook.Create(
            """<row r="1" hidden="1"><c r="A1" t="d"><v>yesterday</v></c></row><row r="2"><c r="A2"><v>7</v></c></row>""");

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(file,
            s_numbers,
            new ExcelImportOptions { VisibleRowsOnly = true },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(7,
            Assert.Single(report.Items!)
                .Value);
    }

    [Fact]
    public async Task PartialWorkbookAbortedByTheParser_KeepsTheCheckedItems()
    {
        // Arrange
        var file = RawWorkbook.Create(
            """<row r="1"><c r="A1"><v>7</v></c></row><row r="2"><c r="A2"><v>8</c></row>""");

        var sheets = ExcelImport.Create();
        var sheet = sheets.Add(TabularSheetName.Create("Sheet1"), s_numbers);

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, report.FatalError?.Code);
        Assert.False(report.IsComplete);
        Assert.Equal(1, report.RowsImported);
        Assert.Equal(7,
            Assert.Single(report.Get(sheet)
                    .Items!)
                .Value);
    }

    [Fact]
    public async Task CancellationBetweenRowsOfAPartialWorkbook_KeepsTheCheckedRowsLikeOneSheet()
    {
        // Arrange
        using var singleCancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var workbookCancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var sheets = ExcelImport.Create();
        var sheet = sheets.Add(TabularSheetName.Create("Sheet1"), CancelingMap(workbookCancellation));

        // Act
        // WHY: The equal result of one sheet and a workbook is the checked contract, so both runs form one action.
        var single = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(Rows(),
            CancelingMap(singleCancellation),
            new ExcelImportOptions { Strategy = ImportStrategy.Partial },
            singleCancellation.Token);

        var workbook = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(Rows(),
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            workbookCancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, ReportStatus.Canceled), (single.Status, workbook.Status));
        Assert.NotEmpty(single.Items!);
        Assert.Equal(single.Items!.Select(item => item.Value),
            workbook.Get(sheet)
                .Items!.Select(item => item.Value));

        Assert.Equal(single.RowsImported, workbook.RowsImported);

        return;

        static MemoryStream Rows() => RawWorkbook.Create(
            """<row r="1"><c r="A1"><v>7</v></c></row><row r="2"><c r="A2"><v>8</v></c></row>""");

        // WHY: The rule of the first row requests the cancellation, which the import observes before the next row.
        static TabularMap<Number> CancelingMap(
            CancellationTokenSource cancellation
        ) => TabularMap
            .For<Number>()
            .Column(x => x.Value,
                index: 0,
                configure: c => c.Validate(value =>
                    {
                        cancellation.Cancel();

                        return value > 0;
                    },
                    code: TabularErrorCode.Create("test.positive")))
            .Build();
    }

    public static TheoryData<string, string> PathEntries()
    {
        var data = new TheoryData<string, string>();
        foreach (var path in new[] { "bad\0.xlsx", "" })
        {
            foreach (var entry in new[] { "import", "workbook import", "export", "workbook export" })
            {
                data.Add(path, entry);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PathEntries))]
    public async Task InvalidOrEmptyPath_IsAConfigurationReportForOneAndSeveralSheets(
        string path,
        string entry
    )
    {
        // Arrange
        var format = new ExcelFormat(new ExcelFormatOptions());

        // Act
        var error = await FatalErrorAsync(format, entry, path);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, error?.Kind);
    }

    [Theory]
    [InlineData("import")]
    [InlineData("workbook import")]
    [InlineData("export")]
    public async Task UnrequestedCancellationOfInputOrTarget_IsAFailedReport(
        string entry
    )
    {
        // Arrange
        var format = new ExcelFormat(new ExcelFormatOptions());

        // Act
        var error = await FatalErrorAsync(format, entry, new CancelingStream());

        // Assert
        Assert.Equal(TabularErrorCodes.Io.Failed, error?.Code);
    }

    [Fact]
    public async Task CancellationDuringTheFirstEnumeration_WritesNoByteToTheStream()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = new MemoryStream();

        // Act
        var report =
            await new ExcelFormat(new ExcelFormatOptions()).ExportAsync(CancelingRows(cancellation),
                s_numbers,
                target,
                cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, StreamOutputState.NotWritten, 0L),
            (report.Status, report.StreamOutput, target.Length));

        return;

        static IEnumerable<Number> CancelingRows(
            CancellationTokenSource source
        )
        {
            source.Cancel();
            source.Token.ThrowIfCancellationRequested();

            yield return new Number(1);
        }
    }

    [Fact]
    public async Task BrokenXmlAfterTheEndRow_IsStillAParserError()
    {
        // Arrange
        var file = RawWorkbook.Create(
            """<row r="1"><c r="A1"><v>7</v></c></row><row r="2"><c r="A2"><v>8</v></c></row><row r="3"><c r="A3"><v>9</c></row>""");

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(file,
            s_numbers,
            new ExcelImportOptions { EndRow = 1 },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, report.FatalError?.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeaderBeyondTheCellLimits_IsALimitErrorBeforeWriting(
        bool lineBreaks
    )
    {
        // Arrange
        var header = lineBreaks
            ? string.Concat(Enumerable.Repeat("x\n", 254))
            : new string('x', 32_768);

        var map = TabularMap
            .For<Text>()
            .Column(x => x.Value, index: 0, configure: c => c.Header(header))
            .Build();

        // Act
        var (report, file) = await new CheckedExcelFormat(new ExcelFormat(new ExcelFormatOptions())).ExportAsync(
            Array.Empty<Text>(),
            map,
            new ExcelExportOptions { WriteHeader = true },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(TabularErrorCodes.Schema.LimitExceeded, (report.FatalError ?? report.Errors[0]).Code);
        Assert.Null(file);
    }

    [Fact]
    public async Task FieldLimit_CountsTheDecodedValue()
    {
        // Arrange
        var file = RawWorkbook.Create("""<row r="1"><c r="A1" t="inlineStr"><is><t>_x0041_</t></is></c></row>""");

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions { MaxFieldCharacters = 1 }).ImportAsync(file,
            s_texts,
            Token);

        // Assert
        Assert.Equal("A",
            Assert.Single(report.Items!)
                .Value);
    }

    [Theory]
    [InlineData("", "tabular.resource.limitExceeded")]
    [InlineData("&bogus;", "tabular.parsing.invalidSyntax")]
    public async Task OverlongSharedStringInTheRange_IsALimitErrorAndDamagedXmlStaysASyntaxError(
        string tail,
        string expected
    )
    {
        // Arrange
        // WHY: The text beyond the limit is not kept, but the part is still parsed to its end, so damaged XML behind
        // the limit is still found.
        var file = RawWorkbook.Create("""<row r="1"><c r="A1" t="s"><v>0</v></c></row>""",
            $"""<si><t>{new string('a', 20000)}{tail}</t></si>""");

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions { MaxFieldCharacters = 100 }).ImportAsync(file,
            s_texts,
            Token);

        // Assert
        Assert.Equal(expected, (report.FatalError ?? Assert.Single(report.Errors)).Code.Value);
    }

    [Theory]
    [InlineData(1, true, 50)]
    [InlineData(null, false, 50)]
    [InlineData(1, true, 80)]
    [InlineData(null, false, 80)]
    public async Task FieldLimit_AppliesToSharedStringsOnlyInsideTheReadRange(
        int? endRow,
        bool succeeds,
        int length
    )
    {
        // Arrange
        // WHY: The long shared string is used only in row 2; with EndRow 1 it is no field value. 80 characters are
        // beyond seven times the limit, which the loader does not keep.
        var file = RawWorkbook.Create("""
            <row r="1"><c r="A1" t="s"><v>0</v></c></row><row r="2"><c r="A2" t="s"><v>1</v></c></row>
            """,
            $"""<si><t>short</t></si><si><t>{new string('a', length)}</t></si>""");

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions { MaxFieldCharacters = 10 }).ImportAsync(file,
            s_texts,
            new ExcelImportOptions { EndRow = endRow },
            Token);

        // Assert
        Assert.Equal(succeeds, report.Status == ReportStatus.Succeeded);
        Assert.Equal(succeeds
                ? null
                : TabularErrorCodes.Resource.LimitExceeded,
            report.FatalError?.Code);
    }

    [Fact]
    public void Color_WithATrailingLineFeed_IsRejected()
    {
        // Act
        var exception = Record.Exception(() => ExcelColor.Create("#FFFFFF\n"));

        // Assert
        Assert.IsType<ArgumentException>(exception, exactMatch: false);
    }

    [Theory]
    [InlineData("Users\n")]
    [InlineData("A1\n")]
    public void TableName_WithATrailingLineFeed_IsRejected(
        string name
    )
    {
        // Act
        var exception = Record.Exception(() => ExcelTableName.Create(name));

        // Assert
        Assert.IsType<ArgumentException>(exception, exactMatch: false);
    }

    [Fact]
    public async Task SheetNameWithAControlCharacter_IsWrittenWithItsFilter()
    {
        // Arrange
        var name = TabularSheetName.Create("A\u0001B");
        var format = new CheckedExcelFormat(new ExcelFormat(new ExcelFormatOptions()));

        // Act
        var (report, file) = await format.ExportAsync(new[] { new Text("x") },
            s_texts,
            new ExcelExportOptions
            {
                SheetName = name,
                WriteHeader = true,
            },
            Token);

        // Assert
        Assert.True(report.Status == ReportStatus.Succeeded,
            string.Join(" | ", report.Errors.Select(error => error.Message)));

        Assert.Empty(WorkbookProblems(file!));
    }

    /// <summary>Runs one entry point with a path or a stream and returns its fatal error.</summary>
    private static async Task<TabularError?> FatalErrorAsync(
        ExcelFormat format,
        string entry,
        object input
    )
    {
        var importSheets = ExcelImport.Create();
        importSheets.Add(TabularSheetName.Create("Sheet1"), s_numbers);
        var exportSheets = ExcelExport.Create();
        exportSheets.Add(TabularSheetName.Create("Sheet1"), new[] { new Number(1) }, s_numbers);

        return (entry, input) switch
        {
            ("import", string path) => (await format.ImportAsync(path, s_numbers, Token)).FatalError,
            ("import", Stream stream) => (await format.ImportAsync(stream, s_numbers, Token)).FatalError,
            ("workbook import", string path) =>
                (await format.ImportAsync(path, importSheets.Build(), Token)).FatalError,
            ("workbook import", Stream stream) => (await format.ImportAsync(stream, importSheets.Build(), Token))
                .FatalError,
            ("export", string path) => (await format.ExportAsync(new[] { new Number(1) }, s_numbers, path, Token))
                .FatalError,
            ("export", Stream stream) => (await format.ExportAsync(new[] { new Number(1) }, s_numbers, stream, Token))
                .FatalError,
            ("workbook export", string path) => (await format.ExportAsync(exportSheets.Build(), path, Token))
                .FatalError,
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "Unknown entry point."),
        };
    }

    /// <summary>A stream whose reads and writes report a cancellation that the call did not request.</summary>
    private sealed class CancelingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => throw new OperationCanceledException();

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => throw new OperationCanceledException();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => throw new OperationCanceledException();

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => throw new OperationCanceledException();
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Number(int Value);

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Text(string Value);
}
