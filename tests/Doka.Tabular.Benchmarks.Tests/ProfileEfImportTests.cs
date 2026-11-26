namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>The EF Core import of every storage profile stores every record.</summary>
public sealed class ProfileEfImportTests
{
    [Theory]
    [MemberData(nameof(ProfileRuns.Storage), MemberType = typeof(ProfileRuns))]
    public async Task EfImport_StoresEveryRecord(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(ProfileRuns.Profile(id), cancellationToken: ProfileRuns.Token);
        using var database = ProfileDatabase.Create();

        // Act
        var outcome = await run.ImportIntoAsync(database.Target(), ProfileRuns.Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, (long)run.Profile.Rows), (outcome.Status, outcome.RowsRead));
        Assert.Equal(run.Profile.Rows, database.Count());
    }
}
