namespace Doka.Tabular.Excel;

/// <summary>
/// Prepared Excel format with defaults, limits, and diagnostics. Imports XLSX Transitional, XLSX Strict, and XLSM;
/// exports XLSX Transitional. Instances are immutable and can be shared.
/// </summary>
[PublicAPI]
public sealed partial class ExcelFormat
{
    private readonly ExcelFormatOptions _options;
    private readonly FormatRuntime _runtime;

    /// <summary>Creates the format.</summary>
    /// <param name="options">Infrastructure, limits, and call defaults.</param>
    public ExcelFormat(
        ExcelFormatOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _runtime = new FormatRuntime(
            options.Diagnostics,
            options.TimeProvider,
            new ResourceLimits(
                options.ImportBufferBytes,
                options.OutputBufferBytes,
                options.AllowSpill,
                options.TemporaryDirectory ?? Path.GetTempPath(),
                options.MaxTemporaryBytes,
                options.MaxInputBytes,
                options.MaxFieldCharacters,
                options.MaxExpandedBytes),
            "Doka.Tabular.Excel");
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    internal async Task<ImportReport<TItem>> RunImportAsync<TItem>(
        ImportInput input,
        ImportShape<TItem> shape,
        TabularImportDestination<TItem>? destination,
        ExcelImportOptions? options,
        CancellationToken cancellationToken
    )
    {
        var call = Merge(options);
        var problems = new List<TabularError>(_runtime.Problems);
        if (destination?.MapProblem(shape.Map) is { } mapProblem)
        {
            problems.Add(mapProblem);
        }

        var settings = ImportSettingsOf(call, destination?.SourceKeys);
        var startRow = call.StartRow ?? 1;
        CheckImport(call, startRow, settings, problems);
        var context = new ConversionContext(TabularFormatKind.Excel,
            settings.Culture,
            settings.TrimFields,
            settings.Translations,
            settings.MissingTranslation);

        var format = new ExcelImportFormat(
            call.SheetName,
            call.ExpectedVariant,
            new ExcelReadSettings(startRow,
                call.EndRow,
                call.VisibleRowsOnly ?? false,
                settings.Binding != ColumnBinding.ByIndex),
            _runtime.Limits);

        using var scope = _runtime.Scope("import",
            "excel",
            settings.Strategy == ImportStrategy.Strict
                ? "strict"
                : "partial");

        return await ImportRunner
            .RunAsync(input,
                format,
                shape,
                settings,
                context,
                destination?.CreateSink(new ImportCall()),
                _runtime.Limits,
                scope,
                problems,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ImportSettings ImportSettingsOf(
        ExcelImportOptions call,
        IReadOnlyList<TabularSourceKey>? targetKeys
    ) => new()
    {
        Strategy = call.Strategy ?? ImportStrategy.Strict,
        Binding = call.Binding ?? ColumnBinding.ByIndex,
        TrimFields = call.TrimFields ?? false,
        Culture = call.Culture ?? CultureInfo.InvariantCulture,
        Translations = call.Translations,
        MissingTranslation = call.MissingTranslation ?? MissingTranslationBehavior.Error,
        NormalizeHeader = call.NormalizeHeader,
        MaxErrorDetails = call.MaxErrorDetails,
        SourceKey = call.SourceKey,
        TargetKeys = targetKeys ?? [],
    };

    private static void CheckImport(
        ExcelImportOptions call,
        int startRow,
        ImportSettings settings,
        List<TabularError> problems
    )
    {
        if (startRow < 1
            || call.EndRow < startRow
            || call.EndRow > ExcelWorkbookWriter.MaxRows)
        {
            problems.Add(TabularError.Configuration(
                "StartRow and EndRow must lie within the sheet and EndRow must not precede StartRow."));
        }

        if (ErrorCollector.LimitProblem(settings.MaxErrorDetails) is { } limitProblem)
        {
            problems.Add(limitProblem);
        }
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    internal async Task<ExportRunResult> RunExportAsync<TSource>(
        ExportSource<TSource>? source,
        ExportShape<TSource> shape,
        ExportTarget target,
        ExcelExportOptions? options,
        CancellationToken cancellationToken
    )
    {
        var call = Merge(options);
        var errors = new ErrorCollector(call.MaxErrorDetails);
        var layout = call.Layout ?? new ExcelLayoutOptions();
        using var scope = _runtime.Scope("export",
            "excel",
            layout.Mode == ExcelLayoutMode.Table
                ? "table"
                : "range");

        var configuration = new List<TabularError>(_runtime.Problems);
        if (ErrorCollector.LimitProblem(call.MaxErrorDetails) is { } limitProblem)
        {
            configuration.Add(limitProblem);
        }

        if (source is null)
        {
            configuration.Add(TabularError.Configuration(
                "The query does not support asynchronous enumeration; it would be read synchronously."));
        }

        CheckTarget(target, configuration);
        var settings = ExportSettingsOf(call);
        var plan = ExportPlan<TSource>.Prepare(shape, settings, ExportContext(settings), configuration);
        var sheetName = call.SheetName ?? TabularSheetName.Create("Export");
        var sheetPlan = plan is null
            ? null
            : ExcelLayoutValidation.Plan(sheetName,
                layout,
                plan.Columns,
                plan.ExportKeys,
                settings.WriteHeader,
                new ExcelTableNames(),
                configuration);

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < configuration.Count; errorIndex++)
        {
            var error = configuration[errorIndex];
            errors.AddFatal(error);
        }

        Func<Stream, CancellationToken, Task<ProduceResult>> produce = configuration.Count > 0
            ? ExportRunner.NothingProduced
            : new SheetExport<TSource>(source!, plan!, sheetPlan!, sheetName, settings.WriteHeader, errors)
                .ProduceAsync;

        return await ExportRunner
            .RunAsync(target,
                produce,
                errors,
                scope,
                _runtime.Limits,
                "excel",
                TabularType.Excel.ContentType,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ExportSettings ExportSettingsOf(
        ExcelExportOptions call
    ) => new()
    {
        WriteHeader = call.WriteHeader ?? false,
        Culture = call.Culture ?? CultureInfo.InvariantCulture,
        Translations = call.Translations,
        MissingTranslation = call.MissingTranslation ?? MissingTranslationBehavior.Error,
        ColumnKeys = call.ColumnKeys,
    };

    private static ConversionContext ExportContext(
        ExportSettings settings
    ) => new(TabularFormatKind.Excel,
        settings.Culture,
        false,
        settings.Translations,
        settings.MissingTranslation);

    internal static void CheckTarget(
        ExportTarget target,
        List<TabularError> errors
    )
    {
        if (target is PathExportTarget { Path: var path }
            && Path.GetExtension(path) is { } extension
            && (extension.Equals(".xls", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xlsb", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(TabularError.Configuration(
                "The export writes XLSX; a target path ending in .xls, .xlsm, or .xlsb is rejected."));
        }
    }

    private ExcelImportOptions Merge(
        ExcelImportOptions? call
    )
    {
        var defaults = _options.Import ?? new ExcelImportOptions();
        if (call is null)
        {
            return defaults;
        }

        return new ExcelImportOptions
        {
            Strategy = call.Strategy ?? defaults.Strategy,
            Binding = call.Binding ?? defaults.Binding,
            TrimFields = call.TrimFields ?? defaults.TrimFields,
            Culture = call.Culture ?? defaults.Culture,
            Translations = call.Translations ?? defaults.Translations,
            MissingTranslation = call.MissingTranslation ?? defaults.MissingTranslation,
            NormalizeHeader = call.NormalizeHeader ?? defaults.NormalizeHeader,
            MaxErrorDetails = call.MaxErrorDetails ?? defaults.MaxErrorDetails,
            SourceKey = call.SourceKey ?? defaults.SourceKey,
            SheetName = call.SheetName ?? defaults.SheetName,
            StartRow = call.StartRow ?? defaults.StartRow,
            EndRow = call.EndRow ?? defaults.EndRow,
            VisibleRowsOnly = call.VisibleRowsOnly ?? defaults.VisibleRowsOnly,
            ExpectedVariant = call.ExpectedVariant ?? defaults.ExpectedVariant,
        };
    }

    private ExcelExportOptions Merge(
        ExcelExportOptions? call
    )
    {
        var defaults = _options.Export ?? new ExcelExportOptions();
        if (call is null)
        {
            return defaults;
        }

        return new ExcelExportOptions
        {
            WriteHeader = call.WriteHeader ?? defaults.WriteHeader,
            Culture = call.Culture ?? defaults.Culture,
            Translations = call.Translations ?? defaults.Translations,
            MissingTranslation = call.MissingTranslation ?? defaults.MissingTranslation,
            ColumnKeys = call.ColumnKeys ?? defaults.ColumnKeys,
            MaxErrorDetails = call.MaxErrorDetails ?? defaults.MaxErrorDetails,
            SheetName = call.SheetName ?? defaults.SheetName,
            Layout = call.Layout ?? defaults.Layout,
        };
    }

    private static async Task<ExportReport> Report(
        Task<ExportRunResult> run
    ) => (await run.ConfigureAwait(false)).Report;

    private static async Task<BufferedExportResult> Buffered(
        Task<ExportRunResult> run
    )
    {
        var result = await run.ConfigureAwait(false);

        return new BufferedExportResult(result.Report, result.File);
    }

    /// <summary>The single sheet of a table export, written into a new workbook.</summary>
    private sealed record SheetExport<TSource>(
        ExportSource<TSource> Source,
        ExportPlan<TSource> Plan,
        ExcelSheetPlan SheetPlan,
        TabularSheetName SheetName,
        bool WriteHeader,
        ErrorCollector Errors
    )
    {
        public async Task<ProduceResult> ProduceAsync(
            Stream stream,
            CancellationToken cancellationToken
        )
        {
            await using var workbook = new ExcelWorkbookWriter(stream);
            TableExportOutcome outcome;
            await using (var sheet = await workbook
                .AddSheetAsync(SheetPlan, cancellationToken)
                .ConfigureAwait(false))
            {
                outcome = await ExportEngine
                    .WriteTableAsync(Source,
                        Plan,
                        WriteHeader,
                        sheet,
                        Errors,
                        SheetName,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (outcome.Success)
            {
                await workbook
                    .CompleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return new ProduceResult(outcome.Success, outcome.Canceled, outcome.RowsRead, outcome.RowsWritten);
        }
    }
}
