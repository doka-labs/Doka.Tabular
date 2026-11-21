namespace Doka.Tabular.Excel;

/// <summary>Collects the sheets of a multi-sheet export; the registration order is the sheet order.</summary>
[PublicAPI]
public sealed class ExcelExportBuilder
{
    private readonly List<WorkbookExportSheet> _sheets = [];

    internal ExcelExportBuilder() { }

    /// <summary>Adds a sheet with a synchronous source.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="name">The sheet name.</param>
    /// <param name="rows">The rows; enumerated once.</param>
    /// <param name="map">The map of the sheet.</param>
    /// <param name="options">Header, selection, translations, and layout of the sheet.</param>
    /// <returns>This builder.</returns>
    public ExcelExportBuilder Add<T>(
        TabularSheetName name,
        IEnumerable<T> rows,
        TabularMap<T> map,
        ExcelSheetExportOptions? options = null
    ) => Add(name, ExportSource<T>.From(Required(rows)), map, options);

    /// <summary>Adds a sheet with an asynchronous source.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="name">The sheet name.</param>
    /// <param name="rows">The rows; enumerated once and asynchronously.</param>
    /// <param name="map">The map of the sheet.</param>
    /// <param name="options">Header, selection, translations, and layout of the sheet.</param>
    /// <returns>This builder.</returns>
    [OverloadResolutionPriority(1)]
    public ExcelExportBuilder Add<T>(
        TabularSheetName name,
        IAsyncEnumerable<T> rows,
        TabularMap<T> map,
        ExcelSheetExportOptions? options = null
    ) => Add(name, ExportSource<T>.From(Required(rows)), map, options);

    /// <summary>Adds a sheet with a query source, enumerated asynchronously.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="name">The sheet name.</param>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map of the sheet.</param>
    /// <param name="options">Header, selection, translations, and layout of the sheet.</param>
    /// <returns>This builder.</returns>
    [OverloadResolutionPriority(2)]
    public ExcelExportBuilder Add<T>(
        TabularSheetName name,
        IQueryable<T> rows,
        TabularMap<T> map,
        ExcelSheetExportOptions? options = null
    ) => Add(name, ExportSource<T>.FromQuery(Required(rows)), map, options);

    /// <summary>Builds the immutable workbook definition.</summary>
    /// <returns>The definition.</returns>
    public ExcelWorkbookExport Build() => new([.. _sheets]);

    private ExcelExportBuilder Add<T>(
        TabularSheetName name,
        ExportSource<T>? source,
        TabularMap<T> map,
        ExcelSheetExportOptions? options
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(map);
        _sheets.Add(new WorkbookExportSheet<T>(name,
            source,
            ExportShape<T>.ForMap(map),
            options ?? new ExcelSheetExportOptions()));

        return this;
    }

    private static TRows Required<TRows>(
        TRows rows
    )
        where TRows : class
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows;
    }
}
