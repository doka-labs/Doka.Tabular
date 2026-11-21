using FsCheck.Xunit;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

/// <summary>
/// Values written by the Excel export are read back unchanged. Every run draws new values; a failure is a real defect,
/// and its reported counterexample becomes a fixed test before the fix.
/// </summary>
public sealed class ExcelPropertyTests
{
    private static readonly TabularMap<TextLine> s_texts = TabularMap
        .For<TextLine>()
        .Column(x => x.Number, index: 0)
        .Column(x => x.Text, index: 1, configure: c => c.Trim(false))
        .Build();

    private static readonly TabularMap<NumberLine> s_numbers = TabularMap
        .For<NumberLine>()
        .Column(x => x.Number, index: 0)
        .Column(x => x.Value, index: 1)
        .Build();

    private static readonly ExcelFormat s_format = new(new ExcelFormatOptions());

    [Property]
    public async Task Text_IsReadBackUnchanged(
        string?[] texts
    )
    {
        // Arrange
        var lines = texts
            .Select((text, index) => new TextLine(index, PropertyText.WellFormed(text)))
            .ToArray();

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) = await s_format.ExportAsync(lines, s_texts, TestContext.Current.CancellationToken);
        await using var owned = file;
        var import = await s_format.ImportAsync(file!, s_texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, ReportStatus.Succeeded), (export.Status, import.Status));
        Assert.Empty(WorkbookProblems(file!));
        Assert.Equal(lines.Select(line => PropertyText.Empty(line.Text)),
            import.Items!.Select(line => PropertyText.Empty(line.Text)));
    }

    [Fact]
    public async Task EmptyText_AfterAnotherText_IsReadBackAsEmpty()
    {
        // Arrange
        // WHY: Counterexample of Text_IsReadBackUnchanged: an empty slice at the end of the reused text buffer.
        TextLine[] lines = [new(0, "p"), new(1, "")];

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) = await s_format.ExportAsync(lines, s_texts, TestContext.Current.CancellationToken);
        await using var owned = file;
        var import = await s_format.ImportAsync(file!, s_texts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, ReportStatus.Succeeded), (export.Status, import.Status));
        Assert.Empty(WorkbookProblems(file!));
        Assert.Equal(["p", null], import.Items!.Select(line => PropertyText.Empty(line.Text)));
    }

    [Property]
    public async Task FiniteDoubleWithAtMost15SignificantDigits_IsReadBackUnchanged(
        double value
    )
    {
        // Arrange
        NumberLine[] lines = [new(0, Storable(value))];

        // Act
        // WHY: The round trip of the own output is the checked contract, so export and reimport form one action.
        var (export, file) = await s_format.ExportAsync(lines, s_numbers, TestContext.Current.CancellationToken);
        await using var owned = file;
        var import = await s_format.ImportAsync(file!, s_numbers, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, ReportStatus.Succeeded), (export.Status, import.Status));
        Assert.Empty(WorkbookProblems(file!));
        Assert.Equal(lines, import.Items);
    }

    // WHY: An Excel worksheet holds no subnormal numbers; the smallest positive number is 2.22507385850721E-308.
    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(-1e-310)]
    [InlineData(2.2250738585072E-308)]
    public async Task SubnormalDouble_IsOutOfRange(
        double value
    )
    {
        // Act
        var (export, _) = await s_format.ExportAsync(new[] { new NumberLine(0, value) },
            s_numbers,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TabularErrorCodes.Conversion.OutOfRange,
            Assert.Single(export.Errors)
                .Code);
    }

    // WHY: Excel keeps 15 significant digits; the export rejects a value that needs more instead of rounding it.
    [Theory]
    [InlineData(0.30000000000000004)]
    [InlineData(1.0000000000000002)]
    [InlineData(-123456789.12345678)]
    [InlineData(1.7976931348623157E308)]
    public async Task DoubleWithMoreThan15SignificantDigits_IsAPrecisionLoss(
        double value
    )
    {
        // Act
        var (export, _) = await s_format.ExportAsync(new[] { new NumberLine(0, value) },
            s_numbers,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReportStatus.Failed, export.Status);
        Assert.Equal(TabularErrorCodes.Conversion.PrecisionLoss,
            Assert.Single(export.Errors)
                .Code);
    }

    /// <summary>
    /// The drawn value rounded to 15 significant digits; a value outside the finite normal range of a worksheet
    /// becomes zero.
    /// </summary>
    private static double Storable(
        double value
    )
    {
        var rounded = double.IsFinite(value)
            ? double.Parse(value.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : 0d;

        return double.IsFinite(rounded) && (rounded == 0 || Math.Abs(rounded) >= 2.22507385850721E-308)
            ? rounded
            : 0d;
    }

    private sealed record TextLine(
        int Number,
        string? Text
    );

    private sealed record NumberLine(
        int Number,
        double Value
    );
}
