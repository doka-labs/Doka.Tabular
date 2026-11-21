namespace Doka.Tabular.Excel;

/// <summary>Typed handle of one sheet definition of a multi-sheet import.</summary>
/// <typeparam name="T">The record type.</typeparam>
[PublicAPI]
public sealed class ExcelImportSheet<T>
{
    internal ExcelImportSheet(
        int ordinal,
        TabularSheetName name
    )
    {
        Ordinal = ordinal;
        Name = name;
    }

    internal int Ordinal { get; }

    /// <summary>Gets the sheet name.</summary>
    public TabularSheetName Name { get; }
}
