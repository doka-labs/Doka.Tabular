using System.Numerics;
using System.Text;

namespace Doka.Tabular;

internal static class ConverterRegistry
{
    private static readonly Dictionary<Type, ValueConverter> s_converters = new()
    {
        [typeof(string)] = new StringConverter(),
        [typeof(char)] = new CharConverter(),
        [typeof(Rune)] = new RuneConverter(),
        [typeof(Guid)] = new GuidConverter(),
        [typeof(bool)] = new BooleanConverter(),
        [typeof(sbyte)] = new IntegerConverter<sbyte>(),
        [typeof(byte)] = new IntegerConverter<byte>(),
        [typeof(short)] = new IntegerConverter<short>(),
        [typeof(ushort)] = new IntegerConverter<ushort>(),
        [typeof(int)] = new IntegerConverter<int>(),
        [typeof(uint)] = new IntegerConverter<uint>(),
        [typeof(long)] = new IntegerConverter<long>(),
        [typeof(ulong)] = new IntegerConverter<ulong>(),
        [typeof(Int128)] = new IntegerConverter<Int128>(),
        [typeof(UInt128)] = new IntegerConverter<UInt128>(),
        [typeof(BigInteger)] = new BigIntegerConverter(),
        [typeof(Half)] = new FloatingConverter<Half>(),
        [typeof(float)] = new FloatingConverter<float>(),
        [typeof(double)] = new FloatingConverter<double>(),
        [typeof(decimal)] = new DecimalConverter(),
        [typeof(DateOnly)] = new DateOnlyConverter(),
        [typeof(TimeOnly)] = new TimeOnlyConverter(),
        [typeof(DateTime)] = new DateTimeConverter(),
        [typeof(DateTimeOffset)] = new DateTimeOffsetConverter(),
        [typeof(TimeSpan)] = new TimeSpanConverter(),
    };

    /// <summary>Returns the type without <see cref="Nullable{T}" />.</summary>
    public static Type Core(
        Type type
    ) => Nullable.GetUnderlyingType(type) ?? type;

    public static bool IsNullableValueType(
        Type type
    ) => Nullable.GetUnderlyingType(type) is not null;

    public static bool IsSupported(
        Type type
    )
    {
        var core = Core(type);

        return core.IsEnum || s_converters.ContainsKey(core);
    }

    /// <summary>Gets the shared converter of a non-enum type.</summary>
    public static ValueConverter? Find(
        Type type
    ) => s_converters.GetValueOrDefault(Core(type));

    public static string UnsupportedReason(
        Type type
    )
    {
        var core = Core(type);

        return core == typeof(nint) || core == typeof(nuint)
            ? $"The type {core.Name} depends on the platform and needs an explicit conversion with Convert."
            : $"The type {core.Name} has no built-in conversion; use Convert, Compose, or a nested map.";
    }
}
