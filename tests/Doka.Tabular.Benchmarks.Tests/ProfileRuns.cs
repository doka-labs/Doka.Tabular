namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>
/// The profiles and checks the profile run tests share. Every benchmark does the measured work correctly for every
/// profile: input and expected result are reproducible, the import returns exactly the expected records and errors,
/// and the export reads back to the same records.
/// </summary>
/// <remarks>
/// WHY: Each check is its own test class, because xUnit runs the classes of an assembly in parallel but the tests of
/// one class one after another; in one class the two profiles of one million rows ran every check in sequence.
/// https://xunit.net/docs/running-tests-in-parallel
/// </remarks>
public static class ProfileRuns
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Formats => new(FormatImportBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Exports => new(FormatExportBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Storage => new(StorageBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Stores => new(FileStoreBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Large => new(ReferenceProfile.CsvLarge.Id, ReferenceProfile.XlsxLarge.Id);

    internal static void AssertExpected(
        ProfileRun run,
        ProfileOutcome outcome
    )
    {
        var errors = run.ExpectedErrorRecords();

        Assert.Equal(errors.Count == 0
                ? ReportStatus.Succeeded
                : ReportStatus.CompletedWithErrors,
            outcome.Status);

        Assert.Equal(run.ExpectedItems(), outcome.Items);
        Assert.Equal(errors, outcome.ErrorRecords);
    }

    internal static ReferenceProfile Profile(
        string id
    ) => ReferenceProfile.All.Single(profile => profile.Id == id);

    /// <summary>A stream that reads forward only, so the import has to buffer it.</summary>
    internal sealed class ForwardOnly : MemoryStream
    {
        public ForwardOnly(
            byte[] bytes
        )
            : base(bytes, writable: false) { }

        public override bool CanSeek => false;
    }
}
