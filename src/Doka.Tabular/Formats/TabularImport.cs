using Doka.Tabular.Csv;
using Doka.Tabular.Excel;

namespace Doka.Tabular;

/// <summary>
/// Common import entry point over prepared format instances. The format is given with every call; the library never
/// detects it, but checks the content against it.
/// </summary>
[PublicAPI]
public sealed partial class TabularImport
{
    private readonly CsvFormat? _csv;
    private readonly ExcelFormat? _excel;

    /// <summary>Prepares the entry point for CSV.</summary>
    /// <param name="csv">The prepared CSV format.</param>
    public TabularImport(
        CsvFormat csv
    )
    {
        ArgumentNullException.ThrowIfNull(csv);
        _csv = csv;
    }

    /// <summary>Prepares the entry point for Excel.</summary>
    /// <param name="excel">The prepared Excel format.</param>
    public TabularImport(
        ExcelFormat excel
    )
    {
        ArgumentNullException.ThrowIfNull(excel);
        _excel = excel;
    }

    /// <summary>Prepares the entry point for CSV and Excel.</summary>
    /// <param name="csv">The prepared CSV format.</param>
    /// <param name="excel">The prepared Excel format.</param>
    public TabularImport(
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
    private Task<ImportReport<TItem>> RunImportAsync<TItem>(
        TabularType type,
        ImportInput input,
        ImportShape<TItem> shape,
        TabularImportDestination<TItem>? destination,
        TabularImportOptions? options,
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
                => csv.RunImportAsync(input, shape, destination, options?.ForCsv(), cancellationToken),

            // WHY: The pattern binds the checked non-null value; inlining would read the field or property again.
            // ReSharper disable once InlineTemporaryVariable
            TabularType.Excel when _excel is { } excel
                => excel.RunImportAsync(input, shape, destination, options?.ForExcel(), cancellationToken),
            _ => NotPreparedAsync<TItem>(type, MaxErrorDetailsOf(type, options)),
        };
    }

    /// <summary>The detail limit of the requested format, the inner setting before the common one.</summary>
    private static int? MaxErrorDetailsOf(
        TabularType type,
        TabularImportOptions? options
    ) => type == TabularType.Csv
        ? options?.ForCsv()
            .MaxErrorDetails
        : options?.ForExcel()
            .MaxErrorDetails;

    private static Task<ImportReport<TItem>> NotPreparedAsync<TItem>(
        TabularType type,
        int? maxErrorDetails
    )
    {
        var errors = new ErrorCollector(maxErrorDetails);
        errors.Add(TabularError.Configuration($"The format {type} was not prepared for this entry point."));
        if (ErrorCollector.LimitProblem(maxErrorDetails) is { } limitProblem)
        {
            errors.Add(limitProblem);
        }

        var diagnostics = new TabularReportDiagnostics(Guid.NewGuid());

        return Task.FromResult(
            new ImportReport<TItem>(
                new ImportReportData(
                    ReportStatus.Failed,
                    false,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    errors.Snapshot(),
                    errors.TotalCount,
                    errors.OmittedCount,
                    null,
                    diagnostics,
                    null),
                []));
    }
}
