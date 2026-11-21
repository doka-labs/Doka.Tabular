namespace Doka.Tabular.Excel;

internal abstract class WorkbookExportSheet
{
    protected WorkbookExportSheet(
        TabularSheetName name,
        ExcelSheetExportOptions options
    )
    {
        Name = name;
        Options = options;
    }

    public TabularSheetName Name { get; }

    protected ExcelSheetExportOptions Options { get; }

    /// <summary>Prepares the plan with the effective settings; returns <see langword="null" /> with errors.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public abstract PreparedExportSheet? Prepare(
        ExcelWorkbookExportOptions workbook,
        ExcelExportOptions defaults,
        ExcelTableNames tables,
        List<TabularError> errors
    );
}

internal sealed class WorkbookExportSheet<T> : WorkbookExportSheet
{
    private readonly ExportShape<T> _shape;
    private readonly ExportSource<T>? _source;

    public WorkbookExportSheet(
        TabularSheetName name,
        ExportSource<T>? source,
        ExportShape<T> shape,
        ExcelSheetExportOptions options
    )
        : base(name, options)
    {
        _source = source;
        _shape = shape;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public override PreparedExportSheet? Prepare(
        ExcelWorkbookExportOptions workbook,
        ExcelExportOptions defaults,
        ExcelTableNames tables,
        List<TabularError> errors
    )
    {
        if (_source is null)
        {
            errors.Add(TabularError.Configuration(
                "The query does not support asynchronous enumeration; it would be read synchronously.",
                new TabularErrorLocation { SheetName = Name }));

            return null;
        }

        var settings = SettingsOf(workbook, defaults);
        var context = new ConversionContext(TabularFormatKind.Excel,
            settings.Culture,
            false,
            settings.Translations,
            settings.MissingTranslation);

        var sheetErrors = new List<TabularError>();
        var plan = ExportPlan<T>.Prepare(_shape, settings, context, sheetErrors);
        var layout = Options.Layout ?? workbook.Layout ?? defaults.Layout ?? new ExcelLayoutOptions();
        var sheetPlan = plan is null
            ? null
            : ExcelLayoutValidation.Plan(Name,
                layout,
                plan.Columns,
                plan.ExportKeys,
                settings.WriteHeader,
                tables,
                sheetErrors);

        errors.AddRange(sheetErrors.Select(WithSheet));

        return sheetPlan is null
            ? null
            : new Prepared(sheetPlan, settings.WriteHeader, _source, plan!);
    }

    /// <summary>The settings of the sheet: sheet options first, then workbook options, then format defaults.</summary>
    private ExportSettings SettingsOf(
        ExcelWorkbookExportOptions workbook,
        ExcelExportOptions defaults
    ) => new()
    {
        WriteHeader = Options.WriteHeader ?? workbook.WriteHeader ?? defaults.WriteHeader ?? false,
        Culture = Options.Culture ?? workbook.Culture ?? defaults.Culture ?? CultureInfo.InvariantCulture,
        Translations = Options.Translations ?? workbook.Translations ?? defaults.Translations,
        MissingTranslation =
            workbook.MissingTranslation ?? defaults.MissingTranslation ?? MissingTranslationBehavior.Error,
        ColumnKeys = Options.ColumnKeys,
    };

    /// <summary>Adds the sheet name to an error of the sheet that has no sheet location yet.</summary>
    private TabularError WithSheet(
        TabularError error
    ) => error.Location.SheetName is null
        ? error.WithLocation(new TabularErrorLocation
        {
            SheetName = Name,
            ColumnKey = error.Location.ColumnKey,
            PropertyPath = error.Location.PropertyPath,
            ColumnIndex = error.Location.ColumnIndex,
        })
        : error;

    private sealed class Prepared : PreparedExportSheet
    {
        private readonly ExportPlan<T> _plan;
        private readonly ExportSource<T> _source;

        [SetsRequiredMembers]
        public Prepared(
            ExcelSheetPlan plan,
            bool writeHeader,
            ExportSource<T> source,
            ExportPlan<T> exportPlan
        )
        {
            Plan = plan;
            WriteHeader = writeHeader;
            _source = source;
            _plan = exportPlan;
        }

        public override async Task<TableExportOutcome> WriteAsync(
            ExcelWorkbookWriter workbook,
            ErrorCollector errors,
            CancellationToken cancellationToken
        )
        {
            await using var sheet = await workbook
                .AddSheetAsync(Plan, cancellationToken)
                .ConfigureAwait(false);

            return await ExportEngine
                .WriteTableAsync(_source,
                    _plan,
                    WriteHeader,
                    sheet,
                    errors,
                    Plan.Name,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
