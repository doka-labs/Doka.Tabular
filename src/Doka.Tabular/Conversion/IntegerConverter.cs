using System.Numerics;

namespace Doka.Tabular;

/// <summary>Fixed-size integers from 8 to 128 bits.</summary>
internal sealed class IntegerConverter<TInteger> : ValueConverter<TInteger>
    where TInteger : struct, IBinaryInteger<TInteger>, IMinMaxValue<TInteger>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.Integer;

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    ) => NumberRules.CommonProblems<TInteger>(settings, isInteger: true, isFloating: false);

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out TInteger value,
        out ConversionFailure failure
    )
    {
        if (!NumberRules.TryParse(text, settings, context, exponentByDefault: false, out var parsed, out failure))
        {
            value = default;

            return ReadStatus.Failed;
        }

        if (parsed.HadDecimalPoint
            && !settings.AllowDecimalPoint)
        {
            return Fail(
                ConversionFailure.InvalidFormat("A decimal point is not allowed for an integer field."),
                out value,
                out failure);
        }

        return parsed is { IsSmall: true, Exponent: 0 }
            ? FromSmall(parsed.Negative, parsed.Small, out value, out failure)
            : FromExact(parsed.Value, out value, out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out TInteger value,
        out ConversionFailure failure
    )
    {
        if (cell.Kind != CellKind.Number)
        {
            return base.TryReadNative(cell, settings, context, out value, out failure);
        }

        if (!double.IsFinite(cell.Number))
        {
            return Fail(
                ConversionFailure.Unsupported("The cell does not contain a finite number."),
                out value,
                out failure);
        }

        // WHY: Whole numbers up to 2^53 are exact in a double; larger or fractional values take the exact path.
        // WHY: The exact comparison is the point: it accepts only doubles without a fractional part.
        var number = cell.Number;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (Math.Abs(number) <= 9007199254740992d
            && number == Math.Floor(number))
        {
            return FromSmall(number < 0, (ulong)Math.Abs(number), out value, out failure);
        }

        return FromExact(ExactDecimal.FromDouble(number), out value, out failure);
    }

    protected override bool TryWriteText(
        TInteger value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (settings is { ExportFormat: null, TextStyle: NumberTextStyle.Plain })
        {
            output.AppendFormatted(value, "D", context.CultureFor(settings));

            return true;
        }

        var exact = Exact(value);
        if (settings.ExportFormat is null)
        {
            NumberRules.WritePlain(exact, settings, context, output);

            return true;
        }

        return NumberRules.TryWriteWithExportFormat(value, exact, settings, context, output, out failure);
    }

    public override bool TryWriteExcel(
        TInteger value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        // WHY: Integers with at most 15 digits are exact doubles whose shortest text is the integer itself.
        if (value <= s_excelLimit
            && value >= s_excelNegativeLimit)
        {
            failure = default;
            cell = OutputCell.FromNumber(double.CreateChecked(value), settings.ExcelNumberFormat);

            return true;
        }

        var big = BigInteger.CreateChecked(value);

        return NumberRules.TryExcelNumber((double)big, Exact(value), settings, out cell, out failure);
    }

    private static ExactDecimal Exact(
        TInteger value
    ) => new(TInteger.IsNegative(value), BigInteger.Abs(BigInteger.CreateChecked(value)), 0);

    private static ReadStatus FromSmall(
        bool negative,
        ulong magnitude,
        out TInteger value,
        out ConversionFailure failure
    )
    {
        if (magnitude == 0)
        {
            return Read(TInteger.Zero, out value, out failure);
        }

        if (!negative)
        {
            return magnitude <= s_maxMagnitude
                ? Read(TInteger.CreateChecked(magnitude), out value, out failure)
                : Fail(
                    ConversionFailure.OutOfRange("The value is outside the range of the target type."),
                    out value,
                    out failure);
        }

        // WHY: The magnitude of the minimum value is one larger than the maximum, so negate in Int128.
        var signed = -(Int128)magnitude;

        return signed >= s_minSigned
            ? Read(TInteger.CreateChecked(signed), out value, out failure)
            : Fail(
                ConversionFailure.OutOfRange("The value is outside the range of the target type."),
                out value,
                out failure);
    }

    private static ReadStatus FromExact(
        ExactDecimal exact,
        out TInteger value,
        out ConversionFailure failure
    )
    {
        if (!exact.IsInteger)
        {
            return Fail(ConversionFailure.InvalidFormat("The value is not a whole number."), out value, out failure);
        }

        var integer = exact.ToBigIntegerExact();

        return integer < s_min || integer > s_max
            ? Fail(
                ConversionFailure.OutOfRange("The value is outside the range of the target type."),
                out value,
                out failure)
            : Read(TInteger.CreateChecked(integer), out value, out failure);
    }

    // WHY: The range belongs to each integer type, so one value per closed generic type is intended.
    // ReSharper disable StaticMemberInGenericType
    private static readonly BigInteger s_min = BigInteger.CreateChecked(TInteger.MinValue);
    private static readonly BigInteger s_max = BigInteger.CreateChecked(TInteger.MaxValue);
    private static readonly UInt128 s_maxMagnitude = UInt128.CreateSaturating(TInteger.MaxValue);
    private static readonly Int128 s_minSigned = Int128.CreateSaturating(TInteger.MinValue);
    private static readonly TInteger s_excelLimit = TInteger.CreateSaturating(999_999_999_999_999L);
    private static readonly TInteger s_excelNegativeLimit = TInteger.CreateSaturating(-999_999_999_999_999L);

    // ReSharper restore StaticMemberInGenericType
}
