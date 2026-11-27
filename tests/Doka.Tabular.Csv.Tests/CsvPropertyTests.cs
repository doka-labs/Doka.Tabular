using FsCheck.Xunit;

namespace Doka.Tabular.Csv.Tests;

/// <summary>
/// Values written by the CSV export are read back unchanged. Every run draws new values; a failure is a real defect,
/// and its reported counterexample becomes a fixed test before the fix.
/// </summary>
public sealed class CsvPropertyTests
{
    private static readonly CultureInfo[] s_cultures =
    [
        CultureInfo.InvariantCulture,
        CultureInfo.GetCultureInfo("de-DE"),
        CultureInfo.GetCultureInfo("de-CH"),
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("fr-FR"),
    ];

    private static readonly TabularMap<TextLine> s_texts = TabularMap
        .For<TextLine>()
        .Column(x => x.Number, index: 0)
        .Column(x => x.Text, index: 1, configure: c => c.Trim(false))
        .Build();

    private static readonly TabularMap<DecimalLine> s_decimals = TabularMap
        .For<DecimalLine>()
        .Column(x => x.Number, index: 0)
        .Column(x => x.Value, index: 1)
        .Build();

    private static readonly CsvFormat s_format = new(new CsvFormatOptions());

    [Property]
    public async Task Text_IsReadBackUnchanged(
        string?[] texts
    )
    {
        // Arrange
        var lines = texts
            .Select((text, index) => new TextLine(index, PropertyText.WellFormed(text)))
            .ToArray();

        var target = new MemoryStream();

        // Act
        // WHY: The round trip is the checked contract, so export and reimport form one action.
        var export = await s_format.ExportAsync(lines, s_texts, target, TestContext.Current.CancellationToken);
        target.Position = 0;
        var import = await s_format.ImportAsync(target, s_texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        Assert.Equal(ReportStatus.Succeeded, import.Status);
        Assert.Equal(
            lines.Select(line => PropertyText.Empty(line.Text)),
            import.Items!.Select(line => PropertyText.Empty(line.Text)));
    }

    [Property]
    public async Task Decimal_IsReadBackUnchangedInEachCulture(
        decimal[] values,
        byte cultureChoice
    )
    {
        // Arrange
        var culture = s_cultures[cultureChoice % s_cultures.Length];
        var lines = values
            .Select((value, index) => new DecimalLine(index, value))
            .ToArray();

        var target = new MemoryStream();

        // Act
        // WHY: The round trip is the checked contract, so export and reimport form one action.
        var export = await s_format.ExportAsync(
            lines,
            s_decimals,
            target,
            new CsvExportOptions { Culture = culture },
            TestContext.Current.CancellationToken);

        target.Position = 0;
        var import = await s_format.ImportAsync(
            target,
            s_decimals,
            new CsvImportOptions { Culture = culture },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        Assert.Equal(ReportStatus.Succeeded, import.Status);
        Assert.Equal(lines, import.Items);
    }

    private sealed record TextLine(
        int Number,
        string? Text
    );

    private sealed record DecimalLine(
        int Number,
        decimal Value
    );
}
