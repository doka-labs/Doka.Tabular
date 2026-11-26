namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>The store export of every file store profile stores one file that reads back.</summary>
public sealed class ProfileStoreExportTests
{
    [Theory]
    [MemberData(nameof(ProfileRuns.Stores), MemberType = typeof(ProfileRuns))]
    public async Task StoreExport_StoresOneFileThatReadsBack(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(ProfileRuns.Profile(id), cancellationToken: ProfileRuns.Token);

        // Act
        // WHY: The stored file must read back, so storing and reimport form one action.
        var (report, store) = await run.ExportToStoreAsync(ProfileRuns.Token);
        var stored = store.Files.Values.ToList();
        var outcome = await run.ImportAsync(new MemoryStream(stored[0], writable: false), ProfileRuns.Token);

        // Assert
        Assert.Single(stored);
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        ProfileRuns.AssertExpected(run, outcome);
    }
}
