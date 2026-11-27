using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>A cancellation thrown by the input ends the table like one observed between rows.</summary>
public sealed partial class PipelineTests
{
    [Theory]
    [InlineData(true, ReportStatus.Canceled)]
    [InlineData(false, ReportStatus.Failed)]
    public async Task CancellationThrownByTheCleanupOfTheSource_CountsAsTheCallsOnlyWhileItIsCanceled(
        bool canceled,
        ReportStatus expected
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var map = TabularMap
            .For<Numbered>()
            .Column(
                x => x.Number,
                index: 0,
                configure: c => c.Convert(
                    number => number,
                    number =>
                    {
                        if (canceled)
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            // WHY: The write function runs during the awaited export, before the source is disposed.
                            cancellation.Cancel();
                        }

                        return number;
                    }))
            .Build();

        // Act
        var report = await CsvInstance.ExportAsync(
            new CancelingCleanupSource(cancellation),
            map,
            new MemoryStream(),
            cancellation.Token);

        // Assert
        Assert.Equal(expected, report.Status);
    }

    [Fact]
    public async Task CancellationThrownByTheInput_ReleasesNoRowOfATableWithASourceKey()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();

        // WHY: More rows than one read buffer, so rows are taken before the read that observes the cancellation.
        var text = "7\n7\n" + string.Join('\n', Enumerable.Range(100, 20_000)) + "\n";

        // Act
        var report = await CsvInstance.ImportAsync(
            new CancelingAtTheEndStream(Encoding.UTF8.GetBytes(text), cancellation),
            s_numbers,
            new CsvImportOptions
            {
                Strategy = ImportStrategy.Partial,
                SourceKey = s_numbers.Key(x => x.Number),
            },
            cancellation.Token);

        // Assert
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Empty(report.Items ?? []);
    }

    [Theory]
    [InlineData("rule")]
    [InlineData("read")]
    [InlineData("factory")]
    public async Task CancellationOfTheCallInAnImportFunction_IsACanceledReport(
        string function
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("1\n2"),
            ImportMap(function, cancellation),
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, null), (report.Status, report.FatalError));

        return;

        static TabularMap<Numbered> ImportMap(
            string function,
            CancellationTokenSource source
        ) => function switch
        {
            "rule" => TabularMap
                .For<Numbered>()
                .Column(x => x.Number, index: 0, configure: c => c.Validate(_ => Cancel(source)))
                .Build(),
            "read" => TabularMap
                .For<Numbered>()
                .Column(
                    x => x.Number,
                    index: 0,
                    configure: c => c.Convert<string, int>(
                        _ => Cancel(source)
                            ? 1
                            : 0,
                        number => $"{number}"))
                .Build(),
            _ => TabularMap
                .For<Numbered>()
                .Column(x => x.Number, index: 0)
                .UseFactory(_ => _ => Cancel(source)
                    ? new Numbered(1)
                    : new Numbered(0))
                .Build(),
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOfTheCallInAnExportFunction_IsACanceledReport(
        bool rule
    )
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();

        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Numbered(1), new Numbered(2) },
            ExportMap(rule, cancellation),
            new MemoryStream(),
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, null), (report.Status, report.FatalError));

        return;

        static TabularMap<Numbered> ExportMap(
            bool rule,
            CancellationTokenSource source
        ) => TabularMap
            .For<Numbered>()
            .Column(
                x => x.Number,
                index: 0,
                configure: c =>
                {
                    if (rule)
                    {
                        c.Validate(_ => Cancel(source), alsoOnExport: true);
                    }
                    else
                    {
                        c.Convert<string, int>(
                            int.Parse,
                            _ => Cancel(source)
                                ? "1"
                                : "0");
                    }
                })
            .Build();
    }

    /// <summary>Cancels the call from inside an application function, which then observes the cancellation.</summary>
    private static bool Cancel(
        CancellationTokenSource cancellation
    )
    {
        cancellation.Cancel();
        cancellation.Token.ThrowIfCancellationRequested();

        return true;
    }

    /// <summary>One record; its cleanup throws a cancellation of the token it was given.</summary>
    private sealed class CancelingCleanupSource : IAsyncEnumerable<Numbered>
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelingCleanupSource(
            CancellationTokenSource cancellation
        )
        {
            _cancellation = cancellation;
        }

        public IAsyncEnumerator<Numbered> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        ) => new Enumerator(_cancellation);

        private sealed class Enumerator : IAsyncEnumerator<Numbered>
        {
            private readonly CancellationTokenSource _cancellation;
            private bool _moved;

            public Enumerator(
                CancellationTokenSource cancellation
            )
            {
                _cancellation = cancellation;
            }

            public Numbered Current => new(1);

            public ValueTask<bool> MoveNextAsync()
            {
                var next = !_moved;
                _moved = true;

                return ValueTask.FromResult(next);
            }

            public ValueTask DisposeAsync() => throw new OperationCanceledException(_cancellation.Token);
        }
    }

    /// <summary>Returns its bytes in the requested pieces; the read after the last byte requests the cancellation.</summary>
    private sealed class CancelingAtTheEndStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly CancellationTokenSource _cancellation;
        private int _position;

        public CancelingAtTheEndStream(
            byte[] bytes,
            CancellationTokenSource cancellation
        )
        {
            _bytes = bytes;
            _cancellation = cancellation;
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

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_position == _bytes.Length)
            {
                _cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var count = Math.Min(buffer.Length, _bytes.Length - _position);
            _bytes
                .AsSpan(_position, count)
                .CopyTo(buffer.Span);

            _position += count;

            return ValueTask.FromResult(count);
        }

        public override void Flush() { }

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
    }
}
