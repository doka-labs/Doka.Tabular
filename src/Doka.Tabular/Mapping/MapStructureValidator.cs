namespace Doka.Tabular;

/// <summary>Structural checks of <c>Build()</c>: everything that does not depend on a call or on file content.</summary>
internal static class MapStructureValidator
{
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public static List<TabularError> Validate(
        MapModel model
    )
    {
        var errors = new List<TabularError>();
        var fields = new List<FlatField>();
        MapFlattening.Flatten(model, null, EmbeddingContext.Root, fields);
        ValidateMembers(model, null, errors);

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
        {
            var field = fields[fieldIndex];
            ValidateField(field, errors);
        }

        HeaderText.AddIndistinct(fields
                .Where(field => field.IsExported)
                .Select(field => (field, HeaderText.Fixed(field))),
            errors);

        // WHY: Each duplicate group adds one configuration error; a query would hide that side effect.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var group in fields
            .GroupBy(field => field.Key)
            .Where(group => group.Count() > 1))
        {
            errors.Add(TabularError.Configuration(
                $"The key {group.Key} is used by {string.Join(", ", group.Select(field => field.Path.Value))}; keys must be unique across the map.",
                new TabularErrorLocation
                {
                    ColumnKey = group.Key,
                    PropertyPath = group.First()
                        .Path,
                }));
        }

        return errors;
    }

    private static void ValidateMembers(
        MapModel model,
        TabularPropertyPath? path,
        List<TabularError> errors
    )
    {
        // WHY: Each duplicate group adds one configuration error; a query would hide that side effect.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var group in model
            .Members
            .GroupBy(member => member.Name)
            .Where(group => group.Count() > 1))
        {
            errors.Add(TabularError.Configuration(
                $"The property {group.Key} is mapped more than once.",
                new TabularErrorLocation { PropertyPath = Combine(path, group.Key) }));
        }

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var memberIndex = 0; memberIndex < model.Members.Count; memberIndex++)
        {
            var member = model.Members[memberIndex];
            var memberPath = Combine(path, member.Name);

            // WHY: One guarded branch; a switch statement would add a case scope without a readability gain.
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            // WHY: The value type must be the property type; a cast in the expression would read or write the property
            // with an unchecked conversion. Other types are bound through Convert or a factory.
            if (member is ColumnMember or ComposeMember
                && member.Kind.Type != member.Property.PropertyType)
            {
                errors.Add(TabularError.Configuration(
                    $"The field {member.Name} has the type {member.Property.PropertyType.Name}, the column maps {member.Kind.Type.Name}; use Convert or a factory.",
                    new TabularErrorLocation { PropertyPath = memberPath }));
            }

            if (member is ComposeMember { Parts.Count: < 2 or > 4 })
            {
                errors.Add(TabularError.Configuration("A composed value has 2 to 4 parts.",
                    new TabularErrorLocation { PropertyPath = memberPath }));
            }

            if (member is NestedMember nested)
            {
                AddUnknownTargets(nested.Map, nested.Embedding, memberPath, errors);
            }
        }
    }

    /// <summary>
    /// Adds one configuration error per key or header override of an inclusion whose target is not a field of the
    /// included map; such a setting would have no effect.
    /// </summary>
    public static void AddUnknownTargets(
        MapModel map,
        EmbeddingSettings embedding,
        TabularPropertyPath? path,
        List<TabularError> errors
    )
    {
        var fields = new List<FlatField>();
        MapFlattening.Flatten(map, null, EmbeddingContext.Root, fields);
        var known = fields
            .Select(field => field.Path.Value)
            .ToHashSet(StringComparer.Ordinal);

        // WHY: Each unknown target adds one configuration error; the loop keeps that side effect explicit.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var target in embedding.ExportKeys.Keys.Concat(embedding.Headers.Keys))
        {
            if (!known.Contains(target))
            {
                errors.Add(TabularError.Configuration(
                    $"The inclusion refers to {target}, which is not a field of the included map.",
                    new TabularErrorLocation { PropertyPath = path }));
            }
        }
    }

    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private static void ValidateField(
        FlatField field,
        List<TabularError> errors
    )
    {
        var location = new TabularErrorLocation
        {
            PropertyPath = field.Path,
            ColumnKey = field.Key,
        };

        if (field.Index < 0)
        {
            Add($"The index {field.Index} of {field.Path} is negative.");
        }

        var settings = field.Settings;
        var valueType = settings.Conversion?.BaseType ?? field.ValueType;
        var effectiveSettings = settings.Conversion?.BaseSettings ?? settings;
        if (!ConverterRegistry.IsSupported(valueType))
        {
            Add(ConverterRegistry.UnsupportedReason(valueType));
        }
        else
        {
            var core = ConverterRegistry.Core(valueType);
            var checkedSettings = settings.CheckedConversionSettings();
            var problems = core.IsEnum
                ? EnumConverter.ValidateStructure(core, checkedSettings, field.IsExported)
                : ConverterRegistry.Find(core)!.ValidateSettings(checkedSettings);

            foreach (var problem in problems)
            {
                Add(problem);
            }

            // WHY: Both settings write the Excel column as text, and a number format has no effect on a text cell.
            if (checkedSettings.ExcelNumberFormat is not null)
            {
                if (settings.ExcelAsText
                    || checkedSettings.ExcelAsText)
                {
                    Add("ExcelNumberFormat has no effect with ExcelAsText(), which writes the column as text.");
                }

                if (checkedSettings.AllowNonFiniteValues)
                {
                    Add(
                        "ExcelNumberFormat has no effect with NonFiniteValues(), which writes the Excel column as text.");
                }
            }
        }

        // WHY: With a custom conversion the base type is converted with the settings of its own builder; the same
        // settings on the column would have no effect.
        if (settings.Conversion is not null
            && settings
                .ConversionSettings()
                .ToList() is { Count: > 0 } ignored)
        {
            Add(
                $"The column sets {string.Join(", ", ignored)}, which apply to the base type of the custom conversion; set them in the configure of Convert.");
        }

        // WHY: The export also applies the mask of the base builder; an imported column must not carry either.
        if ((settings.Mask ?? effectiveSettings.Mask) is not null
            && settings.Direction != FieldDirection.ExportOnly)
        {
            Add("Masking is allowed on export-only fields only.");
        }

        // WHY: The reader treats a null marker and an empty text of both levels of a custom conversion as null, so the
        // markers of one level conflict with EmptyStringAsNull of the other as well.
        if ((HasNullMarker(settings) || HasNullMarker(effectiveSettings))
            && (settings.EmptyStringAsNull || effectiveSettings.EmptyStringAsNull))
        {
            Add("NullText and EmptyTranslationKey cannot be combined with EmptyStringAsNull.");
        }

        // WHY: A null marker of white space only reads as an empty value under trimming; null would not read back.
        if ((settings.Trim ?? effectiveSettings.Trim) == true
            && (IsBlank(settings.NullText) || IsBlank(effectiveSettings.NullText)))
        {
            Add("NullText is empty after trimming; a trimming field reads it as an empty value.");
        }

        if (settings.IsHeaderOptional
            && field.ValueType.IsValueType
            && !ConverterRegistry.IsNullableValueType(field.ValueType))
        {
            Add("An optional header requires a nullable field type, because a missing column yields the empty value.");
        }

        return;

        void Add(
            string message
        ) => errors.Add(TabularError.Configuration(message, location));
    }

    private static TabularPropertyPath Combine(
        TabularPropertyPath? path,
        string name
    ) => path is null
        ? TabularPropertyPath.Of(name)
        : path.Append(name);

    private static bool HasNullMarker(
        FieldSettings settings
    ) => settings.NullText is not null || settings.EmptyTranslationKey is not null;

    private static bool IsBlank(
        string? text
    ) => text is { Length: > 0 } && string.IsNullOrWhiteSpace(text);
}
