namespace Doka.Tabular.Excel;

internal sealed partial class WorkbookImportRun
{
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task ReadGuardedAsync()
    {
        try
        {
            await ReadWorkbookAsync()
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _canceled = true;
        }
        catch (TabularResourceException resource)
        {
            _errors.AddFatal(resource.Error);
        }

        // WHY: The cancellation the call requested is caught above; any other one is a failure like these.
        catch (Exception exception) when (exception is OperationCanceledException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            _errors.AddFatal(TabularError.Create(TabularErrorCodes.Io.Failed,
                TabularErrorKind.Technical,
                "Reading the input failed."));
        }

        // WHY: However the reading ended, a call that stops before every sheet is written leaves no import rows
        // pending in an application transaction.
        if (_canceled
            || _errors.Fatal is not null)
        {
            await AbandonPendingScopesAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>Opens the input and the package and reads the sheets; the input is released before the report.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task ReadWorkbookAsync()
    {
        FileStream? ownedFile = null;
        PreparedInput? input = null;
        ExcelPackage? package = null;
        try
        {
            var stream = _input.Stream;
            if (_input.Path is { } path)
            {
                try
                {
                    ownedFile = new FileStream(path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        FileOptions.Asynchronous);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    // WHY: A path the file system rejects by its form is a configuration error, like on export.
                    _errors.AddFatal(TabularError.Configuration("The input path is not valid."));

                    return;
                }

                stream = ownedFile;
            }

            // WHY: The import log starts before the input is hashed, so that an abort while reading it is logged.
            if (_journals is [var journal])
            {
                var start = await journal
                    .StartJournalAsync(_cancellationToken)
                    .ConfigureAwait(false);

                _journal = start.Journal;
                if (!Passed(start))
                {
                    return;
                }
            }

            input = await PreparedInput
                .CreateAsync(stream!,
                    needsSeekable: true,
                    _journals.Count == 1,
                    _runtime.Limits,
                    _files,
                    _cancellationToken)
                .ConfigureAwait(false);

            if (!await PassGateAsync(input)
                .ConfigureAwait(false))
            {
                return;
            }

            package = await OpenPackageAsync(input)
                .ConfigureAwait(false);

            if (package is not null
                && !_configurationFailed
                && LocateSheets(package) is { } located)
            {
                await ProcessSheetsAsync(package, located)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await ReleaseAsync(package, input, ownedFile)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Asks the import log whether the file is read; <see langword="false" /> stops the import.</summary>
    private async Task<bool> PassGateAsync(
        PreparedInput input
    )
    {
        if (_journals is [var journal])
        {
            var gate = await journal
                .GateAsync(input.Hash!, input.ByteSize ?? 0, _cancellationToken)
                .ConfigureAwait(false);

            // WHY: A failed gate decides nothing; the report keeps the reference to the started log.
            _journal = gate.Journal ?? _journal;
            if (!Passed(gate))
            {
                return false;
            }

            _gateStop = gate.Gate != ImportGate.Proceed;
        }

        return _errors.Fatal is null && !_canceled && !_gateStop;
    }

    /// <summary>Takes the errors of a journal step; <see langword="false" /> when the step ends the import.</summary>
    private bool Passed(
        SinkPreparation step
    )
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < step.Errors.Count; errorIndex++)
        {
            var error = step.Errors[errorIndex];
            _errors.Add(error);
        }

        if (step.Fatal is not null)
        {
            _errors.AddFatal(step.Fatal);
        }

        _canceled |= step.Canceled;

        return step.Fatal is null && !step.Canceled;
    }

    /// <summary>Opens the package; an open or variant error is a configuration failure, the package is returned.</summary>
    private async Task<ExcelPackage?> OpenPackageAsync(
        PreparedInput input
    )
    {
        var (package, openError) = await ExcelPackage
            .OpenAsync(input.Stream, _runtime.Limits, _cancellationToken)
            .ConfigureAwait(false);

        openError ??= package is null
            ? null
            : ExcelImportFormat.CheckVariant(package, _workbook.ExpectedVariant ?? _defaults.ExpectedVariant);

        if (openError is not null)
        {
            _errors.Add(openError);
            _configurationFailed = true;
        }

        return package;
    }

    /// <summary>Finds every configured sheet; <see langword="null" /> when one is missing.</summary>
    private ExcelSheetInfo[]? LocateSheets(
        ExcelPackage package
    )
    {
        var located = new ExcelSheetInfo[_prepared.Count];
        for (var index = 0; index < _prepared.Count; index++)
        {
            var name = _prepared[index].Name;
            if (package.Sheets.FirstOrDefault(candidate => candidate.Name == name) is { } sheet)
            {
                located[index] = sheet;

                continue;
            }

            _errors.Add(TabularError.Create(TabularErrorCodes.Schema.MissingSheet,
                TabularErrorKind.Data,
                $"The sheet {name} does not exist; the workbook contains: {string.Join(", ", package.Sheets.Select(item => item.Name.Value))}.",
                new TabularErrorLocation { SheetName = name }));

            _configurationFailed = true;
        }

        return _configurationFailed
            ? null
            : located;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task ProcessSheetsAsync(
        ExcelPackage package,
        ExcelSheetInfo[] located
    )
    {
        for (var index = 0; index < _prepared.Count; index++)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                _canceled = true;

                return;
            }

            if (!await ReadSheetAsync(package, located[index], index)
                .ConfigureAwait(false))
            {
                return;
            }
        }

        if (Shared)
        {
            await WriteSharedAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>Reads one sheet and, without a shared transaction, writes it; <see langword="false" /> stops.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private async Task<bool> ReadSheetAsync(
        ExcelPackage package,
        ExcelSheetInfo sheet,
        int index
    )
    {
        SheetRun run;
        try
        {
            run = await _prepared[index]
                .ReadAsync(package, sheet, _errors, _cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _runs[index] = _prepared[index].LastRun;
            _statuses[index] = ReportStatus.Failed;

            throw;
        }

        _runs[index] = run;
        if (run.Counts.Canceled)
        {
            _canceled = true;
            _statuses[index] = ReportStatus.Canceled;

            return false;
        }

        if (!Shared)
        {
            _written[index] = await run
                .WriteAsync(new ImportSinkWrite(_strategy, run.Counts.Aborted, true),
                    _errors,
                    _cancellationToken)
                .ConfigureAwait(false);

            _statuses[index] = SheetStatus(run.Counts, _written[index]);
            if (_written[index] is { ScopeRolledBack: true })
            {
                RolledBackEarlier(index);
            }

            if (_written[index] is { Canceled: true })
            {
                _canceled = true;

                return false;
            }

            return !run.Counts.Aborted && _errors.Fatal is null;
        }

        if (run.Counts.HasErrors)
        {
            Array.Fill(_statuses, ReportStatus.Failed, 0, index + 1);

            return false;
        }

        _statuses[index] = ReportStatus.Succeeded;

        return true;
    }

    /// <summary>Writes all sheets in one transaction; the first failing sheet abandons every earlier one.</summary>
    private async Task WriteSharedAsync()
    {
        var lastWriter = _prepared.FindLastIndex(sheet => sheet.Sink is not null);
        for (var index = 0; index < _prepared.Count; index++)
        {
            _written[index] = await _runs[index]!
                .WriteAsync(new ImportSinkWrite(_strategy, false, index == lastWriter),
                    _errors,
                    _cancellationToken)
                .ConfigureAwait(false);

            // WHY: When the last writer confirmed the commit of the shared transaction, every earlier sheet is stored;
            // an error after the commit, such as a failing release, must not abandon them.
            if (_written[index] is { CommitConfirmed: true }
                && (_written[index] is { HadErrors: true } || _errors.Fatal is not null))
            {
                Array.Fill(_statuses, ReportStatus.Succeeded, 0, index + 1);
                Array.Fill(_statuses, ReportStatus.NotStarted, index + 1, _statuses.Length - index - 1);

                return;
            }

            if (_written[index] is { HadErrors: true } or { Canceled: true }
                || _errors.Fatal is not null)
            {
                await AbandonAsync(index)
                    .ConfigureAwait(false);

                return;
            }

            _statuses[index] = ReportStatus.Succeeded;
        }
    }

    private async Task AbandonAsync(
        int failed
    )
    {
        for (var earlier = 0; earlier < failed; earlier++)
        {
            _written[earlier] = await _runs[earlier]!
                .AbandonAsync(_errors)
                .ConfigureAwait(false);
        }

        Array.Fill(_statuses, ReportStatus.Failed, 0, failed + 1);
        Array.Fill(_statuses, ReportStatus.NotStarted, failed + 1, _statuses.Length - failed - 1);
        _canceled |= _written[failed] is { Canceled: true };
    }

    /// <summary>
    /// Marks the earlier sheets of the same transaction group as failed, whose pending rows a rollback of the sheet
    /// at <paramref name="rolledBack" /> removed as well.
    /// </summary>
    private void RolledBackEarlier(
        int rolledBack
    )
    {
        var group = _prepared[rolledBack].Sink!.TransactionGroup;
        for (var earlier = 0; earlier < rolledBack; earlier++)
        {
            if (_written[earlier] is { } written
                && ReferenceEquals(_prepared[earlier].Sink?.TransactionGroup, group))
            {
                _written[earlier] = written with
                {
                    RowsPendingCommit = 0,
                    Failed = true,
                };

                _statuses[earlier] = ReportStatus.Failed;
            }
        }
    }

    /// <summary>
    /// Rolls back the shared import scope of an application transaction when the call stops early; the sheets of that
    /// scope then hold no pending rows, or uncertain ones when the rollback is not confirmed.
    /// </summary>
    private async Task AbandonPendingScopesAsync()
    {
        if (Shared)
        {
            return;
        }

        var status = _errors.Fatal is null
            ? ReportStatus.Canceled
            : ReportStatus.Failed;

        var abandoned = new List<object>();
        for (var earlier = _prepared.Count - 1; earlier >= 0; earlier--)
        {
            // WHY: Only an application transaction leaves rows pending; with own scopes per sheet they stay.
            if (_written[earlier] is not { RowsPendingCommit: > 0 }
                || _prepared[earlier].Sink is not { TransactionPerSheet: false } sink
                || abandoned.Contains(sink.TransactionGroup))
            {
                continue;
            }

            abandoned.Add(sink.TransactionGroup);
            var rollback = await _runs[earlier]!
                .AbandonAsync(_errors)
                .ConfigureAwait(false);

            // WHY: A rollback that failed may have left the rows in the application transaction.
            var confirmed = rollback is not ({ Failed: true } or { PersistenceUncertain: true });
            for (var member = 0; member <= earlier; member++)
            {
                if (_written[member] is { } written
                    && ReferenceEquals(_prepared[member].Sink?.TransactionGroup, sink.TransactionGroup))
                {
                    _written[member] = written with
                    {
                        RowsPendingCommit = 0,
                        RowsUncertain = written.RowsUncertain
                        + (confirmed
                            ? 0
                            : written.RowsPendingCommit),
                        Canceled = status == ReportStatus.Canceled,
                        Failed = status == ReportStatus.Failed || !confirmed,
                    };

                    _statuses[member] = confirmed
                        ? status
                        : ReportStatus.Failed;
                }
            }
        }
    }

    private ReportStatus SheetStatus(
        TableCounts counts,
        SinkCounts? written
    ) => new ImportStatusFacts(Canceled: counts.Canceled || written is { Canceled: true },
            Failed: counts.Aborted || counts.SchemaFailed || written is { Failed: true },
            GateStopped: false,
            Skipped: false,
            HadErrors: counts.HasErrors || written is { HadErrors: true },
            ErrorsFail: _strategy == ImportStrategy.Strict)
        .Decide();

    private static async Task ReleaseAsync(
        ExcelPackage? package,
        PreparedInput? input,
        FileStream? ownedFile
    )
    {
        if (package is not null)
        {
            await package
                .DisposeAsync()
                .ConfigureAwait(false);
        }

        if (input is not null)
        {
            await input
                .DisposeAsync()
                .ConfigureAwait(false);
        }

        if (ownedFile is not null)
        {
            await ownedFile
                .DisposeAsync()
                .ConfigureAwait(false);
        }
    }
}
