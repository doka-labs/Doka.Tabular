using System.Text.Json;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

public sealed class ReportValueJsonTests
{
    private static readonly TabularMap<Entry> s_entries = TabularMap
        .For<Entry>()
        .Column(x => x.Name, index: 0)
        .Column(x => x.Count, index: 1)
        .Build();

    [Fact]
    public void Values_RoundTripAsJsonStrings()
    {
        // Arrange
        var values = new ReportValues(
            TabularErrorCodes.Conversion.OutOfRange,
            TabularErrorCode.Create("contact.invalid"),
            TabularSheetName.Create("Contacts"),
            TabularPropertyPath.FromText("Address.PostalCode"));

        var json = JsonSerializer.Serialize(values);
        var read = JsonSerializer.Deserialize<ReportValues>(json)!;

        // Act
        const string expected = """{"LibraryCode":"tabular.conversion.outOfRange","OwnCode":"contact.invalid","""
            + "\"SheetName\":\"Contacts\",\"PropertyPath\":\"Address.PostalCode\"}";

        // Assert
        Assert.Equal(expected, json);

        Assert.Equal(values, read);
    }

    [Theory]
    [InlineData("""{"LibraryCode":"two words"}""", "$.LibraryCode")]
    [InlineData("""{"SheetName":"History"}""", "$.SheetName")]
    [InlineData("""{"SheetName":"a:b"}""", "$.SheetName")]
    [InlineData("""{"PropertyPath":""}""", "$.PropertyPath")]
    [InlineData("""{"PropertyPath":" Name"}""", "$.PropertyPath")]
    [InlineData("""{"OwnCode":7}""", "$.OwnCode")]
    public void Values_RejectTextsTheTypesReject(
        string json,
        string path
    )
    {
        // Act
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ReportValues>(json));

        // Assert
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportErrors_SerializeCodeAndLocationAsStrings()
    {
        // Arrange
        var file = Create(TabularType.Excel, ["Ada", "many"]);
        var report = await Import.ImportAsync(file, TabularType.Excel, s_entries, Token);
        var error = report.Errors[0];

        // Act
        var text = JsonSerializer.Serialize(error);

        // Assert
        Assert.Single(report.Errors);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var location = root.GetProperty("Location");

        Assert.Equal(
            error.Code.Value,
            root
                .GetProperty("Code")
                .GetString());

        Assert.Equal(
            "Sheet1",
            location
                .GetProperty("SheetName")
                .GetString());

        Assert.Equal(
            "Count",
            location
                .GetProperty("PropertyPath")
                .GetString());

        Assert.Equal(
            "count",
            location
                .GetProperty("ColumnKey")
                .GetString(),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ReportValues(
        TabularErrorCode? LibraryCode,
        TabularErrorCode? OwnCode,
        TabularSheetName? SheetName,
        TabularPropertyPath? PropertyPath
    );

    // WHY: Only the import creates entries, through the constructor the map binds by reflection.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed record Entry(
        string Name,
        int Count
    );
}
