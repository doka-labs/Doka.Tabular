namespace Doka.Tabular;

internal sealed partial class ExportPlan<TSource>
{
    /// <summary>
    /// Formats one source object into <paramref name="cells" />; returns <see langword="false" /> with an error that
    /// ends the export. Cell texts are views into the row buffer and stay valid until the next record is formatted.
    /// </summary>
    public bool TryFormat(
        TSource source,
        long recordNumber,
        ITableWriter rows,
        TabularSheetName? sheetName,
        OutputCell[] cells,
        CancellationToken cancellationToken,
        out TabularError? error
    )
    {
        _text.Clear();
        Array.Clear(_rootRead);
        Array.Clear(_columnRead);
        Array.Clear(_composeDone);
        var record = new RecordPosition(recordNumber, rows, sheetName, cancellationToken);
        try
        {
            // WHY: Rules over several fields check the source values before any write function runs; a field rule
            // runs right before its field is formatted, so a violation knows the physical line of the field.
            error = CheckCrossRules(source, record) ?? Format(source, record, cells);
        }
        catch (Exception exception) when (exception is not TabularResourceException
            && Callbacks.Failed(exception, record.CallCancellation))
        {
            error = TabularError.Create(
                TabularErrorCodes.Callback.Failed,
                TabularErrorKind.Technical,
                "Reading a property of the exported record threw an exception.",
                record.Location(null));
        }

        return error is null;
    }

    /// <summary>The first violated export rule of the field, or a technical error when a rule throws.</summary>
    private static TabularError? FieldRuleError(
        ExportColumn column,
        ValueSlot slot,
        in RecordPosition record,
        ReadOnlySpan<OutputCell> preceding
    )
    {
        FieldValidator? violated;
        try
        {
            violated = column.Formatter!.FirstViolated(slot);
        }
        catch (Exception exception) when (Callbacks.Failed(exception, record.CallCancellation))
        {
            return TabularError.Create(
                TabularErrorCodes.Callback.Failed,
                TabularErrorKind.Technical,
                "A validation rule threw an exception.",
                record.FieldLocation(column.Field, preceding));
        }

        return violated is null
            ? null
            : TabularError.Create(
                violated.Code ?? TabularErrorCodes.Validation.Failed,
                TabularErrorKind.Data,
                "The value violates a rule of the field.",
                record.FieldLocation(column.Field, preceding));
    }

    /// <summary>
    /// The kind of a formatting failure; a failing function of the application ends the run technically.
    /// </summary>
    private static TabularErrorKind KindOf(
        TabularErrorCode code
    )
    {
        if (code == TabularErrorCodes.Translation.Missing
            || code == TabularErrorCodes.Configuration.Invalid)
        {
            return TabularErrorKind.Configuration;
        }

        return code == TabularErrorCodes.Callback.Failed
            ? TabularErrorKind.Technical
            : TabularErrorKind.Data;
    }

    /// <summary>Runs the rules over several fields that also apply on export.</summary>
    private TabularError? CheckCrossRules(
        TSource source,
        in RecordPosition record
    )
    {
        foreach (var rule in _rules)
        {
            var root = RootOf(source, rule.Role);
            for (var index = 0; index < rule.Readers.Length; index++)
            {
                rule
                    .Readers[index]
                    .Read(root, rule.Slots[index]);
            }

            var location = record.Location(null, rule.Path, rule.Key);
            bool valid;
            try
            {
                valid = rule.Rule.Rule(rule.Slots);
            }
            catch (Exception exception) when (Callbacks.Failed(exception, record.CallCancellation))
            {
                return TabularError.Create(
                    TabularErrorCodes.Callback.Failed,
                    TabularErrorKind.Technical,
                    "A rule over several fields threw an exception.",
                    location);
            }

            if (!valid)
            {
                return TabularError.Create(
                    rule.Rule.Code ?? TabularErrorCodes.Validation.Failed,
                    TabularErrorKind.Data,
                    "The values violate a rule over several fields.",
                    location);
            }
        }

        return null;
    }

    /// <summary>Formats every column into <paramref name="cells" />; gaps become empty cells.</summary>
    private TabularError? Format(
        TSource source,
        in RecordPosition record,
        OutputCell[] cells
    )
    {
        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var position = 0; position < Columns.Count; position++)
        {
            var column = Columns[position];
            if (column.Formatter is not { } formatter)
            {
                cells[column.Position] = OutputCell.Empty;
                continue;
            }

            // WHY: A composed part has its value only after the write function of its group.
            if (_sources[column.Position] is { Reader: null } part
                && ComposeError(source, column, part, record, cells.AsSpan(0, column.Position)) is { } composeError)
            {
                return composeError;
            }

            if (formatter.HasExportRules
                && FieldRuleError(column, ValueOf(source, column), record, cells.AsSpan(0, column.Position)) is
                    { } ruleError)
            {
                return ruleError;
            }

            if (!formatter.TryFormat(
                ValueOf(source, column),
                Context,
                _text,
                record.CallCancellation,
                out cells[column.Position],
                out var failure))
            {
                return TabularError.Create(
                    failure.Code,
                    KindOf(failure.Code),
                    failure.Message,
                    record.FieldLocation(column.Field, cells.AsSpan(0, column.Position)));
            }
        }

        return null;
    }

    /// <summary>The slot of a column, read once per record; a composed part is filled by its group.</summary>
    private ValueSlot ValueOf(
        TSource source,
        ExportColumn column
    )
    {
        var position = column.Position;
        var slot = _slots[position]!;
        if (_columnRead[position]
            || _sources[position] is not { Reader: { } reader } direct)
        {
            return slot;
        }

        reader.Read(RootOf(source, direct.Role), slot);
        _columnRead[position] = true;

        return slot;
    }

    /// <summary>Splits a composed value into its part slots once per record; an absent owner leaves the parts absent.</summary>
    private TabularError? ComposeError(
        TSource source,
        ExportColumn column,
        ColumnSource part,
        in RecordPosition record,
        ReadOnlySpan<OutputCell> preceding
    )
    {
        if (_composeDone[part.ComposeGroup])
        {
            return null;
        }

        _composeDone[part.ComposeGroup] = true;
        var group = _composes[part.ComposeGroup];
        group.Reader.Read(RootOf(source, group.Role), group.Value);
        if (group.Value.IsMissing)
        {
            // WHY: The parts get the default value like any field of an absent owner; rules see no previous record.
            foreach (var slot in group.Parts)
            {
                slot.SetMissing();
            }

            return null;
        }

        foreach (var slot in group.Parts)
        {
            slot.IsMissing = false;
        }

        try
        {
            group.Compose.Binding.Write(group.Value, group.Parts);

            return null;
        }
        catch (Exception exception) when (Callbacks.Failed(exception, record.CallCancellation))
        {
            return TabularError.Create(
                TabularErrorCodes.Callback.Failed,
                TabularErrorKind.Technical,
                $"The write function of {group.Compose.Name} threw an exception.",
                record.FieldLocation(column.Field, preceding));
        }
    }

    private object? RootOf(
        TSource source,
        int role
    )
    {
        if (!_rootRead[role])
        {
            _roots[role] = _roles[role]
                .Accessor(source!);

            _rootRead[role] = true;
        }

        return _roots[role];
    }

    /// <summary>
    /// The position of the record being formatted, for error locations; a location without formatted preceding
    /// cells names the row where the record starts.
    /// </summary>
    private readonly record struct RecordPosition(
        long RecordNumber,
        ITableWriter Rows,
        TabularSheetName? SheetName,
        CancellationToken CallCancellation
    )
    {
        public TabularErrorLocation Location(
            FlatField? field,
            TabularPropertyPath? path = null,
            ColumnKey? key = null,
            long? rowNumber = null
        ) => new()
        {
            RowNumber = rowNumber ?? Rows.NextRowNumber,
            RecordNumber = RecordNumber,
            ColumnKey = key ?? field?.Key,
            PropertyPath = path ?? field?.Path,
            SheetName = SheetName,
        };

        /// <summary>The location of a field that starts behind the already formatted preceding cells.</summary>
        public TabularErrorLocation FieldLocation(
            FlatField? field,
            ReadOnlySpan<OutputCell> preceding
        ) => Location(field, rowNumber: Rows.RowNumberOf(preceding));
    }
}
