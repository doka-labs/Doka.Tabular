namespace Doka.Tabular;

public sealed partial class TabularExport
{
    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<ExportReport> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        Stream target,
        TabularType type,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);

        return Report(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new StreamExportTarget(target),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<ExportReport> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        Stream target,
        TabularType type,
        TabularExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new StreamExportTarget(target),
                options,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<ExportReport> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        string path,
        TabularType type,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(path);

        return Report(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new PathExportTarget(path),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<ExportReport> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        string path,
        TabularType type,
        TabularExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new PathExportTarget(path),
                options,
                cancellationToken));
    }

    /// <summary>Exports into the output buffer; the file is returned only on complete success.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<BufferedExportResult> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        TabularType type,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);

        return Buffered(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new BufferedExportTarget(),
                null,
                cancellationToken));
    }

    /// <summary>Exports into the output buffer; the file is returned only on complete success.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<BufferedExportResult> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        TabularType type,
        TabularExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(options);

        return Buffered(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new BufferedExportTarget(),
                options,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<ExportReport> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        TabularFileTarget target,
        TabularType type,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);

        return Report(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new FileStoreExportTarget(target),
                null,
                cancellationToken));
    }

    /// <summary>Exports the rows.</summary>
    /// <param name="rows">The rows; enumerated asynchronously.</param>
    /// <param name="map">The row map; exports imported row groups.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="type">The format of the file.</param>
    /// <param name="options">Settings of this call; unset values come from the format instance.</param>
    /// <param name="cancellationToken">Cancels the export; the report has status <see cref="ReportStatus.Canceled" />.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [OverloadResolutionPriority(1)]
    public Task<ExportReport> ExportAsync(
        IAsyncEnumerable<TabularRow> rows,
        RowMap map,
        TabularFileTarget target,
        TabularType type,
        TabularExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(
            RunExportAsync(
                type,
                ExportSource<TabularRow>.From(rows),
                ExportShape<TabularRow>.ForRowMap(map),
                new FileStoreExportTarget(target),
                options,
                cancellationToken));
    }
}
