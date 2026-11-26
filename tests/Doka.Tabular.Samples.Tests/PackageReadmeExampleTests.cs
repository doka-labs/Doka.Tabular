namespace Doka.Tabular.Samples.Tests;

/// <summary>Runs the core example of the package README.</summary>
public sealed class PackageReadmeExampleTests
{
    [Fact]
    public async Task ImportAndExport_WithANegativeStock_WritesAnXlsxWorkbook()
    {
        // Arrange
        using var source = new MemoryStream("A-01,12\nA-02,-1\nA-03,0"u8.ToArray());
        using var target = new MemoryStream();

        // Act
        await PackageReadmeExamples.ImportAndExportAsync(source, target, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(target.Length > 0);
        Assert.Equal("PK"u8.ToArray(),
            target
                .ToArray()[..2]);
    }
}
