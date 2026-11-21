namespace Doka.Tabular.Excel.Tests;

/// <summary>Reading workbooks saved by Excel itself; each test skips until its reference file is provided.</summary>
public sealed partial class ReferenceFileTests
{
    private static readonly ExcelFormat s_format = new(new ExcelFormatOptions());

    private static readonly TabularMap<BenutzerRow> s_benutzer = TabularMap
        .For<BenutzerRow>()
        .Column(x => x.Id, index: 0)
        .Column(x => x.Name, index: 1)
        .Column(x => x.Betrag, index: 2)
        .Column(x => x.Aktiv, index: 3)
        .Column(x => x.Geburtsdatum, index: 4)
        .Column(x => x.Uhrzeit, index: 5)
        .Column(x => x.Zeitpunkt, index: 6)
        .Column(x => x.Dauer, index: 7)
        .Column(x => x.Postleitzahl, index: 8)
        .Column(x => x.Kundennummer, index: 9)
        .Column(x => x.Bemerkung, index: 10)
        .Column(x => x.Formel, index: 11)
        .Build();

    private static readonly TabularMap<NamedUser> s_users = TabularMap
        .For<NamedUser>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Column(x => x.Name, index: 1, configure: c => c.Header("Name"))
        .Build();

    private static readonly TabularMap<ReportRow> s_reportRows = TabularMap
        .For<ReportRow>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Column(x => x.Name,
            index: 1,
            configure: c => c
                .Header("Name")
                .Required())
        .Column(x => x.Betrag, index: 2, configure: c => c.Header("Betrag"))
        .Column(x => x.Eintritt, index: 3, configure: c => c.Header("Eintritt"))
        .Build();

    private static readonly TabularMap<VisibilityRow> s_visibilityRows = TabularMap
        .For<VisibilityRow>()
        .Column(x => x.Id, index: 0)
        .Column(x => x.Gruppe, index: 1)
        .Column(x => x.Betrag, index: 2)
        .Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ExcelImportOptions Partial => new()
    {
        Strategy = ImportStrategy.Partial,
        Binding = ColumnBinding.ByIndexWithHeader,
    };

    /// <summary>Partial import options with the culture that reads the date texts of the set.</summary>
    private static ExcelImportOptions PartialFor(
        ReferenceSet set
    ) => Partial with { Culture = TextCulture(set) };

    // WHY: Excel for Mac with English system language keeps German input such as 01.04.1980 as a text cell; only the
    // culture of the data, not the format of the cell, says that it is the first of April.
    private static CultureInfo? TextCulture(
        ReferenceSet set
    ) => set switch
    {
        ReferenceSet.MacEnglish => CultureInfo.GetCultureInfo("de-DE"),
        ReferenceSet.WindowsGerman => null,
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, null),
    };

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R04_TwoSheets_WithoutName_IsAmbiguousAndNamesTheExistingSheets(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R04", "R04-two-sheets.xlsx");

        // Act
        var report = await s_format.ImportAsync(path, s_users, Partial, Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Schema.AmbiguousSheet, error.Code);
        Assert.Contains("Hinweise", error.Message, StringComparison.Ordinal);
        Assert.Contains("Benutzer", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R04_TwoSheets_ByName_ReadsTheNamedSheet(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R04", "R04-two-sheets.xlsx");

        // Act
        var report = await s_format.ImportAsync(path,
            s_users,
            Partial with { SheetName = TabularSheetName.Create("Benutzer") },
            Token);

        // Assert
        Assert.Equal([new NamedUser(1, "Müller"), new NamedUser(2, "Schmidt")], report.Items);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R04_TwoSheets_WithAMissingName_IsAMissingSheetThatNamesTheExistingSheets(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R04", "R04-two-sheets.xlsx");

        // Act
        var report = await s_format.ImportAsync(path,
            s_users,
            Partial with { SheetName = TabularSheetName.Create("Fehlt") },
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal(TabularErrorCodes.Schema.MissingSheet, error.Code);
        Assert.Contains("Benutzer", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R05_ReadRange_UsesStartRowEndRowAndSkipsEmptyRows(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R05", "R05-read-range.xlsx");

        // Act
        var report = await s_format.ImportAsync(path, s_reportRows, RangeOptions(set) with { EndRow = 80 }, Token);

        // Assert
        var first = report.Errors[0];
        Assert.Equal((5L, 1L), (first.Location.RowNumber!.Value, first.Location.RecordNumber!.Value));
        Assert.Contains(report.Errors, error => error.Location.RowNumber == 10);
        Assert.Contains(report.Errors, error => error.Location.RowNumber == 12);
        Assert.DoesNotContain(report.Errors, error => error.Location.RowNumber is 9 or 82);
        Assert.Null(report.Items!.Single(item => item.Id == 4)
            .Eintritt);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R05_ReadRange_WithoutEndRow_ReadsToTheLastRow(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R05", "R05-read-range.xlsx");

        // Act
        var report = await s_format.ImportAsync(path, s_reportRows, RangeOptions(set), Token);

        // Assert
        Assert.Contains(report.Errors, error => error.Location.RowNumber == 82);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R05_ReadRange_ByIndexWithHeader_ReportsTheSameRowsAsByHeaderName(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R05", "R05-read-range.xlsx");

        // Act
        // WHY: The equal result of both bindings is the checked contract, so both imports form one action.
        var byName = await s_format.ImportAsync(path, s_reportRows, RangeOptions(set) with { EndRow = 80 }, Token);
        var byIndex = await s_format.ImportAsync(path,
            s_reportRows,
            RangeOptions(set) with
            {
                Binding = ColumnBinding.ByIndexWithHeader,
                EndRow = 80,
            },
            Token);

        // Assert
        Assert.Equal(byName.Errors.Select(error => error.Location.RowNumber),
            byIndex.Errors.Select(error => error.Location.RowNumber));
    }

    private static ExcelImportOptions RangeOptions(
        ReferenceSet set
    ) => new()
    {
        Strategy = ImportStrategy.Partial,
        Binding = ColumnBinding.ByHeaderName,
        StartRow = 4,
        Culture = TextCulture(set),
    };

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R06_Visibility_ReadsAllRowsByDefault(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R06", "R06-visibility.xlsx");

        // Act
        var report = await s_format.ImportAsync(path, s_visibilityRows, Partial, Token);

        // Assert
        Assert.Equal((100L, 98), (report.RowsRead, report.Items!.Count));
        Assert.Equal([6L, 21L], report.Errors.Select(error => error.Location.RowNumber!.Value));
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R06_Visibility_VisibleRowsOnly_KeepsTheSheetRowNumbers(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R06", "R06-visibility.xlsx");

        // Act
        var report = await s_format.ImportAsync(path, s_visibilityRows, Partial with { VisibleRowsOnly = true }, Token);

        // Assert
        Assert.Equal((12L, 11), (report.RowsRead, report.Items!.Count));
        Assert.Equal([6L], report.Errors.Select(error => error.Location.RowNumber!.Value));
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R06_Visibility_StrictWithAnInvalidVisibleRow_Fails(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R06", "R06-visibility.xlsx");

        // Act
        var report = await s_format.ImportAsync(path,
            s_visibilityRows,
            Partial with
            {
                Strategy = ImportStrategy.Strict,
                VisibleRowsOnly = true,
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R07_DateSystem1904_GivesTheSameValuesAs1900(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R07", "R07-date-system-1904.xlsx");
        var map = TabularMap
            .For<DateRow>()
            .Column(x => x.Datum, index: 0)
            .Column(x => x.Uhrzeit, index: 1)
            .Column(x => x.Dauer, index: 2)
            .Build();

        // Act
        var report =
            await s_format.ImportAsync(path, map, PartialFor(set) with { Strategy = ImportStrategy.Strict }, Token);

        // Assert
        Assert.Equal(
            [
                new DateRow(new DateOnly(1904, 1, 1), new TimeOnly(12, 0), TimeSpan.FromHours(27)),
                new DateRow(new DateOnly(2024, 4, 1), TimeOnly.MinValue, TimeSpan.Zero),
            ],
            report.Items);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R08_Protected_IsReadLikeAnUnprotectedWorkbookAndStaysUnchanged(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R08", "R08-protected.xlsx");
        var before = await File.ReadAllBytesAsync(path, Token);

        // Act
        var report = await s_format.ImportAsync(path,
            s_users,
            Partial with { SheetName = TabularSheetName.Create("Benutzer") },
            Token);

        // Assert
        Assert.Equal([new NamedUser(1, "Müller"), new NamedUser(2, "Schmidt")], report.Items);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, Token));
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish)]
    [InlineData(ReferenceSet.WindowsGerman)]
    public async Task R09_TableObject_IsReadLikeARange(
        ReferenceSet set
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, "R09", "R09-table-object.xlsx");

        // Act
        var report =
            await s_format.ImportAsync(path, s_users, Partial with { Binding = ColumnBinding.ByHeaderName }, Token);

        // Assert
        Assert.Equal(["Müller", "Schmidt", "Weber", "Fischer"], report.Items!.Select(item => item.Name));
    }

    public static TheoryData<ReferenceSet, string, string, bool> UnsupportedFiles()
    {
        var data = new TheoryData<ReferenceSet, string, string, bool>();
        foreach (var set in new[] { ReferenceSet.MacEnglish, ReferenceSet.WindowsGerman })
        {
            foreach (var (number, fileName) in new[]
                {
                    ("R10", "R10-encrypted.xlsx"), ("R11", "R11-legacy.xls"), ("R12", "R12-binary.xlsb"),
                })
            {
                data.Add(set, number, fileName, false);
                data.Add(set, number, fileName, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnsupportedFiles))]
    public async Task UnsupportedFormats_AreRejectedAlsoThroughForwardOnlyStreams(
        ReferenceSet set,
        string number,
        string fileName,
        bool forwardOnly
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, number, fileName);
        Stream input = forwardOnly
            ? new ForwardOnlyStream(await ReferenceFiles.CopyAsync(path))
            : await ReferenceFiles.CopyAsync(path);

        // Act
        var report = await s_format.ImportAsync(input, s_users, Partial, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Schema.UnsupportedFormat,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(0, report.RowsRead);
    }

    [Theory]
    [InlineData(ReferenceSet.MacEnglish, "R10", "R10-encrypted.xlsx")]
    [InlineData(ReferenceSet.MacEnglish, "R11", "R11-legacy.xls")]
    [InlineData(ReferenceSet.MacEnglish, "R12", "R12-binary.xlsb")]
    [InlineData(ReferenceSet.WindowsGerman, "R10", "R10-encrypted.xlsx")]
    [InlineData(ReferenceSet.WindowsGerman, "R11", "R11-legacy.xls")]
    [InlineData(ReferenceSet.WindowsGerman, "R12", "R12-binary.xlsb")]
    public async Task UnsupportedFormats_AreRejectedFromAPath(
        ReferenceSet set,
        string number,
        string fileName
    )
    {
        // Arrange
        var path = ReferenceFiles.Path(set, number, fileName);

        // Act
        var report = await s_format.ImportAsync(path, s_users, Partial, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Schema.UnsupportedFormat,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(0, report.RowsRead);
    }

    public sealed record BenutzerRow(
        int Id,
        string Name,
        decimal? Betrag,
        bool? Aktiv,
        DateOnly? Geburtsdatum,
        TimeOnly? Uhrzeit,
        DateTime? Zeitpunkt,
        TimeSpan? Dauer,
        string Postleitzahl,
        string Kundennummer,
        string Bemerkung,
        string Formel
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record BenutzerValues(
        int Id,
        string Name,
        decimal? Betrag,
        bool? Aktiv,
        DateOnly? Geburtsdatum,
        TimeOnly? Uhrzeit,
        DateTime? Zeitpunkt,
        TimeSpan? Dauer,
        string Postleitzahl,
        string Kundennummer,
        string Bemerkung
    );

    public sealed record NamedUser(
        int Id,
        string Name
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record ReportRow(
        int Id,
        string Name,
        decimal Betrag,
        DateTime? Eintritt
    );

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    public sealed record VisibilityRow(
        int Id,
        string Gruppe,
        decimal Betrag
    );

    public sealed record DateRow(
        DateOnly Datum,
        TimeOnly Uhrzeit,
        TimeSpan Dauer
    );
}
