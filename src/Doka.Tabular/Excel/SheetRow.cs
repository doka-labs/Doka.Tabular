namespace Doka.Tabular.Excel;

/// <summary>One row element of a worksheet: its number, visibility, cells, and whether any cell has content.</summary>
internal readonly record struct SheetRow(
    long Number,
    bool Hidden,
    List<SourceCell> Cells,
    bool HasContent
);
