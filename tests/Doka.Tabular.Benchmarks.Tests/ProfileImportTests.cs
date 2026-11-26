namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>The import of every format profile returns exactly the expected records and errors.</summary>
public sealed class ProfileImportTests
{
    [Theory]
    [MemberData(nameof(ProfileRuns.Formats), MemberType = typeof(ProfileRuns))]
    public async Task Import_ReturnsTheExpectedRecordsAndErrors(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(ProfileRuns.Profile(id), cancellationToken: ProfileRuns.Token);

        // Act
        var outcome = await run.ImportAsync(ProfileRuns.Token);

        // Assert
        ProfileRuns.AssertExpected(run, outcome);
    }
}
