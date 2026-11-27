using System.Text;

namespace Doka.Tabular.Csv;

/// <summary>
/// Prepared CSV format with defaults, limits, and diagnostics. Instances are immutable and can be shared; every call
/// has its own budgets and state.
/// </summary>
[PublicAPI]
public sealed partial class CsvFormat
{
    private static readonly UTF8Encoding
        s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly CsvFormatOptions _options;
    private readonly FormatRuntime _runtime;

    /// <summary>Creates the format.</summary>
    /// <param name="options">Infrastructure, limits, and call defaults.</param>
    public CsvFormat(
        CsvFormatOptions options
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
                null),
            "Doka.Tabular.Csv");
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    internal async Task<ImportReport<TItem>> RunImportAsync<TItem>(
        ImportInput input,
        ImportShape<TItem> shape,
        TabularImportDestination<TItem>? destination,
        CsvImportOptions? options,
        CancellationToken cancellationToken
    )
    {
        var call = Merge(options);
        var problems = new List<TabularError>(_runtime.Problems);
        if (destination?.MapProblem(shape.Map) is { } mapProblem)
        {
            problems.Add(mapProblem);
        }

        var delimiter = call.Delimiter ?? ',';
        CheckDelimiter(delimiter, problems);
        var settings = ImportSettingsOf(call, destination?.SourceKeys);
        if (ErrorCollector.LimitProblem(settings.MaxErrorDetails) is { } limitProblem)
        {
            problems.Add(limitProblem);
        }

        var context = new ConversionContext(
            TabularFormatKind.Csv,
            settings.Culture,
            settings.TrimFields,
            settings.Translations,
            settings.MissingTranslation);

        var format = new CsvImportFormat(
            new CsvReadSettings(
                delimiter,
                call.AllowLineBreaksInFields ?? true,
                call.RequireCrLf ?? false,
                call.EmptyLinesAsRecords ?? false,
                _runtime.Limits.MaxFieldCharacters),
            call.Encoding ?? s_utf8);

        using var scope = _runtime.Scope(
            "import",
            "csv",
            settings.Strategy == ImportStrategy.Strict
                ? "strict"
                : "partial");

        return await ImportRunner
            .RunAsync(
                input,
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
        CsvImportOptions call,
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

    private static void CheckDelimiter(
        char delimiter,
        List<TabularError> problems
    )
    {
        if (delimiter is '"' or '\r' or '\n')
        {
            problems.Add(
                TabularError.Configuration("The delimiter must not be a quote, carriage return, or line feed."));
        }
    }

    /// <summary>The export encoding must write the delimiter; quote and line breaks are in every supported encoding.</summary>
    private static void CheckEncodable(
        char delimiter,
        Encoding encoding,
        List<TabularError> problems
    )
    {
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        try
        {
            strict.GetByteCount([delimiter]);
        }
        catch (EncoderFallbackException)
        {
            problems.Add(TabularError.Configuration($"The encoding {encoding.WebName} cannot write the delimiter."));
        }
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    internal async Task<ExportRunResult> RunExportAsync<TSource>(
        ExportSource<TSource>? source,
        ExportShape<TSource> shape,
        ExportTarget target,
        CsvExportOptions? options,
        CancellationToken cancellationToken
    )
    {
        var call = Merge(options);
        var errors = new ErrorCollector(call.MaxErrorDetails);
        using var scope = _runtime.Scope("export", "csv", "table");
        var configuration = new List<TabularError>(_runtime.Problems);
        if (ErrorCollector.LimitProblem(call.MaxErrorDetails) is { } limitProblem)
        {
            configuration.Add(limitProblem);
        }

        var delimiter = call.Delimiter ?? ',';
        CheckDelimiter(delimiter, configuration);
        CheckEncodable(delimiter, call.Encoding ?? s_utf8, configuration);
        if (source is null)
        {
            configuration.Add(
                TabularError.Configuration(
                    "The query does not support asynchronous enumeration; it would be read synchronously."));
        }

        var settings = ExportSettingsOf(call);
        var context = new ConversionContext(
            TabularFormatKind.Csv,
            settings.Culture,
            false,
            settings.Translations,
            settings.MissingTranslation);

        var plan = ExportPlan<TSource>.Prepare(shape, settings, context, configuration);

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < configuration.Count; errorIndex++)
        {
            var error = configuration[errorIndex];
            errors.AddFatal(error);
        }

        Func<Stream, CancellationToken, Task<ProduceResult>> produce = configuration.Count > 0
            ? ExportRunner.NothingProduced
            : new TableExport<TSource>(
                    source!,
                    plan!,
                    new CsvWriteSettings(delimiter, call.Encoding ?? s_utf8, call.WriteBom ?? false),
                    settings.WriteHeader,
                    errors)
                .ProduceAsync;

        return await ExportRunner
            .RunAsync(
                target,
                produce,
                errors,
                scope,
                _runtime.Limits,
                "csv",
                TabularType.Csv.ContentType,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ExportSettings ExportSettingsOf(
        CsvExportOptions call
    ) => new()
    {
        WriteHeader = call.WriteHeader ?? false,
        Culture = call.Culture ?? CultureInfo.InvariantCulture,
        Translations = call.Translations,
        MissingTranslation = call.MissingTranslation ?? MissingTranslationBehavior.Error,
        ColumnKeys = call.ColumnKeys,
    };

    private CsvImportOptions Merge(
        CsvImportOptions? call
    )
    {
        var defaults = _options.Import ?? new CsvImportOptions();
        if (call is null)
        {
            return defaults;
        }

        return new CsvImportOptions
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
            Delimiter = call.Delimiter ?? defaults.Delimiter,
            Encoding = call.Encoding ?? defaults.Encoding,
            AllowLineBreaksInFields = call.AllowLineBreaksInFields ?? defaults.AllowLineBreaksInFields,
            RequireCrLf = call.RequireCrLf ?? defaults.RequireCrLf,
            EmptyLinesAsRecords = call.EmptyLinesAsRecords ?? defaults.EmptyLinesAsRecords,
        };
    }

    private CsvExportOptions Merge(
        CsvExportOptions? call
    )
    {
        var defaults = _options.Export ?? new CsvExportOptions();
        if (call is null)
        {
            return defaults;
        }

        return new CsvExportOptions
        {
            WriteHeader = call.WriteHeader ?? defaults.WriteHeader,
            Culture = call.Culture ?? defaults.Culture,
            Translations = call.Translations ?? defaults.Translations,
            MissingTranslation = call.MissingTranslation ?? defaults.MissingTranslation,
            ColumnKeys = call.ColumnKeys ?? defaults.ColumnKeys,
            MaxErrorDetails = call.MaxErrorDetails ?? defaults.MaxErrorDetails,
            Delimiter = call.Delimiter ?? defaults.Delimiter,
            Encoding = call.Encoding ?? defaults.Encoding,
            WriteBom = call.WriteBom ?? defaults.WriteBom,
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

    /// <summary>A table export into one CSV document.</summary>
    private sealed record TableExport<TSource>(
        ExportSource<TSource> Source,
        ExportPlan<TSource> Plan,
        CsvWriteSettings WriteSettings,
        bool WriteHeader,
        ErrorCollector Errors
    )
    {
        public async Task<ProduceResult> ProduceAsync(
            Stream stream,
            CancellationToken cancellationToken
        )
        {
            await using var writer = new CsvTableWriter(stream, WriteSettings);
            var outcome = await ExportEngine
                .WriteTableAsync(
                    Source,
                    Plan,
                    WriteHeader,
                    writer,
                    Errors,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);

            return new ProduceResult(outcome.Success, outcome.Canceled, outcome.RowsRead, outcome.RowsWritten);
        }
    }
}
