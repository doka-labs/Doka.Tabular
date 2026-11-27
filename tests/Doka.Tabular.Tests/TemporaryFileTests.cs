namespace Doka.Tabular.Tests;

public sealed class TemporaryFileTests
{
    [Fact]
    public async Task SpilledImportBuffer_WhoseDeleteFails_IsReportedByTheCleanupOfTheCall()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows does not allow replacing a file that is still open.");
        var directory = Path.Combine(Path.GetTempPath(), $"tabular-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var files = new TemporaryFiles(
                new ResourceLimits(
                    4,
                    4,
                    true,
                    directory,
                    null,
                    null,
                    null,
                    null));

            var buffer = new SpillBuffer(4, true, files, "the test buffer");
            await buffer.WriteAsync(new byte[16], TestFiles.Token);
            var view = await buffer.CompleteAsync(TestFiles.Token);

            // WHY: A directory at the path of the spilled file makes the delete fail for every user, also for root.
            var path = Assert.Single(Directory.GetFiles(directory));
            File.Delete(path);
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "keep"), "x", TestFiles.Token);

            // Act
            // WHY: The retry after the cause is gone belongs to the checked contract, so both cleanups form one action.
            await view.DisposeAsync();
            var blocked = files.CleanupAll();
            Directory.Delete(path, recursive: true);
            var retried = files.CleanupAll();

            // Assert
            Assert.Equal((false, true), (blocked, retried));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HandedOverExportFile_WhoseDeleteFails_IsLoggedWithTheOperationId()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows does not allow replacing a file that is still open.");
        var directory = Path.Combine(Path.GetTempPath(), $"tabular-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var logger = new CapturingLoggerFactory();
            var format = new CsvFormat(
                new CsvFormatOptions
                {
                    OutputBufferBytes = 1024,
                    TemporaryDirectory = directory,
                    Diagnostics = new TabularDiagnosticsOptions { LoggerFactory = logger },
                });

            var map = TabularMap
                .For<Line>()
                .Column(x => x.Text, index: 0)
                .Build();

            var lines = Enumerable
                .Range(0, 2_000)
                .Select(number => new Line($"line {number}"))
                .ToArray();

            var (report, file) = await format.ExportAsync(lines, map, TestFiles.Token);

            // WHY: A directory at the path of the spilled file makes the delete fail for every user, also for root.
            var path = Assert.Single(Directory.GetFiles(directory));
            File.Delete(path);
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "keep"), "x", TestFiles.Token);

            // Act
            await file!.DisposeAsync();

            // Assert
            Assert.Equal(ReportStatus.Succeeded, report.Status);
            var cleanup = Assert.Single(logger.Events, entry => entry.Id == TabularLogEvents.CleanupFailed);
            Assert.Contains(
                cleanup.State,
                pair => pair.Key == "OperationId" && Equals(pair.Value, report.Diagnostics.OperationId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record Line(string Text);
}
