using Doka.Tabular.Csv;
using Doka.Tabular.Excel;

namespace Doka.Tabular;

/// <summary>Common export entry point over prepared format instances; the format is given with every call.</summary>
[PublicAPI]
public sealed partial class TabularExport
{
    private readonly CsvFormat? _csv;
    private readonly ExcelFormat? _excel;

    /// <summary>Prepares the entry point for CSV.</summary>
    /// <param name="csv">The prepared CSV format.</param>
    public TabularExport(
        CsvFormat csv
    )
    {
        ArgumentNullException.ThrowIfNull(csv);
        _csv = csv;
    }

    /// <summary>Prepares the entry point for Excel.</summary>
    /// <param name="excel">The prepared Excel format.</param>
    public TabularExport(
        ExcelFormat excel
    )
    {
        ArgumentNullException.ThrowIfNull(excel);
        _excel = excel;
    }

    /// <summary>Prepares the entry point for CSV and Excel.</summary>
    /// <param name="csv">The prepared CSV format.</param>
    /// <param name="excel">The prepared Excel format.</param>
    public TabularExport(
        CsvFormat csv,
        ExcelFormat excel
    )
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(excel);
        _csv = csv;
        _excel = excel;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private Task<ExportRunResult> RunExportAsync<TSource>(
        TabularType type,
        ExportSource<TSource>? source,
        ExportShape<TSource> shape,
        ExportTarget target,
        TabularExportOptions? options,
        CancellationToken cancellationToken
    )
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The format is not defined.");
        }

        return type switch
        {
            // WHY: The pattern binds the checked non-null value; inlining would read the field or property again.
            // ReSharper disable once InlineTemporaryVariable
            TabularType.Csv when _csv is { } csv
                => csv.RunExportAsync(source, shape, target, options?.ForCsv(), cancellationToken),

            // WHY: The pattern binds the checked non-null value; inlining would read the field or property again.
            // ReSharper disable once InlineTemporaryVariable
            TabularType.Excel when _excel is { } excel
                => excel.RunExportAsync(source, shape, target, options?.ForExcel(), cancellationToken),
            _ => Task.FromResult(NotPrepared(type, target, MaxErrorDetailsOf(type, options))),
        };
    }

    /// <summary>The detail limit of the requested format, the inner setting before the common one.</summary>
    private static int? MaxErrorDetailsOf(
        TabularType type,
        TabularExportOptions? options
    ) => type == TabularType.Csv
        ? options?.ForCsv()
            .MaxErrorDetails
        : options?.ForExcel()
            .MaxErrorDetails;

    private static ExportRunResult NotPrepared(
        TabularType type,
        ExportTarget target,
        int? maxErrorDetails
    )
    {
        var errors = new ErrorCollector(maxErrorDetails);
        var error = TabularError.Configuration($"The format {type} was not prepared for this entry point.");
        errors.AddFatal(error);
        if (ErrorCollector.LimitProblem(maxErrorDetails) is { } limitProblem)
        {
            errors.Add(limitProblem);
        }

        var report = new ExportReport(
            new ExportReportData(
                ReportStatus.Failed,
                false,
                0,
                0,
                errors.Snapshot(),
                errors.TotalCount,
                errors.OmittedCount,
                errors.Fatal,
                target is PathExportTarget
                    ? PathOutputState.NotPublished
                    : null,
                target is StreamExportTarget
                    ? StreamOutputState.NotWritten
                    : null,
                null,
                new TabularReportDiagnostics(Guid.NewGuid())));

        return new ExportRunResult(report, null);
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
}
