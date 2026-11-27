using System.Reflection;

namespace Doka.Tabular;

/// <summary>
/// The value types of one field: the converter type, the base type (the converter type or its nullable form, or the
/// base type of a custom conversion), and the property type. Created once per field by <c>Build()</c>.
/// </summary>
[RequiresDynamicCode("The codec is a generic type closed over the field types at run time.")]
[RequiresUnreferencedCode("The codec binds the field types by reflection.")]
internal abstract class FieldCodec
{
    public abstract Type ValueType { get; }

    public static FieldCodec Create(
        Type valueType,
        CustomConversion? conversion
    )
    {
        var baseType = conversion?.BaseType ?? valueType;
        var core = Nullable.GetUnderlyingType(baseType) ?? baseType;

        return (FieldCodec)Activator.CreateInstance(typeof(FieldCodec<,,>).MakeGenericType(core, baseType, valueType))!;
    }

    public abstract FieldReader CreateReader(
        FlatField field,
        ValueConverter converter,
        int columnIndex,
        string? fileHeader
    );

    public abstract FieldFormatter CreateFormatter(
        FlatField field,
        ValueConverter converter
    );
}

/// <summary>The typed stages between converter, base, and property value.</summary>
/// <typeparam name="TCore">The converter type.</typeparam>
/// <typeparam name="TBase">The converter type or its nullable form, or the base type of a custom conversion.</typeparam>
/// <typeparam name="TValue">The property type.</typeparam>
[RequiresDynamicCode("The stages are closed over the field types at run time.")]
[RequiresUnreferencedCode("The stages bind generic helper methods by reflection.")]
internal sealed class FieldCodec<TCore, TBase, TValue> : FieldCodec
{
    public override Type ValueType => typeof(TValue);

    /// <summary>Gets the step from the converter value to the base value: the value itself or its nullable form.</summary>
    public static Func<TCore, TBase> Lift { get; } = Stage<TCore, TBase>();

    /// <summary>Gets the step from a present base value back to the converter value.</summary>
    public static Func<TBase, TCore> Unlift { get; } = Stage<TBase, TCore>();

    /// <summary>Gets the step from base to property value without a custom conversion, where both types are equal.</summary>
    public static Func<TBase, TValue> Identity { get; } = Stage<TBase, TValue>();

    /// <summary>Gets the step from property to base value without a custom conversion.</summary>
    public static Func<TValue, TBase> IdentityBack { get; } = Stage<TValue, TBase>();

    public static bool BaseAllowsNull { get; } =
        !typeof(TBase).IsValueType || Nullable.GetUnderlyingType(typeof(TBase)) is not null;

    public static bool BaseIsString { get; } = typeof(TBase) == typeof(string);

    public override FieldReader CreateReader(
        FlatField field,
        ValueConverter converter,
        int columnIndex,
        string? fileHeader
    ) => new FieldReader<TCore, TBase, TValue>(
        field,
        (ValueConverter<TCore>)converter,
        columnIndex,
        fileHeader,
        ReadMap(field.Settings.Conversion));

    public override FieldFormatter CreateFormatter(
        FlatField field,
        ValueConverter converter
    ) => new FieldFormatter<TCore, TBase, TValue>(
        field,
        (ValueConverter<TCore>)converter,
        WriteMap(field.Settings.Conversion));

    /// <summary>The custom read function, or the identity when the base type is the property type.</summary>
    private static Func<TBase, TValue>? ReadMap(
        CustomConversion? conversion
    ) => conversion is CustomConversion<TBase, TValue> typed
        ? typed.Read
        : null;

    private static Func<TValue, TBase>? WriteMap(
        CustomConversion? conversion
    ) => conversion is CustomConversion<TBase, TValue> typed
        ? typed.Write
        : null;

    /// <summary>
    /// The identity when both types are equal, the nullable step when one type is the nullable form of the other, and
    /// otherwise a step that is never called.
    /// </summary>
    private static Func<TFrom, TTo> Stage<TFrom, TTo>()
    {
        if (typeof(TFrom) == typeof(TTo))
        {
            return (Func<TFrom, TTo>)(Delegate)new Func<TFrom, TFrom>(Stages.Same);
        }

        string step;
        Type core;
        if (Nullable.GetUnderlyingType(typeof(TTo)) == typeof(TFrom))
        {
            (step, core) = (nameof(Stages.ToNullable), typeof(TFrom));
        }
        else if (Nullable.GetUnderlyingType(typeof(TFrom)) == typeof(TTo))
        {
            (step, core) = (nameof(Stages.FromNullable), typeof(TTo));
        }
        else
        {
            // WHY: A custom conversion maps between unrelated types itself, so the identity steps of its codec are
            // never used; other unsupported combinations are reported by the structure validation.
            return _ => throw new InvalidOperationException("The field types do not form a nullable pair.");
        }

        return typeof(Stages)
            .GetMethod(step, BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(core)
            .CreateDelegate<Func<TFrom, TTo>>();
    }
}

/// <summary>The generic steps that the codec binds once per closed field type.</summary>
internal static class Stages
{
    public static T Same<T>(
        T value
    ) => value;

    public static T? ToNullable<T>(
        T value
    )
        where T : struct => value;

    public static T FromNullable<T>(
        T? value
    )
        where T : struct => value.GetValueOrDefault();

    /// <summary>Whether a value is present; a value type is always present.</summary>
    public static bool IsPresent<T>(
        T value
    ) => value is not null;
}
