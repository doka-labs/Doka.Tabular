namespace Doka.Tabular;

public static partial class TabularTimeColumnExtensions
{
    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<TimeSpan, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<TimeSpan, TBuilder>
    {
        /// <summary>Sets the one exact text format; ISO and culture input no longer apply.</summary>
        /// <param name="format">The .NET custom format string.</param>
        /// <returns>The field builder.</returns>
        public TBuilder Format(
            string format
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(format);
            builder.Settings.TimeFormat = format;

            return builder.This;
        }

        /// <summary>Rounds to a positive step of whole ticks in both directions.</summary>
        /// <param name="step">The rounding step.</param>
        /// <param name="mode">Midpoint rule; the default is <see cref="MidpointRounding.ToEven" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder Rounding(
            TimeSpan step,
            MidpointRounding mode = MidpointRounding.ToEven
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
            builder.Settings.TimeRounding = new TimeRounding(step, mode);

            return builder.This;
        }

        /// <summary>Sets the unit of the native Excel number; the default is days.</summary>
        /// <param name="unit">The unit.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ExcelDuration(
            ExcelDurationUnit unit
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ExcelDurationUnit = unit;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<TimeSpan?, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<TimeSpan?, TBuilder>
    {
        /// <summary>Sets the one exact text format; ISO and culture input no longer apply.</summary>
        /// <param name="format">The .NET custom format string.</param>
        /// <returns>The field builder.</returns>
        public TBuilder Format(
            string format
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrEmpty(format);
            builder.Settings.TimeFormat = format;

            return builder.This;
        }

        /// <summary>Rounds to a positive step of whole ticks in both directions.</summary>
        /// <param name="step">The rounding step.</param>
        /// <param name="mode">Midpoint rule; the default is <see cref="MidpointRounding.ToEven" />.</param>
        /// <returns>The field builder.</returns>
        public TBuilder Rounding(
            TimeSpan step,
            MidpointRounding mode = MidpointRounding.ToEven
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
            builder.Settings.TimeRounding = new TimeRounding(step, mode);

            return builder.This;
        }

        /// <summary>Sets the unit of the native Excel number; the default is days.</summary>
        /// <param name="unit">The unit.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ExcelDuration(
            ExcelDurationUnit unit
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ExcelDurationUnit = unit;

            return builder.This;
        }
    }
}
