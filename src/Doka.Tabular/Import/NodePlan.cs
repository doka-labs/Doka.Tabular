using System.Reflection;

namespace Doka.Tabular;

/// <summary>
/// How the record of one map node is created on import. A plan belongs to one call and owns the slots, flags, and
/// rule inputs it reuses for every record.
/// </summary>
[RequiresDynamicCode("The record creator is compiled from an expression tree at run time.")]
[RequiresUnreferencedCode("The record creator binds constructors and properties by reflection.")]
internal sealed class NodePlan
{
    private NodePlan(
        MapNode node,
        MemberPlan[] members,
        MapModel[] models,
        RecordCreator? creator
    )
    {
        Node = node;
        Members = members;
        Models = models;
        Creator = creator;

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        Slots = members
            .Select(member => member.Slot)
            .ToArray();

        Failed = new bool[members.Length];
        Values = new RecordValues(Positions(node.Model, members), Slots);

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        Rules = node
            .Model
            .Rules
            .Select(RuleInputs)
            .ToArray();
    }

    public MapNode Node { get; }

    public MemberPlan[] Members { get; }

    /// <summary>Gets the model of the node followed by the models of its enclosing nodes, for expected exceptions.</summary>
    public MapModel[] Models { get; }

    public RecordCreator? Creator { get; }

    /// <summary>Gets the member slots in member order.</summary>
    public ValueSlot[] Slots { get; }

    /// <summary>Gets whether a member failed in the current record.</summary>
    public bool[] Failed { get; }

    public RecordValues Values { get; }

    /// <summary>Gets each rule with the member positions it reads; a rule with an unmapped member is never run.</summary>
    public (CrossRule Rule, int[]? Positions, ValueSlot[] Inputs)[] Rules { get; }

    /// <summary>The slot position of every member ordinal of the model, -1 for members without a slot in this plan.</summary>
    private static int[] Positions(
        MapModel model,
        MemberPlan[] members
    )
    {
        var positions = new int[model.Members.Count];
        for (var ordinal = 0; ordinal < positions.Length; ordinal++)
        {
            positions[ordinal] = Array.FindIndex(members, plan => ReferenceEquals(plan.Member, model.Members[ordinal]));
        }

        return positions;
    }

    /// <summary>Reports every member the factory reads that this call does not import.</summary>
    private static bool FactoryReadsImported(
        MapNode node,
        MemberPlan[] members,
        List<TabularError> errors
    )
    {
        var model = node.Model;
        var imported = true;

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var readIndex = 0; readIndex < model.FactoryReads.Count; readIndex++)
        {
            var member = model.Members[model.FactoryReads[readIndex]];
            if (Array.FindIndex(members, plan => ReferenceEquals(plan.Member, member)) >= 0)
            {
                continue;
            }

            errors.Add(
                TabularError.Configuration(
                    $"The factory of {model.Type.Name} reads the property {member.Name}, which is not imported.",
                    new TabularErrorLocation { PropertyPath = TabularPropertyPath.Of(member.Name) }));

            imported = false;
        }

        return imported;
    }

    public static NodePlan? Create(
        MapNode node,
        IReadOnlyDictionary<FlatField, FieldReader> readers,
        List<TabularError> errors
    ) => Create(node, readers, errors, []);

    private static NodePlan? Create(
        MapNode node,
        IReadOnlyDictionary<FlatField, FieldReader> readers,
        List<TabularError> errors,
        MapModel[] enclosing
    )
    {
        MapModel[] models = [node.Model, .. enclosing];
        var members = MemberPlans(node, readers, errors, models);
        if (node.Model.Factory is not null)
        {
            return FactoryReadsImported(node, members, errors)
                ? new NodePlan(node, members, models, null)
                : null;
        }

        var type = node.Model.Type;
        var location = new TabularErrorLocation { PropertyPath = node.Path };
        var fitting = new List<(ConstructorInfo Constructor, CreatorArgument[] Arguments)>();
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            if (Bind(constructor, type, members, location, errors) is { } bound)
            {
                fitting.Add((constructor, bound));
            }
        }

        if (fitting is not [var (chosen, arguments)])
        {
            errors.Add(
                TabularError.Configuration(
                    fitting.Count == 0
                        ? $"No public constructor of {type.Name} fits the map; use a factory."
                        : $"Several public constructors of {type.Name} fit the map; use a factory to choose one.",
                    location));

            return null;
        }

        var setters = ResolveSetters(type, members, arguments, location, errors);

        return new NodePlan(node, members, models, Accessors.Creator(chosen, arguments, setters));
    }

    /// <summary>The members of the node that the source maps; nested nodes without mapped members are left out.</summary>
    private static MemberPlan[] MemberPlans(
        MapNode node,
        IReadOnlyDictionary<FlatField, FieldReader> readers,
        List<TabularError> errors,
        MapModel[] models
    )
    {
        var members = new List<MemberPlan>();
        foreach (var member in node.Members)
        {
            switch (member)
            {
                case ColumnNode column when readers.TryGetValue(column.Field, out var reader):
                    members.Add(
                        new MemberPlan(member.Member, member.Member.Kind.CreateSlot(), reader, null, null, null));

                    break;
                case ComposeNode compose when compose.Parts.All(readers.ContainsKey):
                    members.Add(
                        new MemberPlan(
                            member.Member,
                            member.Member.Kind.CreateSlot(),
                            null,
                            compose
                                .Parts
                                .Select(part => readers[part])
                                .ToArray(),

                            // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
                            // ReSharper disable once UseCollectionExpression
                            compose
                                .Compose
                                .Parts
                                .Select(part => part.Kind.CreateSlot())
                                .ToArray(),
                            null));

                    break;

                // WHY: A substructure without any imported field is not created on import, so its constructor is not
                // checked either.
                case NestedNode nested when Imports(nested.Node, readers):
                    if (Create(nested.Node, readers, errors, models) is { Members.Length: > 0 } inner)
                    {
                        members.Add(
                            new MemberPlan(
                                member.Member,
                                member.Member.Kind.CreateSlot(),
                                null,
                                null,
                                null,
                                inner));
                    }

                    break;
            }
        }

        return [.. members];
    }

    /// <summary>Whether the node or one of its substructures has a field this call imports.</summary>
    private static bool Imports(
        MapNode node,
        IReadOnlyDictionary<FlatField, FieldReader> readers
    )
    {
        foreach (var member in node.Members)
        {
            var imported = member switch
            {
                ColumnNode column => readers.ContainsKey(column.Field),
                ComposeNode compose => compose.Parts.All(readers.ContainsKey),
                NestedNode nested => Imports(nested.Node, readers),
                _ => false,
            };

            if (imported)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The arguments of every parameter of the constructor; <see langword="null" /> when it does not fit.</summary>
    private static CreatorArgument[]? Bind(
        ConstructorInfo constructor,
        Type type,
        MemberPlan[] members,
        TabularErrorLocation location,
        List<TabularError> errors
    )
    {
        var parameters = constructor.GetParameters();
        var arguments = new CreatorArgument[parameters.Length];
        for (var position = 0; position < parameters.Length; position++)
        {
            var parameter = parameters[position];
            var candidates = new List<int>();
            for (var index = 0; index < members.Length; index++)
            {
                if (string.Equals(members[index].Member.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
                    && parameter.ParameterType.IsAssignableFrom(members[index].Member.ValueType))
                {
                    candidates.Add(index);
                }
            }

            switch (candidates)
            {
                case [var candidate]:
                    arguments[position] = new CreatorArgument(candidate, members[candidate].Member.Kind.Type, null);
                    break;
                case [] when parameter.IsOptional:
                    arguments[position] = new CreatorArgument(
                        null,
                        null,
                        parameter.HasDefaultValue
                            ? parameter.DefaultValue
                            : null);

                    break;
                case []:
                    return null;
                default:
                    var names = string.Join(" and ", candidates.Select(index => members[index].Member.Name));
                    errors.Add(
                        TabularError.Configuration(
                            $"The constructor parameter {parameter.Name} of {type.Name} matches {names}; use a factory.",
                            location));

                    return null;
            }
        }

        return arguments;
    }

    /// <summary>The members not bound by the constructor; each must have a public setter.</summary>
    private static List<(PropertyInfo Property, int Slot, Type SlotType)> ResolveSetters(
        Type type,
        MemberPlan[] members,
        CreatorArgument[] arguments,
        TabularErrorLocation location,
        List<TabularError> errors
    )
    {
        var bound = arguments
            .Where(argument => argument.Slot is not null)
            .Select(argument => argument.Slot!.Value)
            .ToHashSet();

        var setters = new List<(PropertyInfo, int, Type)>();
        for (var index = 0; index < members.Length; index++)
        {
            if (bound.Contains(index))
            {
                continue;
            }

            var member = members[index].Member;
            if (member.Property.SetMethod is { IsPublic: true })
            {
                setters.Add((member.Property, index, member.Kind.Type));
                continue;
            }

            errors.Add(
                TabularError.Configuration(
                    $"The property {member.Name} of {type.Name} is neither bound by the constructor nor publicly settable; use a factory.",
                    location));
        }

        return setters;
    }

    private (CrossRule Rule, int[]? Positions, ValueSlot[] Inputs) RuleInputs(
        CrossRule rule
    )
    {
        var positions = new int[rule.Members.Count];
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] = Array.FindIndex(Members, member => member.Member == rule.Members[index]);
            if (positions[index] < 0)
            {
                return (rule, null, []);
            }
        }

        // WHY: Fluent materialization stays explicit as ToArray or ToList instead of a spread.
        // ReSharper disable once UseCollectionExpression
        return (rule, positions, positions
            .Select(position => Slots[position])
            .ToArray());
    }
}

/// <summary>One mapped member of a node: a column, a composed value with its parts, or a nested record.</summary>
internal sealed record MemberPlan(
    MapMember Member,
    ValueSlot Slot,
    FieldReader? Reader,
    IReadOnlyList<FieldReader>? Parts,
    ValueSlot[]? PartSlots,
    NodePlan? Nested
);
