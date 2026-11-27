using System.Numerics;

namespace Doka.Tabular;

/// <summary>
/// A parsed number: sign, mantissa, and decimal exponent. Mantissas of up to 19 digits stay in
/// <see cref="Small" />, so the common cases need no <see cref="BigInteger" />.
/// </summary>
internal readonly record struct ParsedNumberText(
    bool Negative,
    bool IsSmall,
    ulong Small,
    BigInteger Large,
    int Exponent,
    bool HadDecimalPoint,
    bool HadExponent
)
{
    public ExactDecimal Value => new(
        Negative,
        IsSmall
            ? Small
            : Large,
        Exponent);
}
