namespace Doka.Tabular;

internal sealed record ShapeRole(
    MapModel Model,
    EmbeddingSettings? Embedding,
    object? Identity = null
);

/// <summary>What one import row produces: the roles and how their records form the item.</summary>
internal sealed class ImportShape<TItem>
{
    private ImportShape(
        object map,
        IReadOnlyList<ShapeRole> roles,
        Func<IReadOnlyList<object>, TItem> combine,
        bool keepsItems
    )
    {
        Map = map;
        Roles = roles;
        Combine = combine;
        KeepsItems = keepsItems;
    }

    /// <summary>Gets whether the combined item keeps the list of role records, so each row needs its own list.</summary>
    public bool KeepsItems { get; }

    /// <summary>Gets the map of the call, a <see cref="TabularMap{T}" /> or a <see cref="RowMap" />.</summary>
    public object Map { get; }

    public IReadOnlyList<ShapeRole> Roles { get; }

    public Func<IReadOnlyList<object>, TItem> Combine { get; }

    public static ImportShape<T> ForMap<T>(
        TabularMap<T> map
    ) => new(map, [new ShapeRole(map.Model, null)], items => (T)items[0], keepsItems: false);

    public static ImportShape<TabularRow> ForRowMap(
        RowMap map
    ) => new(map,
        map
            .Roles
            .Select(role => new ShapeRole(role.Model, role.Embedding, role.Identity))
            .ToArray(),
        items => new TabularRow(map, items),
        keepsItems: true);
}

/// <summary>Everything an import needs per call, prepared before the first read.</summary>
internal sealed class ImportPlan<TItem>
{
    private ImportPlan(
        ImportShape<TItem> shape,
        ConversionContext context,
        ImportSettings settings,
        List<(MapNode Root, IReadOnlyList<FlatField> Fields)> roles,
        Dictionary<FlatField, ValueConverter> converters,
        Dictionary<FlatField, string?> expectedHeaders
    )
    {
        Shape = shape;
        Context = context;
        Settings = settings;
        RoleFields = roles;
        Converters = converters;
        ExpectedHeaders = expectedHeaders;
    }

    public ImportShape<TItem> Shape { get; }

    public ConversionContext Context { get; }

    public ImportSettings Settings { get; }

    private List<(MapNode Root, IReadOnlyList<FlatField> Fields)> RoleFields { get; }

    private Dictionary<FlatField, ValueConverter> Converters { get; }

    private Dictionary<FlatField, string?> ExpectedHeaders { get; }

    public IReadOnlyList<NodePlan> Roles { get; private set; } = [];

    public int MaxIndex { get; private set; } = -1;

    public bool NeedsHeader => Settings.Binding != ColumnBinding.ByIndex;

    /// <summary>Checks all call requirements that do not depend on file content.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public static ImportPlan<TItem> Prepare(
        ImportShape<TItem> shape,
        ImportSettings settings,
        ConversionContext context,
        List<TabularError> errors
    )
    {
        var roles = new List<(MapNode, IReadOnlyList<FlatField>)>();
        var converters = new Dictionary<FlatField, ValueConverter>();
        var headers = new Dictionary<FlatField, string?>();

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var roleIndex = 0; roleIndex < shape.Roles.Count; roleIndex++)
        {
            var role = shape.Roles[roleIndex];
            var fields = new List<FlatField>();
            var embedding = role.Embedding is null
                ? EmbeddingContext.Root
                : EmbeddingContext.Root.Enter(role.Embedding, string.Empty);

            var root = MapFlattening.Flatten(role.Model, null, embedding, fields);
            var imported = fields
                .Where(field => field.IsImported)
                .ToList();

            roles.Add((root, imported));

            // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var fieldIndex = 0; fieldIndex < imported.Count; fieldIndex++)
            {
                var field = imported[fieldIndex];
                converters[field] = ConverterFor(field, context, errors);
                if (settings.Binding == ColumnBinding.ByHeaderName)
                {
                    headers[field] = ExpectedHeader(field, context, errors);
                }
            }
        }

        // WHY: Each key without an imported role adds one configuration error; kept explicit.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var key in settings.AllKeys.Where(key => shape.Roles.All(role => role.Model.Type != key.ItemType)))
        {
            errors.Add(TabularError.Configuration(
                $"The source key belongs to {key.ItemType.Name}, which is not imported by this map."));
        }

        var plan = new ImportPlan<TItem>(shape, context, settings, roles, converters, headers);
        if (settings.Binding != ColumnBinding.ByHeaderName)
        {
            plan.Bind(null, errors, null);
        }
        else
        {
            // Constructor checks do not depend on header positions; check them before the first read.
            plan.CheckConstruction(errors);
        }

        return plan;
    }

    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    private static ValueConverter ConverterFor(
        FlatField field,
        ConversionContext context,
        List<TabularError> errors
    )
    {
        context.FreezeCultures(field.Settings);
        if (field.Settings.BlankNullMarker(context) is { } blank)
        {
            errors.Add(TabularError.Configuration(blank, FieldLocation(field)));
        }

        var readType = ConverterRegistry.Core(field.Settings.Conversion?.BaseType ?? field.ValueType);
        if (!readType.IsEnum)
        {
            var found = ConverterRegistry.Find(readType)!;
            errors.AddRange(found
                .ValidateCall(field.Settings.CheckedConversionSettings(), context)
                .Select(problem => TabularError.Configuration(problem, FieldLocation(field))));

            return found;
        }

        var problems = new List<string>();
        var converter = EnumConverter.Create(readType,
            field.Settings.CheckedConversionSettings(),
            context,
            forExport: false,
            problems);

        errors.AddRange(problems.Select(problem => TabularError.Configuration(problem, FieldLocation(field))));

        return converter;
    }

    /// <summary>The header a field is bound by; a field without header cannot be bound by header name.</summary>
    private static string? ExpectedHeader(
        FlatField field,
        ConversionContext context,
        List<TabularError> errors
    )
    {
        if (!HeaderText.TryResolve(field, context, out var header, out var headerError))
        {
            errors.Add(headerError!);
        }
        else if (string.IsNullOrWhiteSpace(header))
        {
            // WHY: An empty header, also a translated or prefixed one, names no column; Excel stores it as no cell.
            errors.Add(TabularError.Configuration(
                $"The field {field.Path} has no header and cannot be bound by header name.",
                FieldLocation(field)));
        }

        return header;
    }

    private static TabularErrorLocation FieldLocation(
        FlatField field
    ) => new()
    {
        ColumnKey = field.Key,
        PropertyPath = field.Path,
    };

    /// <summary>Resolves column positions (from the header record for header-name binding) and creates the node plans.</summary>
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public void Bind(
        SourceRecord? header,
        List<TabularError> errors,
        TabularSheetName? sheetName
    )
    {
        var readers = new Dictionary<FlatField, FieldReader>();
        var errorsBefore = errors.Count;
        var normalizedHeaders = NormalizedHeaders(header);
        foreach (var (_, fields) in RoleFields)
        {
            // WHY: Indexed access reads the list without an enumerator through the interface.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var index = field.Index;
                string? fileHeader = null;
                if (Settings.Binding == ColumnBinding.ByHeaderName
                    && !TryLocate(field,
                        header,
                        normalizedHeaders,
                        sheetName,
                        errors,
                        out index,
                        out fileHeader))
                {
                    continue;
                }

                readers[field] = field.Codec.CreateReader(field, Converters[field], index, fileHeader);

                MaxIndex = Math.Max(MaxIndex, index);
            }
        }

        // WHY: A header schema error ends the table; plans built without the unbound fields would only add
        // follow-up construction errors that CheckConstruction already ruled out before reading.
        Roles = errors.Count > errorsBefore
            ? []
            : CreateRoles(readers, errors);
    }

    private List<string?> NormalizedHeaders(
        SourceRecord? header
    ) => Settings.Binding == ColumnBinding.ByHeaderName && header is not null
        ? header
            .Cells
            .Select(cell => cell.Kind == CellKind.Text
                ? Normalize(cell.Text.ToString())
                : null)

            // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
            // ReSharper disable once UseCollectionExpression
            .ToList()
        : [];

    /// <summary>Finds the column of a field by its header; <see langword="false" /> when the field is not read.</summary>
    private bool TryLocate(
        FlatField field,
        SourceRecord? header,
        List<string?> normalizedHeaders,
        TabularSheetName? sheetName,
        List<TabularError> errors,
        out int index,
        out string? fileHeader
    )
    {
        index = -1;
        fileHeader = null;
        if (ExpectedHeaders.GetValueOrDefault(field) is not { } expected)
        {
            return false;
        }

        var normalized = Normalize(expected);
        var matches = normalizedHeaders
            .Select((text, position) => (text, position))
            .Where(entry => entry.text is not null
                && string.Equals(entry.text, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.position)
            .ToList();

        var (code, message) = matches switch
        {
            [_] => (null, null),
            [] when field.Settings.IsHeaderOptional => (null, null),
            [] => (TabularErrorCodes.Schema.MissingColumn, "A required header is missing."),
            _ => (TabularErrorCodes.Schema.AmbiguousColumn, "Several columns match the header."),
        };

        if (code is not null)
        {
            errors.Add(TabularError.Create(code,
                TabularErrorKind.Data,
                message!,
                new TabularErrorLocation
                {
                    ColumnKey = field.Key,
                    PropertyPath = field.Path,
                    ExpectedHeader = expected,
                    SheetName = sheetName,
                    RowNumber = header?.RowNumber,
                }));

            return false;
        }

        if (matches is [var found])
        {
            index = found;
            fileHeader = header!
                .Cells[found]
                .Text
                .ToString();
        }

        return true;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private List<NodePlan> CreateRoles(
        Dictionary<FlatField, FieldReader> readers,
        List<TabularError> errors
    )
    {
        var plans = new List<NodePlan>();
        foreach (var (root, _) in RoleFields)
        {
            if (NodePlan.Create(root, readers, errors) is { } plan)
            {
                plans.Add(plan);
            }
        }

        return plans;
    }

    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private void CheckConstruction(
        List<TabularError> errors
    )
    {
        var readers = new Dictionary<FlatField, FieldReader>();
        foreach (var (_, fields) in RoleFields)
        {
            // WHY: Indexed access reads the list without an enumerator through the interface.
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                readers[field] = field.Codec.CreateReader(field, Converters[field], field.Index, null);
            }
        }

        foreach (var (root, _) in RoleFields)
        {
            NodePlan.Create(root, readers, errors);
        }
    }

    private string Normalize(
        string text
    )
    {
        if (Settings.NormalizeHeader is { } normalize)
        {
            return normalize(text);
        }

        return text.Trim();
    }
}
