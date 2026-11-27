namespace Doka.Tabular;

/// <summary>
/// One single-table import: opens the input, passes the journal gate, reads the table, writes to the sink, and
/// builds the report. The run owns its state exclusively; every step records its effect on that state. Disposing
/// the run releases the source, the prepared input, the opened file, and the sink, after the report is built.
/// </summary>
internal sealed partial class ImportRun<TItem> : IAsyncDisposable
{
    private readonly CancellationToken _cancellationToken;
    private readonly ConversionContext _context;
    private readonly ErrorCollector _errors;
    private readonly TemporaryFiles _files;
    private readonly IImportFormat _format;
    private readonly ImportInput _input;
    private readonly ResourceLimits _limits;
    private readonly TableOutcome<TItem> _outcome = new();
    private readonly IReadOnlyList<TabularError> _preconditions;
    private readonly OperationScope _scope;
    private readonly ImportSettings _settings;
    private readonly ImportShape<TItem> _shape;
    private readonly IImportSink<TItem>? _sink;
    private bool _canceled;
    private bool _configurationFailed;
    private bool _gateStop;
    private ImportJournalResult? _journal;
    private FileStream? _ownedFile;
    private PreparedInput? _prepared;
    private IAsyncDisposable? _sourceResources;
    private SinkOutcome<TItem>? _written;

    public ImportRun(
        ImportInput input,
        IImportFormat format,
        ImportShape<TItem> shape,
        ImportSettings settings,
        ConversionContext context,
        IImportSink<TItem>? sink,
        ResourceLimits limits,
        OperationScope scope,
        IReadOnlyList<TabularError> preconditions,
        CancellationToken cancellationToken
    )
    {
        _input = input;
        _format = format;
        _shape = shape;
        _settings = settings;
        _context = context;
        _sink = sink;
        _limits = limits;
        _scope = scope;
        _preconditions = preconditions;
        _cancellationToken = cancellationToken;
        _errors = new ErrorCollector(settings.MaxErrorDetails);
        _files = new TemporaryFiles(limits);
    }

    private bool Strict => _settings.Strategy == ImportStrategy.Strict;

    /// <summary>Releases the source, the prepared input, the opened file, and the sink, in this order.</summary>
    public async ValueTask DisposeAsync()
    {
        await ReleaseInputAsync()
            .ConfigureAwait(false);

        if (_sink is not null)
        {
            await _sink
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public async Task<ImportReport<TItem>> RunAsync()
    {
        _scope.Started();
        try
        {
            await ReadAsync()
                .ConfigureAwait(false);

            return await FinishAsync()
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _canceled = true;

            return await FinishAsync()
                .ConfigureAwait(false);
        }
        catch (TabularResourceException resource)
        {
            _errors.AddFatal(resource.Error);

            return await FinishAsync()
                .ConfigureAwait(false);
        }

        // WHY: The cancellation the call requested is caught above; any other one is a failure like these.
        catch (Exception exception) when (exception is OperationCanceledException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ObjectDisposedException)
        {
            _errors.AddFatal(
                TabularError.Create(
                    TabularErrorCodes.Io.Failed,
                    TabularErrorKind.Technical,
                    "Reading the input failed."));

            return await FinishAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>Runs every step up to the sink; a step that stops the import has recorded why.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task ReadAsync()
    {
        if (await PrepareAsync()
                .ConfigureAwait(false) is not { } plan)
        {
            return;
        }

        if (_cancellationToken.IsCancellationRequested)
        {
            _canceled = true;

            return;
        }

        if (OpenInput() is not { } stream)
        {
            return;
        }

        // WHY: The import log starts before the input is hashed, so that an abort while reading it is logged.
        if (_sink is { RequiresInputHash: true } journaled)
        {
            var start = await journaled
                .StartJournalAsync(_cancellationToken)
                .ConfigureAwait(false);

            _journal = start.Journal;
            if (!Passed(start))
            {
                return;
            }
        }

        _prepared = await PreparedInput
            .CreateAsync(
                stream,
                _format.NeedsSeekableInput,
                _sink is { RequiresInputHash: true },
                _limits,
                _files,
                _cancellationToken)
            .ConfigureAwait(false);

        // WHY: The pattern binds the checked non-null value; inlining would read the field or property again.
        // ReSharper disable once InlineTemporaryVariable
        if (_sink is { RequiresInputHash: true } gated
            && !await PassGateAsync(gated, _prepared)
                .ConfigureAwait(false))
        {
            return;
        }

        if (await OpenSourceAsync(_prepared)
                .ConfigureAwait(false) is not { } source)
        {
            return;
        }

        await ImportEngine
            .ReadTableAsync(source, plan, _outcome, _errors, _cancellationToken)
            .ConfigureAwait(false);

        _canceled = _outcome.Canceled;
        if (_sink is not null
            && !_canceled)
        {
            await WriteAsync(_sink)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Builds the plan and prepares the sink; <see langword="null" /> when the configuration fails.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task<ImportPlan<TItem>?> PrepareAsync()
    {
        var configuration = new List<TabularError>(_preconditions);
        if (_input.Problem is { } inputProblem)
        {
            configuration.Add(inputProblem);
        }

        var plan = ImportPlan<TItem>.Prepare(_shape, _settings, _context, configuration);
        if (_sink is not null
            && configuration.Count == 0)
        {
            var preparation = await _sink
                .PrepareAsync(
                    new ImportSinkStart(
                        _settings.Strategy,
                        false,
                        _scope.OperationId,
                        _scope.Time),
                    _cancellationToken)
                .ConfigureAwait(false);

            configuration.AddRange(preparation.Errors);
            if (preparation.Fatal is not null)
            {
                _errors.AddFatal(preparation.Fatal);
            }
        }

        if (configuration.Count == 0
            && _errors.Fatal is null)
        {
            return plan;
        }

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < configuration.Count; errorIndex++)
        {
            var error = configuration[errorIndex];
            _errors.Add(error);
        }

        _configurationFailed = true;

        return null;
    }

    /// <summary>The caller's stream, or the opened input file; <see langword="null" /> when it cannot be opened.</summary>
    private Stream? OpenInput()
    {
        if (_input.Path is not { } path)
        {
            return _input.Stream!;
        }

        try
        {
            _ownedFile = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return _ownedFile;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            // WHY: A path the file system rejects by its form is a configuration error, like on export.
            _errors.AddFatal(TabularError.Configuration("The input path is not valid."));

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _errors.AddFatal(
                TabularError.Create(
                    TabularErrorCodes.Io.Failed,
                    TabularErrorKind.Technical,
                    "The input file could not be opened."));

            return null;
        }
    }

    /// <summary>Asks the journal whether the file is read; <see langword="false" /> stops the import.</summary>
    private async Task<bool> PassGateAsync(
        IImportSink<TItem> sink,
        PreparedInput prepared
    )
    {
        var gate = await sink
            .GateAsync(prepared.Hash!, prepared.ByteSize ?? 0, _cancellationToken)
            .ConfigureAwait(false);

        // WHY: A failed gate decides nothing; the report keeps the reference to the started log.
        _journal = gate.Journal ?? _journal;
        if (!Passed(gate))
        {
            return false;
        }

        _gateStop = gate.Gate != ImportGate.Proceed;

        return !_gateStop;
    }

    /// <summary>Takes the errors of a journal step; <see langword="false" /> when the step ends the import.</summary>
    private bool Passed(
        SinkPreparation gate
    )
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < gate.Errors.Count; errorIndex++)
        {
            var error = gate.Errors[errorIndex];
            _errors.Add(error);
        }

        if (gate.Fatal is not null)
        {
            _errors.AddFatal(gate.Fatal);

            return false;
        }

        if (gate.Canceled)
        {
            _canceled = true;

            return false;
        }

        return true;
    }

    /// <summary>Checks the content and opens the record source; <see langword="null" /> when that fails.</summary>
    private async Task<IRecordSource?> OpenSourceAsync(
        PreparedInput prepared
    )
    {
        var (source, openError, owned) = await _format
            .OpenAsync(prepared, _context, _cancellationToken)
            .ConfigureAwait(false);

        _sourceResources = owned;
        if (openError is null)
        {
            return source!;
        }

        if (openError.Kind == TabularErrorKind.Technical)
        {
            _errors.AddFatal(openError);
        }
        else
        {
            _errors.Add(openError);
            _configurationFailed = true;
        }

        return null;
    }

    /// <summary>Writes the valid rows; a strict import with errors writes nothing.</summary>
    private async Task WriteAsync(
        IImportSink<TItem> sink
    )
    {
        var rows = Strict && (_outcome.HadDataErrors || _outcome.Aborted || _outcome.SchemaFailed)
            ? []
            : _outcome.Valid;

        if (rows.Count == 0
            && Strict)
        {
            return;
        }

        _written = await sink
            .WriteAsync(
                rows,
                new ImportSinkWrite(_settings.Strategy, _outcome.Aborted, true),
                _cancellationToken)
            .ConfigureAwait(false);

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < _written.Errors.Count; errorIndex++)
        {
            var error = _written.Errors[errorIndex];
            _errors.Add(error);
        }

        if (_written.Fatal is not null)
        {
            _errors.AddFatal(_written.Fatal);
        }

        if (_written.PersistenceUncertain)
        {
            _scope.PersistenceUncertain();
        }

        _canceled |= _written.Canceled;
    }

    /// <summary>
    /// Releases the source, the prepared input, and the opened file; before the cleanup of the call, so that a
    /// temporary file whose delete fails is reported.
    /// </summary>
    private async Task ReleaseInputAsync()
    {
        if (_sourceResources is not null)
        {
            await _sourceResources
                .DisposeAsync()
                .ConfigureAwait(false);

            _sourceResources = null;
        }

        if (_prepared is not null)
        {
            await _prepared
                .DisposeAsync()
                .ConfigureAwait(false);

            _prepared = null;
        }

        if (_ownedFile is not null)
        {
            await _ownedFile
                .DisposeAsync()
                .ConfigureAwait(false);

            _ownedFile = null;
        }
    }

    /// <summary>How the read ended, as needed for status, items, and completeness.</summary>
    private readonly record struct Ending(
        bool Aborted,
        bool DataFailed
    );
}
