namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>
/// Every benchmark does the measured work correctly for every profile: input and expected result are reproducible,
/// the import returns exactly the expected records and errors, and the export reads back to the same records.
/// </summary>
public sealed class ProfileRunTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> Formats => new(FormatImportBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Exports => new(FormatExportBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Storage => new(StorageBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Stores => new(FileStoreBenchmarks.Profiles.Select(profile => profile.Id));

    public static TheoryData<string> Large => new(ReferenceProfile.CsvLarge.Id, ReferenceProfile.XlsxLarge.Id);

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Input_AndExpectedResult_AreReproducible(
        string id
    )
    {
        // Arrange
        var profile = Profile(id);

        // Act
        var first = await ProfileRun.PrepareAsync(profile, cancellationToken: Token);
        var second = await ProfileRun.PrepareAsync(profile, cancellationToken: Token);

        // Assert
        Assert.Equal(first.Input, second.Input);
        Assert.Equal(first.ExpectedItems(), second.ExpectedItems());
        Assert.Equal(first.ExpectedErrorRecords(), second.ExpectedErrorRecords());
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Import_ReturnsTheExpectedRecordsAndErrors(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(Profile(id), cancellationToken: Token);

        // Act
        var outcome = await run.ImportAsync(Token);

        // Assert
        AssertExpected(run, outcome);
    }

    [Theory]
    [MemberData(nameof(Exports))]
    public async Task Export_WritesEveryRecordAndReadsBack(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(Profile(id), cancellationToken: Token);
        using var file = new MemoryStream();

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var report = await run.ExportAsync(file, Token);
        file.Position = 0;
        var outcome = await run.ImportAsync(file, Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, (long)run.Profile.Rows), (report.Status, report.RowsWritten));
        AssertExpected(run, outcome);
    }

    [Theory]
    [MemberData(nameof(Large))]
    public async Task LargeExportFromAContinuouslyGeneratingAsyncSource_ReadsBackInCountOrderAndValues(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(Profile(id), cancellationToken: Token);
        var path = Path.Combine(Path.GetTempPath(), $"tabular-large-{Guid.NewGuid():N}");
        try
        {
            // Act
            // WHY: The reimport of the export is the checked contract, so both form one action.
            ExportReport report;
            await using (var file = File.Create(path))
            {
                report = await run.ExportStreamingAsync(file, Token);
            }

            ProfileOutcome outcome;
            await using (var file = File.OpenRead(path))
            {
                outcome = await run.ImportAsync(file, Token);
            }

            // Assert
            Assert.Equal((ReportStatus.Succeeded, (long)run.Profile.Rows), (report.Status, report.RowsWritten));
            AssertExpected(run, outcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [MemberData(nameof(Large))]
    public async Task WithSpillingToTemporaryFiles_TheResultsEqualThoseOfARunInMemoryOnly(
        string id
    )
    {
        // Arrange
        // WHY: Buffers of 64 KiB are far below the file size, so the buffered export and the forward-only import
        // spill to temporary files; the other run forbids spilling with buffers large enough for the whole file.
        const long small = 64 * 1024;
        const long enough = 1L << 31;
        var spilling = new ProfileFormats(new CsvFormat(new CsvFormatOptions
            {
                ImportBufferBytes = small,
                OutputBufferBytes = small,
            }),
            new ExcelFormat(new ExcelFormatOptions
            {
                ImportBufferBytes = small,
                OutputBufferBytes = small,
            }));

        var inMemory = new ProfileFormats(new CsvFormat(new CsvFormatOptions
            {
                ImportBufferBytes = enough,
                OutputBufferBytes = enough,
                AllowSpill = false,
            }),
            new ExcelFormat(new ExcelFormatOptions
            {
                ImportBufferBytes = enough,
                OutputBufferBytes = enough,
                AllowSpill = false,
            }));

        var spilled = await ProfileRun.PrepareAsync(Profile(id), spilling, Token);
        var held = await ProfileRun.PrepareAsync(Profile(id), inMemory, Token);

        // Act
        // WHY: The comparison of both runs is the checked contract, so both runs form one action.
        var spilledExport = await spilled.ExportBufferedAsync(Token);
        var heldExport = await held.ExportBufferedAsync(Token);
        var spilledImport = await spilled.ImportAsync(new ForwardOnly(spilledExport.File), Token);
        var heldImport = await held.ImportAsync(new ForwardOnly(heldExport.File), Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, ReportStatus.Succeeded), (spilledExport.Status, heldExport.Status));
        Assert.Equal(heldExport.File, spilledExport.File);
        Assert.Equal((heldImport.Status, heldImport.RowsRead), (spilledImport.Status, spilledImport.RowsRead));
        Assert.Equal(heldImport.Items, spilledImport.Items);
        Assert.Equal(spilled.ExpectedItems(), spilledImport.Items);
    }

    [Theory]
    [MemberData(nameof(Storage))]
    public async Task EfImport_StoresEveryRecord(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(Profile(id), cancellationToken: Token);
        using var database = ProfileDatabase.Create();

        // Act
        var outcome = await run.ImportIntoAsync(database.Target(), Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, (long)run.Profile.Rows), (outcome.Status, outcome.RowsRead));
        Assert.Equal(run.Profile.Rows, database.Count());
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task StoreExport_StoresOneFileThatReadsBack(
        string id
    )
    {
        // Arrange
        var run = await ProfileRun.PrepareAsync(Profile(id), cancellationToken: Token);

        // Act
        // WHY: The stored file must read back, so storing and reimport form one action.
        var (report, store) = await run.ExportToStoreAsync(Token);
        var stored = store.Files.Values.ToList();
        var outcome = await run.ImportAsync(new MemoryStream(stored[0], writable: false), Token);

        // Assert
        Assert.Single(stored);
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        AssertExpected(run, outcome);
    }

    private static void AssertExpected(
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

    private static ReferenceProfile Profile(
        string id
    ) => ReferenceProfile.All.Single(profile => profile.Id == id);

    /// <summary>A stream that reads forward only, so the import has to buffer it.</summary>
    private sealed class ForwardOnly : MemoryStream
    {
        public ForwardOnly(
            byte[] bytes
        )
            : base(bytes, writable: false) { }

        public override bool CanSeek => false;
    }
}
