using System.IO.Compression;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

/// <summary>
/// The escaped string form of ECMA-376 Part 1, 22.9.2.19 as Excel applies it: invalid XML characters and CR are
/// escaped, LF and TAB only in attributes, an underscore only before an escape form; escapes read in any case.
/// </summary>
public sealed class ExcelEscapeTests
{
    private static readonly string s_lineFeed = $"{(char)0x0A}";
    private static readonly string s_tab = $"{(char)0x09}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> UnescapedTexts => new()
    {
        { $"a{s_lineFeed}b", "_x000A_" },
        { $"a{s_tab}b", "_x0009_" },
        { "a_b", "_x005F_" },
        { "_x00", "_x005F_" },
        { "_x004G_", "_x005F_" },
    };

    [Theory]
    [MemberData(nameof(UnescapedTexts))]
    public async Task CellText_IsEscapedOnlyWhereExcelEscapes(
        string text,
        string absent
    )
    {
        // Act
        // WHY: The written form and its reimport are the checked contract, so both form one action.
        var (_, file) = await ExcelInstance.ExportAsync(new[] { new Row(1, text) }, Rows("Name"), Token);
        var report = await ExcelInstance.ImportAsync(file!, Rows("Name"), Token);

        // Assert
        Assert.DoesNotContain(absent, Entry(file!, "xl/worksheets/"), StringComparison.Ordinal);
        Assert.Equal(text,
            Assert.Single(report.Items!)
                .Name);
    }

    [Theory]
    [InlineData(0x09, "_x0009_")]
    [InlineData(0x0A, "_x000A_")]
    public async Task TableColumnName_EscapesLineFeedAndTab(
        int character,
        string escaped
    )
    {
        // Arrange
        var header = $"A{(char)character}B";

        // Act
        var (report, file) = await ExcelInstance.ExportAsync(new[] { new Row(1, "x") },
            Rows(header),
            new ExcelExportOptions
            {
                WriteHeader = true,
                Layout = new ExcelLayoutOptions { Mode = ExcelLayoutMode.Table },
            },
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Contains($"name=\"A{escaped}B\"", Entry(file!, "xl/tables/"), StringComparison.Ordinal);
        Assert.DoesNotContain(escaped, Entry(file!, "xl/worksheets/"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("_x005f_x0041_", "_x0041_")]
    [InlineData("a_x000d_", "a\r")]
    [InlineData("_x004a_", "J")]
    [InlineData("_x004A_", "J")]
    public async Task EscapeInLowerOrUpperCase_IsDecoded(
        string stored,
        string expected
    )
    {
        // Arrange
        // WHY: The format names hexadecimal characters without a case, and Excel writes _x005f_ in lower case.
        var file = RawWorkbook.Create(
            $"""<row r="1"><c r="A1"><v>1</v></c><c r="B1" t="inlineStr"><is><t>{stored}</t></is></c></row>""");

        // Act
        var report = await ExcelInstance.ImportAsync(file, Rows("Name"), Token);

        // Assert
        Assert.Equal(expected,
            Assert.Single(report.Items!)
                .Name);
    }

    [Fact]
    public async Task LoneSurrogate_IsEscapedAndReadsBack()
    {
        // Arrange
        var text = $"a{(char)0xD800}b";

        // Act
        // WHY: The written form and its reimport are the checked contract, so both form one action.
        var (_, file) = await ExcelInstance.ExportAsync(new[] { new Row(1, text) }, Rows("Name"), Token);
        var report = await ExcelInstance.ImportAsync(file!, Rows("Name"), Token);

        // Assert
        Assert.Contains("_xD800_", Entry(file!, "xl/worksheets/"), StringComparison.Ordinal);
        Assert.Equal(text,
            Assert.Single(report.Items!)
                .Name);
    }

    private static TabularMap<Row> Rows(
        string header
    ) => TabularMap
        .For<Row>()
        .Column(x => x.Id, index: 0, configure: c => c.Header("Id"))
        .Column(x => x.Name, index: 1, configure: c => c.Header(header))
        .Build();

    private static string Entry(
        Stream file,
        string prefix
    )
    {
        file.Position = 0;
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.Entries.First(item => item.FullName.StartsWith(prefix, StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());

        return reader.ReadToEnd();
    }

    private sealed record Row(
        int Id,
        string Name
    );
}
