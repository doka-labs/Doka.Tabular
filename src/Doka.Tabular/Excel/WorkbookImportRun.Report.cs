namespace Doka.Tabular.Excel;

internal sealed partial class WorkbookImportRun
{
    private async Task<ExcelWorkbookImportReport> FinishAsync()
    {
        var anyErrors = _errors.TotalCount > 0;
        var overall = new ImportStatusFacts(Canceled: _canceled && _errors.Fatal is null,
                Failed: _configurationFailed || _errors.Fatal is not null,
                GateStopped: _gateStop,
                Skipped: _journal is { Decision: ImportJournalDecision.Skipped },
                HadErrors: anyErrors,
                ErrorsFail: Shared)
            .Decide();

        var (sheetReports, released) = SheetReports(!anyErrors && !_canceled);
        var data = await CompleteSinksAsync(ReportData(overall, released))
            .ConfigureAwait(false);

        _scope.Completed(data.Status,
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

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        return new ExcelWorkbookImportReport(data,
            sheetReports,
            _definition
                .Sheets
                .Select(sheet => sheet.Handle)
                .ToArray());
    }

    /// <summary>The sheet reports and the number of valid rows released without a write target.</summary>
    private (List<object> Reports, long Released) SheetReports(
        bool allValid
    )
    {
        var reports = new List<object>(_prepared.Count);
        long released = 0;
        for (var index = 0; index < _prepared.Count; index++)
        {
            // WHY: A parser or technical abort or a cancellation in a partial import keeps the rows checked until then;
            // with a source key the run has already emptied them, because the duplicate check is incomplete.
            var release = _strategy == ImportStrategy.Partial || _perSheet
                ? _statuses[index] is ReportStatus.Succeeded or ReportStatus.CompletedWithErrors
                || (_strategy == ImportStrategy.Partial
                    && _runs[index] is { Counts.Aborted: true } or { Counts.Canceled: true })
                : allValid;

            if (release
                && _prepared[index].Sink is null)
            {
                released += _runs[index]?.Counts.ValidRows ?? 0;
            }

            reports.Add(_runs[index] is { } run
                ? run.Report(_statuses[index], _written[index], release, _scope.Diagnostics)
                : _prepared[index]
                    .EmptyReport(_statuses[index], _scope.Diagnostics));
        }

        return (reports, released);
    }

    private ImportReportData ReportData(
        ReportStatus overall,
        long released
    )
    {
        var totals = _written
            .OfType<SinkCounts>()
            .ToList();

        return new ImportReportData(overall,
            !_canceled
            && _errors.Fatal is null
            && !_configurationFailed
            && _runs.All(run => run is { Counts: { Aborted: false, SchemaFailed: false } }),
            _runs.Sum(run => run?.Counts.RowsRead ?? 0),
            released + totals.Sum(counts => counts.RowsImported),
            _runs.Sum(run => run?.Counts.RowsRejected ?? 0) + totals.Sum(counts => counts.RowsRejected),
            totals.Sum(counts => counts.RowsSkipped),
            totals.Sum(counts => counts.RowsPendingCommit),
            totals.Sum(counts => counts.RowsUncertain),
            _errors.Snapshot(),
            _errors.TotalCount,
            _errors.OmittedCount,
            _errors.Fatal,
            _scope.Diagnostics,
            _journal);
    }

    /// <summary>Finishes and releases every write target; completion errors are added to the report.</summary>
    private async Task<ImportReportData> CompleteSinksAsync(
        ImportReportData data
    )
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var sinkIndex = 0; sinkIndex < _sinks.Count; sinkIndex++)
        {
            var sink = _sinks[sinkIndex];
            var completion = await sink
                .FinishAsync(data)
                .ConfigureAwait(false);

            // WHY: Indexed access reads the list without an enumerator through the interface.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var errorIndex = 0; errorIndex < completion.Count; errorIndex++)
            {
                var error = completion[errorIndex];
                _errors.Add(error);
                _scope.CompletionFailed();
            }

            await sink
                .DisposeAsync()
                .ConfigureAwait(false);
        }

        return _errors.TotalCount == data.TotalErrorCount
            ? data
            : data with
            {
                Errors = _errors.Snapshot(),
                TotalErrorCount = _errors.TotalCount,
                OmittedErrorCount = _errors.OmittedCount,
            };
    }
}
