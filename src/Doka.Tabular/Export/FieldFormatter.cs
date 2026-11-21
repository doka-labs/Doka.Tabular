namespace Doka.Tabular;

/// <summary>Formats the typed value of one exported field for one call.</summary>
internal abstract class FieldFormatter
{
    protected FieldFormatter(
        FlatField field
    )
    {
        Field = field;
    }

    protected FlatField Field { get; }

    /// <summary>Gets whether the field has rules that also run on export.</summary>
    public abstract bool HasExportRules { get; }

    /// <summary>
    /// Formats the slot value into a cell; the text is appended to <paramref name="output" />. A cancellation of the
    /// call inside an application function passes on.
    /// </summary>
    public abstract bool TryFormat(
        ValueSlot slot,
        ConversionContext context,
        TextBuffer output,
        CancellationToken callCancellation,
        out OutputCell cell,
        out ConversionFailure failure
    );

    /// <summary>Runs the rules that also apply on export; returns the violated rule, or throws its exception.</summary>
    public abstract FieldValidator? FirstViolated(
        ValueSlot slot
    );
}

/// <summary>The typed formatter of one field.</summary>
/// <typeparam name="TCore">The converter type.</typeparam>
/// <typeparam name="TBase">The base type.</typeparam>
/// <typeparam name="TValue">The property type.</typeparam>
[RequiresDynamicCode("The formatter belongs to a codec closed over the field types at run time.")]
[RequiresUnreferencedCode("The formatter belongs to a codec that binds the field types by reflection.")]
internal sealed class FieldFormatter<TCore, TBase, TValue> : FieldFormatter
{
    private readonly FieldValidator<TBase>[] _baseExportRules;
    private readonly ValueConverter<TCore> _converter;
    private readonly Func<TValue, TBase>? _custom;
    private readonly FieldValidator<TValue>[] _exportRules;
    private readonly FieldSettings _writeSettings;

    public FieldFormatter(
        FlatField field,
        ValueConverter<TCore> converter,
        Func<TValue, TBase>? custom
    )
        : base(field)
    {
        _converter = converter;
        _custom = custom;
        _writeSettings = field.Settings.Conversion?.BaseSettings ?? field.Settings;

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        _exportRules = field
            .Settings
            .Validators
            .OfType<FieldValidator<TValue>>()
            .Where(validator => validator.AlsoOnExport)
            .ToArray();

        // WHY: Rules of the base builder of a custom conversion check the base value the write function produces.
        // ReSharper disable once UseCollectionExpression
        _baseExportRules = (field.Settings.Conversion?.BaseSettings.Validators ?? [])
            .OfType<FieldValidator<TBase>>()
            .Where(validator => validator.AlsoOnExport)
            .ToArray();
    }

    public override bool HasExportRules => _exportRules.Length > 0;

    public override bool TryFormat(
        ValueSlot slot,
        ConversionContext context,
        TextBuffer output,
        CancellationToken callCancellation,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        var settings = Field.Settings;
        var typed = (ValueSlot<TValue>)slot;
        if (slot.IsMissing
            || !Stages.IsPresent(typed.Value))
        {
            // WHY: Explicit export rules also apply when the value is empty; the base value is then the empty value.
            if (!TryBaseRules(default!, callCancellation, out failure))
            {
                cell = OutputCell.Empty;

                return false;
            }

            // WHY: The cell of an absent optional owner stays empty; a null marker belongs to a null value only.
            if (slot.IsMissing)
            {
                cell = OutputCell.Empty;

                return true;
            }

            return TryFormatNull(NullSettings(settings), context, out cell, out failure);
        }

        // WHY: The write function of the application runs once; its result is checked and written.
        if (!TryBase(typed.Value, callCancellation, out var value, out failure)
            || !TryBaseRules(value, callCancellation, out failure))
        {
            cell = OutputCell.Empty;

            return false;
        }

        if (!Stages.IsPresent(value))
        {
            return TryFormatNull(NullSettings(settings), context, out cell, out failure);
        }

        var core = FieldCodec<TCore, TBase, TValue>.Unlift(value);
        var written = context.Format == TabularFormatKind.Excel && !settings.ExcelAsText
            ? _converter.TryWriteExcel(core, _writeSettings, context, output, out cell, out failure)
            : _converter.TryWriteCell(core, _writeSettings, context, output, out cell, out failure);

        // WHY: The reader trims with the setting of the column, otherwise with that of the base type, before it compares
        // the null markers; the export compares the same way.
        var trim = (settings.Trim ?? _writeSettings.Trim) == true;

        return written
            && TryFinish(settings, context, trim, output, ref cell, out failure)
            && (ReferenceEquals(_writeSettings, settings)
                || TryFinish(_writeSettings, context, trim, output, ref cell, out failure));
    }

    /// <summary>The settings whose null marker is written: the column's, otherwise the base type's.</summary>
    private FieldSettings NullSettings(
        FieldSettings settings
    ) => settings.NullText is not null || settings.EmptyTranslationKey is not null
        ? settings
        : _writeSettings;

    /// <summary>The export rules of the base builder on the base value the write function produced.</summary>
    private bool TryBaseRules(
        TBase value,
        CancellationToken callCancellation,
        out ConversionFailure failure
    )
    {
        failure = default;

        // WHY: Runs per exported value; a query would capture the value in a closure.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var validator in _baseExportRules)
        {
            bool valid;
            try
            {
                valid = validator.Rule(value);
            }
            catch (Exception exception) when (Callbacks.Failed(exception, callCancellation))
            {
                failure = new ConversionFailure(TabularErrorCodes.Callback.Failed,
                    "A validation rule threw an exception.");

                return false;
            }

            if (!valid)
            {
                failure = new ConversionFailure(validator.Code ?? TabularErrorCodes.Validation.Failed,
                    "The value violates a rule of the field.");

                return false;
            }
        }

        return true;
    }

    public override FieldValidator? FirstViolated(
        ValueSlot slot
    )
    {
        var value = ((ValueSlot<TValue>)slot).Value;

        // WHY: Runs per exported value; a query would capture the value in a closure.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var validator in _exportRules)
        {
            if (!validator.Rule(value))
            {
                return validator;
            }
        }

        return null;
    }

    /// <summary>The base value: the write function of a custom conversion, or the value itself.</summary>
    private bool TryBase(
        TValue value,
        CancellationToken callCancellation,
        out TBase result,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (_custom is null)
        {
            result = FieldCodec<TCore, TBase, TValue>.IdentityBack(value);

            return true;
        }

        try
        {
            result = _custom(value);

            return true;
        }
        catch (Exception exception) when (Callbacks.Failed(exception, callCancellation))
        {
            result = default!;
            failure = new ConversionFailure(TabularErrorCodes.Callback.Failed,
                "The write function of the custom conversion threw an exception.");

            return false;
        }
    }

    /// <summary>Checks the reserved null text and applies the mask; a native cell becomes text for both.</summary>
    private static bool TryFinish(
        FieldSettings settings,
        ConversionContext context,
        bool trim,
        TextBuffer output,
        ref OutputCell cell,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (settings is { NullText: null, EmptyTranslationKey: null, Mask: null })
        {
            return true;
        }

        // WHY: The reader takes the fixed null text and the translated empty text as null, so neither may be a value.
        var text = TextOf(cell, output);
        var compared = trim
            ? text.Span.Trim()
            : text.Span;

        if (cell.Kind != OutputKind.Empty
            && ((settings.NullText is { } reserved && compared.SequenceEqual(FieldReader.Marker(reserved, trim)))
                || (settings.EmptyTranslationKey is { } key
                    && context.Translations is { } translations
                    && translations.TryGetValue(key, out var translated)
                    && compared.SequenceEqual(FieldReader.Marker(translated, trim)))))
        {
            failure = ConversionFailure.InvalidFormat("The value equals the null text of the field.");

            return false;
        }

        if (settings.Mask is { } mask)
        {
            var start = output.Length;
            output.Append(text.Span);
            Masking.Apply(output.Written(start), mask);
            cell = OutputCell.FromText(output.Slice(start));
        }

        return true;
    }

    /// <summary>The text a cell denotes; a native number is its round-trip text.</summary>
    private static ReadOnlyMemory<char> TextOf(
        in OutputCell cell,
        TextBuffer output
    )
    {
        if (cell.Kind != OutputKind.Number)
        {
            return cell.Text;
        }

        var start = output.Length;
        output.AppendFormatted(cell.Number, "R", CultureInfo.InvariantCulture);

        return output.Slice(start);
    }

    /// <summary>An absent value stays empty unless the field has a null text or an empty translation.</summary>
    private static bool TryFormatNull(
        FieldSettings settings,
        ConversionContext context,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        cell = OutputCell.Empty;
        failure = default;
        if (settings is { EmptyTranslationKey: null, NullText: null })
        {
            return true;
        }

        if (!Translations.Resolve(settings.EmptyTranslationKey,
            settings.NullText,
            context,
            out var nullText,
            out failure))
        {
            return false;
        }

        cell = OutputCell.FromText(nullText.AsMemory());

        return true;
    }
}
