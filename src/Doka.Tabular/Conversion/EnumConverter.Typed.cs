namespace Doka.Tabular;

/// <summary>The typed adapter of an enum converter; the raw bits are reinterpreted without boxing.</summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
internal sealed class EnumConverter<TEnum> : ValueConverter<TEnum>
    where TEnum : struct, Enum
{
    // WHY: Signedness belongs to each enum type, so one value per closed generic type is intended.
    // ReSharper disable once StaticMemberInGenericType
    private static readonly bool s_isSigned = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum)))
        is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;

    private readonly EnumConverter _engine;

    public EnumConverter(
        EnumConverter engine
    )
    {
        _engine = engine;
    }

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out TEnum value,
        out ConversionFailure failure
    )
    {
        var status = _engine.TryReadRaw(text, settings, out var raw, out failure);
        value = FromRaw(raw);

        return status;
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out TEnum value,
        out ConversionFailure failure
    )
    {
        if (cell.Kind != CellKind.Number)
        {
            return base.TryReadNative(cell, settings, context, out value, out failure);
        }

        var status = _engine.TryReadNativeRaw(cell, settings, out var raw, out failure);
        value = FromRaw(raw);

        return status;
    }

    protected override bool TryWriteText(
        TEnum value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    ) => _engine.TryWriteRaw(
        ToRaw(value),
        settings,
        context,
        excel: false,
        output,
        out _,
        out failure);

    public override bool TryWriteExcel(
        TEnum value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    ) => _engine.TryWriteRaw(
        ToRaw(value),
        settings,
        context,
        excel: true,
        output,
        out cell,
        out failure);

    /// <summary>The bits of the value, sign-extended for signed underlying types and masked to their width.</summary>
    private static ulong ToRaw(
        TEnum value
    ) => Unsafe.SizeOf<TEnum>() switch
    {
        1 => s_isSigned
            ? (byte)Unsafe.As<TEnum, sbyte>(ref value)
            : Unsafe.As<TEnum, byte>(ref value),
        2 => s_isSigned
            ? (ushort)Unsafe.As<TEnum, short>(ref value)
            : Unsafe.As<TEnum, ushort>(ref value),
        4 => s_isSigned
            ? (uint)Unsafe.As<TEnum, int>(ref value)
            : Unsafe.As<TEnum, uint>(ref value),
        _ => Unsafe.As<TEnum, ulong>(ref value),
    };

    private static TEnum FromRaw(
        ulong raw
    )
    {
        switch (Unsafe.SizeOf<TEnum>())
        {
            case 1:
                var small = (byte)raw;

                return Unsafe.As<byte, TEnum>(ref small);
            case 2:
                var medium = (ushort)raw;

                return Unsafe.As<ushort, TEnum>(ref medium);
            case 4:
                var large = (uint)raw;

                return Unsafe.As<uint, TEnum>(ref large);
            default:
                return Unsafe.As<ulong, TEnum>(ref raw);
        }
    }
}
