namespace Doka.Tabular;

internal sealed partial class ImportRun<TItem>
{
    private async Task<ImportReport<TItem>> FinishAsync()
    {
        await ReleaseInputAsync()
            .ConfigureAwait(false);

        CleanUpFiles();
        await ReleaseSinkAsync()
            .ConfigureAwait(false);

        var ending = new Ending(Aborted: _outcome.Aborted || _errors.Fatal is not null,
            DataFailed: _outcome.HadDataErrors
            || (_written?.RowsRejected ?? 0) > 0
            || (_written?.Errors.Count ?? 0) > 0);

        var status = new ImportStatusFacts(Canceled: _canceled && _errors.Fatal is null,
                Failed: _configurationFailed || ending.Aborted || _outcome.SchemaFailed,
                GateStopped: _gateStop,
                Skipped: _journal is { Decision: ImportJournalDecision.Skipped },
                HadErrors: ending.DataFailed,
                ErrorsFail: Strict)
            .Decide();

        AddDuplicateError();
        var (items, imported) = Items(ending);
        var data = await CompleteSinkAsync(ReportData(status, ending, imported))
            .ConfigureAwait(false);

        ReportCompleted(data);

        return new ImportReport<TItem>(data, items);
    }

    /// <summary>
    /// Releases the own resources of the write target before the report and any completion callback; a failure is
    /// the fatal error of the run and does not prevent the report.
    /// </summary>
    private async Task ReleaseSinkAsync()
    {
        if (_sink is null)
        {
            return;
        }

        try
        {
            await _sink
                .ReleaseAsync()
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _errors.AddFatal(TabularError.Create(TabularErrorCodes.Persistence.OperationFailed,
                TabularErrorKind.Technical,
                "The own data context of the import could not be released."));
        }
    }

    private void CleanUpFiles()
    {
        if (_files.CleanupAll())
        {
            return;
        }

        _errors.AddFatal(TabularError.Create(TabularErrorCodes.Io.Failed,
            TabularErrorKind.Technical,
            "An own temporary file could not be deleted."));

        _scope.CleanupFailed();
    }

    private void AddDuplicateError()
    {
        if (_gateStop
            && _journal is { Decision: ImportJournalDecision.RejectedDuplicate }
            && _errors.TotalCount == 0)
        {
            _errors.Add(TabularError.Create(TabularErrorCodes.Validation.Failed,
                TabularErrorKind.Data,
                "The file was already imported in this scope."));
        }
    }

    /// <summary>The items of the report: from the sink, or the valid rows when the import released them.</summary>
    private (List<TItem>? Items, long Imported) Items(
        Ending ending
    )
    {
        if (_sink is not null)
        {
            return (_written?.Items, _written?.RowsImported ?? 0);
        }

        var withheld = _configurationFailed
            || _gateStop
            || (Strict && (ending.DataFailed || ending.Aborted || _outcome.SchemaFailed || _canceled));

        var items = withheld
            ? []
            : _outcome
                .Valid
                .Select(row => row.Item)
                .ToList();

        return (items, items.Count);
    }

    private ImportReportData ReportData(
        ReportStatus status,
        Ending ending,
        long imported
    ) => new(status,
        !_canceled && !ending.Aborted && !_configurationFailed && !_outcome.SchemaFailed && !_gateStop,
        _outcome.RowsRead,
        imported,
        _outcome.RowsRejected + (_written?.RowsRejected ?? 0),
        _written?.RowsSkipped ?? 0,
        _written?.RowsPendingCommit ?? 0,
        _written?.RowsUncertain ?? 0,
        _errors.Snapshot(),
        _errors.TotalCount,
        _errors.OmittedCount,
        _errors.Fatal,
        _scope.Diagnostics,
        _journal);

    /// <summary>Lets the sink finish; errors of its completion function are added to the report.</summary>
    private async Task<ImportReportData> CompleteSinkAsync(
        ImportReportData data
    )
    {
        if (_sink is null)
        {
            return data;
        }

        var completion = await _sink
            .FinishAsync(data)
            .ConfigureAwait(false);

        if (completion.Count == 0)
        {
            return data;
        }

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < completion.Count; errorIndex++)
        {
            var error = completion[errorIndex];
            _errors.Add(error);
        }

        _scope.CompletionFailed();

        return data with
        {
            Errors = _errors.Snapshot(),
            TotalErrorCount = _errors.TotalCount,
            OmittedErrorCount = _errors.OmittedCount,
        };
    }

    private void ReportCompleted(
        ImportReportData data
    ) => _scope.Completed(data.Status,
        data.IsComplete,
        data.FatalError,
        data.TotalErrorCount,
        data.OmittedErrorCount,
        new ReportCounters(data.RowsRead,
            data.RowsImported,
            data.RowsRejected,
            data.RowsSkipped,
            data.RowsPendingCommit,
            data.RowsUncertain,
            0),
        _errors.TotalCount > 0,
        _errors.HasTechnical);
}
