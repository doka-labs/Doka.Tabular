using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// Excel stores no cell for an empty field, and the import skips rows without cells. A record whose fields are all
/// empty would be lost without a message, so its Excel export is a data error; CSV keeps such records.
/// </summary>
public sealed class EmptyRecordTests
{
    private static readonly TabularMap<Single> s_single = TabularMap
        .For<Single>()
        .Column(x => x.Amount, index: 0)
        .Build();

    private static readonly TabularMap<Keyed> s_keyed = TabularMap
        .For<Keyed>()
        .Column(x => x.Key, index: 0)
        .Column(x => x.Amount, index: 1)
        .Build();

    [Fact]
    public async Task RecordWithOnlyEmptyFields_IsADataErrorOfTheExcelExport()
    {
        // Act
        var report = await ExcelInstance.Format.ExportAsync(new[] { new Single(1), new Single(null) },
            s_single,
            new MemoryStream(),
            Token);

        // Assert
        var error = Assert.Single(report.Errors);
        Assert.Equal((ReportStatus.Failed, TabularErrorCodes.Conversion.MissingRepresentation, 2L),
            (report.Status, error.Code, error.Location.RecordNumber));
    }

    [Fact]
    public async Task RecordWithOnlyEmptyFields_IsADataErrorInAWorkbook()
    {
        // Arrange
        var sheets = ExcelExport.Create();
        sheets.Add(TabularSheetName.Create("Sheet1"), new[] { new Single(null) }, s_single);

        // Act
        var report = await ExcelInstance.Format.ExportAsync(sheets.Build(), new MemoryStream(), Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.MissingRepresentation,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task RecordWithOnlyEmptyFields_IsADataErrorOfTheFormatNeutralExportWithoutFile()
    {
        // Act
        var (report, file) = await Export.ExportAsync(new[] { new Single(null) },
            s_single,
            TabularType.Excel,
            new TabularExportOptions { WriteHeader = true },
            Token);

        // Assert
        Assert.Null(file);
        Assert.Equal(TabularErrorCodes.Conversion.MissingRepresentation,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task RecordWithOnlyEmptyFields_ReadsBackFromCsv()
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) = await Export.ExportAsync(new[] { new Single(1), new Single(null) },
            s_single,
            TabularType.Csv,
            Token);

        var report = await Import.ImportAsync(file!, TabularType.Csv, s_single, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        Assert.Equal([new Single(1), new Single(null)], report.Items!);
    }

    [Fact]
    public async Task EmptySource_ExportsToExcel()
    {
        // Act
        var (report, file) = await Export.ExportAsync(Array.Empty<Single>(),
            s_single,
            TabularType.Excel,
            new TabularExportOptions { WriteHeader = true },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.NotNull(file);
    }

    [Fact]
    public async Task RecordsWithOneValue_ExportToExcelAndReadBack()
    {
        // Arrange
        Keyed[] rows = [new("a", null), new("b", 2)];

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) = await Export.ExportAsync(rows, s_keyed, TabularType.Excel, Token);
        var report = await Import.ImportAsync(file!, TabularType.Excel, s_keyed, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, export.Status);
        Assert.Equal(rows, report.Items!);
    }

    private sealed record Single(
        int? Amount
    );

    private sealed record Keyed(
        string Key,
        int? Amount
    );
}
