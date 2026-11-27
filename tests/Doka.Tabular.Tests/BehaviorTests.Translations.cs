using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed partial class BehaviorTests
{
    private static readonly Place[] s_activeThenSuspended =
        [new("X", Status.Active), new("X", Status.Active), new("X", Status.Active), new("X", Status.Suspended)];

    private static readonly Dictionary<string, string> s_collidingGerman = new()
    {
        ["city"] = "Ort",
        ["status.Active"] = "Aktiv",
        ["status.Approved"] = "Aktiv",
        ["status.Suspended"] = "Gesperrt",
    };

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Translations_DriveTheExportedHeader(
        TabularType type
    )
    {
        // Act
        var (_, file) = await Export.ExportAsync(
            new[] { new Place("Berlin", Status.Active) },
            s_places,
            type,
            new TabularExportOptions
            {
                WriteHeader = true,
                Translations = s_english,
            },
            Token);

        // Assert
        Assert.Equal(["City", "Status"], Cells(type, file!)[0]);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task Translations_DriveTheReimport(
        TabularType type
    )
    {
        // Arrange
        var rows = new[] { new Place("Berlin", Status.Active) };

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(
            rows,
            s_places,
            type,
            new TabularExportOptions
            {
                WriteHeader = true,
                Translations = s_english,
            },
            Token);

        var report = await Import.ImportAsync(
            file!,
            type,
            s_places,
            new TabularImportOptions
            {
                Binding = ColumnBinding.ByHeaderName,
                Translations = s_english,
            },
            Token);

        // Assert
        Assert.Equal(rows, report.Items);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task MissingTranslation_FailsBeforeWriting(
        TabularType type
    )
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await Export.ExportAsync(
            new[] { new Place("Berlin", Status.Active) },
            s_places,
            target,
            type,
            new TabularExportOptions
            {
                WriteHeader = true,
                Translations = WithoutKey("city"),
            },
            Token);

        // Assert
        var error = report.FatalError ?? report.Errors[0];
        Assert.Equal((TabularErrorCodes.Translation.Missing, TabularErrorKind.Configuration), (error.Code, error.Kind));
        Assert.Contains("city", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, target.Length);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task MissingTranslation_FallsBackToTheFixedTextWhenAllowed(
        TabularType type
    )
    {
        // Act
        var report = await Export.ExportAsync(
            new[] { new Place("Berlin", Status.Active) },
            s_places,
            type,
            new TabularExportOptions
            {
                WriteHeader = true,
                Translations = WithoutKey("city"),
                MissingTranslation = MissingTranslationBehavior.UseFixedText,
            },
            Token);

        // Assert
        Assert.Equal("Ort", Cells(type, report.File!)[0][0]);
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task NoTranslations_WriteTheFixedTexts(
        TabularType type
    )
    {
        // Act
        var report = await Export.ExportAsync(
            new[] { new Place("Berlin", Status.Active) },
            s_places,
            type,
            new TabularExportOptions { WriteHeader = true },
            Token);

        // Assert
        Assert.Equal(["Ort", "Status"], Cells(type, report.File!)[0]);
    }

    private static Dictionary<string, string> WithoutKey(
        string key
    ) => s_english
        .Where(entry => entry.Key != key)
        .ToDictionary();

    [Fact]
    public async Task MissingValueTranslation_OfAValueThatDoesNotOccur_DoesNotMatter()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            s_activeThenSuspended.Take(3),
            s_places,
            new MemoryStream(),
            new CsvExportOptions { Translations = WithoutKey("status.Suspended") },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task MissingValueTranslation_OfAnOccurringValue_FailsAtItsRow()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            s_activeThenSuspended,
            s_places,
            new MemoryStream(),
            new CsvExportOptions { Translations = WithoutKey("status.Suspended") },
            Token);

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Translation.Missing, 4L),
            (report.FatalError!.Code, report.FatalError.Location.RowNumber!.Value));
    }

    [Fact]
    public async Task MissingValueTranslation_FallsBackToTheFixedTextWhenAllowed()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            s_activeThenSuspended,
            s_places,
            new CsvExportOptions
            {
                Translations = WithoutKey("status.Suspended"),
                MissingTranslation = MissingTranslationBehavior.UseFixedText,
            },
            Token);

        // Assert
        Assert.Equal("Suspended", Cells(TabularType.Csv, report.File!)[3][1]);
    }

    [Fact]
    public async Task MissingValueTranslation_OfAnUnselectedField_DoesNotMatter()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            s_activeThenSuspended,
            s_places,
            new MemoryStream(),
            new CsvExportOptions
            {
                Translations = new Dictionary<string, string> { ["city"] = "City" },
                ColumnKeys = [ColumnKey.Create("city")],
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task TranslationCollision_IsAConfigurationErrorOfTheExport()
    {
        // Act
        var report = await CsvInstance.ExportAsync(
            new[] { new Place("X", Status.Active) },
            s_places,
            new MemoryStream(),
            new CsvExportOptions { Translations = s_collidingGerman },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
    }

    [Fact]
    public async Task TranslationCollision_IsAConfigurationErrorOfTheImport()
    {
        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("X,Aktiv"),
            s_places,
            new CsvImportOptions { Translations = s_collidingGerman },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task Import_AcceptsTheTranslationTheFixedTextAndTheMemberName()
    {
        // Arrange
        var valid = new Dictionary<string, string>
        {
            ["city"] = "Ort",
            ["status.Active"] = "Aktiv",
            ["status.Approved"] = "Genehmigt",
            ["status.Suspended"] = "Gesperrt",
        };

        // Act
        var report = await CsvInstance.ImportAsync(
            TestInput.Utf8("X,Aktiv\nX,Active\nX,active"),
            s_places,
            new CsvImportOptions { Translations = valid },
            Token);

        // Assert
        Assert.All(report.Items!, item => Assert.Equal(Status.Active, item.State));
    }
}
