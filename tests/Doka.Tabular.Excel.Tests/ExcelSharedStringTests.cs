namespace Doka.Tabular.Excel.Tests;

/// <summary>A shared string beyond the field limit is not held: the table keeps a marker instead of the text.</summary>
public sealed class ExcelSharedStringTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(50, false)]
    [InlineData(11, false)]
    [InlineData(10, true)]
    public async Task SharedStringBeyondTheLimitAfterDecoding_IsNotHeld(
        int length,
        bool held
    )
    {
        // Arrange
        var file = RawWorkbook.Create(
            """<row r="1"><c r="A1" t="s"><v>0</v></c></row>""",
            $"<si><t>{new string('a', length)}</t></si>");

        // Act
        var (package, _) = await ExcelPackage.OpenAsync(file, Limits(10), Token);
        await using var owned = package;

        // Assert
        Assert.Equal(held, package!.SharedStrings[0] is not null);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public async Task SharedStringWithEscapes_IsJudgedByItsDecodedLength(
        int characters,
        bool held
    )
    {
        // Arrange
        // WHY: Each escape stores one character in seven, so the stored text is far beyond the limit in both cases.
        var file = RawWorkbook.Create(
            """<row r="1"><c r="A1" t="s"><v>0</v></c></row>""",
            $"<si><t>{string.Concat(Enumerable.Repeat("_x0041_", characters))}</t></si>");

        // Act
        var (package, _) = await ExcelPackage.OpenAsync(file, Limits(10), Token);
        await using var owned = package;

        // Assert
        Assert.Equal(
            held
                ? new string('A', characters)
                : null,
            package!.SharedStrings[0]);
    }

    [Fact]
    public async Task RichTextSharedString_IsJudgedByTheLengthOfAllRuns()
    {
        // Arrange
        var file = RawWorkbook.Create(
            """<row r="1"><c r="A1" t="s"><v>0</v></c></row>""",
            "<si><r><t>aaaaaa</t></r><r><t>bbbbbb</t></r></si>");

        // Act
        var (package, _) = await ExcelPackage.OpenAsync(file, Limits(10), Token);
        await using var owned = package;

        // Assert
        Assert.Null(package!.SharedStrings[0]);
    }

    private static ResourceLimits Limits(
        long maxFieldCharacters
    ) => new(
        ResourceLimits.DefaultBufferBytes,
        ResourceLimits.DefaultBufferBytes,
        true,
        Path.GetTempPath(),
        null,
        null,
        maxFieldCharacters,
        null);
}
