namespace Doka.Tabular.Csv.Tests;

/// <summary>Stream ownership, path publication, returned files, and the resource limits.</summary>
public sealed partial class CsvStreamAndResourceTests : IDisposable
{
    private static readonly TabularMap<Row> s_map = TabularMap
        .For<Row>()
        .Column(x => x.Key, index: 0)
        .Column(x => x.Value, index: 1)
        .Build();

    private readonly string _directory = Directory.CreateTempSubdirectory("tabular-tests-")
        .FullName;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Import_PassedStream_IsReadAndStaysOpen()
    {
        // Arrange
        await using var stream = TestInput.Utf8("a,1\nb,2");

        // Act
        var report = await Format()
            .ImportAsync(stream, s_map, Token);

        // Assert
        Assert.Equal([new Row("a", 1), new Row("b", 2)], report.Items);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task Import_ForwardOnlyStream_IsReadAndNotDisposed()
    {
        // Arrange
        var forward = new ForwardOnlyStream(TestInput.Utf8("a,1\nb,2"));

        // Act
        var report = await Format()
            .ImportAsync(forward, s_map, Token);

        // Assert
        Assert.Equal([new Row("a", 1), new Row("b", 2)], report.Items);
        Assert.False(forward.Disposed);
    }

    [Fact]
    public async Task Import_Path_IsReadAndTheFileIsClosed()
    {
        // Arrange
        var path = Path.Combine(_directory, "input.csv");
        await File.WriteAllTextAsync(path, "a,1\nb,2", Token);

        // Act
        var report = await Format()
            .ImportAsync(path, s_map, Token);

        // Assert
        Assert.Equal([new Row("a", 1), new Row("b", 2)], report.Items);
        await using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public async Task Export_PassedStream_IsCompleteAndStaysOpen()
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await Format()
            .ExportAsync(Rows(3), s_map, target, Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, StreamOutputState.Complete), (report.Status, report.StreamOutput));
        Assert.Equal("k1,1\r\nk2,2\r\nk3,3"u8.ToArray(), target.ToArray());
        Assert.True(target.CanWrite);
    }

    [Fact]
    public async Task Export_Path_PublishesTheSameContentWithoutTemporaryFile()
    {
        // Arrange
        var path = Path.Combine(_directory, "out.csv");

        // Act
        var report = await Format()
            .ExportAsync(Rows(3), s_map, path, Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, PathOutputState.Published), (report.Status, report.PathOutput));
        Assert.Equal("k1,1\r\nk2,2\r\nk3,3"u8.ToArray(), await File.ReadAllBytesAsync(path, Token));
        Assert.Equal(
            ["out.csv"],
            Directory
                .GetFiles(_directory)
                .Select(Path.GetFileName));
    }

    [Fact]
    public async Task Export_ForwardOnlyStream_IsNotDisposed()
    {
        // Arrange
        var forward = new ForwardOnlyStream(new MemoryStream());

        // Act
        var report = await Format()
            .ExportAsync(Rows(3), s_map, forward, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.False(forward.Disposed);
    }

    [Fact]
    public async Task PathExport_LateConverterFailure_LeavesTheExistingFileByteIdenticalAndNoTemporaryFile()
    {
        // Arrange
        var path = Path.Combine(_directory, "out.csv");
        await File.WriteAllTextAsync(path, "previous", Token);
        var failing = TabularMap
            .For<Row>()
            .Column(x => x.Key, index: 0)
            .Column(
                x => x.Value,
                index: 1,
                configure: c => c.Convert<string, int>(
                    read: text => int.Parse(text, CultureInfo.InvariantCulture),
                    write: value => value == 999
                        ? throw new FormatException()
                        : value.ToString(CultureInfo.InvariantCulture)))
            .Build();

        // Act
        var report = await Format()
            .ExportAsync(
                Rows(998)
                    .Append(new Row("x", 999)),
                failing,
                path,
                Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(PathOutputState.NotPublished, report.PathOutput);
        Assert.Equal("previous", await File.ReadAllTextAsync(path, Token));
        Assert.Equal(
            ["out.csv"],
            Directory
                .GetFiles(_directory)
                .Select(Path.GetFileName));
    }

    [Fact]
    public async Task PathExport_CanceledBeforePublication_DoesNotCreateTheTarget()
    {
        // Arrange
        var path = Path.Combine(_directory, "new.csv");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);

        // Act
        var report = await Format()
            .ExportAsync(CancelAfter(cancellation, 50), s_map, path, cancellation.Token);

        // Assert
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task StreamExport_FailureAfterOutput_LeavesTheWrittenBytesAndReportsPartialOutput()
    {
        // Arrange
        var failing = TabularMap
            .For<Row>()
            .Column(x => x.Key, index: 0)
            .Column(
                x => x.Value,
                index: 1,
                configure: c => c.Convert<string, int>(
                    read: text => int.Parse(text, CultureInfo.InvariantCulture),
                    write: value => value == 50_000
                        ? throw new FormatException()
                        : value.ToString(CultureInfo.InvariantCulture)))
            .Build();

        var target = new MemoryStream();

        // Act
        var report = await Format()
            .ExportAsync(Rows(50_000), failing, target, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(StreamOutputState.Partial, report.StreamOutput);
        Assert.True(target.Length > 0);
    }

    [Fact]
    public async Task BufferedExport_Failure_ReturnsNoFileAndLeavesNoTemporaryFile()
    {
        // Arrange
        var failing = TabularMap
            .For<Row>()
            .Column(x => x.Key, index: 0)
            .Column(
                x => x.Value,
                index: 1,
                configure: c => c.Convert<string, int>(
                    read: text => int.Parse(text, CultureInfo.InvariantCulture),
                    write: value => value == 4000
                        ? throw new FormatException()
                        : value.ToString(CultureInfo.InvariantCulture)))
            .Build();

        // Act
        var (report, file) = await Format(bufferBytes: 1024)
            .ExportAsync(Rows(5000), failing, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Null(file);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task BufferedExport_SpilledFile_IsSeekableAtZero()
    {
        // Act
        var (report, file) = await Format(bufferBytes: 1024)
            .ExportAsync(Rows(5000), s_map, Token);

        // Assert
        await using var owned = file;
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.NotNull(file);
        Assert.Equal((0L, true, false), (file.Position, file.CanSeek, file.CanWrite));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task BufferedExport_SpilledFile_IsRemovedOnDispose()
    {
        // Arrange
        var (_, file) = await Format(bufferBytes: 1024)
            .ExportAsync(Rows(5000), s_map, Token);

        // Act
        await file!.DisposeAsync();

        // Assert
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task BufferedExport_CanceledBeforeTheHandOver_IsCanceledWithoutFile()
    {
        // Arrange
        // WHY: A last row over the flush threshold of the writer leaves nothing pending when the table completes,
        // so the cancellation of the released source first reaches the hand-over of the spilled file.
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Row[] rows = [new("a", 1), new(new string('k', 70000), 2)];

        // Act
        var (report, file) = await Format(bufferBytes: 0)
            .ExportAsync(new CancelOnDispose(cancellation, rows), s_map, cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, 2L), (report.Status, report.RowsWritten));
        Assert.Null(file);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task BufferedExport_OverTheBufferWithoutSpill_IsAResourceErrorWithoutFile()
    {
        // Arrange
        var format = new CsvFormat(
            new CsvFormatOptions
            {
                OutputBufferBytes = 1024,
                AllowSpill = false,
                TemporaryDirectory = _directory,
            });

        // Act
        var (report, file) = await format.ExportAsync(Rows(5000), s_map, Token);

        // Assert
        Assert.Null(file);
        Assert.Equal(TabularErrorCodes.Resource.LimitExceeded, report.FatalError?.Code);
    }

    [Fact]
    public async Task MaxInputBytes_AtTheLimit_Succeeds()
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions { MaxInputBytes = 7 });

        // Act
        var report = await format.ImportAsync(new ForwardOnlyStream(TestInput.Utf8("a,1\nb,2")), s_map, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task MaxInputBytes_OneByteBelowTheInput_IsALimitError()
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions { MaxInputBytes = 6 });

        // Act
        var report = await format.ImportAsync(new ForwardOnlyStream(TestInput.Utf8("a,1\nb,2")), s_map, Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorCodes.Resource.LimitExceeded),
            (report.Status, report.FatalError?.Code));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(4, false)]
    public async Task MaxFieldCharacters_AppliesToUnmappedFieldsAsWell(
        long limit,
        bool succeeds
    )
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions { MaxFieldCharacters = limit });

        // Act
        var report = await format.ImportAsync(TestInput.Utf8("a,1,12345"), s_map, Token);

        // Assert
        Assert.Equal(
            succeeds
                ? ReportStatus.Succeeded
                : ReportStatus.Failed,
            report.Status);
    }

    [Fact]
    public async Task ForwardOnlyInput_WithRepeatedKeyAndSmallSpillingBuffer_ReportsTheDuplicateLikeWithEnoughMemory()
    {
        // Arrange
        var lines = Enumerable
            .Range(1, 2000)
            .Select(number => $"k{number},{number}")
            .Append("k1,0")
            .ToList();

        var key = s_map.Key(x => x.Key);
        var options = new CsvImportOptions
        {
            Strategy = ImportStrategy.Partial,
            SourceKey = key,
        };

        // Act
        // WHY: The equal result with and without spilling is the checked contract, so both runs form one action.
        var small = await Format(bufferBytes: 512)
            .ImportAsync(new ForwardOnlyStream(TestInput.Utf8(string.Join("\n", lines))), s_map, options, Token);

        var large = await Format()
            .ImportAsync(new ForwardOnlyStream(TestInput.Utf8(string.Join("\n", lines))), s_map, options, Token);

        // Assert
        Assert.Equal(
            large.Errors.Select(error => error.Location.RecordNumber),
            small.Errors.Select(error => error.Location.RecordNumber));

        Assert.Equal(2, small.Errors.Count);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task NegativeLimit_IsAConfigurationErrorBeforeReading()
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions { MaxInputBytes = -1 });

        // Act
        var report = await format.ImportAsync(TestInput.Utf8("a,1"), s_map, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(0, report.RowsRead);
    }

    private CsvFormat Format(
        long bufferBytes = 16 * 1024 * 1024
    ) => new(
        new CsvFormatOptions
        {
            ImportBufferBytes = bufferBytes,
            OutputBufferBytes = bufferBytes,
            TemporaryDirectory = _directory,
        });

    private static IEnumerable<Row> Rows(
        int count
    ) => Enumerable
        .Range(1, count)
        .Select(number => new Row($"k{number}", number));

    // WHY: The source never ends by itself; the export stops it through the canceled token.
    // ReSharper disable once IteratorNeverReturns
    private static async IAsyncEnumerable<Row> CancelAfter(
        CancellationTokenSource cancellation,
        int count
    )
    {
        for (var number = 1;; number++)
        {
            if (number == count)
            {
                await cancellation.CancelAsync();
            }

            await Task.Yield();

            yield return new Row($"k{number}", number);
        }
    }

    [Fact]
    public async Task AsyncOnlyTarget_ReceivesBytesWhileTheSourceStillDelivers()
    {
        // Arrange
        var target = new AsyncOnlyStream();
        long seenDuringExport = 0;

        // Act
        var report = await Format()
            .ExportAsync(Source(), s_map, target, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.True(seenDuringExport > 0, "no bytes reached the target before the source ended");
        Assert.True(target.Written > seenDuringExport);

        return;

        async IAsyncEnumerable<Row> Source()
        {
            for (var number = 1; number <= 50_000; number++)
            {
                if (number == 49_000)
                {
                    seenDuringExport = target.Written;
                }

                await Task.Yield();

                yield return new Row($"k{number}", number);
            }
        }
    }

    /// <summary>A source that cancels the export while its enumerator is disposed after the last row.</summary>
    private sealed class CancelOnDispose : IAsyncEnumerable<Row>
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly IEnumerable<Row> _rows;

        public CancelOnDispose(
            CancellationTokenSource cancellation,
            IEnumerable<Row> rows
        )
        {
            _cancellation = cancellation;
            _rows = rows;
        }

        public IAsyncEnumerator<Row> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        ) => new Enumerator(_cancellation, _rows.GetEnumerator());

        private sealed class Enumerator : IAsyncEnumerator<Row>
        {
            private readonly CancellationTokenSource _cancellation;
            private readonly IEnumerator<Row> _rows;

            public Enumerator(
                CancellationTokenSource cancellation,
                IEnumerator<Row> rows
            )
            {
                _cancellation = cancellation;
                _rows = rows;
            }

            public Row Current => _rows.Current;

            public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(_rows.MoveNext());

            public async ValueTask DisposeAsync()
            {
                _rows.Dispose();
                await _cancellation.CancelAsync();
            }
        }
    }

    private sealed record Row(
        string Key,
        int Value
    );
}
