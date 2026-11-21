namespace Doka.Tabular.Excel;

/// <summary>Excel part of a single-sheet import: package checks, variant, sheet choice, record source.</summary>
internal sealed class ExcelImportFormat : IImportFormat
{
    private readonly ExcelVariant? _expected;
    private readonly ResourceLimits _limits;
    private readonly ExcelReadSettings _settings;
    private readonly TabularSheetName? _sheetName;

    public ExcelImportFormat(
        TabularSheetName? sheetName,
        ExcelVariant? expected,
        ExcelReadSettings settings,
        ResourceLimits limits
    )
    {
        _sheetName = sheetName;
        _expected = expected;
        _settings = settings;
        _limits = limits;
    }

    public bool NeedsSeekableInput => true;

    public async ValueTask<(IRecordSource? Source, TabularError? Error, IAsyncDisposable? Owned)> OpenAsync(
        PreparedInput input,
        ConversionContext context,
        CancellationToken cancellationToken
    )
    {
        var (package, error) = await ExcelPackage
            .OpenAsync(input.Stream, _limits, cancellationToken)
            .ConfigureAwait(false);

        if (package is null)
        {
            return (null, error, null);
        }

        var variantError = CheckVariant(package, _expected);
        if (variantError is not null)
        {
            await package
                .DisposeAsync()
                .ConfigureAwait(false);

            return (null, variantError, null);
        }

        var (sheet, sheetError) = SelectSheet(package, _sheetName);
        if (sheet is null)
        {
            await package
                .DisposeAsync()
                .ConfigureAwait(false);

            return (null, sheetError, null);
        }

        context.Uses1904DateSystem = package.Uses1904DateSystem;
        var source = new ExcelSheetSource(package, sheet, _settings);

        return (source, null, new CombinedDisposable(source, package));
    }

    internal static TabularError? CheckVariant(
        ExcelPackage package,
        ExcelVariant? expected
    ) => expected is { } wanted && wanted != package.DetectedVariant
        ? TabularError.Create(TabularErrorCodes.Schema.FormatMismatch,
            TabularErrorKind.Data,
            $"The workbook is {package.DetectedVariant}, but {wanted} was expected.")
        : null;

    private static (ExcelSheetInfo? Sheet, TabularError? Error) SelectSheet(
        ExcelPackage package,
        TabularSheetName? name
    )
    {
        var names = string.Join(", ", package.Sheets.Select(sheet => sheet.Name.Value));
        if (name is not null)
        {
            var found = package.Sheets.FirstOrDefault(sheet => sheet.Name == name);

            return found is null
                ? (null, TabularError.Create(TabularErrorCodes.Schema.MissingSheet,
                    TabularErrorKind.Data,
                    $"The sheet {name} does not exist; the workbook contains: {names}.",
                    new TabularErrorLocation { SheetName = name }))
                : (found, null);
        }

        return package.Sheets.Count == 1
            ? (package.Sheets[0], null)
            : (null, TabularError.Create(
                package.Sheets.Count == 0
                    ? TabularErrorCodes.Schema.MissingSheet
                    : TabularErrorCodes.Schema.AmbiguousSheet,
                TabularErrorKind.Data,
                package.Sheets.Count == 0
                    ? "The workbook contains no sheet."
                    : $"The workbook contains several sheets ({names}); choose one with SheetName."));
    }
}
