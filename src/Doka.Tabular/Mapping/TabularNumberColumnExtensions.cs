using System.Numerics;

namespace Doka.Tabular;

/// <summary>Settings for numeric fields.</summary>
[PublicAPI]
public static class TabularNumberColumnExtensions
{
    /// <param name="builder">The field builder.</param>
    extension<TNumber, TBuilder>(
        TabularValueBuilder<TNumber, TBuilder> builder
    )
        where TNumber : struct, INumberBase<TNumber>
        where TBuilder : TabularValueBuilder<TNumber, TBuilder>
    {
        /// <summary>Accepts a decimal point for integers when the exact value is whole, for example <c>42.0</c>.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowDecimalPoint()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowDecimalPoint = true;

            return builder.This;
        }

        /// <summary>Accepts an exponent for <see cref="decimal" /> and integers; floating-point types accept it by default.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowExponent()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowExponent = true;

            return builder.This;
        }

        /// <summary>Sets percent or currency text style.</summary>
        /// <param name="style">The text style.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TextStyle(
            NumberTextStyle style
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.TextStyle = style;

            return builder.This;
        }

        /// <summary>Sets the .NET format string for text output; a format that changes the value requires rounding.</summary>
        /// <param name="format">The .NET numeric format string.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ExportFormat(
            string format
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(format);
            builder.Settings.ExportFormat = format;

            return builder.This;
        }

        /// <summary>Rounds to a number of decimal places in both directions.</summary>
        /// <param name="decimals">Number of decimal places.</param>
        /// <param name="mode">Midpoint rule; the default is <see cref="MidpointRounding.ToEven" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder Rounding(
            int decimals,
            MidpointRounding mode = MidpointRounding.ToEven
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentOutOfRangeException.ThrowIfNegative(decimals);
            builder.Settings.NumericRounding = new NumericRounding(decimals, null, mode);

            return builder.This;
        }

        /// <summary>Rounds to a number of significant digits in both directions.</summary>
        /// <param name="digits">Number of significant digits.</param>
        /// <param name="mode">Midpoint rule; the default is <see cref="MidpointRounding.ToEven" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder SignificantDigitsRounding(
            int digits,
            MidpointRounding mode = MidpointRounding.ToEven
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);
            builder.Settings.NumericRounding = new NumericRounding(null, digits, mode);

            return builder.This;
        }

        /// <summary>Releases the texts <c>NaN</c>, <c>Infinity</c>, and <c>-Infinity</c>; in Excel the column becomes text.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder NonFiniteValues()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowNonFiniteValues = true;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TNumber, TBuilder>(
        TabularValueBuilder<TNumber?, TBuilder> builder
    )
        where TNumber : struct, INumberBase<TNumber>
        where TBuilder : TabularValueBuilder<TNumber?, TBuilder>
    {
        /// <summary>Accepts a decimal point for integers when the exact value is whole, for example <c>42.0</c>.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowDecimalPoint()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowDecimalPoint = true;

            return builder.This;
        }

        /// <summary>Accepts an exponent for <see cref="decimal" /> and integers; floating-point types accept it by default.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowExponent()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowExponent = true;

            return builder.This;
        }

        /// <summary>Sets percent or currency text style.</summary>
        /// <param name="style">The text style.</param>
        /// <returns>The field builder.</returns>
        public TBuilder TextStyle(
            NumberTextStyle style
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.TextStyle = style;

            return builder.This;
        }

        /// <summary>Sets the .NET format string for text output; a format that changes the value requires rounding.</summary>
        /// <param name="format">The .NET numeric format string.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ExportFormat(
            string format
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(format);
            builder.Settings.ExportFormat = format;

            return builder.This;
        }

        /// <summary>Rounds to a number of decimal places in both directions.</summary>
        /// <param name="decimals">Number of decimal places.</param>
        /// <param name="mode">Midpoint rule; the default is <see cref="MidpointRounding.ToEven" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder Rounding(
            int decimals,
            MidpointRounding mode = MidpointRounding.ToEven
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentOutOfRangeException.ThrowIfNegative(decimals);
            builder.Settings.NumericRounding = new NumericRounding(decimals, null, mode);

            return builder.This;
        }

        /// <summary>Rounds to a number of significant digits in both directions.</summary>
        /// <param name="digits">Number of significant digits.</param>
        /// <param name="mode">Midpoint rule; the default is <see cref="MidpointRounding.ToEven" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder SignificantDigitsRounding(
            int digits,
            MidpointRounding mode = MidpointRounding.ToEven
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(digits);
            builder.Settings.NumericRounding = new NumericRounding(null, digits, mode);

            return builder.This;
        }

        /// <summary>Releases the texts <c>NaN</c>, <c>Infinity</c>, and <c>-Infinity</c>; in Excel the column becomes text.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder NonFiniteValues()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowNonFiniteValues = true;

            return builder.This;
        }
    }
}
