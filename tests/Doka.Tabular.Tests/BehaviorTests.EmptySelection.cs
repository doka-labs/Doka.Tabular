using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// An empty column selection is a user's choice, not a mistake: the export gives an empty table without a header and
/// does not query the source, since no record could be represented without columns.
/// </summary>
public sealed partial class BehaviorTests
{
    private static readonly Employee[] s_oneEmployee = [new("a@example.org", 1, "Berlin", "10115")];

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task EmptyColumnSelection_GivesAnEmptyTableWithoutReadingTheSource(
        TabularType type
    )
    {
        // Arrange
        var read = false;

        // Act
        var (report, file) = await Export.ExportAsync(
            Rows(),
            s_employees,
            type,
            new TabularExportOptions
            {
                WriteHeader = true,
                ColumnKeys = [],
            },
            Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Succeeded, 0L, 0L, false),
            (report.Status, report.RowsRead, report.RowsWritten, read));

        Assert.Empty(Cells(type, file!));

        return;

        IEnumerable<Employee> Rows()
        {
            read = true;

            yield return new Employee("a@example.org", 1, "Berlin", "10115");
        }
    }

    [Theory]
    [MemberData(nameof(Types), MemberType = typeof(TestFiles))]
    public async Task EmptyColumnSelection_ReadsBackAsAnEmptyTable(
        TabularType type
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (_, file) = await Export.ExportAsync(
            new[] { new Employee("a@example.org", 1, "Berlin", "10115") },
            s_employees,
            type,
            new TabularExportOptions
            {
                WriteHeader = true,
                ColumnKeys = [],
            },
            Token);

        var report = await Import.ImportAsync(file!, type, s_employees, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Empty(report.Items!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyColumnSelection_GivesAnEmptyCsvLikeAnEmptySource(
        bool bom
    )
    {
        // Arrange
        var empty = new MemoryStream();
        var stream = new MemoryStream();

        // Act
        // WHY: The equality with an empty source is the checked contract, so both exports form one action; the byte
        // order mark marks the encoding of the file, not its content, so an empty source writes it too.
        await CsvInstance.ExportAsync(
            Array.Empty<Employee>(),
            s_employees,
            empty,
            new CsvExportOptions { WriteBom = bom },
            Token);

        var report = await CsvInstance.ExportAsync(s_oneEmployee, s_employees, stream, NoColumns(bom), Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(empty.ToArray(), stream.ToArray());
        Assert.Equal(
            bom
                ? 3
                : 0,
            stream.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyColumnSelection_PublishesAnEmptyCsvFileLikeAnEmptySource(
        bool bom
    )
    {
        // Arrange
        var empty = new MemoryStream();
        var path = Path.Combine(Path.GetTempPath(), $"tabular-empty-{Guid.NewGuid():N}.csv");

        // Act
        // WHY: The equality with an empty source is the checked contract, so both exports form one action.
        await CsvInstance.ExportAsync(
            Array.Empty<Employee>(),
            s_employees,
            empty,
            new CsvExportOptions { WriteBom = bom },
            Token);

        var report = await CsvInstance.ExportAsync(s_oneEmployee, s_employees, path, NoColumns(bom), Token);

        // Assert
        try
        {
            Assert.Equal(ReportStatus.Succeeded, report.Status);
            Assert.Equal(empty.ToArray(), await File.ReadAllBytesAsync(path, Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CsvExportOptions NoColumns(
        bool bom
    ) => new()
    {
        WriteHeader = true,
        WriteBom = bom,
        ColumnKeys = [],
    };

    [Fact]
    public async Task EmptyColumnSelection_GivesAValidEmptySheetWithoutFrozenHeader()
    {
        // Arrange
        var single = new MemoryStream();

        // Act
        var report = await ExcelInstance.ExportAsync(
            s_oneEmployee,
            s_employees,
            single,
            new ExcelExportOptions
            {
                WriteHeader = true,
                ColumnKeys = [],
                Layout = new ExcelLayoutOptions { Mode = ExcelLayoutMode.Table },
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Empty(Cells(TabularType.Excel, single));
        Assert.Equal([false], Frozen(single));
    }

    [Fact]
    public async Task EmptyColumnSelectionOfOneSheet_GivesAnEmptySheetBesideAFullOne()
    {
        // Arrange
        var sheets = ExcelExport.Create();
        sheets.Add(
            TabularSheetName.Create("Leer"),
            s_oneEmployee,
            s_employees,
            new ExcelSheetExportOptions { ColumnKeys = [] });

        sheets.Add(TabularSheetName.Create("Voll"), s_oneEmployee, s_employees);
        var file = new MemoryStream();

        // Act
        var report = await ExcelInstance.ExportAsync(
            sheets.Build(),
            file,
            new ExcelWorkbookExportOptions { WriteHeader = true },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Empty(Cells(TabularType.Excel, file, "Leer"));
        Assert.Equal(
            2,
            Cells(TabularType.Excel, file, "Voll")
                .Count);

        Assert.Equal([false, true], Frozen(file));
    }

    private static bool[] Frozen(
        Stream file
    )
    {
        using var document = OpenWorkbook(file);
        var workbookPart = Assert.IsType<WorkbookPart>(document.WorkbookPart);

        return
        [
            .. Assert
                .IsType<Workbook>(workbookPart.Workbook)
                .Descendants<Sheet>()
                .Select(sheet => Assert
                    .IsType<Worksheet>(
                        Assert
                            .IsType<WorksheetPart>(workbookPart.GetPartById(Assert.IsType<string>(sheet.Id?.Value)))
                            .Worksheet)
                    .Descendants<Pane>()
                    .Any()),
        ];
    }

    [Fact]
    public async Task EmptyColumnSelection_KeepsTheLayoutRulesOfTheWidth()
    {
        // Act
        // WHY: An empty selection gives width 0; a frozen column beyond it stays a configuration error.
        var report = await ExcelInstance.Format.ExportAsync(
            s_oneEmployee,
            s_employees,
            new MemoryStream(),
            new ExcelExportOptions
            {
                ColumnKeys = [],
                Layout = new ExcelLayoutOptions { FrozenColumns = 1 },
            },
            Token);

        // Assert
        Assert.Equal(
            TabularErrorKind.Configuration,
            Assert.Single(report.Errors)
                .Kind);
    }
}
