using System.Numerics;

namespace Doka.Tabular;

internal sealed partial class EnumConverter
{
    public ReadStatus TryReadRaw(
        ReadOnlySpan<char> text,
        FieldSettings settings,
        out ulong raw,
        out ConversionFailure failure
    )
    {
        failure = default;
        raw = 0;
        if (settings.AllowNumericInput
            && IsNumericText(text))
        {
            return FromNumber(BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                out raw,
                out failure);
        }

        var lookup = Lookup;
        if (!_shape.IsFlags)
        {
            if (lookup.TryGetValue(text, out raw))
            {
                return ReadStatus.Value;
            }

            failure = ConversionFailure.InvalidFormat("The text does not denote a value of this field.");

            return ReadStatus.Failed;
        }

        var any = false;
        var rest = text;
        while (true)
        {
            var end = rest.IndexOf(_separator);
            var component = (end < 0
                    ? rest
                    : rest[..end])
                .Trim(" \t");

            if (!component.IsEmpty)
            {
                if (!lookup.TryGetValue(component, out var part))
                {
                    failure = ConversionFailure.InvalidFormat(
                        "The text contains a part that does not denote a flag of this field.");

                    return ReadStatus.Failed;
                }

                raw |= part;
                any = true;
            }

            if (end < 0)
            {
                break;
            }

            rest = rest[(end + 1)..];
        }

        return any
            ? ReadStatus.Value
            : ReadStatus.Empty;
    }

    public ReadStatus TryReadNativeRaw(
        in SourceCell cell,
        FieldSettings settings,
        out ulong raw,
        out ConversionFailure failure
    )
    {
        raw = 0;
        if (!settings.AllowNumericInput)
        {
            failure = ConversionFailure.InvalidFormat("Numbers are not allowed for this field.");

            return ReadStatus.Failed;
        }

        if (!double.IsFinite(cell.Number))
        {
            failure = ConversionFailure.Unsupported("The cell does not contain a finite number.");

            return ReadStatus.Failed;
        }

        var exact = ExactDecimal.FromDouble(cell.Number);
        if (exact.IsInteger)
        {
            return FromNumber(exact.ToBigIntegerExact(), out raw, out failure);
        }

        failure = ConversionFailure.InvalidFormat("The number is not a whole number.");

        return ReadStatus.Failed;
    }

    /// <summary>Formats a value as text into <paramref name="output" />, or as a native number for Excel.</summary>
    public bool TryWriteRaw(
        ulong raw,
        FieldSettings settings,
        ConversionContext context,
        bool excel,
        TextBuffer output,
        out OutputCell cell,
        out ConversionFailure failure
    )
    {
        cell = OutputCell.Empty;
        var valid = _shape.IsFlags
            ? EnumShape.IsValidFlags(raw, _memberRaws)
            : _declared.Contains(raw);

        if (!valid)
        {
            failure = new ConversionFailure(_shape.IsFlags
                    ? TabularErrorCodes.Conversion.OutOfRange
                    : TabularErrorCodes.Conversion.MissingRepresentation,
                "The value is not a declared value of the enum.");

            return false;
        }

        if (settings.ExportAsNumber)
        {
            var number = _shape.ToNumber(raw);
            var exact = new ExactDecimal(number.Sign < 0, BigInteger.Abs(number), 0);
            if (excel)
            {
                return NumberRules.TryExcelNumber((double)number, exact, settings, out cell, out failure);
            }

            failure = default;
            var numberStart = output.Length;
            exact.WritePlain(output, CultureInfo.InvariantCulture);
            cell = OutputCell.FromText(output.Slice(numberStart));

            return true;
        }

        var start = output.Length;
        if (!TryWriteText(raw, settings, context, output, out failure))
        {
            output.Truncate(start);

            return false;
        }

        cell = OutputCell.FromText(output.Slice(start));

        return true;
    }

    private bool TryWriteText(
        ulong raw,
        FieldSettings settings,
        ConversionContext context,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        if (!_shape.IsFlags
            || raw == 0
            || _declared.Contains(raw))
        {
            return TryWriteTextOf(raw, settings, context, raw == 0 && !_declared.Contains(0), output, out failure);
        }

        Span<ulong> components = stackalloc ulong[64];
        var count = Components(raw, components);
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                output.Append(_separator);
            }

            if (!TryWriteTextOf(components[index], settings, context, isZeroWithoutMember: false, output, out failure))
            {
                return false;
            }
        }

        failure = default;

        return true;
    }

    private bool TryWriteTextOf(
        ulong raw,
        FieldSettings settings,
        ConversionContext context,
        bool isZeroWithoutMember,
        TextBuffer output,
        out ConversionFailure failure
    )
    {
        var fixedText = _fixedTexts.GetValueOrDefault(raw);
        if (settings.EnumTranslationKey is not null)
        {
            if (Translations.Resolve(_keys[raw], fixedText, context, out var text, out failure))
            {
                output.Append(text);

                return true;
            }

            if (failure.Code == TabularErrorCodes.Translation.Missing
                && fixedText is null
                && context.Translations is null)
            {
                failure = new ConversionFailure(TabularErrorCodes.Conversion.MissingRepresentation,
                    "The value 0 has no member and no translation.");
            }

            return false;
        }

        if (fixedText is null || isZeroWithoutMember)
        {
            failure = new ConversionFailure(TabularErrorCodes.Conversion.MissingRepresentation,
                "The value 0 has no member, no translation, and no numeric output.");

            return false;
        }

        failure = default;
        output.Append(fixedText);

        return true;
    }

    /// <summary>The flags of a composite value in ascending order; returns their number.</summary>
    private int Components(
        ulong raw,
        Span<ulong> components
    )
    {
        var count = 0;
        var allSingle = true;
        for (var bit = 0; bit < _shape.Bits; bit++)
        {
            var mask = 1UL << bit;
            if ((raw & mask) != 0
                && !_singles.Contains(mask))
            {
                allSingle = false;
                break;
            }
        }

        if (allSingle)
        {
            for (var bit = 0; bit < _shape.Bits; bit++)
            {
                var mask = 1UL << bit;
                if ((raw & mask) != 0)
                {
                    components[count++] = mask;
                }
            }

            return count;
        }

        var covered = 0UL;
        foreach (var member in _descending)
        {
            if (member != 0
                && (member & raw) == member
                && (member & ~covered) != 0)
            {
                components[count++] = member;
                covered |= member;
            }
        }

        components[..count]
            .Sort();

        return count;
    }

    private static bool IsNumericText(
        ReadOnlySpan<char> text
    )
    {
        var start = text.Length > 0 && text[0] is '+' or '-'
            ? 1
            : 0;

        return text.Length > start
            && text[start..]
                .IndexOfAnyExceptInRange('0', '9')
            < 0;
    }

    private ReadStatus FromNumber(
        BigInteger number,
        out ulong raw,
        out ConversionFailure failure
    )
    {
        failure = default;
        if (!_shape.TryFromNumber(number, out raw))
        {
            failure = ConversionFailure.OutOfRange("The number is outside the range of the enum's base type.");

            return ReadStatus.Failed;
        }

        var valid = _shape.IsFlags
            ? EnumShape.IsValidFlags(raw, _memberRaws)
            : _declared.Contains(raw);

        if (valid)
        {
            return ReadStatus.Value;
        }

        failure = ConversionFailure.InvalidFormat("The number is not a declared value of the enum.");

        return ReadStatus.Failed;
    }
}
