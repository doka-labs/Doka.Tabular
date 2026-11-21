using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

/// <summary>Cell values, sheets and position, range and table mode, formatting, freeze and filter, widths, limits, and several sheets.</summary>
public sealed partial class ExcelExportTests : IDisposable
{
    private static readonly TabularMap<Booking> s_bookings = TabularMap
        .For<Booking>()
        .Column(x => x.Amount,
            index: 0,
            key: ColumnKey.Create("amount"),
            configure: c => c
                .Header("Betrag")
                .ExcelNumberFormat("#,##0.00 [$EUR];[Red]-#,##0.00 [$EUR]"))
        .Column(x => x.At,
            index: 1,
            key: ColumnKey.Create("at"),
            configure: c => c
                .Header("Zeitpunkt")
                .ExcelNumberFormat("dd.mm.yyyy \" - \" hh:mm:ss"))
        .Column(x => x.PostalCode, index: 2, key: ColumnKey.Create("postalCode"), configure: c => c.Header("PLZ"))
        .Build();

    private static readonly TabularMap<Customer> s_customers = TabularMap
        .For<Customer>()
        .Column(x => x.Id, index: 0, key: ColumnKey.Create("id"), configure: c => c.Header("Id"))
        .Column(x => x.Name, index: 1, key: ColumnKey.Create("name"), configure: c => c.Header("Name"))
        .Build();

    private static readonly TabularMap<Order> s_orders = TabularMap
        .For<Order>()
        .Column(x => x.Number, index: 0, configure: c => c.Header("Nummer"))
        .Column(x => x.Total, index: 1, configure: c => c.Header("Summe"))
        .Build();

    private static readonly Booking[] s_rows =
    [
        new(2m, new DateTime(2024, 1, 31, 23, 0, 0), "01067"),
        new(10m, new DateTime(2024, 2, 1, 8, 30, 0), "80331"),
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("tabular-excel-")
        .FullName;

    public static TheoryData<ExcelLayoutOptions, bool> InvalidLayouts => new()
    {
        { new ExcelLayoutOptions { Mode = ExcelLayoutMode.Table }, false },
        { new ExcelLayoutOptions { TableName = ExcelTableName.Create("Buchungen") }, true },
        { new ExcelLayoutOptions { TableStyle = ExcelTableStyle.Medium2 }, true },
        { new ExcelLayoutOptions { Filter = ExcelHeaderFeature.Enabled }, false },
        { new ExcelLayoutOptions { FreezeHeader = ExcelHeaderFeature.Enabled }, false },
        { new ExcelLayoutOptions { FrozenColumns = -1 }, true },
        { new ExcelLayoutOptions { FrozenColumns = 4 }, true },
        { new ExcelLayoutOptions { HeaderHeight = 410 }, true },
        { new ExcelLayoutOptions { MaximumAutoWidth = 256 }, true },
        {
            new ExcelLayoutOptions
            {
                Columns = new Dictionary<ColumnKey, ExcelColumnLayout>
                {
                    [ColumnKey.Create("unknown")] = new() { Width = 10 },
                },
            },
            true
        },
        {
            new ExcelLayoutOptions
            {
                Columns = new Dictionary<ColumnKey, ExcelColumnLayout>
                {
                    [ColumnKey.Create("amount")] = new() { Width = 0 },
                },
            },
            true
        },
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Range_WritesNativeValuesFormatsFilterAndFrozenHeader()
    {
        // Act
        var (report, file) =
            await ExcelInstance.ExportAsync(s_rows, s_bookings, new ExcelExportOptions { WriteHeader = true }, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        using var document = OpenWorkbook(file!);
        var sheet = Single(document);
        Assert.Equal("Export",
            document.WorkbookPart!.Workbook!.Sheets!
                .Elements<Sheet>()
                .Single()
                .Name!.Value);

        Assert.Equal("A1:C3", sheet.Worksheet!.GetFirstChild<AutoFilter>()!.Reference!.Value);
        var pane = sheet
            .Worksheet
            .Descendants<Pane>()
            .Single();

        Assert.Equal((1d, PaneStateValues.Frozen), (pane.VerticalSplit!.Value, pane.State!.Value));
        Assert.Equal(["n", "n", "inlineStr"], CellTypes(file!, 2));
        Assert.Equal(["2", "10"],
            Cells(TabularType.Excel, file!)
                .Skip(1)
                .Select(row => row[0]));

        Assert.Equal(["01067", "80331"],
            Cells(TabularType.Excel, file!)
                .Skip(1)
                .Select(row => row[2]));

        var formats = document.WorkbookPart.WorkbookStylesPart!.Stylesheet!.NumberingFormats!
            .Elements<NumberingFormat>()
            .Select(format => format.FormatCode!.Value)
            .ToList();

        Assert.Contains("#,##0.00 [$EUR];[Red]-#,##0.00 [$EUR]", formats);
        Assert.Contains("dd.mm.yyyy \" - \" hh:mm:ss", formats);
    }

    [Fact]
    public async Task Table_UsesTheDefaultStyleAndAutomaticNames()
    {
        // Arrange
        var workbook = ExcelExport
            .Create()
            .Add(TabularSheetName.Create("Buchungen"),
                s_rows,
                s_bookings,
                new ExcelSheetExportOptions
                {
                    WriteHeader = true,
                    Layout = new ExcelLayoutOptions { Mode = ExcelLayoutMode.Table },
                })
            .Add(TabularSheetName.Create("Kunden"),
                new[] { new Customer(1, "Ada") },
                s_customers,
                new ExcelSheetExportOptions
                {
                    WriteHeader = true,
                    Layout = new ExcelLayoutOptions { Mode = ExcelLayoutMode.Table },
                })
            .Build();

        // Act
        var (report, file) = await ExcelInstance.ExportAsync(workbook, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        using var document = OpenWorkbook(file!);
        var tables = document.WorkbookPart!
            .WorksheetParts
            .SelectMany(part => part.TableDefinitionParts)
            .Select(part => part.Table!)
            .OrderBy(table => table.Id!.Value)
            .ToList();

        Assert.Equal(["TabularExport", "TabularExport_2"], tables.Select(table => table.Name!.Value));
        Assert.All(tables,
            table => Assert.Equal(("TableStyleMedium2", true),
                (table.TableStyleInfo!.Name!.Value, table.TableStyleInfo.ShowRowStripes!.Value)));

        Assert.Equal(["Buchungen", "Kunden"],
            document.WorkbookPart.Workbook!.Sheets!
                .Elements<Sheet>()
                .Select(sheet => sheet.Name!.Value));
    }

    [Fact]
    public async Task HeaderStyleAndColumnLayout_AreWrittenAsConfigured()
    {
        // Arrange
        var layout = new ExcelLayoutOptions
        {
            HeaderFill = ExcelColor.Create("#3A5A8C"),
            HeaderFontColor = ExcelColor.Create("#FFFFFF"),
            HeaderBold = true,
            HeaderVerticalAlignment = ExcelVerticalAlignment.Center,
            HeaderHeight = 22,
            Columns = new Dictionary<ColumnKey, ExcelColumnLayout>
            {
                [ColumnKey.Create("POSTALCODE")] = new()
                {
                    Width = 16,
                    HorizontalAlignment = ExcelHorizontalAlignment.Right,
                },
            },
        };

        // Act
        var (_, file) = await ExcelInstance.ExportAsync(s_rows,
            s_bookings,
            new ExcelExportOptions
            {
                WriteHeader = true,
                Layout = layout,
            },
            Token);

        // Assert
        using var document = OpenWorkbook(file!);
        var sheet = Single(document);
        var header = sheet.Worksheet!
            .Descendants<Row>()
            .First();

        Assert.Equal(22d, header.Height!.Value);
        var styles = document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;
        var headerFormat = styles.CellFormats!
            .Elements<CellFormat>()
            .ElementAt((int)header
                .Elements<Cell>()
                .First()
                .StyleIndex!.Value);

        var fill = styles.Fills!
            .Elements<Fill>()
            .ElementAt((int)headerFormat.FillId!.Value)
            .PatternFill!;

        Assert.Equal("FF3A5A8C", fill.ForegroundColor!.Rgb!.Value);
        Assert.NotNull(styles.Fonts!
            .Elements<Font>()
            .ElementAt((int)headerFormat.FontId!.Value)
            .Bold);

        Assert.Equal(16d,
            sheet
                .Worksheet
                .Descendants<Column>()
                .Single(column => column.Min!.Value == 3)
                .Width!.Value);
    }

    [Fact]
    public async Task UnstyledExport_HasNoOwnCellFormatting()
    {
        // Act
        var (_, file) = await ExcelInstance.ExportAsync(new[] { new Customer(1, "Ada") },
            s_customers,
            new ExcelExportOptions { WriteHeader = true },
            Token);

        // Assert
        using var document = OpenWorkbook(file!);
        var stylesheet = document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;
        Assert.Equal(2,
            stylesheet.Fills!
                .Elements<Fill>()
                .Count());

        Assert.Single(stylesheet.Fonts!.Elements<Font>());
    }

    [Fact]
    public async Task StartPosition_ShiftsHeaderDataFilterAndFrozenRows()
    {
        // Arrange
        var layout = new ExcelLayoutOptions
        {
            StartRow = 4,
            StartColumn = 2,
            FrozenColumns = 1,
        };

        // Act
        var (_, file) = await ExcelInstance.ExportAsync(s_rows,
            s_bookings,
            new ExcelExportOptions
            {
                WriteHeader = true,
                Layout = layout,
            },
            Token);

        // Assert
        using var document = OpenWorkbook(file!);
        var worksheet = Single(document)
            .Worksheet!;

        Assert.Equal("B4:D6", worksheet.GetFirstChild<AutoFilter>()!.Reference!.Value);
        var pane = worksheet
            .Descendants<Pane>()
            .Single();

        Assert.Equal((4d, 2d), (pane.VerticalSplit!.Value, pane.HorizontalSplit!.Value));
    }

    [Theory]
    [InlineData("History")]
    [InlineData("A/B")]
    [InlineData("'Quoted")]
    [InlineData("1234567890123456789012345678901x")]
    public void InvalidSheetNames_AreRejectedNotChanged(
        string name
    )
    {
        // Act
        var exception = Record.Exception(() => TabularSheetName.Create(name));

        // Assert
        Assert.IsType<ArgumentException>(exception, exactMatch: false);
    }

    [Theory]
    [MemberData(nameof(InvalidLayouts), DisableDiscoveryEnumeration = true)]
    public async Task InvalidLayouts_FailBeforeWriting(
        ExcelLayoutOptions layout,
        bool writeHeader
    )
    {
        // Arrange
        var target = new MemoryStream();

        // Act
        var report = await ExcelInstance.ExportAsync(s_rows,
            s_bookings,
            target,
            new ExcelExportOptions
            {
                WriteHeader = writeHeader,
                Layout = layout,
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(0, target.Length);
    }

    [Theory]
    [InlineData(false, ExcelLayoutMode.Table)]
    [InlineData(true, ExcelLayoutMode.Table)]
    [InlineData(false, ExcelLayoutMode.Range)]
    public async Task DuplicateTranslatedHeaders_FailInEveryMode_AndGapsFailTheTable(
        bool gap,
        ExcelLayoutMode mode
    )
    {
        // Arrange
        // WHY: Fixed headers that differ only in case already fail at Build; translated ones fail per call.
        var map = gap
            ? TabularMap
                .For<Customer>()
                .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
                .Column(x => x.Name, index: 2, configure: c => c.Header("Name"))
                .Build()
            : TabularMap
                .For<Customer>()
                .Column(x => x.Id, index: 0, configure: c => c.HeaderTranslationKey("id"))
                .Column(x => x.Name, index: 1, configure: c => c.HeaderTranslationKey("name"))
                .Build();

        var options = new ExcelExportOptions
        {
            WriteHeader = true,
            Layout = new ExcelLayoutOptions { Mode = mode },
            Translations = new Dictionary<string, string>
            {
                ["id"] = "Name",
                ["name"] = "NAME",
            },
        };

        // Act
        var report =
            await ExcelInstance.ExportAsync(new[] { new Customer(1, "a") }, map, new MemoryStream(), options, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
    }

    [Fact]
    public async Task EmptySource_WritesOnlyTheHeaderWithoutFilterOrTable()
    {
        // Act
        var (report, file) = await ExcelInstance.ExportAsync(Array.Empty<Booking>(),
            s_bookings,
            new ExcelExportOptions
            {
                WriteHeader = true,
                Layout = new ExcelLayoutOptions { Mode = ExcelLayoutMode.Table },
            },
            Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, 0L), (report.Status, report.RowsWritten));
        using var document = OpenWorkbook(file!);
        var sheet = Single(document);
        Assert.Single(sheet.Worksheet!.Descendants<Row>());
        Assert.Null(sheet.Worksheet.GetFirstChild<AutoFilter>());
        Assert.Empty(sheet.TableDefinitionParts);
    }

    private static WorksheetPart Single(
        SpreadsheetDocument document
    ) => document.WorkbookPart!.WorksheetParts.Single();

    public sealed record Booking(
        decimal Amount,
        DateTime At,
        string PostalCode
    );

    public sealed record Customer(
        int Id,
        string Name
    );

    public sealed record Order(
        int Number,
        decimal Total
    );
}
