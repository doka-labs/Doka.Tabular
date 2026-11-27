namespace Doka.Tabular.Csv;

public sealed partial class CsvFormat
{
    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        Stream target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);

        return Report(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new StreamExportTarget(target),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        Stream target,
        CsvExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new StreamExportTarget(target),
                options,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        string path,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(path);

        return Report(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new PathExportTarget(path),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        string path,
        CsvExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new PathExportTarget(path),
                options,
                cancellationToken));
    }

    /// <summary>Exports into the output buffer; the file is returned only on complete success.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<BufferedExportResult> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);

        return Buffered(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new BufferedExportTarget(),
                null,
                cancellationToken));
    }

    /// <summary>Exports into the output buffer; the file is returned only on complete success.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<BufferedExportResult> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        CsvExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        return Buffered(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new BufferedExportTarget(),
                options,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        TabularFileTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);

        return Report(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new FileStoreExportTarget(target),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        IEnumerable<TabularRow> rows,
        RowMap map,
        TabularFileTarget target,
        CsvExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new FileStoreExportTarget(target),
                options,
                cancellationToken));
    }
}
