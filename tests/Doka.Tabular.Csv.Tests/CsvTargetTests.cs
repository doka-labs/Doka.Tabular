namespace Doka.Tabular.Csv.Tests;

/// <summary>Canceled writes and flushes, growing seekable inputs, and delimiters the encoding cannot write.</summary>
public sealed partial class CsvTargetTests
{
    private static readonly TabularMap<Name> s_map = TabularMap
        .For<Name>()
        .Column(x => x.Text, index: 0)
        .Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CanceledFinalFlush_IsACanceledReport()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var target = new InterruptingStream(cancellation, interruptWrite: false);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            new[] { new Name("Ada") },
            s_map,
            target,
            cancellation.Token);

        // Assert
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Equal(StreamOutputState.Unknown, report.StreamOutput);
    }

    [Fact]
    public async Task CanceledWriteAfterTheFirstByte_IsNotReportedAsNotWritten()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var target = new InterruptingStream(cancellation, interruptWrite: true);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            new[] { new Name("Ada") },
            s_map,
            target,
            cancellation.Token);

        // Assert
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Equal(StreamOutputState.Unknown, report.StreamOutput);
    }

    [Fact]
    public async Task SeekableInputThatGrows_IsHeldToTheByteLimit()
    {
        // Arrange
        var input = new MemoryStream();
        input.Write("Ada"u8);
        input.Position = 0;
        var growing = new GrowingStream(input, "\nBob"u8.ToArray());

        // Act
        var report = await new CsvFormat(new CsvFormatOptions { MaxInputBytes = 3 }).ImportAsync(growing, s_map, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Resource.LimitExceeded, report.FatalError?.Code);
    }

    [Fact]
    public async Task DelimiterTheEncodingCannotWrite_IsAConfigurationError()
    {
        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            new[] { new Name("Ada") },
            s_map,
            new MemoryStream(),
            new CsvExportOptions
            {
                Encoding = Encoding.Latin1,
                Delimiter = '\u674E',
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record Name(string Text);

    /// <summary>A target that cancels the call in its first write after one byte, or in the final flush.</summary>
    private sealed class InterruptingStream : Stream
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly bool _interruptWrite;

        public InterruptingStream(
            CancellationTokenSource cancellation,
            bool interruptWrite
        )
        {
            _cancellation = cancellation;
            _interruptWrite = interruptWrite;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override Task FlushAsync(
            CancellationToken cancellationToken
        )
        {
            _cancellation.Cancel();

            return Task.FromCanceled(_cancellation.Token);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (!_interruptWrite)
            {
                return ValueTask.CompletedTask;
            }

            // WHY: The first byte reaches the target before the write is canceled.
            _cancellation.Cancel();

            return ValueTask.FromCanceled(_cancellation.Token);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException();

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

    /// <summary>A seekable input whose content grows after its length was taken, before the first read.</summary>
    private sealed class GrowingStream : Stream
    {
        private readonly MemoryStream _inner;
        private byte[]? _more;

        public GrowingStream(
            MemoryStream inner,
            byte[] more
        )
        {
            _inner = inner;
            _more = more;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() { }

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        ) => Read(buffer.AsSpan(offset, count));

        public override int Read(
            Span<byte> buffer
        )
        {
            Grow();

            return _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(Read(buffer.Span));

        public override long Seek(
            long offset,
            SeekOrigin origin
        ) => _inner.Seek(offset, origin);

        public override void SetLength(
            long value
        ) => throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException();

        private void Grow()
        {
            if (_more is null)
            {
                return;
            }

            var position = _inner.Position;
            _inner.Seek(0, SeekOrigin.End);
            _inner.Write(_more);
            _inner.Position = position;
            _more = null;
        }
    }
}
