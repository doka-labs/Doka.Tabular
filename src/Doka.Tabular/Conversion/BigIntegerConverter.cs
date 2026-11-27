using System.Numerics;

namespace Doka.Tabular;

internal sealed class BigIntegerConverter : ValueConverter<BigInteger>
{
    public override ExcelWidthClass WidthClass => ExcelWidthClass.Integer;

    public override IEnumerable<string> ValidateSettings(
        FieldSettings settings
    ) => NumberRules.CommonProblems<BigInteger>(settings, isInteger: true, isFloating: false);

    protected override ReadStatus TryRead(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        ConversionContext context,
        out BigInteger value,
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

        return FromExact(parsed.Value, out value, out failure);
    }

    public override ReadStatus TryReadNative(
        in SourceCell cell,
        FieldSettings settings,
        ConversionContext context,
        out BigInteger value,
        out ConversionFailure failure
    ) => cell.Kind != CellKind.Number || !double.IsFinite(cell.Number)
        ? base.TryReadNative(cell, settings, context, out value, out failure)
        : FromExact(ExactDecimal.FromDouble(cell.Number), out value, out failure);

    protected override bool TryWriteText(
        BigInteger value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        failure = default;
        var exact = new ExactDecimal(value.Sign < 0, BigInteger.Abs(value), 0);
        if (settings.ExportFormat is null)
        {
            NumberRules.WritePlain(exact, settings, context, output);

            return true;
        }

        return NumberRules.TryWriteWithExportFormat(value, exact, settings, context, output, out failure);
    }

    public override bool TryWriteExcel(
        BigInteger value,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    ) => NumberRules.TryExcelNumber(
        (double)value,
        new ExactDecimal(value.Sign < 0, BigInteger.Abs(value), 0),
        settings,
        out cell,
        out failure);

    private static ReadStatus FromExact(
        ExactDecimal exact,
        out BigInteger value,
        out ConversionFailure failure
    ) => exact.IsInteger
        ? Read(exact.ToBigIntegerExact(), out value, out failure)
        : Fail(ConversionFailure.InvalidFormat("The value is not a whole number."), out value, out failure);
}
