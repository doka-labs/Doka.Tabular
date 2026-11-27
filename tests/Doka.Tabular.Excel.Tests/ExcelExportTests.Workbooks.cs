using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

public sealed partial class ExcelExportTests
{
    [Fact]
    public async Task SeveralSheets_ExportIndependently()
    {
        // Arrange
        var workbook = ExcelExport
            .Create()
            .Add(
                TabularSheetName.Create("Kunden"),
                new[] { new Customer(1, "Ada") },
                s_customers,
                new ExcelSheetExportOptions
                {
                    WriteHeader = true,
                    ColumnKeys = [ColumnKey.Create("name")],
                })
            .Add(
                TabularSheetName.Create("Bestellungen"),
                Async(),
                s_orders,
                new ExcelSheetExportOptions { WriteHeader = true })
            .Build();

        // Act
        var (report, file) = await ExcelInstance.ExportAsync(workbook, Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, 2L), (report.Status, report.RowsWritten));
        Assert.Equal([["Name"], ["Ada"]], Cells(TabularType.Excel, file!, "Kunden"));
        Assert.Equal([["Nummer", "Summe"], ["7", "12.5"]], Cells(TabularType.Excel, file!, "Bestellungen"));

        return;

        static async IAsyncEnumerable<Order> Async()
        {
            await Task.Yield();

            yield return new Order(7, 12.5m);
        }
    }

    [Fact]
    public async Task LaterSheetFailure_KeepsTheExistingFile()
    {
        // Arrange
        var path = Path.Combine(_directory, "book.xlsx");
        await File.WriteAllTextAsync(path, "previous", Token);

        // Act
        var report = await ExcelInstance.ExportAsync(FailingWorkbook(), path, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal("previous", await File.ReadAllTextAsync(path, Token));
    }

    [Fact]
    public async Task LaterSheetFailure_InAPassedStream_IsPartialOutput()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var report = await ExcelInstance.ExportAsync(FailingWorkbook(), stream, Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, StreamOutputState.Partial), (report.Status, report.StreamOutput));
    }

    /// <summary>A workbook whose second sheet fails at its only record, after the first sheet was written.</summary>
    private static ExcelWorkbookExport FailingWorkbook()
    {
        var failing = TabularMap
            .For<Order>()
            .Column(x => x.Number, index: 0)
            .Column(
                x => x.Total,
                index: 1,
                configure: c => c.Convert<string, decimal>(
                    read: decimal.Parse,
                    write: total => total < 0
                        ? throw new InvalidOperationException()
                        : total.ToString(CultureInfo.InvariantCulture)))
            .Build();

        return ExcelExport
            .Create()
            .Add(TabularSheetName.Create("Kunden"), new[] { new Customer(1, "Ada") }, s_customers)
            .Add(TabularSheetName.Create("Bestellungen"), new[] { new Order(1, -1) }, failing)
            .Build();
    }

    [Fact]
    public async Task SeveralSheets_InvalidSecondDefinitionWritesNothing()
    {
        // Arrange
        var target = new MemoryStream();
        var workbook = ExcelExport
            .Create()
            .Add(TabularSheetName.Create("Kunden"), new[] { new Customer(1, "Ada") }, s_customers)
            .Add(TabularSheetName.Create("KUNDEN"), new[] { new Customer(2, "Bob") }, s_customers)
            .Build();

        // Act
        var report = await ExcelInstance.ExportAsync(workbook, target, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(0, target.Length);
    }

    [Fact]
    public async Task SeveralSheets_ImportInConfiguredOrderWithTypedResults()
    {
        // Arrange
        var file = Workbook(
            ("Hinweise", [["Text"]]),
            ("Bestellungen", [["Nummer", "Summe"], [7, 12.5]]),
            ("Kunden", [["Id", "Name"], [1, "Ada"], [2, "Bob"]]));

        var definition = ExcelImport.Create();
        var customers = definition.Add(
            TabularSheetName.Create("Kunden"),
            s_customers,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByHeaderName });

        var orders = definition.Add(
            TabularSheetName.Create("Bestellungen"),
            s_orders,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByIndexWithHeader });

        // Act
        var report = await ExcelInstance.ImportAsync(new ForwardOnlyStream(file), definition.Build(), Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(
            ["Ada", "Bob"],
            report.Get(customers)
                .Items!.Select(item => item.Name));

        Assert.Equal(
            new Order(7, 12.5m),
            Assert.Single(
                report.Get(orders)
                    .Items!));

        Assert.Equal(3, report.RowsImported);
    }

    [Fact]
    public async Task SheetHandleOfAnotherDefinition_IsRejected()
    {
        // Arrange
        var file = Workbook(("Kunden", [["Id", "Name"], [1, "Ada"]]));
        var definition = ExcelImport.Create();
        var own = definition.Add(
            TabularSheetName.Create("Kunden"),
            s_customers,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByIndexWithHeader });

        var other = ExcelImport.Create();
        var foreign = other.Add(TabularSheetName.Create("Andere"), s_customers);

        // Act
        var report = await ExcelInstance.ImportAsync(file, definition.Build(), Token);

        // Assert
        Assert.Equal(
            ReportStatus.Succeeded,
            report.Get(own)
                .Status);

        Assert.Throws<ArgumentException>(() => report.Get(foreign));
    }

    [Fact]
    public async Task SeveralSheets_StrictFailureFailsEveryCheckedSheetAndLeavesLaterOnesNotStarted()
    {
        // Arrange
        var file = Workbook(
            ("Kunden", [["Id", "Name"], [1, "Ada"]]),
            ("Bestellungen", [["Nummer", "Summe"], ["x", 1]]),
            ("Rest", [["Id", "Name"], [3, "Cy"]]));

        var definition = ExcelImport.Create();
        var customers = definition.Add(
            TabularSheetName.Create("Kunden"),
            s_customers,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByIndexWithHeader });

        var orders = definition.Add(
            TabularSheetName.Create("Bestellungen"),
            s_orders,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByIndexWithHeader });

        var rest = definition.Add(
            TabularSheetName.Create("Rest"),
            s_customers,
            new ExcelSheetImportOptions { Binding = ColumnBinding.ByIndexWithHeader });

        // Act
        var report = await ExcelInstance.ImportAsync(file, definition.Build(), Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(
            (ReportStatus.Failed, ReportStatus.Failed, ReportStatus.NotStarted),
            (report.Get(customers)
                .Status, report.Get(orders)
                .Status, report.Get(rest)
                .Status));

        Assert.Empty(
            report.Get(customers)
                .Items!);

        Assert.Equal(0, report.RowsImported);
        Assert.Equal(
            "Bestellungen",
            Assert.Single(report.Errors)
                .Location.SheetName?.Value);
    }

    [Fact]
    public async Task MissingSheet_IsASchemaError()
    {
        // Arrange
        var file = Workbook(("Kunden", [["Id", "Name"], [1, "Ada"]]));
        var missing = ExcelImport.Create();
        missing.Add(TabularSheetName.Create("Fehlt"), s_customers);

        // Act
        var report = await ExcelInstance.ImportAsync(file, missing.Build(), Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Schema.MissingSheet,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task SeveralSheets_SameMapWithOwnTranslationsDoNotMix()
    {
        // Arrange
        var map = TabularMap
            .For<Customer>()
            .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
            .Column(
                x => x.Name,
                index: 1,
                configure: c => c
                    .Header("Name")
                    .HeaderTranslationKey("name"))
            .Build();

        var workbook = ExcelExport
            .Create()
            .Add(
                TabularSheetName.Create("Deutsch"),
                new[] { new Customer(1, "Ada") },
                map,
                new ExcelSheetExportOptions
                {
                    WriteHeader = true,
                    Translations = new Dictionary<string, string> { ["name"] = "Name" },
                })
            .Add(
                TabularSheetName.Create("English"),
                new[] { new Customer(1, "Ada") },
                map,
                new ExcelSheetExportOptions
                {
                    WriteHeader = true,
                    Translations = new Dictionary<string, string> { ["name"] = "Full name" },
                })
            .Build();

        // Act
        var (_, file) = await ExcelInstance.ExportAsync(workbook, Token);

        // Assert
        Assert.Equal("Name", Cells(TabularType.Excel, file!, "Deutsch")[0][1]);
        Assert.Equal("Full name", Cells(TabularType.Excel, file!, "English")[0][1]);
    }
}
