namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>Input and expected result of every format profile are reproducible.</summary>
public sealed class ProfileInputTests
{
    [Theory]
    [MemberData(nameof(ProfileRuns.Formats), MemberType = typeof(ProfileRuns))]
    public async Task Input_AndExpectedResult_AreReproducible(
        string id
    )
    {
        // Arrange
        var profile = ProfileRuns.Profile(id);

        // Act
        var first = await ProfileRun.PrepareAsync(profile, cancellationToken: ProfileRuns.Token);
        var second = await ProfileRun.PrepareAsync(profile, cancellationToken: ProfileRuns.Token);

        // Assert
        Assert.Equal(first.Input, second.Input);
        Assert.Equal(first.ExpectedItems(), second.ExpectedItems());
        Assert.Equal(first.ExpectedErrorRecords(), second.ExpectedErrorRecords());
    }
}
