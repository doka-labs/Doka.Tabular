using System.Buffers;
using System.Numerics;

namespace Doka.Tabular;

/// <summary>Exact decimal value parsed from text: (-1)^sign * mantissa * 10^exponent.</summary>
internal readonly record struct ExactDecimal(
    bool Negative,
    BigInteger Mantissa,
    int Exponent
)
{
    public bool IsZero => Mantissa.IsZero;

    public bool IsInteger
    {
        get
        {
            if (Exponent >= 0
                || Mantissa.IsZero)
            {
                return true;
            }

            var divisor = BigInteger.Pow(10, -Exponent);

            return BigInteger.Remainder(Mantissa, divisor)
                .IsZero;
        }
    }

    public int SignificantDigits => Mantissa.IsZero
        ? 1
        : DigitCount(Normalize()
            .Mantissa);

    public BigInteger ToBigIntegerExact()
    {
        var magnitude = Exponent >= 0
            ? Mantissa * BigInteger.Pow(10, Exponent)
            : Mantissa / BigInteger.Pow(10, -Exponent);

        return Negative
            ? -magnitude
            : magnitude;
    }

    /// <summary>The nearest value of a binary floating-point type, parsed from the invariant scientific form.</summary>
    public TFloat ToFloat<TFloat>()
        where TFloat : IFloatingPointIeee754<TFloat>
    {
        var length = DigitCount(Mantissa) + 16;
        var rented = length > 256
            ? ArrayPool<char>.Shared.Rent(length)
            : null;

        try
        {
            var text = rented is null
                ? stackalloc char[length]
                : rented.AsSpan(0, length);

            var written = 0;
            if (Negative)
            {
                text[written++] = '-';
            }

            Mantissa.TryFormat(text[written..], out var digits, default, CultureInfo.InvariantCulture);
            written += digits;
            text[written++] = 'E';
            Exponent.TryFormat(text[written..], out var exponentDigits, default, CultureInfo.InvariantCulture);
            written += exponentDigits;

            return TFloat.Parse(text[..written], NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>The number of decimal digits of a non-negative integer.</summary>
    public static int DigitCount(
        BigInteger value
    )
    {
        if (value <= ulong.MaxValue)
        {
            var small = (ulong)value;
            var count = 1;
            while (small >= 10)
            {
                small /= 10;
                count++;
            }

            return count;
        }

        // WHY: Log10 is a floating-point estimate; the comparison with the power of ten makes the count exact.
        var estimate = (int)Math.Floor(BigInteger.Log10(value)) + 1;
        if (value >= BigInteger.Pow(10, estimate))
        {
            return estimate + 1;
        }

        return value < BigInteger.Pow(10, estimate - 1)
            ? estimate - 1
            : estimate;
    }

    /// <summary>Removes trailing zeros of the mantissa.</summary>
    public ExactDecimal Normalize()
    {
        if (Mantissa.IsZero)
        {
            return new ExactDecimal(false, BigInteger.Zero, 0);
        }

        var mantissa = Mantissa;
        var exponent = Exponent;
        while (BigInteger.Remainder(mantissa, 10)
            .IsZero)
        {
            mantissa /= 10;
            exponent++;
        }

        return new ExactDecimal(Negative, mantissa, exponent);
    }

    /// <summary>Converts a finite double into its exact decimal expansion.</summary>
    public static ExactDecimal FromDouble(
        double value
    )
    {
        if (value == 0)
        {
            return new ExactDecimal(false, BigInteger.Zero, 0);
        }

        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponentBits = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        BigInteger mantissa;
        int binaryExponent;
        if (exponentBits == 0)
        {
            mantissa = fraction;
            binaryExponent = -1074;
        }
        else
        {
            mantissa = fraction | (1L << 52);
            binaryExponent = exponentBits - 1075;
        }

        if (binaryExponent >= 0)
        {
            return new ExactDecimal(negative, mantissa << binaryExponent, 0).Normalize();
        }

        // m * 2^-k == m * 5^k * 10^-k
        var k = -binaryExponent;

        return new ExactDecimal(negative, mantissa * BigInteger.Pow(5, k), -k).Normalize();
    }

    /// <summary>Rounds to a number of decimal places.</summary>
    public ExactDecimal RoundToDecimals(
        int decimals,
        MidpointRounding mode
    )
    {
        if (-Exponent <= decimals)
        {
            return this;
        }

        var drop = -Exponent - decimals;
        var divisor = BigInteger.Pow(10, drop);
        var quotient = BigInteger.DivRem(Mantissa, divisor, out var remainder);
        quotient = RoundQuotient(quotient, remainder, divisor, mode, Negative);

        return new ExactDecimal(Negative && !quotient.IsZero, quotient, -decimals);
    }

    /// <summary>Rounds to a number of significant digits.</summary>
    public ExactDecimal RoundToSignificantDigits(
        int digits,
        MidpointRounding mode
    )
    {
        if (Mantissa.IsZero)
        {
            return this;
        }

        var length = DigitCount(Mantissa);
        if (length <= digits)
        {
            return this;
        }

        var drop = length - digits;
        var divisor = BigInteger.Pow(10, drop);
        var quotient = BigInteger.DivRem(Mantissa, divisor, out var remainder);
        quotient = RoundQuotient(quotient, remainder, divisor, mode, Negative);

        return new ExactDecimal(Negative && !quotient.IsZero, quotient, Exponent + drop);
    }

    private static BigInteger RoundQuotient(
        BigInteger quotient,
        BigInteger remainder,
        BigInteger divisor,
        MidpointRounding mode,
        bool negative
    )
    {
        if (remainder.IsZero)
        {
            return quotient;
        }

        var twice = remainder * 2;
        var comparison = twice.CompareTo(divisor);
        var up = mode switch
        {
            MidpointRounding.ToEven => comparison > 0 || (comparison == 0 && !quotient.IsEven),
            MidpointRounding.AwayFromZero => comparison >= 0,
            MidpointRounding.ToZero => false,
            MidpointRounding.ToPositiveInfinity => !negative,
            MidpointRounding.ToNegativeInfinity => negative,
            _ => comparison > 0,
        };

        return up
            ? quotient + 1
            : quotient;
    }

    /// <summary>Appends the value without exponent and without grouping, using the decimal separator of the culture.</summary>
    public void WritePlain(
        TextBuffer output,
        CultureInfo culture
    ) => WritePlain(output, culture.NumberFormat, culture.NumberFormat.NumberDecimalSeparator);

    /// <summary>Appends the plain digits with the decimal separator of a number style, such as currency.</summary>
    public void WritePlain(
        TextBuffer output,
        NumberFormatInfo format,
        string decimalSeparator
    )
    {
        if (Negative && !Mantissa.IsZero)
        {
            output.Append(format.NegativeSign);
        }

        var digitCount = DigitCount(Mantissa);
        var rented = digitCount > 128
            ? ArrayPool<char>.Shared.Rent(digitCount)
            : null;

        try
        {
            var digits = rented is null
                ? stackalloc char[digitCount]
                : rented.AsSpan(0, digitCount);

            Mantissa.TryFormat(digits, out _, default, CultureInfo.InvariantCulture);
            WritePlainDigits(output, digits, Exponent, decimalSeparator);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Appends digits scaled by a power of ten; there is always one digit before the separator.</summary>
    public static void WritePlainDigits(
        TextBuffer output,
        ReadOnlySpan<char> digits,
        int exponent,
        string decimalSeparator
    )
    {
        var decimals = Math.Max(-exponent, 0);
        var integerDigits = digits.Length - decimals;
        if (integerDigits <= 0)
        {
            output.Append('0');
            output.Append(decimalSeparator);
            output.Append('0', -integerDigits);
            output.Append(digits);

            return;
        }

        output.Append(digits[..integerDigits]);
        if (exponent > 0)
        {
            output.Append('0', exponent);
        }

        if (decimals > 0)
        {
            output.Append(decimalSeparator);
            output.Append(digits[integerDigits..]);
        }
    }
}
