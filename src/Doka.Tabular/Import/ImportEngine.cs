namespace Doka.Tabular;

/// <summary>Reads one table, binds, converts, and validates; duplicate keys are resolved per table.</summary>
internal static partial class ImportEngine
{
    /// <summary>
    /// Reads one table into <paramref name="outcome" />, which the caller owns, so that the rows and counters read
    /// before a technical abort stay with the caller.
    /// </summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public static async Task ReadTableAsync<TItem>(
        IRecordSource source,
        ImportPlan<TItem> plan,
        TableOutcome<TItem> outcome,
        ErrorCollector errors,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            outcome.Canceled = true;

            return;
        }

        try
        {
            if (plan.NeedsHeader
                && !await ReadHeaderAsync(source, plan, outcome, errors, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            await ReadRecordsAsync(source, plan, outcome, errors, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // WHY: A technical abort keeps the rows checked so far, except with a source key, whose duplicate overview
            // is then incomplete; a thrown cancellation of the call ends the table like a cancellation between rows.
            if (cancellationToken.IsCancellationRequested)
            {
                outcome.Canceled = true;
            }
            else
            {
                outcome.Aborted = true;
            }

            if (plan.Settings.AllKeys.Any())
            {
                outcome.Valid.Clear();
            }

            throw;
        }

        var keys = plan.Settings.AllKeys.ToList();
        if (keys.Count > 0
            && outcome.Aborted)
        {
            // WHY: After an abort the duplicate overview is incomplete; no row of the table is released.
            outcome.Valid.Clear();
        }
        else if (keys.Count > 0)
        {
            // WHY: Every key checks the same rows; a row a first key rejects can still repeat the value of a second.
            var candidates = outcome.Valid.ToList();
            var rejected = new HashSet<ImportedRow<TItem>>();

            // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                var key = keys[keyIndex];
                bool applied;
                try
                {
                    applied = ApplySourceKey(candidates, key, plan, errors, rejected, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // WHY: A comparer that observes the canceled token of the call ends the check as canceled.
                    applied = false;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // WHY: The key getter and comparer are application code; the unfinished check releases no row.
                    errors.AddFatal(TabularError.Create(TabularErrorCodes.Callback.Failed,
                        TabularErrorKind.Technical,
                        "A key or comparer of the source key threw an exception.",
                        new TabularErrorLocation { SheetName = source.SheetName }));

                    outcome.Aborted = true;
                    Reject(outcome, rejected);
                    outcome.Valid.Clear();

                    return;
                }

                if (!applied)
                {
                    // WHY: An unfinished duplicate check releases no row of the table; the rows already rejected
                    // with a reported error still count.
                    outcome.Canceled = true;
                    Reject(outcome, rejected);
                    outcome.Valid.Clear();

                    return;
                }
            }

            Reject(outcome, rejected);
        }
    }

    /// <summary>Counts and removes the rows the source keys rejected with a reported error.</summary>
    private static void Reject<TItem>(
        TableOutcome<TItem> outcome,
        HashSet<ImportedRow<TItem>> rejected
    )
    {
        if (rejected.Count == 0)
        {
            return;
        }

        outcome.HadDataErrors = true;
        outcome.RowsRejected += rejected.Count;
        outcome.Valid.RemoveAll(rejected.Contains);
    }

    /// <summary>Reads and binds the header; <see langword="false" /> when no data row is to be read.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private static async Task<bool> ReadHeaderAsync<TItem>(
        IRecordSource source,
        ImportPlan<TItem> plan,
        TableOutcome<TItem> outcome,
        ErrorCollector errors,
        CancellationToken cancellationToken
    )
    {
        var first = await source
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);

        if (first.Kind is RecordReadKind.Fatal or RecordReadKind.BrokenRecord)
        {
            errors.AddFatal(first.Error!);
            outcome.Aborted = true;

            return false;
        }

        var bindErrors = new List<TabularError>();
        try
        {
            plan.Bind(first.Record, bindErrors, source.SheetName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // WHY: NormalizeHeader is application code; a cancellation the call did not request is its failure.
            errors.AddFatal(TabularError.Create(TabularErrorCodes.Callback.Failed,
                TabularErrorKind.Technical,
                "NormalizeHeader threw an exception.",
                new TabularErrorLocation { SheetName = source.SheetName }));

            outcome.Aborted = true;

            return false;
        }

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < bindErrors.Count; errorIndex++)
        {
            var error = bindErrors[errorIndex];
            errors.Add(error);
        }

        // WHY: A schema error of the header makes every row unreadable; no data row is processed.
        outcome.HadDataErrors |= bindErrors.Count > 0;
        outcome.SchemaFailed = bindErrors.Count > 0;

        return bindErrors.Count == 0 && first.Kind != RecordReadKind.End;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private static async Task ReadRecordsAsync<TItem>(
        IRecordSource source,
        ImportPlan<TItem> plan,
        TableOutcome<TItem> outcome,
        ErrorCollector errors,
        CancellationToken cancellationToken
    )
    {
        var recordNumber = 0L;
        var row = new RowContext(source.SheetName, cancellationToken);
        var items = new List<object>(plan.Roles.Count);
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await source
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            switch (read.Kind)
            {
                case RecordReadKind.End:
                    return;
                case RecordReadKind.Fatal:
                    errors.AddFatal(read.Error!);
                    outcome.Aborted = true;

                    return;
                case RecordReadKind.BrokenRecord:
                    recordNumber++;
                    outcome.RowsRead++;
                    errors.Add(WithRecordNumber(read.Error!, recordNumber));
                    outcome.RowsRejected++;
                    outcome.HadDataErrors = true;
                    break;
                case RecordReadKind.Record:
                    recordNumber++;
                    outcome.RowsRead++;
                    row.Start(read.Record!, recordNumber);
                    if (!TakeRecord(source,
                        plan,
                        read.Record!,
                        row,
                        items,
                        outcome,
                        errors))
                    {
                        return;
                    }

                    break;
                default:
                    throw new UnreachableException($"The record read kind {read.Kind} is not handled.");
            }
        }

        outcome.Canceled = true;
    }

    /// <summary>Builds the items of one record; <see langword="false" /> when a fatal error ends the table.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private static bool TakeRecord<TItem>(
        IRecordSource source,
        ImportPlan<TItem> plan,
        SourceRecord record,
        RowContext row,
        List<object> items,
        TableOutcome<TItem> outcome,
        ErrorCollector errors
    )
    {
        if (source.MissingIsStructural
            && plan.MaxIndex >= record.Cells.Count)
        {
            row.Errors.Add(TabularError.Create(TabularErrorCodes.Schema.MissingColumn,
                TabularErrorKind.Data,
                $"The record has {record.Cells.Count} fields; index {plan.MaxIndex} is mapped.",
                row.Location(plan.MaxIndex)));
        }
        else if (BuildItems(plan, record, row, items))
        {
            var combined = plan.Shape.KeepsItems
                ? [.. items]
                : items;

            outcome.Valid.Add(new ImportedRow<TItem>(plan.Shape.Combine(combined),
                record.RowNumber,
                row.RecordNumber,
                source.SheetName));

            return true;
        }

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var errorIndex = 0; errorIndex < row.Errors.Count; errorIndex++)
        {
            var error = row.Errors[errorIndex];
            errors.Add(error);
        }

        if (row.Errors.Count > 0)
        {
            outcome.RowsRejected++;
            outcome.HadDataErrors = true;
        }

        if (row.Fatal is null)
        {
            return true;
        }

        errors.AddFatal(row.Fatal);
        outcome.Aborted = true;

        return false;
    }

    /// <summary>Builds the items of every role into <paramref name="items" />; <see langword="false" /> on errors.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private static bool BuildItems<TItem>(
        ImportPlan<TItem> plan,
        SourceRecord record,
        RowContext row,
        List<object> items
    )
    {
        items.Clear();
        var roles = plan.Roles;

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var index = 0; index < roles.Count; index++)
        {
            if (RecordBuilder.TryBuild(roles[index], record, row, plan.Context, out var item))
            {
                items.Add(item!);
            }

            if (row.Fatal is not null)
            {
                break;
            }
        }

        return !row.HasErrors;
    }

    private static TabularError WithRecordNumber(
        TabularError error,
        long recordNumber
    ) => error.WithLocation(new TabularErrorLocation
    {
        RowNumber = error.Location.RowNumber,
        RecordNumber = recordNumber,
        ColumnIndex = error.Location.ColumnIndex,
        SheetName = error.Location.SheetName,
    });
}
