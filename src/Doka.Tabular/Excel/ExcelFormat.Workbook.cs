namespace Doka.Tabular.Excel;

/// <summary>Multi-sheet import and export.</summary>
public sealed partial class ExcelFormat
{
    /// <summary>Imports several sheets in the configured order.</summary>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The workbook report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExcelWorkbookImportReport> ImportAsync(
        Stream source,
        ExcelWorkbookImport sheets,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sheets);

        return RunWorkbookImportAsync(new ImportInput(source, null), sheets, null, cancellationToken);
    }

    /// <summary>Imports several sheets in the configured order.</summary>
    /// <param name="source">The input stream; it stays open.</param>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="options">Call-wide settings.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The workbook report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExcelWorkbookImportReport> ImportAsync(
        Stream source,
        ExcelWorkbookImport sheets,
        ExcelWorkbookImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(options);

        return RunWorkbookImportAsync(new ImportInput(source, null), sheets, options, cancellationToken);
    }

    /// <summary>Imports several sheets in the configured order.</summary>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The workbook report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExcelWorkbookImportReport> ImportAsync(
        string path,
        ExcelWorkbookImport sheets,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(sheets);

        return RunWorkbookImportAsync(new ImportInput(null, path), sheets, null, cancellationToken);
    }

    /// <summary>Imports several sheets in the configured order.</summary>
    /// <param name="path">The local path; the library opens and closes the file.</param>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="options">Call-wide settings.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The workbook report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExcelWorkbookImportReport> ImportAsync(
        string path,
        ExcelWorkbookImport sheets,
        ExcelWorkbookImportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(options);

        return RunWorkbookImportAsync(new ImportInput(null, path), sheets, options, cancellationToken);
    }

    /// <summary>Exports several sheets in registration order.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        Stream target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(target);

        return Report(RunWorkbookExportAsync(sheets, new StreamExportTarget(target), null, cancellationToken));
    }

    /// <summary>Exports several sheets in registration order.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="target">The output stream; it stays open.</param>
    /// <param name="options">Call-wide settings.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        Stream target,
        ExcelWorkbookExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(RunWorkbookExportAsync(sheets, new StreamExportTarget(target), options, cancellationToken));
    }

    /// <summary>Exports several sheets in registration order.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        string path,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(path);

        return Report(RunWorkbookExportAsync(sheets, new PathExportTarget(path), null, cancellationToken));
    }

    /// <summary>Exports several sheets in registration order.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="path">The local path; published atomically.</param>
    /// <param name="options">Call-wide settings.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        string path,
        ExcelWorkbookExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);

        return Report(RunWorkbookExportAsync(sheets, new PathExportTarget(path), options, cancellationToken));
    }

    /// <summary>Exports several sheets in registration order; the file is returned only on complete success.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<BufferedExportResult> ExportAsync(
        ExcelWorkbookExport sheets,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);

        return Buffered(RunWorkbookExportAsync(sheets, new BufferedExportTarget(), null, cancellationToken));
    }

    /// <summary>Exports several sheets in registration order; the file is returned only on complete success.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="options">Call-wide settings.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report and the file.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<BufferedExportResult> ExportAsync(
        ExcelWorkbookExport sheets,
        ExcelWorkbookExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(options);

        return Buffered(RunWorkbookExportAsync(sheets, new BufferedExportTarget(), options, cancellationToken));
    }

    /// <summary>Exports several sheets in registration order.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        TabularFileTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(target);

        return Report(RunWorkbookExportAsync(sheets, new FileStoreExportTarget(target), null, cancellationToken));
    }

    /// <summary>Exports several sheets in registration order.</summary>
    /// <param name="sheets">The sheet definitions.</param>
    /// <param name="target">The storage target.</param>
    /// <param name="options">Call-wide settings.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The report.</returns>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public Task<ExportReport> ExportAsync(
        ExcelWorkbookExport sheets,
        TabularFileTarget target,
        ExcelWorkbookExportOptions options,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        return Report(RunWorkbookExportAsync(sheets, new FileStoreExportTarget(target), options, cancellationToken));
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task<ExcelWorkbookImportReport> RunWorkbookImportAsync(
        ImportInput input,
        ExcelWorkbookImport definition,
        ExcelWorkbookImportOptions? options,
        CancellationToken cancellationToken
    )
    {
        var workbook = options ?? new ExcelWorkbookImportOptions();
        var defaults = _options.Import ?? new ExcelImportOptions();
        var strategy = workbook.Strategy ?? defaults.Strategy ?? ImportStrategy.Strict;
        using var scope = _runtime.Scope("import",
            "excel",
            strategy == ImportStrategy.Strict
                ? "strict"
                : "partial");

        return await new WorkbookImportRun(_runtime,
                input,
                definition,
                workbook,
                defaults,
                scope,
                cancellationToken)
            .RunAsync()
            .ConfigureAwait(false);
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task<ExportRunResult> RunWorkbookExportAsync(
        ExcelWorkbookExport definition,
        ExportTarget target,
        ExcelWorkbookExportOptions? options,
        CancellationToken cancellationToken
    )
    {
        var workbook = options ?? new ExcelWorkbookExportOptions();
        var defaults = _options.Export ?? new ExcelExportOptions();
        var errors = new ErrorCollector(workbook.MaxErrorDetails ?? defaults.MaxErrorDetails);
        using var scope = _runtime.Scope("export", "excel", "workbook");
        var configuration = new List<TabularError>(_runtime.Problems);
        if (ErrorCollector.LimitProblem(workbook.MaxErrorDetails ?? defaults.MaxErrorDetails) is { } limitProblem)
        {
            configuration.Add(limitProblem);
        }

        CheckWorkbookExport(definition, target, configuration);
        var tables = new ExcelTableNames();
        var sheets = definition
            .Sheets
            .Select(sheet => sheet.Prepare(workbook, defaults, tables, configuration))
            .ToList();

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < configuration.Count; errorIndex++)
        {
            var error = configuration[errorIndex];
            errors.AddFatal(error);
        }

        return await ExportRunner
            .RunAsync(target,
                configuration.Count > 0
                    ? ExportRunner.NothingProduced
                    : (stream, token) => ProduceWorkbookAsync(sheets, errors, stream, token),
                errors,
                scope,
                _runtime.Limits,
                "excel",
                TabularType.Excel.ContentType,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void CheckWorkbookExport(
        ExcelWorkbookExport definition,
        ExportTarget target,
        List<TabularError> configuration
    )
    {
        CheckTarget(target, configuration);
        if (definition.Sheets.Count == 0)
        {
            configuration.Add(TabularError.Configuration("A multi-sheet export needs at least one sheet."));
        }

        // WHY: Each duplicate group adds one configuration error; a query would hide that side effect.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var group in definition
            .Sheets
            .GroupBy(sheet => sheet.Name)
            .Where(group => group.Count() > 1))
        {
            configuration.Add(TabularError.Configuration($"The sheet name {group.Key} is used more than once.",
                new TabularErrorLocation { SheetName = group.Key }));
        }
    }

    /// <summary>Writes every sheet in order; the first unsuccessful sheet ends the workbook.</summary>
    private static async Task<ProduceResult> ProduceWorkbookAsync(
        List<PreparedExportSheet?> sheets,
        ErrorCollector errors,
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        await using var writer = new ExcelWorkbookWriter(stream);
        long read = 0;
        long written = 0;

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
        {
            var sheet = sheets[sheetIndex];
            var outcome = await sheet!
                .WriteAsync(writer, errors, cancellationToken)
                .ConfigureAwait(false);

            read += outcome.RowsRead;
            written += outcome.RowsWritten;
            if (!outcome.Success)
            {
                return new ProduceResult(false, outcome.Canceled, read, written);
            }
        }

        await writer
            .CompleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProduceResult(true, false, read, written);
    }
}
