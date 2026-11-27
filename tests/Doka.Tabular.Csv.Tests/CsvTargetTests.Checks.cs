namespace Doka.Tabular.Csv.Tests;

/// <summary>Targets that cannot work end the export with a report, before the source is read where possible.</summary>
public sealed partial class CsvTargetTests
{
    [Fact]
    public async Task ForbiddenFinalFlush_IsAFailedReport()
    {
        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            new[] { new Name("Ada") },
            s_map,
            new ForbiddenFlushStream(),
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorCodes.Io.Failed), (report.Status, report.FatalError?.Code));
    }

    [Fact]
    public async Task InvalidTemporaryDirectory_IsAFailedReportWithoutFile()
    {
        // Arrange
        var format = new CsvFormat(
            new CsvFormatOptions
            {
                OutputBufferBytes = 0,
                TemporaryDirectory = "bad\0directory",
            });

        // Act
        var (report, file) = await format.ExportAsync(new[] { new Name("Ada") }, s_map, Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, null), (report.Status, file));
    }

    [Theory]
    [InlineData("bad\0.csv")]
    [InlineData("")]
    public async Task InvalidOrEmptyPath_IsAConfigurationReportOfTheImport(
        string path
    )
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions());

        // Act
        var report = await format.ImportAsync(path, s_map, Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorKind.Configuration), (report.Status, report.FatalError?.Kind));
    }

    [Theory]
    [InlineData("bad\0.csv")]
    [InlineData("")]
    public async Task InvalidOrEmptyPath_IsAConfigurationReportOfTheExport(
        string path
    )
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions());

        // Act
        var report = await format.ExportAsync(new[] { new Name("Ada") }, s_map, path, Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorKind.Configuration), (report.Status, report.FatalError?.Kind));
    }

    [Fact]
    public async Task NullPathOfTheImport_StaysAnArgumentError()
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions());

        // Act
        var exception = await Record.ExceptionAsync(() => format.ImportAsync((string)null!, s_map, Token));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public async Task NullPathOfTheExport_StaysAnArgumentError()
    {
        // Arrange
        var format = new CsvFormat(new CsvFormatOptions());

        // Act
        var exception = await Record.ExceptionAsync(() => format.ExportAsync(
            new[] { new Name("Ada") },
            s_map,
            (string)null!,
            Token));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public async Task KnownConfigurationError_CreatesNoTemporaryFile()
    {
        // Arrange
        // WHY: A missing directory makes every attempt to create the temporary file fail with its own error.
        var path = Path.Combine(Path.GetTempPath(), $"tabular-missing-{Guid.NewGuid():N}", "out.csv");

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            new[] { new Name("Ada") },
            s_map,
            path,
            new CsvExportOptions { ColumnKeys = [ColumnKey.Create("missing")] },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorKind.Configuration,
            Assert.Single(report.Errors)
                .Kind);
    }

    [Fact]
    public async Task ReadOnlyTargetStream_IsAConfigurationErrorBeforeTheSourceIsRead()
    {
        // Arrange
        var read = false;

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            Rows(),
            s_map,
            new MemoryStream([], writable: false),
            Token);

        // Assert
        Assert.Equal(
            (TabularErrorKind.Configuration, false, StreamOutputState.NotWritten),
            (report.FatalError?.Kind, read, report.StreamOutput));

        return;

        IEnumerable<Name> Rows()
        {
            read = true;

            yield return new Name("Ada");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringTheFirstEnumeration_WritesNoByteToTheStream(
        bool bom
    )
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = new MemoryStream();

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            CancelingRows(cancellation),
            s_map,
            target,
            new CsvExportOptions
            {
                WriteBom = bom,
                WriteHeader = true,
            },
            cancellation.Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Canceled, StreamOutputState.NotWritten, 0L),
            (report.Status, report.StreamOutput, target.Length));

        return;

        static IEnumerable<Name> CancelingRows(
            CancellationTokenSource source
        )
        {
            source.Cancel();
            source.Token.ThrowIfCancellationRequested();

            yield return new Name("Ada");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrequestedCancellationOfTheTarget_IsAFailedReport(
        bool atFlush
    )
    {
        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ExportAsync(
            new[] { new Name("Ada") },
            s_map,
            new CancelingTargetStream(atFlush),
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorCodes.Io.Failed), (report.Status, report.FatalError?.Code));
    }

    [Fact]
    public async Task UnrequestedCancellationOfTheInput_IsAFailedReport()
    {
        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(new CancelingInputStream(), s_map, Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorCodes.Io.Failed), (report.Status, report.FatalError?.Code));
    }

    /// <summary>A target whose write or flush reports a cancellation that the call did not request.</summary>
    private sealed class CancelingTargetStream : MemoryStream
    {
        private readonly bool _atFlush;

        public CancelingTargetStream(
            bool atFlush
        )
        {
            _atFlush = atFlush;
        }

        public override Task FlushAsync(
            CancellationToken cancellationToken
        ) => _atFlush
            ? throw new OperationCanceledException()
            : Task.CompletedTask;

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => _atFlush
            ? base.WriteAsync(buffer, cancellationToken)
            : throw new OperationCanceledException();
    }

    /// <summary>An input whose read reports a cancellation that the call did not request.</summary>
    private sealed class CancelingInputStream : MemoryStream
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
    }

    /// <summary>A stream whose flush is refused, like a target the process may no longer write.</summary>
    private sealed class ForbiddenFlushStream : MemoryStream
    {
        public override Task FlushAsync(
            CancellationToken cancellationToken
        ) => throw new UnauthorizedAccessException("The flush was refused.");
    }
}
