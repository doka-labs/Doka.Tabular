namespace Doka.Tabular.Csv.Tests;

public sealed class SmokeTests
{
    private static readonly TabularMap<Article> s_map = TabularMap
        .For<Article>()
        .Column(x => x.Number, index: 0)
        .Column(x => x.Stock, index: 1)
        .Build();

    private static CsvFormat Format() => new(new CsvFormatOptions());

    private static MemoryStream Utf8(
        string text
    ) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task ImportAsync_PartialWithInvalidStock_ReturnsValidRowsAndOneError()
    {
        // Act
        var report = await Format()
            .ImportAsync(
                Utf8("A-01,12\nA-02,unbekannt\nA-03,0"),
                s_map,
                new CsvImportOptions { Strategy = ImportStrategy.Partial },
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal([new Article("A-01", 12), new Article("A-03", 0)], report.Items);
        var error = Assert.Single(report.Errors);
        Assert.Equal(2, error.Location.RecordNumber);
        Assert.Equal(1, error.Location.ColumnIndex);
        Assert.Equal("Stock", error.Location.PropertyPath?.Value);
    }

    [Fact]
    public async Task ImportAsync_StrictWithInvalidStock_ReturnsNoRows()
    {
        // Act
        var report = await Format()
            .ImportAsync(Utf8("A-01,12\nA-02,unbekannt\nA-03,0"), s_map, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.True(report.IsComplete);
        Assert.Empty(report.Items!);
        Assert.Equal(1, report.RowsRejected);
    }

    [Fact]
    public async Task ExportAsync_TwoRows_WritesCrLfSeparatedRecords()
    {
        // Act
        var (report, file) = await Format()
            .ExportAsync(
                new[] { new Article("A-01", 12), new Article("A-03", 0) },
                s_map,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.NotNull(file);
        await using (file)
        {
            using var reader = new StreamReader(file);
            Assert.Equal("A-01,12\r\nA-03,0", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
    }

    private sealed record Article(
        string Number,
        int Stock
    );
}
