using System.Reflection;

namespace Doka.Tabular;

internal abstract class MapMember
{
    protected MapMember(
        PropertyInfo property,
        ValueKind kind
    )
    {
        Property = property;
        Kind = kind;
    }

    public PropertyInfo Property { get; }

    /// <summary>Gets the typed slot factory of the member value.</summary>
    public ValueKind Kind { get; }

    public string Name => Property.Name;

    public Type ValueType => Property.PropertyType;
}

internal sealed class ColumnMember : MapMember
{
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public ColumnMember(
        PropertyInfo property,
        ValueKind kind,
        int index,
        ColumnKey key,
        FieldSettings settings
    )
        : base(property, kind)
    {
        Index = index;
        Key = key;
        Settings = settings;
        Codec = FieldCodec.Create(kind.Type, settings.Conversion);
    }

    public int Index { get; }

    public ColumnKey Key { get; }

    public FieldSettings Settings { get; }

    public FieldCodec Codec { get; }
}

internal sealed class ComposePart
{
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public ComposePart(
        int index,
        ColumnKey key,
        ValueKind kind,
        FieldSettings settings
    )
    {
        Index = index;
        Key = key;
        Kind = kind;
        Settings = settings;
        Codec = FieldCodec.Create(kind.Type, settings.Conversion);
    }

    public int Index { get; }

    public ColumnKey Key { get; }

    public ValueKind Kind { get; }

    public FieldSettings Settings { get; }

    public FieldCodec Codec { get; }
}

internal sealed class ComposeMember : MapMember
{
    public ComposeMember(
        PropertyInfo property,
        IReadOnlyList<ComposePart> parts,
        ComposeBinding binding
    )
        : base(property, binding.Kind)
    {
        Parts = parts;
        Binding = binding;
        IsNullable = Nullability.IsNullable(property);
    }

    public IReadOnlyList<ComposePart> Parts { get; }

    /// <summary>Gets whether the composed property accepts <see langword="null" /> from the read function.</summary>
    public bool IsNullable { get; }

    public ComposeBinding Binding { get; }
}

internal sealed class EmbeddingSettings
{
    public int StartIndex { get; set; }

    public ColumnKey? KeyPrefix { get; set; }

    public string? HeaderPrefix { get; set; }

    public string? HeaderPrefixTranslationKey { get; set; }

    public Dictionary<string, ColumnKey> ExportKeys { get; private set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Headers { get; private set; } = new(StringComparer.Ordinal);

    public bool IsImportOnly { get; set; }

    public EmbeddingSettings Clone()
    {
        var copy = (EmbeddingSettings)MemberwiseClone();
        copy.ExportKeys = new Dictionary<string, ColumnKey>(ExportKeys, StringComparer.Ordinal);
        copy.Headers = new Dictionary<string, string>(Headers, StringComparer.Ordinal);

        return copy;
    }
}

internal sealed class NestedMember : MapMember
{
    public NestedMember(
        PropertyInfo property,
        ValueKind kind,
        MapModel map,
        EmbeddingSettings embedding,
        bool isNullable
    )
        : base(property, kind)
    {
        Map = map;
        Embedding = embedding;
        IsNullable = isNullable;
    }

    public MapModel Map { get; }

    public EmbeddingSettings Embedding { get; }

    public bool IsNullable { get; }
}

internal sealed record ExpectedException(
    Type ExceptionType,
    TabularErrorCode Code
);

/// <summary>A rule over several members; <see cref="Rule" /> reads the member slots in member order.</summary>
internal sealed class CrossRule
{
    public CrossRule(
        IReadOnlyList<MapMember> members,
        Func<ValueSlot[], bool> rule,
        TabularErrorCode? code,
        bool alsoOnExport
    )
    {
        Members = members;
        Rule = rule;
        Code = code;
        AlsoOnExport = alsoOnExport;
    }

    public IReadOnlyList<MapMember> Members { get; }

    public Func<ValueSlot[], bool> Rule { get; }

    public TabularErrorCode? Code { get; }

    public bool AlsoOnExport { get; }
}

/// <summary>Immutable structure of a built map, shared by all calls.</summary>
internal sealed class MapModel
{
    public MapModel(
        Type type,
        IReadOnlyList<MapMember> members,
        Func<RecordValues, object>? factory,
        IReadOnlyList<int> factoryReads,
        IReadOnlyList<ExpectedException> expectations,
        IReadOnlyList<CrossRule> rules
    )
    {
        Type = type;
        Members = members;
        Factory = factory;
        FactoryReads = factoryReads;
        Expectations = expectations;
        Rules = rules;
    }

    public Type Type { get; }

    public IReadOnlyList<MapMember> Members { get; }

    public Func<RecordValues, object>? Factory { get; }

    /// <summary>Gets the ordinals of the members the factory reads.</summary>
    public IReadOnlyList<int> FactoryReads { get; }

    private IReadOnlyList<ExpectedException> Expectations { get; }

    public IReadOnlyList<CrossRule> Rules { get; }

    public TabularErrorCode? FindExpectation(
        Exception exception
    )
    {
        // WHY: The first expectation in declaration order wins; no interface enumerator, no closure.
        // ReSharper disable once ForCanBeConvertedToForeach
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var index = 0; index < Expectations.Count; index++)
        {
            if (Expectations[index]
                .ExceptionType
                .IsInstanceOfType(exception))
            {
                return Expectations[index].Code;
            }
        }

        return null;
    }
}

/// <summary>The converted member values of one record for a factory, read from the slots of the node.</summary>
/// <summary>
/// The member values of the record being created, read by member ordinal. One instance belongs to one plan and is
/// reused for every record.
/// </summary>
internal sealed class RecordValues
{
    private readonly int[] _positions;
    private readonly ValueSlot[] _slots;
    private object? _typed;

    /// <param name="positions">The slot position of every member ordinal of the model, -1 for members not imported.</param>
    /// <param name="slots">The member slots of the plan.</param>
    public RecordValues(
        int[] positions,
        ValueSlot[] slots
    )
    {
        _positions = positions;
        _slots = slots;
    }

    /// <summary>The value of the member with <paramref name="ordinal" />; the plan has checked that it is imported.</summary>
    public TValue Get<TValue>(
        int ordinal
    ) => ((ValueSlot<TValue>)_slots[_positions[ordinal]]).Value;

    /// <summary>The typed view passed to a factory, created once per plan.</summary>
    public TabularValues<T> Typed<T>() => (TabularValues<T>)(_typed ??= new TabularValues<T>(this));
}
