namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>The export of every export profile reads back to the same records.</summary>
public sealed class ProfileExportTests
{
    [Theory]
    [MemberData(nameof(ProfileRuns.Exports), MemberType = typeof(ProfileRuns))]
    public async Task Export_WritesEveryRecordAndReadsBack(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(ProfileRuns.Profile(id), cancellationToken: ProfileRuns.Token);
        using var file = new MemoryStream();

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var report = await run.ExportAsync(file, ProfileRuns.Token);
        file.Position = 0;
        var outcome = await run.ImportAsync(file, ProfileRuns.Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, (long)run.Profile.Rows), (report.Status, report.RowsWritten));
        ProfileRuns.AssertExpected(run, outcome);
    }
}
