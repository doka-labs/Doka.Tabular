using System.Numerics;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>Static description of an enum type: members, values, attribute texts, and width.</summary>
internal sealed class EnumShape
{
    private static readonly Dictionary<Type, EnumShape> s_cache = [];

    private readonly TypeCode _code;

    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    private EnumShape(
        Type type
    )
    {
        Type = type;
        Underlying = Enum.GetUnderlyingType(type);
        _code = Type.GetTypeCode(Underlying);
        Bits = _code switch
        {
            TypeCode.SByte or TypeCode.Byte or TypeCode.Boolean => 8,
            TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => 16,
            TypeCode.Int32 or TypeCode.UInt32 => 32,
            TypeCode.Int64 or TypeCode.UInt64 => 64,
            _ => throw new UnreachableException($"The underlying type {Underlying} is not an enum base type."),
        };

        IsSigned = _code is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;

        IsFlags = type.IsDefined(typeof(FlagsAttribute), inherit: false);
        Members = type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => new EnumMember(
                field.Name,
                field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()
                    ?.Name,
                ToRaw(field.GetValue(null)!)))
            .ToArray();
    }

    public Type Type { get; }

    private Type Underlying { get; }

    public int Bits { get; }

    private bool IsSigned { get; }

    public bool IsFlags { get; }

    public IReadOnlyList<EnumMember> Members { get; }

    private ulong Mask => Bits == 64
        ? ulong.MaxValue
        : (1UL << Bits) - 1;

    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public static EnumShape Of(
        Type type
    )
    {
        lock (s_cache)
        {
            if (!s_cache.TryGetValue(type, out var shape))
            {
                shape = new EnumShape(type);
                s_cache[type] = shape;
            }

            return shape;
        }
    }

    private ulong ToRaw(
        object value
    ) => IsSigned
        ? unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)) & Mask
        : Convert.ToUInt64(value, CultureInfo.InvariantCulture);

    public object FromRaw(
        ulong raw
    )
    {
        // WHY: Each arm boxes its own type; without the casts to object the arms would share one numeric type as
        // soon as one arm is removed or changed, so the casts stay although the current set of arms has no common type.
        // ReSharper disable RedundantCast
        var typed = _code switch
        {
            TypeCode.SByte => (object)(sbyte)raw,
            TypeCode.Byte => (object)(byte)raw,
            TypeCode.Int16 => (object)(short)raw,
            TypeCode.UInt16 => (object)(ushort)raw,
            TypeCode.Int32 => (object)(int)raw,
            TypeCode.UInt32 => (object)(uint)raw,
            TypeCode.Int64 => (object)(long)raw,

            // WHY: Enum.ToObject accepts a UInt64 for every underlying type; char and bool enums only come from
            // assemblies not written in C#.
            TypeCode.UInt64 or TypeCode.Char or TypeCode.Boolean => (object)raw,
            _ => throw new UnreachableException($"The underlying type {Underlying} is not an enum base type."),
        };

        // ReSharper restore RedundantCast

        return Enum.ToObject(Type, typed);
    }

    public BigInteger ToNumber(
        ulong raw
    )
    {
        if (!IsSigned
            || (raw & (1UL << (Bits - 1))) == 0)
        {
            return raw;
        }

        return new BigInteger(raw) - (BigInteger.One << Bits);
    }

    public bool TryFromNumber(
        BigInteger number,
        out ulong raw
    )
    {
        raw = 0;
        var min = IsSigned
            ? -(BigInteger.One << (Bits - 1))
            : BigInteger.Zero;

        var max = IsSigned
            ? (BigInteger.One << (Bits - 1)) - 1
            : (BigInteger.One << Bits) - 1;

        if (number < min
            || number > max)
        {
            return false;
        }

        raw = (ulong)(number & Mask);

        return true;
    }

    public static bool IsValidFlags(
        ulong raw,
        ulong[] memberRaws
    )
    {
        if (raw == 0)
        {
            return true;
        }

        var covered = 0UL;

        // WHY: Combines the covered bits without a closure over the checked value.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var member in memberRaws)
        {
            if (member != 0
                && (member & raw) == member)
            {
                covered |= member;
            }
        }

        return covered == raw;
    }
}
