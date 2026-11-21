namespace Doka.Tabular.Excel;

internal abstract class WorkbookImportSheet
{
    protected WorkbookImportSheet(
        object handle,
        TabularSheetName name,
        ExcelSheetImportOptions options
    )
    {
        Handle = handle;
        Name = name;
        Options = options;
    }

    /// <summary>Gets the typed handle that the builder returned for this sheet.</summary>
    public object Handle { get; }

    public TabularSheetName Name { get; }

    protected ExcelSheetImportOptions Options { get; }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public abstract PreparedImportSheet Prepare(
        ExcelWorkbookImportOptions workbook,
        ExcelImportOptions defaults,
        ImportCall call,
        List<TabularError> errors
    );
}

internal sealed class WorkbookImportSheet<T> : WorkbookImportSheet
{
    private readonly TabularImportDestination<T>? _destination;
    private readonly ImportShape<T> _shape;

    public WorkbookImportSheet(
        ExcelImportSheet<T> handle,
        ImportShape<T> shape,
        TabularImportDestination<T>? destination,
        ExcelSheetImportOptions options
    )
        : base(handle, handle.Name, options)
    {
        _shape = shape;
        _destination = destination;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public override PreparedImportSheet Prepare(
        ExcelWorkbookImportOptions workbook,
        ExcelImportOptions defaults,
        ImportCall call,
        List<TabularError> errors
    )
    {
        var settings = new ImportSettings
        {
            Strategy = workbook.Strategy ?? defaults.Strategy ?? ImportStrategy.Strict,
            Binding = Options.Binding ?? workbook.Binding ?? defaults.Binding ?? ColumnBinding.ByIndex,
            TrimFields = Options.TrimFields ?? workbook.TrimFields ?? defaults.TrimFields ?? false,
            Culture = Options.Culture ?? workbook.Culture ?? defaults.Culture ?? CultureInfo.InvariantCulture,
            Translations = Options.Translations ?? workbook.Translations ?? defaults.Translations,
            MissingTranslation =
                workbook.MissingTranslation ?? defaults.MissingTranslation ?? MissingTranslationBehavior.Error,
            NormalizeHeader = workbook.NormalizeHeader ?? defaults.NormalizeHeader,
            MaxErrorDetails = workbook.MaxErrorDetails ?? defaults.MaxErrorDetails,
            SourceKey = Options.SourceKey,
            TargetKeys = _destination?.SourceKeys ?? [],
        };

        var startRow = Options.StartRow ?? 1;
        if (startRow < 1
            || Options.EndRow < startRow)
        {
            errors.Add(TabularError.Configuration("StartRow and EndRow must lie within the sheet.",
                new TabularErrorLocation { SheetName = Name }));
        }

        var context = new ConversionContext(TabularFormatKind.Excel,
            settings.Culture,
            settings.TrimFields,
            settings.Translations,
            settings.MissingTranslation);

        var sheetErrors = new List<TabularError>();
        if (_destination?.MapProblem(_shape.Map) is { } mapProblem)
        {
            sheetErrors.Add(mapProblem);
        }

        var plan = ImportPlan<T>.Prepare(_shape, settings, context, sheetErrors);
        errors.AddRange(sheetErrors.Select(error => error.Location.SheetName is null
            ? error.WithLocation(new TabularErrorLocation
            {
                SheetName = Name,
                ColumnKey = error.Location.ColumnKey,
                PropertyPath = error.Location.PropertyPath,
            })
            : error));

        return new Prepared(plan, _destination?.CreateSink(call))
        {
            Name = Name,
            ReadSettings = new ExcelReadSettings(startRow,
                Options.EndRow,
                Options.VisibleRowsOnly ?? false,
                settings.Binding != ColumnBinding.ByIndex),
            Context = context,
        };
    }

    private sealed class Prepared : PreparedImportSheet
    {
        private readonly ImportPlan<T> _plan;
        private readonly SinkHandle? _sink;

        public Prepared(
            ImportPlan<T> plan,
            IImportSink<T>? sink
        )
        {
            _plan = plan;
            _sink = sink is null
                ? null
                : new SinkHandle(sink);
        }

        public override IImportSinkHandle? Sink => _sink;

        [RequiresDynamicCode(
            "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
        [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
        public override async Task<SheetRun> ReadAsync(
            ExcelPackage package,
            ExcelSheetInfo sheet,
            ErrorCollector errors,
            CancellationToken cancellationToken
        )
        {
            Context.Uses1904DateSystem = package.Uses1904DateSystem;
            await using var source = new ExcelSheetSource(package, sheet, ReadSettings);
            var outcome = new TableOutcome<T>();
            try
            {
                await ImportEngine
                    .ReadTableAsync(source, _plan, outcome, errors, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // WHY: The rows checked before a technical abort stay with the workbook report of this sheet.
                LastRun = RunOf(outcome);

                throw;
            }

            return LastRun = RunOf(outcome);
        }

        private Run RunOf(
            TableOutcome<T> outcome
        ) => new(this, outcome)
        {
            Counts = new TableCounts(outcome.RowsRead,
                outcome.RowsRejected,
                outcome.HadDataErrors,
                outcome.Aborted,
                outcome.Canceled,
                outcome.SchemaFailed,
                outcome.Valid.Count),
        };

        public override object EmptyReport(
            ReportStatus status,
            TabularReportDiagnostics diagnostics
        ) => new ImportReport<T>(new ImportReportData(status,
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                [],
                0,
                0,
                null,
                diagnostics,
                null),
            _sink is null
                ? []
                : null);

        private sealed class Run : SheetRun
        {
            private readonly TableOutcome<T> _outcome;
            private readonly Prepared _owner;
            private SinkOutcome<T>? _written;

            public Run(
                Prepared owner,
                TableOutcome<T> outcome
            )
            {
                _owner = owner;
                _outcome = outcome;
            }

            public override async Task<SinkCounts?> WriteAsync(
                ImportSinkWrite write,
                ErrorCollector errors,
                CancellationToken cancellationToken
            )
            {
                if (_owner._sink is null)
                {
                    return null;
                }

                var rows = write.Strategy == ImportStrategy.Strict && Counts.HasErrors
                    ? []
                    : _outcome.Valid;

                _written = await _owner
                    ._sink
                    .Sink
                    .WriteAsync(rows, write, cancellationToken)
                    .ConfigureAwait(false);

                return Collect(_written, errors);
            }

            public override async Task<SinkCounts?> AbandonAsync(
                ErrorCollector errors
            )
            {
                if (_owner._sink is null)
                {
                    return null;
                }

                _written = await _owner
                    ._sink
                    .Sink
                    .AbandonAsync()
                    .ConfigureAwait(false);

                return Collect(_written, errors);
            }

            /// <summary>The valid items of an import without sink, or none when they are not released.</summary>
            private List<T> ValidItems(
                bool release
            ) => release

                // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
                // ReSharper disable once UseCollectionExpression
                ? _outcome
                    .Valid
                    .Select(row => row.Item)
                    .ToList()
                : [];

            public override object Report(
                ReportStatus status,
                SinkCounts? written,
                bool releaseItems,
                TabularReportDiagnostics diagnostics
            )
            {
                var items = _owner._sink is null
                    ? ValidItems(releaseItems)
                    : _written?.Items;

                var imported = _owner._sink is null
                    ? items!.Count
                    : written?.RowsImported ?? 0;

                var data = new ImportReportData(
                    status,
                    Counts is { Aborted: false, Canceled: false, SchemaFailed: false }
                    && written is not ({ Failed: true } or { Canceled: true }),
                    Counts.RowsRead,
                    imported,
                    Counts.RowsRejected + (written?.RowsRejected ?? 0),
                    written?.RowsSkipped ?? 0,
                    written?.RowsPendingCommit ?? 0,
                    written?.RowsUncertain ?? 0,
                    [],
                    0,
                    0,
                    null,
                    diagnostics,
                    null);

                return new ImportReport<T>(data, items);
            }

            private static SinkCounts Collect(
                SinkOutcome<T> outcome,
                ErrorCollector errors
            )
            {
                // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var errorIndex = 0; errorIndex < outcome.Errors.Count; errorIndex++)
                {
                    var error = outcome.Errors[errorIndex];
                    errors.Add(error);
                }

                if (outcome.Fatal is not null)
                {
                    errors.AddFatal(outcome.Fatal);
                }

                // WHY: A technical end of the writing phase fails the sheet; one after a confirmed commit does not.
                return new SinkCounts(outcome.RowsImported,
                    outcome.RowsPendingCommit,
                    outcome.RowsSkipped,
                    outcome.RowsUncertain,
                    outcome.RowsRejected,
                    outcome.Errors.Count > 0 || outcome.Fatal is not null || outcome.RowsRejected > 0,
                    outcome.Canceled,
                    outcome.CommitConfirmed,
                    outcome.Fatal is not null && !outcome.CommitConfirmed,
                    outcome.ScopeRolledBack,
                    outcome.PersistenceUncertain);
            }
        }

        private sealed class SinkHandle : IImportSinkHandle
        {
            public SinkHandle(
                IImportSink<T> sink
            )
            {
                Sink = sink;
            }

            public IImportSink<T> Sink { get; }

            public bool RequiresInputHash => Sink.RequiresInputHash;

            public object TransactionGroup => Sink.TransactionGroup;

            public bool TransactionPerSheet => Sink.TransactionPerSheet;

            public Task<SinkPreparation> PrepareAsync(
                ImportSinkStart start,
                CancellationToken cancellationToken
            ) => Sink.PrepareAsync(start, cancellationToken);

            public Task<SinkPreparation> StartJournalAsync(
                CancellationToken cancellationToken
            ) => Sink.StartJournalAsync(cancellationToken);

            public Task<SinkPreparation> GateAsync(
                Sha256Hash hash,
                long byteSize,
                CancellationToken cancellationToken
            ) => Sink.GateAsync(hash, byteSize, cancellationToken);

            public ValueTask ReleaseAsync() => Sink.ReleaseAsync();

            public Task<IReadOnlyList<TabularError>> FinishAsync(
                ImportReportData report
            ) => Sink.FinishAsync(report);

            public ValueTask DisposeAsync() => Sink.DisposeAsync();
        }
    }
}
