namespace Doka.Tabular.Excel;

public sealed partial class ExcelFormat
{
    /// <summary>Imports and returns the records in the report.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The map.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        Stream source,
        TabularMap<T> map,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);

        return RunImportAsync(new ImportInput(source, null), ImportShape<T>.ForMap(map), null, null, cancellationToken);
    }

    /// <summary>Imports and returns the records in the report.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The map.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        Stream source,
        TabularMap<T> map,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(source, null),
            ImportShape<T>.ForMap(map),
            null,
            options,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        Stream source,
        TabularMap<T> map,
        TabularImportDestination<T> destination,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);

        return RunImportAsync(new ImportInput(source, null),
            ImportShape<T>.ForMap(map),
            destination,
            null,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        Stream source,
        TabularMap<T> map,
        TabularImportDestination<T> destination,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(source, null),
            ImportShape<T>.ForMap(map),
            destination,
            options,
            cancellationToken);
    }

    /// <summary>Imports and returns the records in the report.</summary>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The row map.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        Stream source,
        RowMap map,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);

        return RunImportAsync(new ImportInput(source, null),
            ImportShape<TabularRow>.ForRowMap(map),
            null,
            null,
            cancellationToken);
    }

    /// <summary>Imports and returns the records in the report.</summary>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The row map.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        Stream source,
        RowMap map,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(source, null),
            ImportShape<TabularRow>.ForRowMap(map),
            null,
            options,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The row map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        Stream source,
        RowMap map,
        TabularImportDestination<TabularRow> destination,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);

        return RunImportAsync(new ImportInput(source, null),
            ImportShape<TabularRow>.ForRowMap(map),
            destination,
            null,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="map">The row map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        Stream source,
        RowMap map,
        TabularImportDestination<TabularRow> destination,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(source, null),
            ImportShape<TabularRow>.ForRowMap(map),
            destination,
            options,
            cancellationToken);
    }

    /// <summary>Imports and returns the records in the report.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The map.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        string path,
        TabularMap<T> map,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);

        return RunImportAsync(new ImportInput(null, path), ImportShape<T>.ForMap(map), null, null, cancellationToken);
    }

    /// <summary>Imports and returns the records in the report.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The map.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        string path,
        TabularMap<T> map,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(null, path),
            ImportShape<T>.ForMap(map),
            null,
            options,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        string path,
        TabularMap<T> map,
        TabularImportDestination<T> destination,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);

        return RunImportAsync(new ImportInput(null, path),
            ImportShape<T>.ForMap(map),
            destination,
            null,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<T>> ImportAsync<T>(
        string path,
        TabularMap<T> map,
        TabularImportDestination<T> destination,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(null, path),
            ImportShape<T>.ForMap(map),
            destination,
            options,
            cancellationToken);
    }

    /// <summary>Imports and returns the records in the report.</summary>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The row map.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        string path,
        RowMap map,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);

        return RunImportAsync(new ImportInput(null, path),
            ImportShape<TabularRow>.ForRowMap(map),
            null,
            null,
            cancellationToken);
    }

    /// <summary>Imports and returns the records in the report.</summary>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The row map.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        string path,
        RowMap map,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(null, path),
            ImportShape<TabularRow>.ForRowMap(map),
            null,
            options,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The row map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        string path,
        RowMap map,
        TabularImportDestination<TabularRow> destination,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);

        return RunImportAsync(new ImportInput(null, path),
            ImportShape<TabularRow>.ForRowMap(map),
            destination,
            null,
            cancellationToken);
    }

    /// <summary>Imports into the destination.</summary>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="map">The row map.</param>
    /// <param name="destination">The write target.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the import; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ImportReport<TabularRow>> ImportAsync(
        string path,
        RowMap map,
        TabularImportDestination<TabularRow> destination,
        ExcelImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        return RunImportAsync(new ImportInput(null, path),
            ImportShape<TabularRow>.ForRowMap(map),
            destination,
            options,
            cancellationToken);
    }
}
