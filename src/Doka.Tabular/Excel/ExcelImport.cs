namespace Doka.Tabular.Excel;

/// <summary>Entry point for multi-sheet imports.</summary>
[PublicAPI]
public static class ExcelImport
{
    /// <summary>Starts an ordered list of sheet definitions.</summary>
    /// <returns>A new builder.</returns>
    public static ExcelImportBuilder Create() => new();
}
