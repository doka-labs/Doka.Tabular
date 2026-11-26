namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>A large export from an asynchronous source reads back in count, order and values.</summary>
public sealed class LargeStreamingExportTests
{
    [Theory]
    [MemberData(nameof(ProfileRuns.Large), MemberType = typeof(ProfileRuns))]
    public async Task LargeExportFromAContinuouslyGeneratingAsyncSource_ReadsBackInCountOrderAndValues(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(ProfileRuns.Profile(id), cancellationToken: ProfileRuns.Token);
        var path = Path.Combine(Path.GetTempPath(), $"tabular-large-{Guid.NewGuid():N}");
        try
        {
            // Act
            // WHY: The reimport of the export is the checked contract, so both form one action.
            ExportReport report;
            await using (var file = File.Create(path))
            {
                report = await run.ExportStreamingAsync(file, ProfileRuns.Token);
            }

            ProfileOutcome outcome;
            await using (var file = File.OpenRead(path))
            {
                outcome = await run.ImportAsync(file, ProfileRuns.Token);
            }

            // Assert
            Assert.Equal((ReportStatus.Succeeded, (long)run.Profile.Rows), (report.Status, report.RowsWritten));
            ProfileRuns.AssertExpected(run, outcome);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
