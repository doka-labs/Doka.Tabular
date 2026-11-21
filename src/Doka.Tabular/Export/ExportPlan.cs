using System.Reflection;

namespace Doka.Tabular;

/// <summary>How the value of one output column is read: directly, or as a part of a composed member.</summary>
internal sealed record ColumnSource(
    int Role,
    SlotReader? Reader,
    int ComposeGroup
);

/// <summary>One composed member of a role: the reader of its value and the slots of its parts.</summary>
internal sealed record ComposeGroup(
    int Role,
    ComposeMember Compose,
    SlotReader Reader,
    ValueSlot Value,
    ValueSlot[] Parts
);

/// <summary>A rule over several members that runs on export, with the readers and slots of its members.</summary>
internal sealed record ExportRule(
    int Role,
    CrossRule Rule,
    SlotReader[] Readers,
    ValueSlot[] Slots,
    TabularPropertyPath Path,
    ColumnKey? Key
);

/// <summary>
/// Everything an export needs per call, checked before the first write. The plan owns the slots, root values, and
/// text buffer it reuses for every record.
/// </summary>
[RequiresDynamicCode("Field readers and formatters are closed over the field types at run time.")]
[RequiresUnreferencedCode("Field readers bind the mapped properties by reflection.")]
internal sealed partial class ExportPlan<TSource>
{
    private readonly bool[] _columnRead;
    private readonly bool[] _composeDone;
    private readonly ComposeGroup[] _composes;
    private readonly ExportRole[] _roles;
    private readonly bool[] _rootRead;
    private readonly object?[] _roots;
    private readonly ExportRule[] _rules;
    private readonly ValueSlot?[] _slots;
    private readonly ColumnSource?[] _sources;
    private readonly TextBuffer _text = new();

    private ExportPlan(
        ConversionContext context,
        IReadOnlyList<ExportColumn> columns,
        ExportRole[] roles,
        ColumnSource?[] sources,
        ValueSlot?[] slots,
        ComposeGroup[] composes,
        ExportRule[] rules
    )
    {
        Context = context;
        Columns = columns;
        _roles = roles;
        _sources = sources;
        _slots = slots;
        _composes = composes;
        _rules = rules;
        _roots = new object?[roles.Length];
        _rootRead = new bool[roles.Length];
        _columnRead = new bool[columns.Count];
        _composeDone = new bool[composes.Length];
    }

    public IReadOnlyList<ExportColumn> Columns { get; }

    /// <summary>Gets the effective export keys of every role, also of roles whose columns are not selected.</summary>
    public IReadOnlySet<ColumnKey> ExportKeys { get; private set; } = new HashSet<ColumnKey>();

    private ConversionContext Context { get; }

    public static ExportPlan<TSource>? Prepare(
        ExportShape<TSource> shape,
        ExportSettings settings,
        ConversionContext context,
        List<TabularError> errors
    )
    {
        var access = new Dictionary<FlatField, FieldPath>();
        var rules = new List<RoleRule>();
        var allFields = new List<(FlatField Field, ExportRole Role)>();

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
            Collect(root, [], access, rules, role);
            allFields.AddRange(fields.Select(field => (field, role)));
        }

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        var layout = settings.ColumnKeys is { } keys
            ? SelectedLayout(keys, allFields, errors)
            : IndexLayout(allFields
                    .Where(entry => entry.Field.IsExported)
                    .ToList(),
                errors);

        var columns = new List<ExportColumn>(layout.Count);

        // WHY: Each position creates its column and may add configuration errors; kept explicit.
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var position = 0; position < layout.Count; position++)
        {
            columns.Add(layout[position] is { } entry
                ? CreateColumn(position, entry.Field, entry.Role, settings, context, errors)
                : new ExportColumn(position, null, null, null, null, null));
        }

        // WHY: What the export writes must read back by header name; translations are known only per call.
        HeaderText.AddIndistinct(columns
                .Where(column => column.Field is not null)
                .Select(column => (column.Field!, column.Header)),
            errors);

        if (errors.Count > 0)
        {
            return null;
        }

        var plan = Create(context, columns, [.. shape.Roles], access, rules);
        plan.ExportKeys = allFields
            .Where(entry => entry.Field.IsExported)
            .Select(entry => entry.Field.Key)
            .ToHashSet();

        return plan;
    }

    /// <summary>Creates the readers and slots of every column, composed member, and export rule.</summary>
    private static ExportPlan<TSource> Create(
        ConversionContext context,
        List<ExportColumn> columns,
        ExportRole[] roles,
        Dictionary<FlatField, FieldPath> access,
        List<RoleRule> rules
    )
    {
        var sources = new ColumnSource?[columns.Count];
        var slots = new ValueSlot?[columns.Count];
        var composes = new List<ComposeGroup>();
        var groupOf = new Dictionary<(int, IReadOnlyList<PropertyInfo>), int>();

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            if (column.Field is not { } field)
            {
                continue;
            }

            var role = Array.IndexOf(roles, column.Role);
            var path = access[field];
            if (path.Compose is not { } compose)
            {
                sources[column.Position] = new ColumnSource(role, path.Member.Kind.CreateReader(path.Path), -1);
                slots[column.Position] = path.Member.Kind.CreateSlot();
                continue;
            }

            if (!groupOf.TryGetValue((role, path.Path), out var group))
            {
                group = composes.Count;
                groupOf[(role, path.Path)] = group;

                // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
                // ReSharper disable once UseCollectionExpression
                composes.Add(new ComposeGroup(role,
                    compose,
                    compose.Kind.CreateReader(path.Path),
                    compose.Kind.CreateSlot(),
                    compose
                        .Parts
                        .Select(part => part.Kind.CreateSlot())
                        .ToArray()));
            }

            sources[column.Position] = new ColumnSource(role, null, group);
            slots[column.Position] = composes[group]
                .Parts[path.PartIndex];
        }

        return new ExportPlan<TSource>(context,
            columns,
            roles,
            sources,
            slots,
            [.. composes],
            ExportRules(columns, roles, rules));
    }

    /// <summary>The columns in the order of the selected keys.</summary>
    private static List<(FlatField Field, ExportRole Role)?> SelectedLayout(
        IReadOnlyList<ColumnKey> keys,
        List<(FlatField Field, ExportRole Role)> allFields,
        List<TabularError> errors
    )
    {
        List<(FlatField Field, ExportRole Role)?> layout = [];
        var seen = new HashSet<ColumnKey>();

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var keyIndex = 0; keyIndex < keys.Count; keyIndex++)
        {
            var key = keys[keyIndex];

            // WHY: A deserialized selection such as the JSON array [null] holds null entries despite the
            // non-nullable element type; System.Text.Json does not pass null to the key converter.
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (key is null)
            {
                errors.Add(TabularError.Configuration("The column selection contains an empty entry."));
                continue;
            }

            var location = new TabularErrorLocation { ColumnKey = key };
            if (!seen.Add(key))
            {
                errors.Add(TabularError.Configuration($"The key {key} is selected more than once.", location));
                continue;
            }

            var match = allFields.FirstOrDefault(entry => entry.Field.Key == key);
            if (match.Field is not { IsExported: true })
            {
                errors.Add(TabularError.Configuration(match.Field is null
                        ? $"The key {key} is not a key of the map."
                        : $"The key {key} belongs to an import-only field.",
                    location));

                continue;
            }

            layout.Add(match);
        }

        return layout;
    }

    /// <summary>The columns at the output indexes of the exported fields; unused indexes stay gaps.</summary>
    private static List<(FlatField Field, ExportRole Role)?> IndexLayout(
        List<(FlatField Field, ExportRole Role)> exported,
        List<TabularError> errors
    )
    {
        // WHY: Each duplicate group adds one configuration error; a query would hide that side effect.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var group in exported
            .GroupBy(entry => entry.Field.Index)
            .Where(group => group.Count() > 1))
        {
            var paths = string.Join(", ", group.Select(entry => entry.Field.Path.Value));
            errors.Add(TabularError.Configuration($"Several fields write to output index {group.Key}: {paths}.",
                new TabularErrorLocation { ColumnIndex = group.Key }));
        }

        var width = exported.Count == 0
            ? 0
            : exported.Max(entry => entry.Field.Index) + 1;

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        List<(FlatField Field, ExportRole Role)?> layout =
            Enumerable
                .Repeat<(FlatField, ExportRole)?>(null, width)
                .ToList();

        foreach (var entry in exported.Where(entry => entry.Field.Index >= 0))
        {
            layout[entry.Field.Index] = entry;
        }

        return layout;
    }

    private static ExportColumn CreateColumn(
        int position,
        FlatField field,
        ExportRole role,
        ExportSettings settings,
        ConversionContext context,
        List<TabularError> errors
    )
    {
        context.FreezeCultures(field.Settings);
        if (field.Settings.BlankNullMarker(context) is { } blank)
        {
            errors.Add(TabularError.Configuration(blank,
                new TabularErrorLocation
                {
                    PropertyPath = field.Path,
                    ColumnKey = field.Key,
                }));
        }

        var writeType = ConverterRegistry.Core(field.Settings.Conversion?.BaseType ?? field.ValueType);
        ValueConverter converter;
        if (writeType.IsEnum)
        {
            var problems = new List<string>();
            converter = EnumConverter.Create(writeType,
                field.Settings.CheckedConversionSettings(),
                context,
                forExport: true,
                problems);

            errors.AddRange(problems.Select(problem => TabularError.Configuration(problem,
                new TabularErrorLocation
                {
                    ColumnKey = field.Key,
                    PropertyPath = field.Path,
                })));
        }
        else
        {
            converter = ConverterRegistry.Find(writeType)!;
            errors.AddRange(converter
                .ValidateCall(field.Settings.CheckedConversionSettings(), context)
                .Select(problem => TabularError.Configuration(problem,
                    new TabularErrorLocation
                    {
                        ColumnKey = field.Key,
                        PropertyPath = field.Path,
                    })));
        }

        string? header = null;
        if (settings.WriteHeader
            && !HeaderText.TryResolve(field, context, out header, out var headerError))
        {
            errors.Add(headerError!);
        }

        return new ExportColumn(position,
            field,
            role,
            converter,
            header,
            field.Codec.CreateFormatter(field, converter));
    }

    /// <summary>
    /// The rules over several fields that run on export and whose fields are all selected in their own role; a member is
    /// selected when a selected column of the same role lies at or below its path.
    /// </summary>
    private static ExportRule[] ExportRules(
        List<ExportColumn> columns,
        ExportRole[] roles,
        List<RoleRule> rules
    )
    {
        var selected = new List<(ExportRole Role, string Path)>();

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var position = 0; position < columns.Count; position++)
        {
            if (columns[position] is { IsGap: false, Role: { } role, Field: { } field })
            {
                selected.Add((role, field.Path.Value));
            }
        }

        var result = new List<ExportRule>();

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            var (role, prefix, rule) = rules[ruleIndex];
            if (rule.AlsoOnExport
                && rule.Members.All(member => IsSelected(selected, role, PathOf(prefix, member.Property))))
            {
                // WHY: The error names the first member with its full path and the key of its first column, so two
                // embeddings of the same nested map stay apart.
                var first = PathOf(prefix, rule.Members[0].Property);
                var column = FirstColumn(columns, role, first);
                result.Add(new ExportRule(Array.IndexOf(roles, role),
                    rule,
                    rule
                        .Members
                        .Select(member => member.Kind.CreateReader([.. prefix, member.Property]))
                        .ToArray(),

                    // ReSharper disable once UseCollectionExpression
                    rule
                        .Members
                        .Select(member => member.Kind.CreateSlot())
                        .ToArray(),
                    column?.Path ?? TabularPropertyPath.Of(first),
                    column?.Key));
            }
        }

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        return result.ToArray();
    }

    /// <summary>The field of the first selected column of a member path in a role.</summary>
    private static FlatField? FirstColumn(
        List<ExportColumn> columns,
        ExportRole role,
        string path
    )
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var position = 0; position < columns.Count; position++)
        {
            if (columns[position] is { IsGap: false, Role: { } columnRole, Field: { } field }
                && ReferenceEquals(columnRole, role)
                && (field.Path.Value == path || field.Path.Value.StartsWith(path + ".", StringComparison.Ordinal)))
            {
                return field;
            }
        }

        return null;
    }

    private static bool IsSelected(
        List<(ExportRole Role, string Path)> selected,
        ExportRole role,
        string path
    )
    {
        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var index = 0; index < selected.Count; index++)
        {
            var (selectedRole, selectedPath) = selected[index];
            if (ReferenceEquals(selectedRole, role)
                && (selectedPath == path || selectedPath.StartsWith(path + ".", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static string PathOf(
        IReadOnlyList<PropertyInfo> prefix,
        PropertyInfo property
    ) => prefix.Count == 0
        ? property.Name
        : string.Join('.', prefix.Select(part => part.Name)) + "." + property.Name;

    private static void Collect(
        MapNode node,
        IReadOnlyList<PropertyInfo> prefix,
        Dictionary<FlatField, FieldPath> access,
        List<RoleRule> rules,
        ExportRole role
    )
    {
        foreach (var member in node.Members)
        {
            IReadOnlyList<PropertyInfo> path = [.. prefix, member.Member.Property];
            switch (member)
            {
                case ColumnNode column:
                    access[column.Field] = new FieldPath(path, column.Column, null, 0);
                    break;
                case ComposeNode compose:
                    for (var index = 0; index < compose.Parts.Count; index++)
                    {
                        access[compose.Parts[index]] = new FieldPath(path, compose.Compose, compose.Compose, index);
                    }

                    break;
                case NestedNode nested:
                    Collect(nested.Node, path, access, rules, role);
                    break;
            }
        }

        // WHY: The rules of a nested map belong to it at every depth; the prefix leads from the role to its members.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var ruleIndex = 0; ruleIndex < node.Model.Rules.Count; ruleIndex++)
        {
            rules.Add(new RoleRule(role, prefix, node.Model.Rules[ruleIndex]));
        }
    }

    /// <summary>A rule over several fields with its role and the property path from the role to its map.</summary>
    private sealed record RoleRule(
        ExportRole Role,
        IReadOnlyList<PropertyInfo> Prefix,
        CrossRule Rule
    );

    /// <summary>The property path of an exported field and the member it belongs to.</summary>
    private sealed record FieldPath(
        IReadOnlyList<PropertyInfo> Path,
        MapMember Member,
        ComposeMember? Compose,
        int PartIndex
    );
}
