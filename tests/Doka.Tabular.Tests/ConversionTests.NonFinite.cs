using System.Numerics;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>With NonFiniteValues an Excel column is text throughout; a column has exactly one cell type.</summary>
public sealed partial class ConversionTests
{
    private static readonly double[] s_doubles = [1d, double.NaN, 2.5, double.NegativeInfinity];

    private static readonly float[] s_floats = [1f, float.PositiveInfinity, 0.1f];

    private static readonly Half[] s_halves = [(Half)1, Half.NaN, (Half)0.5];

    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("Half")]
    public async Task NonFiniteValues_MakeTheWholeExcelColumnText(
        string type
    )
    {
        // Act
        var (report, file) = type switch
        {
            "double" => await ExportNonFiniteAsync(s_doubles),
            "float" => await ExportNonFiniteAsync(s_floats),
            _ => await ExportNonFiniteAsync(s_halves),
        };

        // Assert
        var cells = ExcelCells(file!);
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(type switch
            {
                "double" => s_doubles.Length,
                "float" => s_floats.Length,
                _ => s_halves.Length,
            },
            cells.Count);

        Assert.All(cells, cell => Assert.Equal(CellValues.InlineString, cell.DataType?.Value));
    }

    [Theory]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("Half")]
    public async Task NonFiniteValues_ReadBackFromTheTextColumn(
        string type
    )
    {
        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (written, read) = type switch
        {
            "double" => await ReadBackNonFiniteAsync(s_doubles),
            "float" => await ReadBackNonFiniteAsync(s_floats),
            _ => await ReadBackNonFiniteAsync(s_halves),
        };

        // Assert
        Assert.Equal(written, read);
    }

    private static Task<BufferedExportResult> ExportNonFiniteAsync<T>(
        T[] values
    )
        where T : struct, INumberBase<T> => Export.ExportAsync(values
            .Select(value => new Value<T>(value))
            .ToArray(),
        Map<T>(c => c.NonFiniteValues()),
        TabularType.Excel,
        Token);

    private static async Task<(object[] Written, object[] Read)> ReadBackNonFiniteAsync<T>(
        T[] values
    )
        where T : struct, INumberBase<T>
    {
        var (_, file) = await ExportNonFiniteAsync(values);
        var report = await Import.ImportAsync(file!, TabularType.Excel, Map<T>(c => c.NonFiniteValues()), Token);

        return ([.. values.Cast<object>()], [.. report.Items!.Select(item => (object)item.Item)]);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("decimal")]
    [InlineData("long?")]
    [InlineData("BigInteger")]
    public void NonFiniteValuesOnAFieldWithoutNaN_IsAConfigurationError(
        string type
    )
    {
        // Act
        var exception = Record.Exception(() => type switch
        {
            "int" => Map<int>(c => c.NonFiniteValues()),
            "decimal" => Map<decimal>(c => c.NonFiniteValues()),
            "long?" => Map<long?>(c => c.NonFiniteValues()),
            _ => Map<BigInteger>(c => c.NonFiniteValues()),
        });

        // Assert
        Assert.IsType<TabularConfigurationException>(exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonFiniteValuesOnAFieldWithNaN_Builds(
        bool half
    )
    {
        // Act
        var exception = Record.Exception(() => half
            ? Map<Half>(c => c.NonFiniteValues())
            : Map<double?>(c => c.NonFiniteValues()));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task WithoutNonFiniteValues_FiniteValuesStayNativeExcelNumbers()
    {
        // Act
        var (_, file) = await Export.ExportAsync(new[] { new Value<double>(1), new Value<double>(2.5) },
            Map<double>(),
            TabularType.Excel,
            Token);

        // Assert
        Assert.All(ExcelCells(file!), cell => Assert.Null(cell.DataType));
    }

    private static List<Cell> ExcelCells(
        Stream file
    )
    {
        using var document = OpenWorkbook(file);
        var cells = Assert
            .IsType<WorkbookPart>(document.WorkbookPart)
            .WorksheetParts
            .SelectMany(part => Assert
                .IsType<Worksheet>(part.Worksheet)
                .Descendants<Cell>())
            .ToList();

        file.Position = 0;

        return cells;
    }
}
