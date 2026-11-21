using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>Aborts, cleanup, rule selection, report immutability, and cancellation across import and export.</summary>
public sealed partial class PipelineTests
{
    private static readonly TabularErrorCode s_rule = TabularErrorCode.Create("probe.rule");

    private static readonly TabularMap<Numbered> s_numbers = TabularMap
        .For<Numbered>()
        .Column(x => x.Number, index: 0)
        .Build();

    [Fact]
    public async Task TechnicalReadAbort_KeepsTheCheckedRowsAndCounters()
    {
        // Arrange
        // WHY: The reader buffers ahead; enough rows make the input fail after rows have been checked.
        var text = "bad\n" + string.Join('\n', Enumerable.Range(1, 50_000)) + "\n";
        var input = new FailingAfterStream(Encoding.UTF8.GetBytes(text));

        // Act
        var report = await CsvInstance.ImportAsync(input,
            s_numbers,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Io.Failed, report.FatalError?.Code);
        Assert.False(report.IsComplete);
        Assert.True(report.RowsRead > 1);
        Assert.Equal(1, report.RowsRejected);
        Assert.Equal(report.RowsRead - 1, report.RowsImported);
        Assert.Equal(report.RowsImported, report.Items!.Count);
    }

    [Fact]
    public async Task SourceThatThrowsOnRelease_StillGivesTheReportWithTheFirstError()
    {
        // Arrange
        var map = TabularMap
            .For<Line>()
            .Column(x => x.Text, index: 0, configure: c => c.Validate(_ => false, s_rule, alsoOnExport: true))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(new ThrowsOnRelease(), map, new MemoryStream(), Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(s_rule, report.FatalError?.Code);
    }

    [Fact]
    public async Task ExportRuleOfANestedMap_Runs()
    {
        // Arrange
        var address = TabularMap
            .For<Address>()
            .Column(x => x.City, index: 0, key: ColumnKey.Create("city"))
            .Column(x => x.PostalCode, index: 1, key: ColumnKey.Create("postalCode"))
            .Validate(x => x.City, x => x.PostalCode, (_, _) => false, s_rule, alsoOnExport: true)
            .Build();

        var map = TabularMap
            .For<Holder>()
            .Column(x => x.Name, index: 0)
            .Nested(x => x.Address,
                address,
                e => e
                    .StartIndex(1)
                    .KeyPrefix(ColumnKey.Create("address")))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Holder("Ada", new Address("Bonn", "53111")) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        var error = report.FatalError ?? Assert.Single(report.Errors);
        Assert.Equal(s_rule, error.Code);
        Assert.Equal("Address.City", error.Location.PropertyPath?.Value);
        Assert.Equal(ColumnKey.Create("address.city"), error.Location.ColumnKey);
    }

    [Fact]
    public async Task OverlappingImportRules_AreAllReported()
    {
        // Arrange
        var first = TabularErrorCode.Create("probe.first");
        var second = TabularErrorCode.Create("probe.second");
        var map = TabularMap
            .For<Pair>()
            .Column(x => x.A, index: 0)
            .Column(x => x.B, index: 1)
            .Validate(x => x.A, x => x.B, (_, _) => false, first)
            .Validate(x => x.B, x => x.A, (_, _) => false, second)
            .Build();

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("a,b"),
            map,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal([first, second], report.Errors.Select(error => error.Code));
        Assert.Equal(1, report.RowsRejected);
    }

    [Fact]
    public async Task TranslationsChangedDuringAnExport_ApplyFromTheNextCall()
    {
        // Arrange
        var translations = new Dictionary<string, string>
        {
            ["yes"] = "ja",
            ["no"] = "nein",
        };

        var options = new CsvExportOptions { Translations = translations };
        var map = TabularMap
            .For<Flag>()
            .Column(x => x.Value, index: 0, configure: c => c.TranslationKeys("yes", "no"))
            .Build();

        var changing = ChangingAfterFirst([new Flag(true), new Flag(true)], () => translations["yes"] = "oui");

        // Act
        // WHY: The change takes effect only between calls, so the changing call and the next one form one action.
        var first = await CsvInstance.ExportAsync(changing, map, options, Token);
        var second = await CsvInstance.ExportAsync(new[] { new Flag(true) }, map, options, Token);

        // Assert
        Assert.Equal("ja\nja", await TextOf(first));
        Assert.Equal("oui", await TextOf(second));
    }

    [Fact]
    public async Task ExportRuleOfAnUnselectedRole_DoesNotRun()
    {
        // Arrange
        var pair = TabularMap
            .For<Pair>()
            .Column(x => x.A, index: 0, key: ColumnKey.Create("a"))
            .Column(x => x.B, index: 1, key: ColumnKey.Create("b"))
            .Validate(x => x.A, x => x.B, (a, _) => a != "invalid", s_rule, alsoOnExport: true)
            .Build();

        var rows = RowMap.Create();
        var left = rows.Add(pair, e => e.KeyPrefix(ColumnKey.Create("left")));
        var right = rows.Add(pair,
            e => e
                .StartIndex(2)
                .KeyPrefix(ColumnKey.Create("right")));

        var export = rows
            .Build()
            .From<PairRow>()
            .Bind(left, x => x.Left)
            .Bind(right, x => x.Right)
            .Build();

        // Act
        var report = await Export.ExportAsync(new[] { new PairRow(new Pair("ok", "x"), new Pair("invalid", "y")) },
            export,
            TabularType.Csv,
            new TabularExportOptions { ColumnKeys = [ColumnKey.Create("left.a"), ColumnKey.Create("left.b")] },
            Token);

        // Assert
        Assert.True(report.Report.Status == ReportStatus.Succeeded,
            string.Join(" | ", report.Report.Errors.Select(error => error.Code.Value)));
    }

    [Fact]
    public async Task ExportRuleOfAComposedPart_Runs()
    {
        // Arrange
        var map = TabularMap
            .For<Priced>()
            .Compose(x => x.Amount,
                c => c.Field<long>(index: 0,
                    key: ColumnKey.Create("minor"),
                    configure: f => f.Validate(_ => false, s_rule, alsoOnExport: true)),
                c => c.Field<string>(index: 1, key: ColumnKey.Create("currency")),
                read: (minor, currency) => new Money(minor, currency),
                write: money => (money.Minor, money.Currency))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Priced(new Money(100, "EUR")) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(s_rule, (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Fact]
    public async Task ComposedPartRuleOfAnAbsentOwner_DoesNotSeeThePreviousRecord()
    {
        // Arrange
        var priced = TabularMap
            .For<Priced>()
            .Compose(x => x.Amount,
                c => c.Field<long>(index: 0,
                    key: ColumnKey.Create("minor"),
                    configure: f => f.Validate(minor => minor > 0, s_rule, alsoOnExport: true)),
                c => c.Field<string>(index: 1, key: ColumnKey.Create("currency")),
                read: (minor, currency) => new Money(minor, currency),
                write: money => (money.Minor, money.Currency))
            .Build();

        var map = TabularMap
            .For<Order>()
            .Column(x => x.Number, index: 0)
            .Nested(x => x.Price, priced, e => e.StartIndex(1))
            .Build();

        // Act
        // WHY: An absent owner after a present one must behave like an absent owner alone, so both exports form one
        // action.
        var first = await CsvInstance.ExportAsync(new[] { new Order(1, null) }, map, new MemoryStream(), Token);
        var second = await CsvInstance.ExportAsync(
            new[] { new Order(1, new Priced(new Money(7, "EUR"))), new Order(2, null) },
            map,
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal((first.FatalError ?? first.Errors.SingleOrDefault())?.Code,
            (second.FatalError ?? second.Errors.SingleOrDefault())?.Code);
    }

    [Fact]
    public async Task BaseExportRuleOfAnAbsentOwner_Runs()
    {
        // Arrange
        var inner = TabularMap
            .For<Line>()
            .Column(x => x.Text,
                index: 0,
                configure: c => c.Convert<string?, string>(text => text ?? "",
                    text => text,
                    configure: b => b.Validate(_ => false, s_rule, alsoOnExport: true)))
            .Build();

        var map = TabularMap
            .For<Holder2>()
            .Column(x => x.Number, index: 0)
            .Nested(x => x.Line, inner, e => e.StartIndex(1))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Holder2(1, null) }, map, new MemoryStream(), Token);

        // Assert
        Assert.Equal(s_rule, (report.FatalError ?? Assert.Single(report.Errors)).Code);
    }

    [Fact]
    public async Task FailedCleanupOfTheImport_IsTheFatalError()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows does not allow replacing a file that is still open.");
        var directory = Path.Combine(Path.GetTempPath(), $"tabular-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var format = new ExcelFormat(new ExcelFormatOptions
            {
                ImportBufferBytes = 16,
                TemporaryDirectory = directory,
            });

            // WHY: When the input is buffered, a directory replaces the spilled file, so its later delete fails.
            var input = new FailingAfterStream(Workbook(false, null, ("Sheet1", [[1]]))
                    .ToArray(),
                () =>
                {
                    var spilled = Assert.Single(Directory.GetFiles(directory));
                    File.Delete(spilled);
                    Directory.CreateDirectory(spilled);
                    File.WriteAllText(Path.Combine(spilled, "keep"), "x");
                });

            // Act
            var report = await format.ImportAsync(input, s_numbers, Token);

            // Assert
            Assert.Equal(ReportStatus.Failed, report.Status);
            Assert.False(report.IsComplete);
            Assert.Equal(TabularErrorCodes.Io.Failed, report.FatalError?.Code);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReportItems_CannotBeChanged()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1"), s_numbers, Token);

        // Assert
        Assert.Throws<NotSupportedException>(() => ((IList<Numbered>)report.Items!).Clear());
    }

    [Fact]
    public async Task MissingFormat_HonorsTheDetailLimit()
    {
        // Arrange
        var import = new TabularImport(CsvInstance);

        // Act
        var report = await import.ImportAsync(TestInput.Utf8("1"),
            TabularType.Excel,
            s_numbers,
            new TabularImportOptions { MaxErrorDetails = 0 },
            Token);

        // Assert
        Assert.Equal((0, 1L, 1L), (report.Errors.Count, report.TotalErrorCount, report.OmittedErrorCount));
    }

    [Theory]
    [InlineData("csv import")]
    [InlineData("excel import")]
    [InlineData("workbook import")]
    [InlineData("common import")]
    [InlineData("csv export")]
    [InlineData("excel export")]
    [InlineData("workbook export")]
    public async Task NegativeDetailLimit_ShowsItsOwnConfigurationErrorOnEveryEntryPoint(
        string entry
    )
    {
        // Act
        var errors = await NegativeDetailLimitErrorsAsync(entry);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration,
            Assert.Single(errors)
                .Kind);
    }

    private static async Task<IReadOnlyList<TabularError>> NegativeDetailLimitErrorsAsync(
        string entry
    )
    {
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("Sheet1"), s_numbers);
        var export = ExcelExport.Create();
        export.Add(TabularSheetName.Create("Sheet1"), new[] { new Numbered(1) }, s_numbers);

        return entry switch
        {
            "csv import" => (await CsvInstance.ImportAsync(TestInput.Utf8("1"),
                s_numbers,
                new CsvImportOptions { MaxErrorDetails = -1 },
                Token)).Errors,
            "excel import" => (await ExcelInstance.Format.ImportAsync(Create(TabularType.Excel, [1]),
                s_numbers,
                new ExcelImportOptions { MaxErrorDetails = -1 },
                Token)).Errors,
            "workbook import" => (await ExcelInstance.Format.ImportAsync(Create(TabularType.Excel, [1]),
                sheets.Build(),
                new ExcelWorkbookImportOptions { MaxErrorDetails = -1 },
                Token)).Errors,
            "common import" => (await Import.ImportAsync(TestInput.Utf8("1"),
                TabularType.Csv,
                s_numbers,
                new TabularImportOptions { MaxErrorDetails = -1 },
                Token)).Errors,
            "csv export" => (await CsvInstance.ExportAsync(new[] { new Numbered(1) },
                s_numbers,
                new MemoryStream(),
                new CsvExportOptions { MaxErrorDetails = -1 },
                Token)).Errors,
            "excel export" => (await ExcelInstance.Format.ExportAsync(new[] { new Numbered(1) },
                s_numbers,
                new MemoryStream(),
                new ExcelExportOptions { MaxErrorDetails = -1 },
                Token)).Errors,
            _ => (await ExcelInstance.Format.ExportAsync(export.Build(),
                new MemoryStream(),
                new ExcelWorkbookExportOptions { MaxErrorDetails = -1 },
                Token)).Errors,
        };
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("excel")]
    [InlineData("workbook")]
    [InlineData("common")]
    public async Task UnreadableInputStream_IsAConfigurationErrorOnEveryImportEntryPoint(
        string entry
    )
    {
        // Arrange
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("Sheet1"), s_numbers);

        // Act
        var errors = entry switch
        {
            "csv" => (await CsvInstance.ImportAsync(new WriteOnlyStream(), s_numbers, Token)).Errors,
            "excel" => (await ExcelInstance.Format.ImportAsync(new WriteOnlyStream(), s_numbers, Token)).Errors,
            "workbook" => (await ExcelInstance.Format.ImportAsync(new WriteOnlyStream(), sheets.Build(), Token)).Errors,
            _ => (await Import.ImportAsync(new WriteOnlyStream(), TabularType.Csv, s_numbers, Token)).Errors,
        };

        // Assert
        Assert.Equal(("The input stream is not readable.", TabularErrorKind.Configuration),
            (Assert.Single(errors)
                .Message, errors[0].Kind));
    }

    [Fact]
    public async Task EmptyPathOfTheCommonImport_IsAConfigurationReport()
    {
        // Act
        var report = await Import.ImportAsync("", TabularType.Csv, s_numbers, Token);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, report.FatalError?.Kind);
    }

    [Fact]
    public async Task EmptyPathOfTheCommonExport_IsAConfigurationReport()
    {
        // Act
        var report = await new TabularExport(CsvInstance).ExportAsync(new[] { new Numbered(1) },
            s_numbers,
            "",
            TabularType.Csv,
            Token);

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, report.FatalError?.Kind);
    }

    [Theory]
    [InlineData(-1, null, false)]
    [InlineData(null, -1, false)]
    [InlineData(-1, null, true)]
    [InlineData(null, -1, true)]
    public async Task MissingFormat_ReportsANegativeDetailLimitToo(
        int? common,
        int? inner,
        bool export
    )
    {
        // Act
        var errors = export
            ? (await new TabularExport(CsvInstance).ExportAsync(new[] { new Numbered(1) },
                s_numbers,
                new MemoryStream(),
                TabularType.Excel,
                new TabularExportOptions
                {
                    MaxErrorDetails = common,
                    Excel = new ExcelExportOptions { MaxErrorDetails = inner },
                },
                Token)).Errors
            : (await new TabularImport(CsvInstance).ImportAsync(TestInput.Utf8("1"),
                TabularType.Excel,
                s_numbers,
                new TabularImportOptions
                {
                    MaxErrorDetails = common,
                    Excel = new ExcelImportOptions { MaxErrorDetails = inner },
                },
                Token)).Errors;

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains("MaxErrorDetails must not be negative.", errors.Select(error => error.Message));
    }

    [Fact]
    public async Task MissingFormat_HonorsTheInnerDetailLimitOfTheRequestedImportFormat()
    {
        // Act
        var report = await new TabularImport(CsvInstance).ImportAsync(TestInput.Utf8("1"),
            TabularType.Excel,
            s_numbers,
            new TabularImportOptions
            {
                MaxErrorDetails = 1,
                Excel = new ExcelImportOptions { MaxErrorDetails = 0 },
            },
            Token);

        // Assert
        Assert.Equal((0, 1L), (report.Errors.Count, report.OmittedErrorCount));
    }

    [Fact]
    public async Task MissingFormat_HonorsTheInnerDetailLimitOfTheRequestedExportFormat()
    {
        // Act
        var report = await new TabularExport(CsvInstance).ExportAsync(new[] { new Numbered(1) },
            s_numbers,
            new MemoryStream(),
            TabularType.Excel,
            new TabularExportOptions
            {
                MaxErrorDetails = 1,
                Excel = new ExcelExportOptions { MaxErrorDetails = 0 },
            },
            Token);

        // Assert
        Assert.Equal((0, 1L), (report.Errors.Count, report.OmittedErrorCount));
    }

    [Fact]
    public async Task CancellationDuringTheSourceKeyCheck_StopsAndReleasesNoRow()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var key = s_numbers.Key(x => x.Number, new CancelingComparer(cancellation, 5));
        var text = string.Join('\n', Enumerable.Range(1, 20));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(text),
            s_numbers,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = key,
            },
            cancellation.Token);

        // Assert
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Empty(report.Items ?? []);
    }

    [Fact]
    public async Task ComparerObservingTheCanceledToken_EndsTheCallAsCanceled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var key = s_numbers.Key(x => x.Number, new CancelingComparer(cancellation, 5, observe: true));
        var text = string.Join('\n', Enumerable.Range(1, 20));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(text),
            s_numbers,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = key,
            },
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, null), (report.Status, report.FatalError));
        Assert.Empty(report.Items ?? []);
    }

    [Fact]
    public async Task CancellationAtTheLastKey_StopsAndReleasesNoRow()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var key = s_numbers.Key(x => x.Number, new CancelingComparer(cancellation, after: int.MaxValue, atValue: 20));
        var text = string.Join('\n', Enumerable.Range(1, 20));

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8(text),
            s_numbers,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = key,
            },
            cancellation.Token);

        // Assert
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Empty(report.Items ?? []);
    }

    [Fact]
    public async Task ThrowingSourceKeyComparer_EndsTheRunWithAReport()
    {
        // Arrange
        var key = s_numbers.Key(x => x.Number, new ThrowingComparer());

        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("1\n2"),
            s_numbers,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = key,
            },
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorCodes.Callback.Failed, TabularErrorKind.Technical, 2L),
            (report.Status, report.FatalError?.Code, report.FatalError?.Kind, report.RowsRead));

        Assert.Empty(report.Items ?? []);
    }

    [Fact]
    public async Task DuplicateDetails_KeepTheirRelatedRowsWithinTheLimit()
    {
        // Act
        var report = await CsvInstance.ImportAsync(TestInput.Utf8("7\n7\n7"),
            s_numbers,
            new CsvImportOptions
            {
                SourceKey = s_numbers.Key(x => x.Number),
                MaxErrorDetails = 1,
            },
            Token);

        // Assert
        Assert.Equal((3L, 2L), (report.TotalErrorCount, report.OmittedErrorCount));
        Assert.Equal(2,
            Assert.Single(report.Errors)
                .RelatedLocations.Count);

        Assert.Throws<NotSupportedException>(()
            => ((IList<TabularErrorLocation>)report.Errors[0].RelatedLocations).Clear());
    }

    private static async IAsyncEnumerable<Flag> ChangingAfterFirst(
        Flag[] flags,
        Action change
    )
    {
        foreach (var flag in flags)
        {
            yield return flag;
            change();
            await Task.Yield();
        }
    }

    private static async Task<string> TextOf(
        BufferedExportResult result
    )
    {
        await using var file = result.File!;
        using var reader = new StreamReader(file);

        return (await reader.ReadToEndAsync(Token)).ReplaceLineEndings("\n");
    }

    /// <summary>A comparer of the application that fails while the keys are compared.</summary>
    private sealed class ThrowingComparer : IEqualityComparer<int>
    {
        public bool Equals(
            int x,
            int y
        ) => throw new InvalidOperationException("The comparer failed.");

        public int GetHashCode(
            int obj
        ) => throw new InvalidOperationException("The comparer failed.");
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Numbered(int Number);

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Flag(bool Value);

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Line(string Text);

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Address(
        string City,
        string PostalCode
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Holder(
        string Name,
        Address Address
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Pair(
        string A,
        string B
    );

    private sealed record PairRow(
        Pair Left,
        Pair Right
    );

    private sealed record Money(
        long Minor,
        string Currency
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Priced(Money Amount);

    /// <summary>A stream that can only be written, like a file opened for writing.</summary>
    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException("The stream cannot be read.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("The stream cannot be read.");
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Order(
        int Number,
        Priced? Price
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Holder2(
        int Number,
        Line? Line
    );

    /// <summary>A source of one record whose release fails, after the export rule has ended the run.</summary>
    private sealed class ThrowsOnRelease : IAsyncEnumerable<Line>, IAsyncEnumerator<Line>
    {
        private bool _done;

        public IAsyncEnumerator<Line> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        ) => this;

        public Line Current { get; } = new("x");

        public ValueTask<bool> MoveNextAsync()
        {
            var next = !_done;
            _done = true;

            return ValueTask.FromResult(next);
        }

        public ValueTask DisposeAsync() => throw new InvalidOperationException("release failed");
    }

    /// <summary>A comparer that cancels the call after a number of hash computations or at one value.</summary>
    private sealed class CancelingComparer : IEqualityComparer<int>
    {
        private readonly int _after;
        private readonly int? _atValue;
        private readonly CancellationTokenSource _cancellation;
        private readonly bool _observe;
        private int _calls;

        public CancelingComparer(
            CancellationTokenSource cancellation,
            int after,
            int? atValue = null,
            bool observe = false
        )
        {
            _cancellation = cancellation;
            _after = after;
            _atValue = atValue;
            _observe = observe;
        }

        public bool Equals(
            int x,
            int y
        ) => x == y;

        public int GetHashCode(
            int obj
        )
        {
            if (++_calls == _after
                || obj == _atValue)
            {
                _cancellation.Cancel();
                if (_observe)
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                }
            }

            return obj;
        }
    }

    /// <summary>A forward-only stream that returns its bytes, then runs an action or fails at the end.</summary>
    private sealed class FailingAfterStream : Stream
    {
        private readonly Action? _atEnd;
        private readonly MemoryStream _inner;
        private bool _ended;

        public FailingAfterStream(
            byte[] content,
            Action? atEnd = null
        )
        {
            _inner = new MemoryStream(content);
            _atEnd = atEnd;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        ) => Next(_inner.Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(Next(_inner.Read(buffer.Span)));

        public override long Seek(
            long offset,
            SeekOrigin origin
        ) => throw new NotSupportedException();

        public override void SetLength(
            long value
        ) => throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException();

        private int Next(
            int read
        )
        {
            if (read > 0)
            {
                return read;
            }

            if (_atEnd is null)
            {
                throw new IOException("The connection was lost.");
            }

            if (!_ended)
            {
                _ended = true;
                _atEnd();
            }

            return 0;
        }
    }
}
