namespace Doka.Tabular.Excel;

/// <summary>Immutable ordered sheet definitions of a multi-sheet import.</summary>
[PublicAPI]
public sealed class ExcelWorkbookImport
{
    internal ExcelWorkbookImport(
        IReadOnlyList<WorkbookImportSheet> sheets
    )
    {
        Sheets = sheets;
    }

    internal IReadOnlyList<WorkbookImportSheet> Sheets { get; }
}
