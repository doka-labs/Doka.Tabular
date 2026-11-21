namespace Doka.Tabular.Excel;

/// <summary>Collects the sheets of a multi-sheet import; the configured order is the processing order.</summary>
[PublicAPI]
public sealed class ExcelImportBuilder
{
    private readonly List<WorkbookImportSheet> _sheets = [];

    internal ExcelImportBuilder() { }

    /// <summary>Adds a sheet whose records are returned in its sheet result.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="name">The sheet name in the file.</param>
    /// <param name="map">The map of the sheet.</param>
    /// <param name="options">Binding, rows, culture, and read options of the sheet.</param>
    /// <returns>The typed handle of the sheet result.</returns>
    public ExcelImportSheet<T> Add<T>(
        TabularSheetName name,
        TabularMap<T> map,
        ExcelSheetImportOptions? options = null
    ) => Add(name, ImportShape<T>.ForMap(Required(map)), null, options);

    /// <summary>Adds a sheet with a write target.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="name">The sheet name in the file.</param>
    /// <param name="map">The map of the sheet.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="options">Binding, rows, culture, and read options of the sheet.</param>
    /// <returns>The typed handle of the sheet result.</returns>
    public ExcelImportSheet<T> Add<T>(
        TabularSheetName name,
        TabularMap<T> map,
        TabularImportDestination<T> destination,
        ExcelSheetImportOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(destination);

        return Add(name, ImportShape<T>.ForMap(Required(map)), destination, options);
    }

    /// <summary>Adds a sheet read with a row map.</summary>
    /// <param name="name">The sheet name in the file.</param>
    /// <param name="map">The row map.</param>
    /// <param name="options">Binding, rows, culture, and read options of the sheet.</param>
    /// <returns>The typed handle of the sheet result.</returns>
    public ExcelImportSheet<TabularRow> Add(
        TabularSheetName name,
        RowMap map,
        ExcelSheetImportOptions? options = null
    ) => Add(name, ImportShape<TabularRow>.ForRowMap(Required(map)), null, options);

    /// <summary>Adds a sheet read with a row map into a write target.</summary>
    /// <param name="name">The sheet name in the file.</param>
    /// <param name="map">The row map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="options">Binding, rows, culture, and read options of the sheet.</param>
    /// <returns>The typed handle of the sheet result.</returns>
    public ExcelImportSheet<TabularRow> Add(
        TabularSheetName name,
        RowMap map,
        TabularImportDestination<TabularRow> destination,
        ExcelSheetImportOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(destination);

        return Add(name, ImportShape<TabularRow>.ForRowMap(Required(map)), destination, options);
    }

    /// <summary>Builds the immutable workbook definition.</summary>
    /// <returns>The definition.</returns>
    public ExcelWorkbookImport Build() => new([.. _sheets]);

    private ExcelImportSheet<T> Add<T>(
        TabularSheetName name,
        ImportShape<T> shape,
        TabularImportDestination<T>? destination,
        ExcelSheetImportOptions? options
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        var sheet = new ExcelImportSheet<T>(_sheets.Count, name);
        _sheets.Add(new WorkbookImportSheet<T>(sheet, shape, destination, options ?? new ExcelSheetImportOptions()));

        return sheet;
    }

    private static TMap Required<TMap>(
        TMap map
    )
        where TMap : class
    {
        ArgumentNullException.ThrowIfNull(map);

        return map;
    }
}
