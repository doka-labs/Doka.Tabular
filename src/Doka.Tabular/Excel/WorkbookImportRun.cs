namespace Doka.Tabular.Excel;

/// <summary>
/// One multi-sheet import: checks the sheet definitions and write targets, passes the journal gate, reads every
/// sheet, writes per sheet or in one shared transaction, and builds the workbook report. The run owns its state
/// exclusively; every step records its effect on that state.
/// </summary>
internal sealed partial class WorkbookImportRun
{
    private readonly CancellationToken _cancellationToken;
    private readonly List<TabularError> _configuration;
    private readonly ExcelImportOptions _defaults;
    private readonly ExcelWorkbookImport _definition;
    private readonly ErrorCollector _errors;
    private readonly TemporaryFiles _files;
    private readonly ImportInput _input;
    private readonly List<IImportSinkHandle> _journals;
    private readonly bool _perSheet;
    private readonly List<PreparedImportSheet> _prepared;
    private readonly SheetRun?[] _runs;
    private readonly FormatRuntime _runtime;
    private readonly OperationScope _scope;
    private readonly List<IImportSinkHandle> _sinks;
    private readonly ReportStatus[] _statuses;
    private readonly ImportStrategy _strategy;
    private readonly ExcelWorkbookImportOptions _workbook;
    private readonly SinkCounts?[] _written;
    private bool _canceled;
    private bool _configurationFailed;
    private bool _gateStop;
    private ImportJournalResult? _journal;

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public WorkbookImportRun(
        FormatRuntime runtime,
        ImportInput input,
        ExcelWorkbookImport definition,
        ExcelWorkbookImportOptions workbook,
        ExcelImportOptions defaults,
        OperationScope scope,
        CancellationToken cancellationToken
    )
    {
        _runtime = runtime;
        _input = input;
        _definition = definition;
        _workbook = workbook;
        _defaults = defaults;
        _scope = scope;
        _cancellationToken = cancellationToken;
        _strategy = workbook.Strategy ?? defaults.Strategy ?? ImportStrategy.Strict;
        _errors = new ErrorCollector(workbook.MaxErrorDetails ?? defaults.MaxErrorDetails);
        _files = new TemporaryFiles(runtime.Limits);
        _configuration = [.. runtime.Problems];
        if (ErrorCollector.LimitProblem(workbook.MaxErrorDetails ?? defaults.MaxErrorDetails) is { } limitProblem)
        {
            _configuration.Add(limitProblem);
        }

        if (input.Problem is { } inputProblem)
        {
            _configuration.Add(inputProblem);
        }

        var call = new ImportCall();

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _prepared = definition
            .Sheets
            .Select(sheet => sheet.Prepare(workbook, defaults, call, _configuration))
            .ToList();

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _sinks = _prepared
            .Select(sheet => sheet.Sink)
            .OfType<IImportSinkHandle>()
            .ToList();

        _perSheet = _sinks is [{ TransactionPerSheet: true }, ..];

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _journals = _sinks
            .Where(sink => sink.RequiresInputHash)
            .ToList();

        _runs = new SheetRun?[_prepared.Count];
        _written = new SinkCounts?[_prepared.Count];
        _statuses = new ReportStatus[_prepared.Count];
        Array.Fill(_statuses, ReportStatus.NotStarted);
    }

    /// <summary>One shared transaction over all sheets: a strict import without a transaction per sheet.</summary>
    private bool Shared => _strategy == ImportStrategy.Strict && !_perSheet;

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public async Task<ExcelWorkbookImportReport> RunAsync()
    {
        CheckDefinition();
        try
        {
            await PrepareSinksAsync()
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            // WHY: A cancellation while a target is prepared still ends with a report and releases every target.
            _canceled = true;
        }

        _scope.Started();
        if (_configuration.Count > 0
            || _errors.Fatal is not null)
        {
            // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var errorIndex = 0; errorIndex < _configuration.Count; errorIndex++)
            {
                var error = _configuration[errorIndex];
                _errors.Add(error);
            }

            _configurationFailed = true;
        }
        else if (!_canceled)
        {
            await ReadGuardedAsync()
                .ConfigureAwait(false);
        }

        CleanUpFiles();
        await ReleaseSinksAsync()
            .ConfigureAwait(false);

        return await FinishAsync()
            .ConfigureAwait(false);
    }

    private void CheckDefinition()
    {
        if (_definition.Sheets.Count == 0)
        {
            _configuration.Add(TabularError.Configuration("A multi-sheet import needs at least one sheet."));
        }

        if (_definition
            .Sheets
            .GroupBy(sheet => sheet.Name)
            .Any(group => group.Count() > 1))
        {
            _configuration.Add(TabularError.Configuration("A sheet is configured more than once."));
        }

        if (_sinks.Any(sink => sink.TransactionPerSheet != _perSheet))
        {
            _configuration.Add(
                TabularError.Configuration(
                    "All write targets of a workbook must use the same TransactionPerSheet setting."));
        }

        if (Shared
            && _sinks
                .Select(sink => sink.TransactionGroup)
                .Distinct()
                .Count()
            > 1)
        {
            _configuration.Add(
                TabularError.Configuration(
                    "A strict workbook import over several databases cannot be atomic; use one TabularDatabase or TransactionPerSheet."));
        }

        if (_journals.Count > 1)
        {
            _configuration.Add(
                TabularError.Configuration(
                    "One file has one import log; only one write target of a workbook may have an import log."));
        }

        // WHY: Only the database of the import log writes the commit evidence together with its data; a commit in
        // another database could not be proven after a crash, and the duplicate protection would rest on a decision
        // of the application.
        if (_journals.Count > 0
            && _sinks
                .Select(sink => sink.TransactionGroup)
                .Distinct()
                .Count()
            > 1)
        {
            _configuration.Add(
                TabularError.Configuration(
                    "A workbook with an import log writes to one TabularDatabase; the log cannot prove commits in another one."));
        }
    }

    /// <summary>Prepares the write targets in order; the first configuration problem stops the preparation.</summary>
    private async Task PrepareSinksAsync()
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var sinkIndex = 0; sinkIndex < _sinks.Count; sinkIndex++)
        {
            var sink = _sinks[sinkIndex];
            if (_configuration.Count > 0)
            {
                return;
            }

            var preparation = await sink
                .PrepareAsync(
                    new ImportSinkStart(
                        _strategy,
                        true,
                        _scope.OperationId,
                        _scope.Time),
                    _cancellationToken)
                .ConfigureAwait(false);

            _configuration.AddRange(preparation.Errors);
            if (preparation.Fatal is not null)
            {
                _errors.AddFatal(preparation.Fatal);
            }
        }
    }

    /// <summary>
    /// Releases the own resources of every write target before the report and the first completion callback; sinks
    /// of the sheets can share one session. A failure is the fatal error of the run and does not prevent the report.
    /// </summary>
    private async Task ReleaseSinksAsync()
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var sinkIndex = 0; sinkIndex < _sinks.Count; sinkIndex++)
        {
            try
            {
                await _sinks[sinkIndex]
                    .ReleaseAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _errors.AddFatal(
                    TabularError.Create(
                        TabularErrorCodes.Persistence.OperationFailed,
                        TabularErrorKind.Technical,
                        "The own data context of the import could not be released."));
            }
        }
    }

    private void CleanUpFiles()
    {
        if (_files.CleanupAll())
        {
            return;
        }

        _errors.AddFatal(
            TabularError.Create(
                TabularErrorCodes.Io.Failed,
                TabularErrorKind.Technical,
                "An own temporary file could not be deleted."));

        _scope.CleanupFailed();
    }
}
