namespace Doka.Tabular.Excel;

/// <summary>Immutable ordered sheet definitions of a multi-sheet export.</summary>
[PublicAPI]
public sealed class ExcelWorkbookExport
{
    internal ExcelWorkbookExport(
        IReadOnlyList<WorkbookExportSheet> sheets
    )
    {
        Sheets = sheets;
    }

    internal IReadOnlyList<WorkbookExportSheet> Sheets { get; }
}
