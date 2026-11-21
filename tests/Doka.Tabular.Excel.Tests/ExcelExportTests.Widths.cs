using DocumentFormat.OpenXml.Spreadsheet;
using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Excel.Tests;

/// <summary>Estimated widths: the preview keeps its fixed budget.</summary>
public sealed partial class ExcelExportTests
{
    private static readonly TabularMap<TextPair> s_pairs = TabularMap
        .For<TextPair>()
        .Column(x => x.First, index: 0)
        .Column(x => x.Second, index: 1)
        .Build();

    [Fact]
    public async Task RowBeyondThePreviewBudget_IsWrittenWithoutWideningTheColumns()
    {
        // Arrange
        var rows = PreviewRows();

        // Act
        var (report, file) = await ExcelInstance.ExportAsync(rows, s_pairs, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        using var document = OpenWorkbook(file!);
        var columns = Single(document)
            .Worksheet!
            .Descendants<Column>()
            .ToList();

        Assert.Equal((60d, 24d),
            (columns.Single(column => column.Min!.Value == 1)
                    .Width!.Value,
                columns.Single(column => column.Min!.Value == 2)
                    .Width!.Value));
    }

    [Fact]
    public async Task RowBeyondThePreviewBudget_ReadsBack()
    {
        // Arrange
        var rows = PreviewRows();

        // Act
        // WHY: The reimport of the export is the checked contract, so both form one action.
        var (_, file) = await ExcelInstance.ExportAsync(rows, s_pairs, Token);
        var report = await ExcelInstance.ImportAsync(file!, s_pairs, Token);

        // Assert
        Assert.Equal(rows, report.Items);
    }

    // WHY: Fifteen rows of 32,767 characters stay below 1 MiB; the sixteenth row would cross it with its second
    // text, so it is not part of the preview and leaves the second column at its starting width.
    private static TextPair[] PreviewRows()
    {
        var full = new string('a', 32767);

        return
        [
            .. Enumerable
                .Repeat(new TextPair(full, ""), 15)
                .Append(new TextPair(full, full)),
        ];
    }

    public sealed record TextPair(
        string First,
        string Second
    );
}
