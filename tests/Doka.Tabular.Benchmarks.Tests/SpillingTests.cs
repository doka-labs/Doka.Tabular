namespace Doka.Tabular.Benchmarks.Tests;

/// <summary>Spilling to temporary files gives the results of a run in memory only.</summary>
public sealed class SpillingTests
{
    [Theory]
    [MemberData(nameof(ProfileRuns.Large), MemberType = typeof(ProfileRuns))]
    public async Task WithSpillingToTemporaryFiles_TheResultsEqualThoseOfARunInMemoryOnly(
        string id
    )
    {
        // Arrange
        // WHY: Buffers of 64 KiB are far below the file size, so the buffered export and the forward-only import
        // spill to temporary files; the other run forbids spilling with buffers large enough for the whole file.
        const long small = 64 * 1024;
        const long enough = 1L << 31;
        var spilling = new ProfileFormats(
            new CsvFormat(
                new CsvFormatOptions
                {
                    ImportBufferBytes = small,
                    OutputBufferBytes = small,
                }),
            new ExcelFormat(
                new ExcelFormatOptions
                {
                    ImportBufferBytes = small,
                    OutputBufferBytes = small,
                }));

        var inMemory = new ProfileFormats(
            new CsvFormat(
                new CsvFormatOptions
                {
                    ImportBufferBytes = enough,
                    OutputBufferBytes = enough,
                    AllowSpill = false,
                }),
            new ExcelFormat(
                new ExcelFormatOptions
                {
                    ImportBufferBytes = enough,
                    OutputBufferBytes = enough,
                    AllowSpill = false,
                }));

        var spilled = await ProfileRun.PrepareAsync(ProfileRuns.Profile(id), spilling, ProfileRuns.Token);
        var held = await ProfileRun.PrepareAsync(ProfileRuns.Profile(id), inMemory, ProfileRuns.Token);

        // Act
        // WHY: The comparison of both runs is the checked contract, so both runs form one action.
        var spilledExport = await spilled.ExportBufferedAsync(ProfileRuns.Token);
        var heldExport = await held.ExportBufferedAsync(ProfileRuns.Token);
        var spilledImport =
            await spilled.ImportAsync(new ProfileRuns.ForwardOnly(spilledExport.File), ProfileRuns.Token);

        var heldImport = await held.ImportAsync(new ProfileRuns.ForwardOnly(heldExport.File), ProfileRuns.Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, ReportStatus.Succeeded), (spilledExport.Status, heldExport.Status));
        Assert.Equal(heldExport.File, spilledExport.File);
        Assert.Equal((heldImport.Status, heldImport.RowsRead), (spilledImport.Status, spilledImport.RowsRead));
        Assert.Equal(heldImport.Items, spilledImport.Items);
        Assert.Equal(spilled.ExpectedItems(), spilledImport.Items);
    }
}
