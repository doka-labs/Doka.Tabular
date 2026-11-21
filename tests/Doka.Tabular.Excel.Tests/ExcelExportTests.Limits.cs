using System.IO.Compression;
using DocumentFormat.OpenXml.Spreadsheet;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

public sealed partial class ExcelExportTests
{
    private static readonly TabularMap<Order> s_numbers = TabularMap
        .For<Order>()
        .Column(x => x.Number, index: 0, configure: c => c.Header("N"))
        .Build();

    [Theory]
    [InlineData(".xls")]
    [InlineData(".xlsm")]
    [InlineData(".XLSB")]
    public async Task TargetPath_WithUnsupportedExtension_IsRejectedBeforeWriting(
        string extension
    )
    {
        // Arrange
        var path = Path.Combine(_directory, "out" + extension);

        // Act
        var report = await ExcelInstance.ExportAsync(s_rows, s_bookings, path, Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Configuration.Invalid, (report.FatalError ?? report.Errors[0]).Code);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(1_048_577, 1, false)]
    [InlineData(1_048_577, 1, true)]
    [InlineData(1, 16_385, false)]
    [InlineData(1, 16_385, true)]
    public async Task StartCellOutsideTheSheet_IsAConfigurationErrorAlsoForAnEmptySourceOrSelection(
        int startRow,
        int startColumn,
        bool emptySelection
    )
    {
        // Arrange
        var map = TabularMap
            .For<Order>()
            .Column(x => x.Number, index: 0, configure: c => c.Header("N"))
            .Build();

        // Act
        var report = await ExcelInstance.ExportAsync(Array.Empty<Order>(),
            map,
            new MemoryStream(),
            new ExcelExportOptions
            {
                ColumnKeys = emptySelection
                    ? []
                    : null,
                Layout = new ExcelLayoutOptions
                {
                    StartRow = startRow,
                    StartColumn = startColumn,
                },
            },
            Token);

        // Assert
        Assert.Equal((TabularErrorKind.Configuration, TabularErrorCodes.Schema.LimitExceeded),
            (report.FatalError?.Kind, report.FatalError?.Code));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task ForwardOnlyImportWithoutSpill_AtTheBufferSucceeds_OneByteBeyondIsALimitError(
        int bufferOffset,
        bool succeeds
    )
    {
        // Arrange
        // WHY: A workbook needs a seekable input; a forward-only input of exactly the buffer stays in memory.
        var file = await WorkbookAsync();
        var format = new ExcelFormat(new ExcelFormatOptions
        {
            ImportBufferBytes = file.Length + bufferOffset,
            AllowSpill = false,
            TemporaryDirectory = _directory,
        });

        // Act
        var report = await format.ImportAsync(new ForwardOnlyStream(new MemoryStream(file)), OrderMap(), Token);

        // Assert
        Assert.Equal(succeeds
                ? null
                : TabularErrorCodes.Resource.LimitExceeded,
            report.FatalError?.Code);

        Assert.Equal(succeeds
                ? 300
                : 0,
            report.Items?.Count ?? 0);

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task SpilledForwardOnlyImport_AtTheTemporaryLimitSucceeds_OneByteBeyondIsALimitError(
        int limitOffset,
        bool succeeds
    )
    {
        // Arrange
        var file = await WorkbookAsync();
        var format = new ExcelFormat(new ExcelFormatOptions
        {
            ImportBufferBytes = 0,
            MaxTemporaryBytes = file.Length + limitOffset,
            TemporaryDirectory = _directory,
        });

        // Act
        var report = await format.ImportAsync(new ForwardOnlyStream(new MemoryStream(file)), OrderMap(), Token);

        // Assert
        Assert.Equal(succeeds
                ? null
                : TabularErrorCodes.Resource.LimitExceeded,
            report.FatalError?.Code);

        Assert.Equal(succeeds
                ? 300
                : 0,
            report.Items?.Count ?? 0);

        Assert.Empty(Directory.GetFiles(_directory));
    }

    private static TabularMap<Counted> OrderMap() => TabularMap
        .For<Counted>()
        .Column(x => x.Number, index: 0)
        .Build();

    private static async Task<byte[]> WorkbookAsync()
    {
        using var file = new MemoryStream();
        await new ExcelFormat(new ExcelFormatOptions()).ExportAsync(Enumerable
                .Range(1, 300)
                .Select(number => new Counted(number)),
            OrderMap(),
            file,
            Token);

        return file.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryEntry_IsAtTheDosEpoch(
        bool workbook
    )
    {
        // Arrange
        var file = new MemoryStream();

        // Act
        await ExportOrdersAsync(file, workbook);

        // Assert
        file.Position = 0;
        await using var archive = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read, true, null, Token);
        Assert.All(archive.Entries, entry => Assert.Equal(new DateTime(1980, 1, 1), entry.LastWriteTime.DateTime));
    }

    [Fact]
    public async Task EqualData_GiveEqualBytes()
    {
        // Arrange
        var first = new MemoryStream();
        var second = new MemoryStream();

        // Act
        // WHY: Equal bytes of two runs are the checked contract, so both exports form one action.
        await ExportOrdersAsync(first, workbook: false);
        await ExportOrdersAsync(second, workbook: false);

        // Assert
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public async Task StartCellInTheLastRowAndColumn_IsValid()
    {
        // Arrange
        var map = TabularMap
            .For<Order>()
            .Column(x => x.Number, index: 0, configure: c => c.Header("N"))
            .Build();

        // Act
        var report = await ExcelInstance.ExportAsync(new[] { new Order(1, 0) },
            map,
            new MemoryStream(),
            new ExcelExportOptions
            {
                Layout = new ExcelLayoutOptions
                {
                    StartRow = 1_048_576,
                    StartColumn = 16_384,
                },
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task TextCells_StayTextAlsoWhenTheyLookLikeFormulas()
    {
        // Act
        var (_, file) = await ExcelInstance.ExportAsync(new[] { new Customer(1, "=1+1") }, s_customers, Token);

        // Assert
        using var document = OpenWorkbook(file!);
        Assert.Empty(Single(document)
            .Worksheet!.Descendants<CellFormula>());

        Assert.Equal("=1+1", Cells(TabularType.Excel, file!)[0][1]);
    }

    [Fact]
    public async Task RowLimit_AllowsTheLastSheetRow()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(Orders(1_048_575),
            s_numbers,
            Stream.Null,
            new ExcelExportOptions { WriteHeader = true },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task RowLimit_RejectsTheRowAfterTheLastSheetRowWithoutPublishing()
    {
        // Arrange
        var path = Path.Combine(_directory, "large.xlsx");

        // Act
        var report = await ExcelInstance.ExportAsync(Orders(1_048_576),
            s_numbers,
            path,
            new ExcelExportOptions { WriteHeader = true },
            Token);

        // Assert
        Assert.Equal(TabularErrorCodes.Schema.LimitExceeded, report.FatalError?.Code);
        Assert.Equal(PathOutputState.NotPublished, report.PathOutput);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CellTextLimit_IsADataErrorAtTheRow()
    {
        // Act
        var report = await ExcelInstance.ExportAsync(
            new[] { new Customer(1, "a"), new Customer(2, new string('x', 32_768)) },
            s_customers,
            new MemoryStream(),
            Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Schema.LimitExceeded, 2L),
            (report.FatalError!.Code, report.FatalError.Location.RowNumber!.Value));
    }

    [Fact]
    public async Task AsyncOnlyTarget_ReceivesBytesWhileTheSourceStillDelivers()
    {
        // Arrange
        var target = new AsyncOnlyStream();
        long seenDuringExport = 0;

        // Act
        var report = await ExcelInstance.ExportAsync(Source(), s_customers, target, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.True(seenDuringExport > 0, "no bytes reached the target before the source ended");
        Assert.True(target.Written > seenDuringExport);

        return;

        async IAsyncEnumerable<Customer> Source()
        {
            for (var number = 1; number <= 50_000; number++)
            {
                if (number == 49_000)
                {
                    seenDuringExport = target.Written;
                }

                await Task.Yield();

                yield return new Customer(number, $"name {number}");
            }
        }
    }

    private static IEnumerable<Order> Orders(
        int count
    ) => Enumerable
        .Range(1, count)
        .Select(number => new Order(number, 0));

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task WorkbookPathExport_CountsAllSheetsAgainstTheTemporaryBudget(
        int limitOffset,
        bool succeeds
    )
    {
        // Arrange
        // WHY: Each sheet alone is far below the limit; only the whole workbook in its temporary file reaches it.
        using var measured = new MemoryStream();
        await ExcelInstance.ExportAsync(ThreeSheets(), measured, Token);
        var format = new ExcelFormat(new ExcelFormatOptions
        {
            MaxTemporaryBytes = measured.Length + limitOffset,
            TemporaryDirectory = _directory,
        });

        var path = Path.Combine(_directory, "book.xlsx");

        // Act
        var report = await format.ExportAsync(ThreeSheets(), path, Token);

        // Assert
        Assert.Equal(succeeds
                ? null
                : TabularErrorCodes.Resource.LimitExceeded,
            report.FatalError?.Code);

        Assert.Equal(succeeds
                ? [path]
                : [],
            Directory.GetFiles(_directory));

        return;

        static ExcelWorkbookExport ThreeSheets()
        {
            var sheets = ExcelExport.Create();
            foreach (var name in new[] { "A", "B", "C" })
            {
                sheets.Add(TabularSheetName.Create(name), Orders(200), s_numbers);
            }

            return sheets.Build();
        }
    }

    private static async Task ExportOrdersAsync(
        Stream file,
        bool workbook
    )
    {
        if (!workbook)
        {
            await ExcelInstance.ExportAsync(new[] { new Order(1, 0) }, s_numbers, file, Token);

            return;
        }

        var sheets = ExcelExport.Create();
        sheets.Add(TabularSheetName.Create("A"), new[] { new Order(1, 0) }, s_numbers);
        sheets.Add(TabularSheetName.Create("B"), new[] { new Order(2, 0) }, s_numbers);
        await ExcelInstance.ExportAsync(sheets.Build(), file, Token);
    }

    private sealed record Counted(
        int Number
    );
}
