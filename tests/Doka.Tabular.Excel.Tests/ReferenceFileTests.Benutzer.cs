namespace Doka.Tabular.Excel.Tests;

public sealed partial class ReferenceFileTests
{
    private static readonly TabularMap<BenutzerValues> s_benutzerValues = TabularMap
        .For<BenutzerValues>()
        .Column(x => x.Id, index: 0)
        .Column(x => x.Name, index: 1)
        .Column(x => x.Betrag, index: 2)
        .Column(x => x.Aktiv, index: 3)
        .Column(x => x.Geburtsdatum, index: 4, configure: c => c.AllowExcelSerialNumbers())
        .Column(x => x.Uhrzeit, index: 5, configure: c => c.AllowExcelSerialNumbers())
        .Column(x => x.Zeitpunkt, index: 6)
        .Column(x => x.Dauer, index: 7)
        .Column(x => x.Postleitzahl, index: 8)
        .Column(x => x.Kundennummer, index: 9)
        .Column(x => x.Bemerkung, index: 10)
        .Build();

    public static TheoryData<ReferenceSet, string, string> BenutzerFiles()
    {
        var data = new TheoryData<ReferenceSet, string, string>();
        foreach (var set in new[] { ReferenceSet.MacEnglish, ReferenceSet.WindowsGerman })
        {
            data.Add(set, "R01", "R01-transitional.xlsx");
            data.Add(set, "R02", "R02-strict.xlsx");
            data.Add(set, "R03", "R03-macro.xlsm");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BenutzerFiles))]
    public async Task R01ToR03_Benutzer_ReadsEveryCellTypeWithTheSameDiagnostics(
        ReferenceSet set,
        string number,
        string fileName
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, number, fileName);

        // Act
        var report = await s_format.ImportAsync(path, s_benutzer, PartialFor(set), Token);

        // Assert
        Assert.Equal(ReportStatus.CompletedWithErrors, report.Status);
        Assert.Equal(5, report.RowsRead);
        Assert.Equal(
            [
                (3L, 11, TabularErrorCodes.Conversion.UnsupportedValue),
                (6L, 11, TabularErrorCodes.Conversion.UnsupportedValue),
                (7L, 4, TabularErrorCodes.Conversion.InvalidFormat),
                (7L, 5, TabularErrorCodes.Conversion.InvalidFormat),
            ],
            report.Errors.Select(error
                => (error.Location.RowNumber!.Value, error.Location.ColumnIndex!.Value, error.Code)));

        AssertTypedRows(report);
    }

    [Theory]
    [MemberData(nameof(BenutzerFiles))]
    public async Task R01ToR03_Benutzer_ReadsTheSameValues(
        ReferenceSet set,
        string number,
        string fileName
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, number, fileName);

        // Act
        var report = await s_format.ImportAsync(path, s_benutzerValues, PartialFor(set), Token);

        // Assert
        AssertValues(report);
    }

    [Theory]
    [InlineData("R01", "R01-transitional.xlsx")]
    [InlineData("R02", "R02-strict.xlsx")]
    [InlineData("R03", "R03-macro.xlsm")]
    public async Task R01ToR03_MacEnglishWithoutTheDataCulture_RejectsTheGermanDateText(
        string number,
        string fileName
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(ReferenceSet.MacEnglish, number, fileName);

        // Act
        var report = await s_format.ImportAsync(path, s_benutzer, Partial, Token);

        // Assert
        Assert.Contains(
            report.Errors,
            error => error is { Location: { RowNumber: 2, ColumnIndex: 4 } }
                && error.Code == TabularErrorCodes.Conversion.InvalidFormat);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R03_Macro_LeavesTheFileUnchanged(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R03", "R03-macro.xlsm");
        var before = await File.ReadAllBytesAsync(path, Token);

        // Act
        await s_format.ImportAsync(path, s_benutzer, PartialFor(set), Token);

        // Assert
        Assert.Equal(before, await File.ReadAllBytesAsync(path, Token));
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R03_Macro_WithAnExpectedXlsx_IsAFormatMismatch(
        ReferenceSet set
    )
    {
        // Arrange
        var file = await ReferenceFiles.CopyAsync(ReferenceFiles.Path(set, "R03", "R03-macro.xlsm"));

        // Act
        var report = await s_format.ImportAsync(
            file,
            s_users,
            Partial with { ExpectedVariant = ExcelVariant.Xlsx },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.FormatMismatch,
            Assert.Single(report.Errors)
                .Code);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R03_Macro_NeverRuns(
        ReferenceSet set
    )
    {
        // Arrange
        // WHY: The macro of the file would write into cell Z1 when it ran.
        var file = await ReferenceFiles.CopyAsync(ReferenceFiles.Path(set, "R03", "R03-macro.xlsm"));
        var map = TabularMap
            .For<NamedUser>()
            .Column(x => x.Id, index: 0)
            .Column(x => x.Name, index: 25)
            .Build();

        // Act
        var report = await s_format.ImportAsync(
            file,
            map,
            Partial with
            {
                EndRow = 1,
                Binding = ColumnBinding.ByIndex,
            },
            Token);

        // Assert
        Assert.All(report.Items ?? [], item => Assert.Equal("", item.Name));
    }

    private static void AssertTypedRows(
        ImportReport<BenutzerRow> report
    )
    {
        var row2 = report.Items!.Single(item => item.Id == 1);
        Assert.Equal(
            new BenutzerRow(
                1,
                "Müller",
                12.5m,
                true,
                new DateOnly(1980, 4, 1),
                new TimeOnly(14, 30),
                new DateTime(2024, 4, 1, 8, 30, 0),
                TimeSpan.FromHours(27),
                "01234",
                "12345",
                "Straße",
                "25"),
            row2);

        var row4 = report.Items!.Single(item => item.Id == 3);
        Assert.Equal(
            new BenutzerRow(
                3,
                "Müller",
                -0.1m,
                null,
                null,
                null,
                null,
                null,
                "",
                "",
                "_x0041_",
                "=1+1"),
            row4);
    }

    private static void AssertValues(
        ImportReport<BenutzerValues> values
    )
    {
        var all = values.Items!.ToDictionary(item => item.Id);
        Assert.Equal(
            ("Ærøskøbing Ödön 😀", 1000000000000000m, false, new DateOnly(2000, 2, 29), "00000",
                "1234567890123456"),
            (all[2].Name, all[2].Betrag!.Value, all[2].Aktiv!.Value, all[2].Geburtsdatum!.Value, all[2].Postleitzahl,
                all[2].Kundennummer));

        Assert.Equal(
            ("Zeile 1\nZeile 2", new DateTime(1900, 3, 1), TimeSpan.Zero),
            (all[2].Bemerkung, all[2].Zeitpunkt!.Value, all[2].Dauer!.Value));

        Assert.Equal(
            (" Leerzeichen vorn und hinten ", new DateOnly(9999, 12, 31), TimeSpan.FromDays(60), "92991000"),
            (all[4].Name, all[4].Geburtsdatum!.Value, all[4].Dauer!.Value, all[4].Kundennummer));

        Assert.Equal(
            ("Formeltext", true, new DateOnly(2020, 6, 18), new TimeOnly(12, 0),
                new DateTime(
                    2024,
                    4,
                    1,
                    12,
                    30,
                    0,
                    DateTimeKind.Utc), TimeSpan.FromHours(-12), "<tag>&amp;\"'"),
            (all[5].Name, all[5].Aktiv!.Value, all[5].Geburtsdatum!.Value, all[5].Uhrzeit!.Value,
                all[5].Zeitpunkt!.Value, all[5].Dauer!.Value, all[5].Bemerkung));
    }
}
