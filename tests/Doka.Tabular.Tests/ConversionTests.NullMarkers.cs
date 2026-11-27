using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>A padded null marker reads back under trim; markers and labels that trim to nothing fail on both sides.</summary>
public sealed partial class ConversionTests
{
    private static readonly CsvExportOptions s_blankTranslations = new()
    {
        Translations = new Dictionary<string, string>
        {
            ["empty"] = " ",
            ["yes"] = " ",
            ["no"] = "no",
        },
    };

    [Fact]
    public async Task PaddedNullMarkerOfTheField_ReadsBackUnderTrim()
    {
        // Act
        var report = await ReadBackAsync(
            null,
            Map<string?>(c => c
                .NullText(" NA ")
                .Trim()));

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Fact]
    public async Task PaddedNullMarkerOfTheField_IsReadUnderTrim()
    {
        // Act
        var report = await ReadAsync(
            TabularType.Csv,
            " NA ",
            Map<string?>(c => c
                .NullText(" NA ")
                .Trim()));

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Fact]
    public async Task PaddedEmptyTranslation_ReadsBackUnderTrim()
    {
        // Act
        var report = await TranslatedReadBackAsync(
            Map<string?>(c => c
                .EmptyTranslationKey("empty")
                .Trim()),
            new Dictionary<string, string> { ["empty"] = " NA " });

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Fact]
    public async Task PaddedNullMarkerOfAConversion_ReadsBackUnderTrimOfTheField()
    {
        // Act
        var report = await ReadBackAsync(
            new Wrapped("x"),
            Map<Wrapped?>(c => c
                .Trim()
                .Convert<string?, Wrapped?>(
                    text => text is null
                        ? null
                        : new Wrapped(text),
                    _ => null,
                    configure: b => b.NullText(" NA "))));

        // Assert
        Assert.Null(ValueOf(report));
    }

    [Fact]
    public async Task ValueEqualToThePaddedNullMarkerUnderTrim_IsAnExportError()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Value<string?>("NA") },
            Map<string?>(c => c
                .NullText(" NA ")
                .Trim()),
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.InvalidFormat, report.FatalError?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NA")]
    public async Task PaddedNullMarkerWithoutTrim_StaysDistinctFromTheValue(
        string? value
    )
    {
        // Act
        var report = await ReadBackAsync(value, Map<string?>(c => c.NullText(" NA ")));

        // Assert
        Assert.Equal(value, ValueOf(report));
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("flag")]
    [InlineData("label")]
    public async Task TranslationThatTrimsToNothing_IsAConfigurationErrorOfTheExport(
        string kind
    )
    {
        // Act
        var report = kind switch
        {
            "marker" => await CsvInstance.ExportAsync(
                new[] { new Value<string?>(null) },
                Map<string?>(c => c
                    .EmptyTranslationKey("empty")
                    .Trim()),
                new MemoryStream(),
                s_blankTranslations,
                Token),
            "flag" => await CsvInstance.ExportAsync(
                new[] { new Value<bool>(true) },
                Map<bool>(c => c
                    .TranslationKeys("yes", "no")
                    .Trim()),
                new MemoryStream(),
                s_blankTranslations,
                Token),
            _ => await CsvInstance.ExportAsync(
                new[] { new Value<Padded>(Padded.Ready) },
                Map<Padded>(c => c
                    .TranslationKeys(_ => "yes")
                    .Trim()),
                new MemoryStream(),
                s_blankTranslations,
                Token),
        };

        // Assert
        Assert.Equal(TabularErrorKind.Configuration, report.FatalError?.Kind);
    }

    /// <summary>Exports a null value with the translations and reads the written file back with them.</summary>
    private static async Task<ImportReport<Value<string?>>> TranslatedReadBackAsync(
        TabularMap<Value<string?>> map,
        Dictionary<string, string> translations
    )
    {
        var file = new MemoryStream();
        await CsvInstance.ExportAsync(
            new[] { new Value<string?>(null) },
            map,
            file,
            new CsvExportOptions { Translations = translations },
            Token);

        file.Position = 0;

        return await CsvInstance.ImportAsync(file, map, new CsvImportOptions { Translations = translations }, Token);
    }
}
