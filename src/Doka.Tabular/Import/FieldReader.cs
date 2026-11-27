namespace Doka.Tabular;

/// <summary>Reads, converts, and validates one mapped field for one call into a typed slot.</summary>
internal abstract class FieldReader
{
    protected FieldReader(
        FlatField field,
        int columnIndex,
        string? fileHeader
    )
    {
        Field = field;
        ColumnIndex = columnIndex;
        FileHeader = fileHeader;

        // WHY: The trim setting of the column wins; without one, the base type of a custom conversion decides.
        TrimSettings = field.Settings.Trim is null
            ? field.Settings.Conversion?.BaseSettings ?? field.Settings
            : field.Settings;
    }

    protected FlatField Field { get; }

    /// <summary>Gets the settings whose trim rule applies to the text of the field.</summary>
    public FieldSettings TrimSettings { get; }

    /// <summary>Gets the source index read by this call; -1 when an optional header is absent.</summary>
    public int ColumnIndex { get; }

    private string? FileHeader { get; }

    /// <summary>Reads the field into the slot; <see langword="false" /> when an error for the field was recorded.</summary>
    public abstract bool Read(
        SourceRecord record,
        RowContext row,
        ConversionContext context,
        MapModel owner,
        ValueSlot slot
    );

    protected TabularErrorLocation Location(
        RowContext row
    ) => row.Location(
        ColumnIndex >= 0
            ? ColumnIndex
            : null,
        Field.Key,
        Field.Path,
        FileHeader);

    /// <summary>The problem of a cell that no field can read: a rejected formula or an Excel error.</summary>
    protected static string? UnsupportedCell(
        in SourceCell cell,
        FieldSettings settings
    ) => cell switch
    {
        { IsFormula: true } when settings.RejectFormulas => "Formulas are not allowed in this column.",
        { Kind: CellKind.Error } => "The cell contains an Excel error value.",
        { Kind: CellKind.FormulaWithoutResult } => "The formula has no stored result.",
        _ => null,
    };

    /// <summary>
    /// Whether the text is a null marker; a trimmed text is compared with the trimmed marker, so a padded marker the
    /// export wrote reads back as null.
    /// </summary>
    protected static bool IsNullText(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        bool trims
    )
    {
        if (settings.NullText is { } nullText
            && text.SequenceEqual(Marker(nullText, trims)))
        {
            return true;
        }

        return settings.EmptyTranslationKey is { } key
            && context.Translations is { } translations
            && translations.TryGetValue(key, out var translated)
            && text.SequenceEqual(Marker(translated, trims));
    }

    /// <summary>A null marker as a trimmed text sees it.</summary>
    internal static ReadOnlySpan<char> Marker(
        string marker,
        bool trims
    ) => trims
        ? marker
            .AsSpan()
            .Trim()
        : marker;

    protected static string? RawText(
        in SourceCell cell
    ) => cell.Kind switch
    {
        CellKind.Text or CellKind.Error => cell.Text.ToString(),
        CellKind.Number => cell.Number.ToString("R", CultureInfo.InvariantCulture),
        CellKind.Boolean => cell.Boolean
            ? "TRUE"
            : "FALSE",
        _ => null,
    };
}

/// <summary>The typed reader of one field.</summary>
/// <typeparam name="TCore">The converter type.</typeparam>
/// <typeparam name="TBase">The base type.</typeparam>
/// <typeparam name="TValue">The property type.</typeparam>
[RequiresDynamicCode("The reader belongs to a codec closed over the field types at run time.")]
[RequiresUnreferencedCode("The reader belongs to a codec that binds the field types by reflection.")]
internal sealed class FieldReader<TCore, TBase, TValue> : FieldReader
{
    private readonly FieldValidator<TBase>[] _baseValidators;
    private readonly ValueConverter<TCore> _converter;
    private readonly Func<TBase, TValue>? _custom;
    private readonly FieldSettings _readSettings;
    private readonly FieldValidator<TValue>[] _validators;

    public FieldReader(
        FlatField field,
        ValueConverter<TCore> converter,
        int columnIndex,
        string? fileHeader,
        Func<TBase, TValue>? custom
    )
        : base(field, columnIndex, fileHeader)
    {
        _converter = converter;
        _custom = custom;

        // WHY: The converter sees the trim rule the reader applies to the text, the column's before the base type's.
        _readSettings = field.Settings.CheckedConversionSettings();

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _validators = field
            .Settings
            .Validators
            .OfType<FieldValidator<TValue>>()
            .ToArray();

        // WHY: The rules of the base type of a custom conversion run on the base value, before the read function.
        // ReSharper disable once UseCollectionExpression
        _baseValidators = field.Settings.Conversion is null
            ? []
            : _readSettings
                .Validators
                .OfType<FieldValidator<TBase>>()
                .ToArray();
    }

    public override bool Read(
        SourceRecord record,
        RowContext row,
        ConversionContext context,
        MapModel owner,
        ValueSlot slot
    )
    {
        var cell = ColumnIndex < 0
            ? SourceCell.Missing
            : record.CellAt(ColumnIndex);

        var settings = Field.Settings;
        var sourceValue = settings.IncludeSourceValue
            ? RawText(cell)
            : null;

        if (UnsupportedCell(cell, settings) is { } unsupported)
        {
            AddError(row, TabularErrorCodes.Conversion.UnsupportedValue, unsupported, sourceValue);

            return false;
        }

        var status = Convert(cell, context, out var core, out var isNull, out var failure);
        switch (status)
        {
            case CellRead.Blank:
                return Empty(isNull, row, owner, sourceValue, (ValueSlot<TValue>)slot);
            case CellRead.Empty:
                return Empty(isNull: false, row, owner, sourceValue, (ValueSlot<TValue>)slot);
            case CellRead.Failed:
                AddError(row, failure.Code, failure.Message, sourceValue);

                return false;
            case CellRead.Value:
                break;
            default:
                throw new UnreachableException($"The cell read {status} is not handled.");
        }

        var baseValue = FieldCodec<TCore, TBase, TValue>.Lift(core);

        return Check(_baseValidators, baseValue, row, sourceValue)
            && TryMap(baseValue, row, owner, sourceValue, out var value)
            && Validate(value, row, sourceValue, (ValueSlot<TValue>)slot);
    }

    /// <summary>Converts the cell; an empty text or the null text is <see cref="CellRead.Blank" />.</summary>
    private CellRead Convert(
        in SourceCell cell,
        ConversionContext context,
        out TCore value,
        out bool isNull,
        out ConversionFailure failure
    )
    {
        isNull = false;
        ReadStatus status;
        if (cell.IsUtc
            && !(typeof(TCore) == typeof(DateTime) && _readSettings.ExcelAsUtc))
        {
            // WHY: A stated zone fits only a UTC column; any other column would have to change or drop the instant.
            value = default!;
            failure = ConversionFailure.InvalidFormat(
                "The date cell states a time zone; only a UTC column (ExcelAsUtc) reads it.");

            status = ReadStatus.Failed;
        }
        else if (cell.Kind is CellKind.Number or CellKind.Boolean)
        {
            status = _converter.TryReadNative(cell, _readSettings, context, out value, out failure);
        }
        else
        {
            var text = cell.Text;
            var trims = context.TrimFor(TrimSettings);
            if (trims)
            {
                text = text.Trim();
            }

            // WHY: A null marker of the column or of the base type of a custom conversion both denote null.
            isNull = IsNullText(text.Span, Field.Settings, context, trims)
                || (!ReferenceEquals(_readSettings, Field.Settings)
                    && IsNullText(text.Span, _readSettings, context, trims));

            if (isNull || text.IsEmpty)
            {
                value = default!;
                failure = default;

                return CellRead.Blank;
            }

            status = _converter.TryReadText(text, _readSettings, context, out value, out failure);
        }

        return status switch
        {
            ReadStatus.Value => CellRead.Value,
            ReadStatus.Empty => CellRead.Empty,
            ReadStatus.Failed => CellRead.Failed,
            _ => throw new UnreachableException($"The read status {status} is not handled."),
        };
    }

    private bool Empty(
        bool isNull,
        RowContext row,
        MapModel owner,
        string? sourceValue,
        ValueSlot<TValue> slot
    )
    {
        if (Field.Settings.IsRequired
            || _readSettings.IsRequired)
        {
            AddError(row, TabularErrorCodes.Conversion.MissingValue, "A value is required.", sourceValue);

            return false;
        }

        return TryEmptyValue(isNull, row, owner, sourceValue, out var value)
            && Validate(value, row, null, slot);
    }

    /// <summary>
    /// The value of an empty field: the empty string, padded like any other text, or null when the base type allows
    /// it. A custom read function receives that base value like any other.
    /// </summary>
    private bool TryEmptyValue(
        bool isNull,
        RowContext row,
        MapModel owner,
        string? sourceValue,
        out TValue value
    )
    {
        value = default!;
        TBase baseValue;
        if (FieldCodec<TCore, TBase, TValue>.BaseIsString
            && !isNull
            && !Field.Settings.EmptyStringAsNull
            && !_readSettings.EmptyStringAsNull)
        {
            baseValue = (TBase)(object)StringConverter.Pad(ReadOnlySpan<char>.Empty, _readSettings);
        }
        else if (FieldCodec<TCore, TBase, TValue>.BaseAllowsNull)
        {
            baseValue = default!;
        }
        else
        {
            AddError(
                row,
                TabularErrorCodes.Conversion.MissingValue,
                "The field needs a value; the target type is not nullable.",
                sourceValue);

            return false;
        }

        return Check(_baseValidators, baseValue, row, sourceValue)
            && TryMap(baseValue, row, owner, sourceValue, out value);
    }

    /// <summary>Maps the base value to the property value; a custom read function may reject it.</summary>
    private bool TryMap(
        TBase value,
        RowContext row,
        MapModel owner,
        string? sourceValue,
        out TValue result
    )
    {
        if (_custom is null)
        {
            result = FieldCodec<TCore, TBase, TValue>.Identity(value);

            return true;
        }

        try
        {
            result = _custom(value);

            return true;
        }
        catch (Exception exception) when (Callbacks.Failed(exception, row.CallCancellation))
        {
            result = default!;
            if (owner.FindExpectation(exception) is { } code)
            {
                row.Errors.Add(
                    TabularError.Create(
                        code,
                        TabularErrorKind.Data,
                        "The custom conversion rejected the value.",
                        Location(row),
                        sourceValue: sourceValue));
            }
            else
            {
                row.Fatal ??= TabularError.Create(
                    TabularErrorCodes.Callback.Failed,
                    TabularErrorKind.Technical,
                    $"The custom conversion of {Field.Path} threw an unexpected exception.",
                    Location(row));
            }

            return false;
        }
    }

    private bool Validate(
        TValue value,
        RowContext row,
        string? sourceValue,
        ValueSlot<TValue> slot
    )
    {
        if (!Check(_validators, value, row, sourceValue))
        {
            return false;
        }

        slot.Value = value;

        return true;
    }

    /// <summary>
    /// Runs every rule on a value; each violated rule is its own row error, so one rule does not hide another.
    /// </summary>
    private bool Check<TChecked>(
        FieldValidator<TChecked>[] validators,
        TChecked value,
        RowContext row,
        string? sourceValue
    )
    {
        var passed = true;
        foreach (var validator in validators)
        {
            bool valid;
            try
            {
                valid = validator.Rule(value);
            }
            catch (Exception exception) when (Callbacks.Failed(exception, row.CallCancellation))
            {
                row.Fatal ??= TabularError.Create(
                    TabularErrorCodes.Callback.Failed,
                    TabularErrorKind.Technical,
                    $"A validation rule of {Field.Path} threw an exception.",
                    Location(row));

                return false;
            }

            if (!valid)
            {
                row.Errors.Add(
                    TabularError.Create(
                        validator.Code ?? TabularErrorCodes.Validation.Failed,
                        TabularErrorKind.Data,
                        "The value violates a rule of the field.",
                        Location(row),
                        sourceValue: sourceValue));

                passed = false;
            }
        }

        return passed;
    }

    private void AddError(
        RowContext row,
        TabularErrorCode code,
        string message,
        string? sourceValue
    ) => row.Errors.Add(
        TabularError.Create(
            code,
            TabularErrorKind.Data,
            message,
            Location(row),
            sourceValue: sourceValue));

    private enum CellRead
    {
        Value,
        Empty,
        Blank,
        Failed,
    }
}
