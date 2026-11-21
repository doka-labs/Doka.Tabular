namespace Doka.Tabular.Csv.Tests;

/// <summary>RFC 4180 structure, field content, delimiters, and record boundaries.</summary>
public sealed class CsvSyntaxTests
{
    private static readonly TabularMap<One> s_one = TabularMap
        .For<One>()
        .Column(x => x.Value, index: 0)
        .Build();

    private static readonly TabularMap<Two> s_two = TabularMap
        .For<Two>()
        .Column(x => x.First, index: 0)
        .Column(x => x.Second, index: 1)
        .Build();

    private static readonly TabularMap<Three> s_three = TabularMap
        .For<Three>()
        .Column(x => x.A, index: 0)
        .Column(x => x.B, index: 1)
        .Column(x => x.C, index: 2)
        .Build();

    private static readonly CsvFormat s_format = new(new CsvFormatOptions());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Müller")]
    [InlineData("李")]
    [InlineData("Ada\tLovelace")]
    [InlineData("A\0B")]
    public async Task FieldContent_RoundTripsUnchangedAndUnquoted(
        string value
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) = await s_format.ExportAsync(new[] { new One(value) }, s_one, Token);
        await using var stream = file!;
        var text = TestInput.Utf8Text(stream);
        stream.Position = 0;
        var import = await s_format.ImportAsync(stream, s_one, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        Assert.Equal(value, text);
        Assert.Equal(value,
            Assert.Single(import.Items!)
                .Value);
    }

    [Fact]
    public async Task Writer_QuotesDelimiterLineBreaksAndQuotesAndKeepsSpaces()
    {
        // Arrange
        var rows = new[] { new Two("a,b", "x\"y"), new Two("line\r\nbreak", " spaced ") };

        // Act
        var (_, file) = await s_format.ExportAsync(rows, s_two, Token);

        // Assert
        await using var stream = file!;
        Assert.Equal("\"a,b\",\"x\"\"y\"\r\n\"line\r\nbreak\", spaced ", TestInput.Utf8Text(stream));
    }

    [Fact]
    public async Task Semicolon_UsesTheSameQuotingRules()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await s_format.ExportAsync(new[] { new Two("a;b", "c,d") },
            s_two,
            new CsvExportOptions { Delimiter = ';' },
            Token);

        await using var stream = file!;
        var text = TestInput.Utf8Text(stream);
        stream.Position = 0;
        var import = await s_format.ImportAsync(stream, s_two, new CsvImportOptions { Delimiter = ';' }, Token);

        // Assert
        Assert.Equal("\"a;b\";c,d", text);
        Assert.Equal(new Two("a;b", "c,d"), Assert.Single(import.Items!));
    }

    [Fact]
    public async Task SingleColumnExport_EmptyValueIsWrittenAsQuotedEmptyField()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await s_format.ExportAsync(new[] { new One("Ada"), new One(""), new One("Bob") }, s_one, Token);
        await using var stream = file!;
        var text = TestInput.Utf8Text(stream);
        stream.Position = 0;
        var import = await s_format.ImportAsync(stream, s_one, Token);

        // Assert
        Assert.Equal("Ada\r\n\"\"\r\nBob", text);
        Assert.Equal(3, import.Items!.Count);
    }

    [Theory]
    [InlineData("Ada\nBob", false, 2)]
    [InlineData("Ada\r\nBob", false, 2)]
    [InlineData("Ada\r\nBob", true, 2)]
    [InlineData("\"Ada\nLovelace\"\r\nBob", true, 2)]
    public async Task RecordBoundaries_AcceptLfByDefaultAndCrLfWhenRequired(
        string text,
        bool requireCrLf,
        int records
    )
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(text),
            s_one,
            new CsvImportOptions { RequireCrLf = requireCrLf },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(records, report.Items!.Count);
    }

    [Fact]
    public async Task RecordBoundaries_SingleLfWithCrLfRequired_IsAParserError()
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8("Ada\nBob"),
            s_one,
            new CsvImportOptions { RequireCrLf = true },
            Token);

        // Assert
        var error = report.FatalError ?? Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, error.Code);
        Assert.Equal(1, error.Location.RowNumber);
    }

    [Fact]
    public async Task QuotedLineBreak_StaysFieldContent()
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8("\"Ada\nLovelace\"\r\nBob"), s_one, Token);

        // Assert
        Assert.Equal("Ada\nLovelace", report.Items![0].Value);
    }

    [Fact]
    public async Task UnclosedQuote_WithLineBreaksAllowed_AbortsAndKeepsNothingFromThePartialFile()
    {
        // Arrange
        var lines = Enumerable
            .Range(1, 5000)
            .Select(number => number == 100
                ? "100;\"carol@example.org;Carol"
                : $"{number};user{number};User")
            .ToArray();

        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(string.Join("\n", lines)),
            s_three,
            new CsvImportOptions
            {
                Delimiter = ';',
                Strategy = ImportStrategy.Partial,
                SourceKey = s_three.Key(x => x.A),
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.False(report.IsComplete);
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, report.FatalError?.Code);
        Assert.Equal(100, report.FatalError!.Location.RowNumber);
        Assert.Empty(report.Items!);
    }

    [Fact]
    public async Task UnclosedQuote_WithoutLineBreaksInFields_RejectsOnlyThatRecord()
    {
        // Arrange
        var lines = Enumerable
            .Range(1, 5000)
            .Select(number => number == 100
                ? "100;\"carol@example.org;Carol"
                : $"{number};user{number};User")
            .ToArray();

        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(string.Join("\n", lines)),
            s_three,
            new CsvImportOptions
            {
                Delimiter = ';',
                Strategy = ImportStrategy.Partial,
                AllowLineBreaksInFields = false,
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal(4999, report.Items!.Count);
        Assert.Equal(100,
            Assert.Single(report.Errors)
                .Location.RowNumber);
    }

    [Fact]
    public async Task QuotedLineBreak_WithoutLineBreaksInFields_IsARowErrorAndTheNextLineIsARecord()
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8("\"Ada\nLovelace\";1"),
            s_two,
            new CsvImportOptions
            {
                Delimiter = ';',
                Strategy = ImportStrategy.Partial,
                AllowLineBreaksInFields = false,
            },
            Token);

        // Assert
        // WHY: Line 2 is read by the normal rules; its bare quote inside an unquoted field is a syntax error of its own.
        Assert.Equal([1L, 2L], report.Errors.Select(error => error.Location.RowNumber!.Value));
        Assert.All(report.Errors, error => Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, error.Code));
        Assert.Empty(report.Items!);
    }

    [Theory]
    [InlineData(false, new[] { "Ada", "Bob" })]
    [InlineData(true, new[] { "Ada", "", "Bob" })]
    public async Task EmptyLines_AreSkippedByDefaultOrReadAsEmptyRecords(
        bool asRecords,
        string?[] expected
    )
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8("Ada\n\nBob"),
            s_one,
            new CsvImportOptions { EmptyLinesAsRecords = asRecords },
            Token);

        // Assert
        Assert.Equal(expected, report.Items!.Select(item => item.Value));
    }

    [Fact]
    public async Task EmptyLines_KeepPhysicalRowNumbers()
    {
        // Arrange
        var map = TabularMap
            .For<One>()
            .Column(x => x.Value,
                index: 0,
                configure: c => c.Validate(value => value != "Bob", code: TabularErrorCode.Create("test.bob")))
            .Build();

        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8("Ada\n\nBob"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(3,
            Assert.Single(report.Errors)
                .Location.RowNumber);
    }

    [Theory]
    [InlineData("\"a\nb\",bad", 2L, 1L)]
    [InlineData("\"a\r\nb\nc\",bad", 3L, 1L)]
    [InlineData("\"a\nb\",1\ny,bad", 3L, 2L)]
    public async Task FieldError_NamesThePhysicalLineWhereTheFieldStarts(
        string csv,
        long rowNumber,
        long recordNumber
    )
    {
        // Arrange
        var map = TabularMap
            .For<Numbered>()
            .Column(x => x.Text, index: 0)
            .Column(x => x.Number, index: 1)
            .Build();

        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(csv),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        var location = Assert.Single(report.Errors)
            .Location;

        Assert.Equal((rowNumber, recordNumber, 1),
            (location.RowNumber!.Value, location.RecordNumber!.Value,
                location.ColumnIndex!.Value));
    }

    [Theory]
    [InlineData("\"a\nb\",x\"y")]
    [InlineData("\"a\nb\",\"x\"q")]
    public async Task SyntaxErrorOfALaterField_NamesThePhysicalLineWhereTheFieldStarts(
        string csv
    )
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(csv),
            s_two,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal((TabularErrorCodes.Parsing.InvalidSyntax, 2L, 1),
            (error.Code, error.Location.RowNumber!.Value, error.Location.ColumnIndex!.Value));
    }

    [Theory]
    [InlineData(false, 5L)]
    [InlineData(true, 6L)]
    public async Task ExportError_NamesThePhysicalLineBehindMultilineValues(
        bool multilineHeader,
        long rowNumber
    )
    {
        // Arrange
        var map = TabularMap
            .For<Two>()
            .Column(x => x.First,
                index: 0,
                configure: c => c.Header(multilineHeader
                    ? "A\nB"
                    : "A"))
            .Column(x => x.Second, index: 1, configure: c => c.Header("C"))
            .Build();

        // Act
        var report = await s_format.ExportAsync(new[] { new Two("a\nb", "1"), new Two("c\r\nd", "\u674E") },
            map,
            new MemoryStream(),
            new CsvExportOptions
            {
                Encoding = Encoding.Latin1,
                WriteHeader = true,
            },
            Token);

        // Assert
        var location = (report.FatalError ?? Assert.Single(report.Errors)).Location;
        Assert.Equal((rowNumber, 2L, 1),
            (location.RowNumber!.Value, location.RecordNumber!.Value,
                location.ColumnIndex!.Value));
    }

    [Fact]
    public async Task ExportRuleError_NamesThePhysicalLineWhereTheFieldStarts()
    {
        // Arrange
        var map = TabularMap
            .For<Two>()
            .Column(x => x.First, index: 0)
            .Column(x => x.Second,
                index: 1,
                configure: c => c.Validate(value => value != "bad", alsoOnExport: true))
            .Build();

        // Act
        var report = await s_format.ExportAsync(new[] { new Two("a\nb", "bad") }, map, new MemoryStream(), Token);

        // Assert
        Assert.Equal((2L, 1L),
            (report.FatalError!.Location.RowNumber!.Value, report.FatalError.Location.RecordNumber!.Value));
    }

    [Fact]
    public async Task FormattingError_NamesThePhysicalLineWhereTheFieldStarts()
    {
        // Arrange
        var map = TabularMap
            .For<Two>()
            .Column(x => x.First, index: 0)
            .Column(x => x.Second,
                index: 1,
                configure: c => c.Convert<string, string?>(text => text, _ => throw new FormatException()))
            .Build();

        // Act
        var report = await s_format.ExportAsync(new[] { new Two("a\nb", "1") }, map, new MemoryStream(), Token);

        // Assert
        Assert.Equal(2L, report.FatalError!.Location.RowNumber);
    }

    [Theory]
    [InlineData("\"\"", "")]
    [InlineData("   ", "   ")]
    [InlineData("\"a\n\nb\"", "a\n\nb")]
    public async Task EmptyFieldsAndBlankText_AreFieldsNotEmptyLines(
        string csv,
        string expected
    )
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(csv), s_one, Token);

        // Assert
        Assert.Equal(expected,
            Assert.Single(report.Items!)
                .Value);
    }

    [Fact]
    public async Task OnlyDelimiters_AreOneRecordOfEmptyFields()
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(";;"),
            s_three,
            new CsvImportOptions { Delimiter = ';' },
            Token);

        // Assert
        Assert.Equal(new Three("", "", ""), Assert.Single(report.Items!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColumnSelection_ReadsOnlyMappedPositionsAndKeepsQuotedDelimitersInUnmappedColumns(
        bool withHeader
    )
    {
        // Arrange
        var map = TabularMap
            .For<Three>()
            .Column(x => x.A, index: 0)
            .Column(x => x.B, index: 11)
            .Column(x => x.C, index: 29)
            .Build();

        var fields = string.Join(",",
            Enumerable
                .Range(0, 30)
                .Select(index => index == 1
                    ? "\"Nord, West\""
                    : $"v{index}"));

        var header = string.Join(",",
            Enumerable
                .Range(0, 30)
                .Select(index => $"h{index}"));

        var options = new CsvImportOptions
        {
            Binding = withHeader
                ? ColumnBinding.ByIndexWithHeader
                : ColumnBinding.ByIndex,
        };

        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(withHeader
                ? header + "\n" + fields
                : fields),
            map,
            options,
            Token);

        // Assert
        Assert.Equal(new Three("v0", "v11", "v29"), Assert.Single(report.Items!));
    }

    [Theory]
    [InlineData("A-01;42;")]
    [InlineData("A-01;42;;Zusatz")]
    public async Task MinimumWidth_ReachingTheHighestMappedIndex_IsARecord(
        string line
    )
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8(line),
            s_three,
            new CsvImportOptions
            {
                Delimiter = ';',
                Strategy = ImportStrategy.Partial,
            },
            Token);

        // Assert
        Assert.Equal("",
            Assert.Single(report.Items!)
                .C);
    }

    [Fact]
    public async Task MinimumWidth_BelowTheHighestMappedIndex_IsAMissingColumn()
    {
        // Act
        var report = await s_format.ImportAsync(TestInput.Utf8("A-01;42"),
            s_three,
            new CsvImportOptions
            {
                Delimiter = ';',
                Strategy = ImportStrategy.Partial,
            },
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Schema.MissingColumn, error.Code);
        Assert.Equal(2, error.Location.ColumnIndex);
    }

    private sealed record One(string? Value);

    private sealed record Two(
        string? First,
        string? Second
    );

    // WHY: Only the import creates instances, through the constructor the map binds by reflection.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed record Numbered(
        string? Text,
        int Number
    );

    private sealed record Three(
        string? A,
        string? B,
        string? C
    );
}
