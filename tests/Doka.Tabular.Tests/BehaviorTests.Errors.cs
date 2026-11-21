using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class BehaviorTests
{
    private static readonly string s_abortedLines = string.Join("\n",
        Enumerable
            .Range(1, 99)
            .Select(number => $"u{number}@example.org,{number},Berlin,10115")
            .Append("\"open,1,Berlin,10115"));

    private static readonly CsvImportOptions s_partial = new() { Strategy = ImportStrategy.Partial };

    private static readonly Employee s_invalidNumber = new("a@example.org", 0, "Berlin", "10115");

    private static readonly CsvExportOptions s_emailAndPostalCode =
        new() { ColumnKeys = [ColumnKey.Create("email"), ColumnKey.Create("postalCode")] };

    private static readonly string s_countedLines = string.Join("\n",
        Enumerable
            .Range(1, 100)
            .Select(number => number switch
            {
                10 => "a,b,,1",
                20 => "a,c,,1",
                30 => "a,1,,1",
                _ => $"u{number}@example.org,{number},Berlin,10115",
            }));

    private static readonly TabularMap<Employee> s_countedMap = TabularMap
        .For<Employee>()
        .Column(x => x.Email,
            index: 0,
            configure: c => c.Validate(email => email.Contains('@', StringComparison.Ordinal)))
        .Column(x => x.NumericId, index: 1)
        .Column(x => x.City, index: 2)
        .Column(x => x.PostalCode, index: 3)
        .Build();

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task ErrorLocation_NamesRowRecordColumnAndPath(
        TabularType type
    )
    {
        // Arrange
        var file = Create(type,
            ["E-Mail", "Nummer", "Ort", "PLZ"],
            ["a@example.org", "abc", "Berlin", "10115"],
            ["b@example.org", "99999999999", "Berlin", "10115"]);

        // Act
        var report = await Import.ImportAsync(file,
            type,
            s_employees,
            new TabularImportOptions { Binding = ColumnBinding.ByIndexWithHeader },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.True(report.IsComplete);
        Assert.Empty(report.Items!);
        var first = report.Errors[0];
        Assert.Equal((2L, 1L, 1, "NumericId"),
            (first.Location.RowNumber!.Value, first.Location.RecordNumber!.Value, first.Location.ColumnIndex!.Value,
                first.Location.PropertyPath!.Value));

        Assert.Equal(type == TabularType.Excel
                ? "Sheet1"
                : null,
            first.Location.SheetName?.Value);

        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange, report.Errors[1].Code);
    }

    [Fact]
    public async Task ParserAbort_WithoutKey_KeepsTheCheckedRows()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(s_abortedLines), s_employees, s_partial, Token);

        // Assert
        Assert.Equal((99, false), (report.Items!.Count, report.IsComplete));
        Assert.Equal(TabularErrorCodes.Parsing.InvalidSyntax, report.FatalError?.Code);
    }

    [Fact]
    public async Task ParserAbort_Strict_KeepsNoRow()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(s_abortedLines), s_employees, Token);

        // Assert
        Assert.Empty(report.Items!);
    }

    [Fact]
    public async Task ParserAbort_WithKey_KeepsNoRow()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(s_abortedLines),
            s_employees,
            s_partial with { SourceKey = s_employees.Key(x => x.NumericId) },
            Token);

        // Assert
        Assert.Empty(report.Items!);
    }

    [Fact]
    public async Task FieldRule_UsesItsCode()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("E-Mail,Nummer,Ort,PLZ\na@example.org,0,Berlin,10115"),
            s_employees,
            new CsvImportOptions { Binding = ColumnBinding.ByIndexWithHeader },
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(("personnelNumber.mustBePositive", 2L, 1, "NumericId"),
            (error.Code.Value, error.Location.RowNumber!.Value, error.Location.ColumnIndex!.Value,
                error.Location.PropertyPath!.Value));
    }

    [Fact]
    public async Task FieldRuleWithoutCode_UsesTheGeneralValidationCode()
    {
        // Arrange
        var generic = TabularMap
            .For<Pair>()
            .Column(x => x.Email, index: 0)
            .Column(x => x.NumericId, index: 1, configure: c => c.Validate(number => number >= 1))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("E-Mail,Nummer\na@example.org,0"),
            generic,
            new CsvImportOptions { Binding = ColumnBinding.ByIndexWithHeader },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Validation.Failed,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task ThrowingRule_EndsTheRunTechnicallyAtItsRow()
    {
        // Arrange
        var map = TabularMap
            .For<Pair>()
            .Column(x => x.Email, index: 0)
            .Column(x => x.NumericId,
                index: 1,
                configure: c => c.Validate(number => number == 3
                    ? throw new InvalidOperationException()
                    : number != 2))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("a,1\nb,2\nc,3\nd,4"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Validation.Failed, report.Errors[0].Code);
        Assert.Equal((TabularErrorCodes.Callback.Failed, 3L),
            (report.FatalError!.Code, report.FatalError.Location.RowNumber!.Value));

        Assert.False(report.IsComplete);
        Assert.Equal(3, report.RowsRead);
    }

    [Fact]
    public async Task ImportOnlyRule_DoesNotRunOnExport()
    {
        // Arrange
        var importOnly = TabularMap
            .For<Employee>()
            .Column(x => x.Email, index: 0)
            .Column(x => x.NumericId, index: 1, configure: c => c.Validate(number => number >= 1))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(new[] { s_invalidNumber }, importOnly, new MemoryStream(), Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task ExportRule_RunsOnExport()
    {
        // Act
        var report = await CsvInstance.ExportAsync(new[] { s_invalidNumber }, s_employees, new MemoryStream(), Token);

        // Assert
        Assert.Equal("personnelNumber.mustBePositive", (report.FatalError ?? report.Errors[0]).Code.Value);
    }

    [Fact]
    public async Task ExportRule_OfAnUnselectedField_DoesNotRun()
    {
        // Act
        var report = await CsvInstance.ExportAsync(new[] { s_invalidNumber },
            s_employees,
            new MemoryStream(),
            s_emailAndPostalCode,
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task ExportRule_OfASelectedField_Runs()
    {
        // Arrange
        var row = s_invalidNumber with
        {
            NumericId = 1,
            PostalCode = "1011",
        };

        // Act
        var report =
            await CsvInstance.ExportAsync(new[] { row }, s_employees, new MemoryStream(), s_emailAndPostalCode, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Validation.Failed, (report.FatalError ?? report.Errors[0]).Code);
    }

    [Fact]
    public async Task CrossFieldExportRule_OfSelectedFields_Runs()
    {
        // Arrange
        var row = s_invalidNumber with
        {
            NumericId = 1,
            PostalCode = "",
        };

        // Act
        var report = await CsvInstance.ExportAsync(new[] { row },
            s_employees,
            new MemoryStream(),
            new CsvExportOptions { ColumnKeys = [ColumnKey.Create("postalCode"), ColumnKey.Create("city")] },
            Token);

        // Assert
        Assert.Contains((report.FatalError ?? report.Errors[0]).Code.Value,
            new[] { "city.needsPostalCode", TabularErrorCodes.Validation.Failed.Value });
    }

    [Fact]
    public async Task SourceValue_AppearsOnlyForReleasedColumns()
    {
        // Arrange
        var map = TabularMap
            .For<Pair>()
            .Column(x => x.Email,
                index: 0,
                configure: c => c.Validate(email => email.Contains('@', StringComparison.Ordinal)))
            .Column(x => x.NumericId, index: 1, configure: c => c.IncludeSourceValue())
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("invalid,12O4"), map, Token);

        // Assert
        Assert.Equal("12O4",
            report.Errors.Single(error => error.Location.ColumnIndex == 1)
                .SourceValue);

        Assert.Null(report.Errors.Single(error => error.Location.ColumnIndex == 0)
            .SourceValue);

        Assert.All(report.Errors, error => Assert.DoesNotContain("12O4", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ErrorLimit_KeepsCountingAndImportsTheValidRow()
    {
        // Arrange
        var map = TabularMap
            .For<Triple>()
            .Column(x => x.Email,
                index: 0,
                configure: c => c.Validate(email => email.Contains('@', StringComparison.Ordinal)))
            .Column(x => x.NumericId, index: 1)
            .Column(x => x.City, index: 2, configure: c => c.Required())
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("x,a,\ny,b,Bonn\nz,1,\nd@example.org,4,Bonn"),
            map,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                MaxErrorDetails = 2,
            },
            Token);

        // Assert
        Assert.Equal((7L, 2, 5L), (report.TotalErrorCount, report.Errors.Count, report.OmittedErrorCount));
        Assert.Equal(["d@example.org"], report.Items!.Select(item => item.Email));
    }

    [Fact]
    public async Task SourceKey_RejectsEveryOccurrenceWithRelatedLocations()
    {
        // Arrange
        var lines = string.Join("\n",
            Enumerable
                .Range(1, 9)
                .Select(number => $"u{number}@example.org,{(number is 2 or 9 ? 42 : number)},Berlin,10115"));

        var options = new CsvImportOptions
        {
            Strategy = ImportStrategy.Partial,
            SourceKey = s_employees.Key(x => x.NumericId),
        };

        // Act
        var report =
            await CsvInstance.ImportAsync(new ForwardOnlyStream(TestInput.Utf8(lines)), s_employees, options, Token);

        // Assert
        Assert.Equal([2L, 9L], report.Errors.Select(error => error.Location.RecordNumber!.Value));
        Assert.Equal(9L,
            Assert.Single(report.Errors[0].RelatedLocations)
                .RecordNumber);

        Assert.Equal(7, report.Items!.Count);
    }

    [Fact]
    public async Task PartialReport_CountsRowsAndErrors()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(s_countedLines), s_countedMap, s_partial, Token);

        // Assert
        Assert.Equal((ReportStatus.CompletedWithErrors, 97L, 3L, 5L),
            (report.Status, report.RowsImported, report.RowsRejected, report.TotalErrorCount));

        Assert.Null(report.FatalError);
    }

    [Fact]
    public async Task PartialReportWithADetailLimit_CountsTheOmittedErrors()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(s_countedLines),
            s_countedMap,
            s_partial with { MaxErrorDetails = 2 },
            Token);

        // Assert
        Assert.Equal((2, 3L), (report.Errors.Count, report.OmittedErrorCount));
    }

    [Fact]
    public async Task StrictReport_CountsTheRejectedRowsAndKeepsNone()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(s_countedLines), s_countedMap, Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, 0L, 3L, true),
            (report.Status, report.RowsImported, report.RowsRejected, report.IsComplete));

        Assert.Empty(report.Items!);
    }

    [Fact]
    public async Task Cancellation_AfterFiftyRows_IsCanceledWithoutErrors()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var map = TabularMap
            .For<Pair>()
            .Column(x => x.Email, index: 0)
            .Column(x => x.NumericId,
                index: 1,
                configure: c => c.Validate(number =>
                {
                    if (number == 50)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        // WHY: The callback runs during the awaited import, before the captured object is disposed.
                        cancellation.Cancel();
                    }

                    return true;
                }))
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(string.Join("\n",
                Enumerable
                    .Range(1, 1000)
                    .Select(number => $"u,{number}"))),
            map,
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, false, 0L), (report.Status, report.IsComplete, report.TotalErrorCount));
        Assert.Null(report.FatalError);
    }

    [Fact]
    public void ErrorCodes_ListAllThirtyTwoStableValues()
    {
        // Act
        var codes = TabularErrorCodes.All;

        // Assert
        Assert.Equal(32, codes.Count);
        Assert.Equal(codes.Count,
            codes
                .Select(code => code.Value)
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.All(codes, code => Assert.StartsWith("tabular.", code.Value, StringComparison.Ordinal));
        Assert.Contains(codes, code => code.Value == "tabular.persistence.contextRestoreFailed");
    }
}
