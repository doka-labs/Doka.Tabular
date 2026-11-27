namespace Doka.Tabular.Csv.Tests;

/// <summary>
/// The computed buffer accounting at its edges: a file of exactly the buffer stays in memory, one byte more spills,
/// is a limit error without spilling, and counts against the limit of temporary bytes.
/// </summary>
public sealed partial class CsvStreamAndResourceTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public async Task BufferedExport_AtTheBufferStaysInMemory_OneByteBeyondSpills(
        int bufferOffset,
        bool spills
    )
    {
        // Arrange
        var size = await SizeAsync();
        var format = Format(bufferBytes: size + bufferOffset);

        // Act
        var (report, file) = await format.ExportAsync(Rows(500), s_map, Token);

        // Assert
        await using (file)
        {
            Assert.Equal(ReportStatus.Succeeded, report.Status);
            Assert.Equal(size, file!.Length);
            Assert.Equal(
                spills,
                Directory.GetFiles(_directory)
                    .Length
                == 1);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task BufferedExportWithoutSpill_AtTheBufferSucceeds_OneByteBeyondIsALimitError(
        int bufferOffset,
        bool succeeds
    )
    {
        // Arrange
        var size = await SizeAsync();
        var format = new CsvFormat(
            new CsvFormatOptions
            {
                OutputBufferBytes = size + bufferOffset,
                AllowSpill = false,
                TemporaryDirectory = _directory,
            });

        // Act
        var (report, file) = await format.ExportAsync(Rows(500), s_map, Token);

        // Assert
        await using (file)
        {
            Assert.Equal(
                succeeds
                    ? null
                    : TabularErrorCodes.Resource.LimitExceeded,
                report.FatalError?.Code);

            Assert.Equal(succeeds, file is not null);
            Assert.Empty(Directory.GetFiles(_directory));
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task SpilledExport_AtTheTemporaryLimitSucceeds_OneByteBeyondIsALimitError(
        int limitOffset,
        bool succeeds
    )
    {
        // Arrange
        var size = await SizeAsync();
        var format = new CsvFormat(
            new CsvFormatOptions
            {
                OutputBufferBytes = 0,
                MaxTemporaryBytes = size + limitOffset,
                TemporaryDirectory = _directory,
            });

        // Act
        var (report, file) = await format.ExportAsync(Rows(500), s_map, Token);

        // Assert
        await using (file)
        {
            Assert.Equal(
                succeeds
                    ? null
                    : TabularErrorCodes.Resource.LimitExceeded,
                report.FatalError?.Code);

            Assert.Equal(
                succeeds
                    ? 1
                    : 0,
                Directory.GetFiles(_directory)
                    .Length);
        }
    }

    private static async Task<long> SizeAsync()
    {
        using var file = new MemoryStream();
        await new CsvFormat(new CsvFormatOptions()).ExportAsync(Rows(500), s_map, file, Token);

        return file.Length;
    }
}
