using System.Reflection;

namespace Doka.Tabular;

/// <summary>
/// The current value of one field or member of the record being imported or exported. A plan owns its slots
/// exclusively and reuses them for every record, so no value is boxed or kept in a dictionary.
/// </summary>
internal abstract class ValueSlot
{
    /// <summary>Gets or sets whether an intermediate object of the export path is null, so the value is absent.</summary>
    public bool IsMissing { get; set; }

    /// <summary>Gets whether the slot holds <see langword="null" />.</summary>
    public abstract bool HoldsNull { get; }

    /// <summary>Marks the value absent and sets it to the default of its type, as a path reader does.</summary>
    public abstract void SetMissing();
}

internal sealed class ValueSlot<T> : ValueSlot
{
    public T Value { get; set; } = default!;

    public override bool HoldsNull => Value is null;

    public override void SetMissing()
    {
        IsMissing = true;
        Value = default!;
    }
}

/// <summary>The type of a member value; creates the slots that hold it and the readers that fill them.</summary>
internal abstract class ValueKind
{
    public abstract Type Type { get; }

    public abstract ValueSlot CreateSlot();

    /// <summary>Stores a created nested record in a slot of this type.</summary>
    public abstract void Assign(
        ValueSlot slot,
        object? value
    );

    /// <summary>A reader of a property path of the export root into a slot of this type.</summary>
    [RequiresDynamicCode("The path reader is compiled from an expression tree at run time.")]
    [RequiresUnreferencedCode("The path reader binds the mapped properties by reflection.")]
    public abstract SlotReader CreateReader(
        IReadOnlyList<PropertyInfo> path
    );
}

internal sealed class ValueKind<T> : ValueKind
{
    public static ValueKind<T> Instance { get; } = new();

    public override Type Type => typeof(T);

    public override ValueSlot CreateSlot() => new ValueSlot<T>();

    public override void Assign(
        ValueSlot slot,
        object? value
    ) => ((ValueSlot<T>)slot).Value = (T)value!;

    [RequiresDynamicCode("The path reader is compiled from an expression tree at run time.")]
    [RequiresUnreferencedCode("The path reader binds the mapped properties by reflection.")]
    public override SlotReader CreateReader(
        IReadOnlyList<PropertyInfo> path
    ) => new SlotReader<T>(Accessors.Path<T>(path));
}

/// <summary>Reads the value of a property path of the export root into a slot.</summary>
internal abstract class SlotReader
{
    public abstract void Read(
        object? root,
        ValueSlot slot
    );
}

internal sealed class SlotReader<T> : SlotReader
{
    private readonly PathReader<T> _path;

    public SlotReader(
        PathReader<T> path
    )
    {
        _path = path;
    }

    public override void Read(
        object? root,
        ValueSlot slot
    )
    {
        var typed = (ValueSlot<T>)slot;
        typed.Value = _path(root, out var missing);
        typed.IsMissing = missing;
    }
}

internal abstract class FieldValidator
{
    protected FieldValidator(
        TabularErrorCode? code,
        bool alsoOnExport
    )
    {
        Code = code;
        AlsoOnExport = alsoOnExport;
    }

    public TabularErrorCode? Code { get; }

    public bool AlsoOnExport { get; }
}

/// <summary>A rule of one field over its typed value.</summary>
internal sealed class FieldValidator<TValue> : FieldValidator
{
    public FieldValidator(
        Func<TValue, bool> rule,
        TabularErrorCode? code,
        bool alsoOnExport
    )
        : base(code, alsoOnExport)
    {
        Rule = rule;
    }

    public Func<TValue, bool> Rule { get; }
}

/// <summary>A custom type bound through a built-in base type.</summary>
internal abstract class CustomConversion
{
    protected CustomConversion(
        FieldSettings baseSettings
    )
    {
        BaseSettings = baseSettings;
    }

    public abstract Type BaseType { get; }

    public FieldSettings BaseSettings { get; }

    public abstract CustomConversion WithBaseSettings(
        FieldSettings baseSettings
    );
}

internal sealed class CustomConversion<TBase, TCustom> : CustomConversion
{
    public CustomConversion(
        Func<TBase, TCustom> read,
        Func<TCustom, TBase> write,
        FieldSettings baseSettings
    )
        : base(baseSettings)
    {
        Read = read;
        Write = write;
    }

    public override Type BaseType => typeof(TBase);

    public Func<TBase, TCustom> Read { get; }

    public Func<TCustom, TBase> Write { get; }

    public override CustomConversion WithBaseSettings(
        FieldSettings baseSettings
    ) => new CustomConversion<TBase, TCustom>(Read, Write, baseSettings);
}

/// <summary>The typed read and write functions of a composed member.</summary>
internal abstract class ComposeBinding
{
    public abstract ValueKind Kind { get; }

    /// <summary>Combines the part slots into the member slot.</summary>
    public abstract void Read(
        ValueSlot[] parts,
        ValueSlot target
    );

    /// <summary>Splits the member value into the part slots.</summary>
    public abstract void Write(
        ValueSlot source,
        ValueSlot[] parts
    );
}

internal sealed class ComposeBinding<TValue, T1, T2> : ComposeBinding
{
    private readonly Func<T1, T2, TValue> _read;
    private readonly Func<TValue, (T1, T2)> _write;

    public ComposeBinding(
        Func<T1, T2, TValue> read,
        Func<TValue, (T1, T2)> write
    )
    {
        _read = read;
        _write = write;
    }

    public override ValueKind Kind => ValueKind<TValue>.Instance;

    public override void Read(
        ValueSlot[] parts,
        ValueSlot target
    ) => ((ValueSlot<TValue>)target).Value = _read(((ValueSlot<T1>)parts[0]).Value, ((ValueSlot<T2>)parts[1]).Value);

    public override void Write(
        ValueSlot source,
        ValueSlot[] parts
    )
    {
        var (first, second) = _write(((ValueSlot<TValue>)source).Value);
        ((ValueSlot<T1>)parts[0]).Value = first;
        ((ValueSlot<T2>)parts[1]).Value = second;
    }
}

internal sealed class ComposeBinding<TValue, T1, T2, T3> : ComposeBinding
{
    private readonly Func<T1, T2, T3, TValue> _read;
    private readonly Func<TValue, (T1, T2, T3)> _write;

    public ComposeBinding(
        Func<T1, T2, T3, TValue> read,
        Func<TValue, (T1, T2, T3)> write
    )
    {
        _read = read;
        _write = write;
    }

    public override ValueKind Kind => ValueKind<TValue>.Instance;

    public override void Read(
        ValueSlot[] parts,
        ValueSlot target
    ) => ((ValueSlot<TValue>)target).Value = _read(((ValueSlot<T1>)parts[0]).Value,
        ((ValueSlot<T2>)parts[1]).Value,
        ((ValueSlot<T3>)parts[2]).Value);

    public override void Write(
        ValueSlot source,
        ValueSlot[] parts
    )
    {
        var (first, second, third) = _write(((ValueSlot<TValue>)source).Value);
        ((ValueSlot<T1>)parts[0]).Value = first;
        ((ValueSlot<T2>)parts[1]).Value = second;
        ((ValueSlot<T3>)parts[2]).Value = third;
    }
}

internal sealed class ComposeBinding<TValue, T1, T2, T3, T4> : ComposeBinding
{
    private readonly Func<T1, T2, T3, T4, TValue> _read;
    private readonly Func<TValue, (T1, T2, T3, T4)> _write;

    public ComposeBinding(
        Func<T1, T2, T3, T4, TValue> read,
        Func<TValue, (T1, T2, T3, T4)> write
    )
    {
        _read = read;
        _write = write;
    }

    public override ValueKind Kind => ValueKind<TValue>.Instance;

    public override void Read(
        ValueSlot[] parts,
        ValueSlot target
    ) => ((ValueSlot<TValue>)target).Value = _read(((ValueSlot<T1>)parts[0]).Value,
        ((ValueSlot<T2>)parts[1]).Value,
        ((ValueSlot<T3>)parts[2]).Value,
        ((ValueSlot<T4>)parts[3]).Value);

    public override void Write(
        ValueSlot source,
        ValueSlot[] parts
    )
    {
        var (first, second, third, fourth) = _write(((ValueSlot<TValue>)source).Value);
        ((ValueSlot<T1>)parts[0]).Value = first;
        ((ValueSlot<T2>)parts[1]).Value = second;
        ((ValueSlot<T3>)parts[2]).Value = third;
        ((ValueSlot<T4>)parts[3]).Value = fourth;
    }
}
