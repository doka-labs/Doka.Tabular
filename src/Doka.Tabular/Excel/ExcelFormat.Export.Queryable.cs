namespace Doka.Tabular.Excel;

public sealed partial class ExcelFormat
{
    /// <summary>Exports the rows.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<ExportReport> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        Stream target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);

        return Report(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new StreamExportTarget(target),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<ExportReport> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        Stream target,
        ExcelExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new StreamExportTarget(target),
                options,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<ExportReport> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        string path,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(path);

        return Report(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new PathExportTarget(path),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<ExportReport> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        string path,
        ExcelExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new PathExportTarget(path),
                options,
                cancellationToken));
    }

    /// <summary>Exports into the output buffer; the file is returned only on complete success.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<BufferedExportResult> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);

        return Buffered(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new BufferedExportTarget(),
                null,
                cancellationToken));
    }

    /// <summary>Exports into the output buffer; the file is returned only on complete success.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<BufferedExportResult> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        ExcelExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        return Buffered(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new BufferedExportTarget(),
                options,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<ExportReport> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        TabularFileTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);

        return Report(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new FileStoreExportTarget(target),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="rows">The query; it must support asynchronous enumeration.</param>
    /// <param name="map">The map.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(2)]
    public Task<ExportReport> ExportAsync<T>(
        IQueryable<T> rows,
        TabularMap<T> map,
        TabularFileTarget target,
        ExcelExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                ExportSource<T>.FromQuery(rows),
                ExportShape<T>.ForMap(map),
                new FileStoreExportTarget(target),
                options,
                cancellationToken));
    }
}
