using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

public sealed class SmokeTests
{
    private static readonly TabularMap<Contact> s_map = TabularMap
        .For<Contact>()
        .Column(x => x.Name, index: 0, configure: c => c.Header("Name"))
        .Column(x => x.Count, index: 1, configure: c => c.Header("Anzahl"))
        .Column(
            x => x.Amount,
            index: 2,
            configure: c => c
                .Header("Betrag")
                .ExcelNumberFormat("#,##0.00"))
        .Column(
            x => x.Since,
            index: 3,
            configure: c => c
                .Header("Seit")
                .ExcelNumberFormat("dd.mm.yyyy"))
        .Column(x => x.Active, index: 4, configure: c => c.Header("Aktiv"))
        .Column(x => x.PostalCode, index: 5, configure: c => c.Header("PLZ"))
        .Build();

    private static readonly Contact[] s_rows =
    [
        new("Müller _x0041_\r\nZeile", 2, 12.5m, new DateOnly(2024, 4, 1), true, "01067"),
        new("Ærøskøbing 😀", 10, 0.1m, new DateOnly(2024, 5, 31), false, "80331"),
    ];

    [Theory]
    [InlineData(ExcelLayoutMode.Range)]
    [InlineData(ExcelLayoutMode.Table)]
    public async Task ExportAsync_ThenImportAsync_RoundTripsValuesAndPassesSchemaValidation(
        ExcelLayoutMode mode
    )
    {
        // Arrange
        var format = new CheckedExcelFormat(new ExcelFormat(new ExcelFormatOptions()));
        var options = new ExcelExportOptions
        {
            WriteHeader = true,
            Layout = new ExcelLayoutOptions
            {
                Mode = mode,
                HeaderBold = true,
                HeaderFill = ExcelColor.Create("#3A5A8C"),
            },
        };

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (report, file) = await format.ExportAsync(s_rows, s_map, options, TestContext.Current.CancellationToken);
        await using var owned = file;
        var reimport = await format.ImportAsync(
            file!,
            s_map,
            new ExcelImportOptions { Binding = ColumnBinding.ByHeaderName },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            report.Status == ReportStatus.Succeeded,
            string.Join(
                " | ",
                report.Errors.Select(error => $"{error.Code}: {error.Message} {error.Location.ColumnKey}")));

        Assert.Empty(WorkbookProblems(file!));
        Assert.Equal(ReportStatus.Succeeded, reimport.Status);
        Assert.Equal(s_rows, reimport.Items);
    }

    private sealed record Contact(
        string Name,
        int Count,
        decimal Amount,
        DateOnly Since,
        bool Active,
        string PostalCode
    );
}
