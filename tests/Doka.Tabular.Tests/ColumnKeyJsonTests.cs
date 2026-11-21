using System.Text.Json;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed class ColumnKeyJsonTests
{
    private static readonly TabularMap<Person> s_people = TabularMap
        .For<Person>()
        .Column(x => x.PersonnelNumber, index: 0, key: ColumnKey.Create("personnelNumber"))
        .Column(x => x.Email, index: 1, key: ColumnKey.Create("email"))
        .Column(x => x.City, index: 2, key: ColumnKey.Create("city"))
        .Build();

    private static readonly Dictionary<ColumnKey, int> s_widths = new()
    {
        [ColumnKey.Create("email")] = 30,
        [ColumnKey.Create("city")] = 12,
    };

    [Fact]
    public void Deserialize_ReadsKeysFromJsonStrings()
    {
        // Act
        var request = JsonSerializer.Deserialize<ExportRequest>("""{"Columns":["personnelNumber","Email"]}""")!;

        // Assert
        Assert.Equal([ColumnKey.Create("personnelNumber"), ColumnKey.Create("email")], request.Columns!);
        Assert.Equal("Email", request.Columns![1].Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Columns":null}""")]
    public void Deserialize_KeepsAMissingAndANullSelection(
        string json
    )
    {
        // Act
        var request = JsonSerializer.Deserialize<ExportRequest>(json)!;

        // Assert
        Assert.Null(request.Columns);
    }

    [Fact]
    public void Serialize_WritesKeysAsStrings()
    {
        // Arrange
        var request = new ExportRequest([ColumnKey.Create("personnelNumber"), ColumnKey.Create("email")]);

        // Act
        var json = JsonSerializer.Serialize(request);

        // Assert
        Assert.Equal("""{"Columns":["personnelNumber","email"]}""", json);
    }

    [Theory]
    [InlineData("""{"Columns":[""]}""")]
    [InlineData("""{"Columns":[" email"]}""")]
    [InlineData("""{"Columns":["email "]}""")]
    public void Deserialize_RejectsTextThatIsNoKey(
        string json
    )
    {
        // Act
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExportRequest>(json));

        // Assert
        Assert.Contains("$.Columns[0]", exception.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Theory]
    [InlineData("""{"Columns":[1]}""")]
    [InlineData("""{"Columns":[true]}""")]
    [InlineData("""{"Columns":[{"Value":"email"}]}""")]
    public void Deserialize_RejectsValuesThatAreNoString(
        string json
    )
    {
        // Act
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExportRequest>(json));

        // Assert
        Assert.Contains("$.Columns[0]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DictionaryKeys_AreWrittenAsPropertyNames()
    {
        // Act
        var json = JsonSerializer.Serialize(s_widths);

        // Assert
        Assert.Equal("""{"email":30,"city":12}""", json);
    }

    [Fact]
    public void DictionaryKeys_AreReadFromPropertyNamesIgnoringCase()
    {
        // Act
        var read = JsonSerializer.Deserialize<Dictionary<ColumnKey, int>>("""{"email":30,"city":12}""")!;

        // Assert
        Assert.Equal(30, read[ColumnKey.Create("EMAIL")]);
    }

    [Fact]
    public void DictionaryKey_ThatIsNoValidColumnKey_IsAJsonError()
    {
        // Act
        var exception =
            Record.Exception(() => JsonSerializer.Deserialize<Dictionary<ColumnKey, int>>("""{" city":1}"""));

        // Assert
        Assert.IsType<JsonException>(exception);
    }

    [Fact]
    public async Task Export_UsesASelectionReadFromJson()
    {
        // Arrange
        var request = JsonSerializer.Deserialize<ExportRequest>("""{"Columns":["email","personnelNumber"]}""")!;
        var people = new[] { new Person("P-7", "ada@example.org", "Halle") };

        // Act
        var (report, file) = await CsvInstance.ExportAsync(people,
            s_people,
            new CsvExportOptions { ColumnKeys = request.Columns },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal([["ada@example.org", "P-7"]], Cells(TabularType.Csv, file!));
    }

    [Fact]
    public async Task Export_ReportsANullEntryOfASelectionReadFromJson()
    {
        // Arrange
        var request = JsonSerializer.Deserialize<ExportRequest>("""{"Columns":["email",null]}""")!;
        var target = new MemoryStream();

        // Act
        var report = await CsvInstance.ExportAsync(new[] { new Person("P-7", "ada@example.org", "Halle") },
            s_people,
            target,
            new CsvExportOptions { ColumnKeys = request.Columns },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
        Assert.Equal(0, target.Length);
    }

    public sealed record ExportRequest(
        IReadOnlyList<ColumnKey>? Columns
    );

    private sealed record Person(
        string PersonnelNumber,
        string Email,
        string City
    );
}
