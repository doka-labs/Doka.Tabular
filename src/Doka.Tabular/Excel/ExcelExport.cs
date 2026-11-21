namespace Doka.Tabular.Excel;

/// <summary>Entry point for multi-sheet exports.</summary>
[PublicAPI]
public static class ExcelExport
{
    /// <summary>Starts an ordered list of sheet definitions.</summary>
    /// <returns>A new builder.</returns>
    public static ExcelExportBuilder Create() => new();
}
