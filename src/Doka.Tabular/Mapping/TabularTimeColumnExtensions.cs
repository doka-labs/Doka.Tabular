namespace Doka.Tabular;

/// <summary>Settings for date, time, and duration fields.</summary>

// WHY: CA1708 reads the extension blocks for T and T? as members differing only by case
// (https://github.com/dotnet/sdk/issues/51716); the fix (https://github.com/dotnet/sdk/pull/51838) is not in
// SDK 10.0.404. Remove the suppression once the pinned SDK contains it.
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "Analyzer defect for extension blocks.")]
[PublicAPI]
public static partial class TabularTimeColumnExtensions
{
    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<DateOnly, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<DateOnly, TBuilder>
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

        /// <summary>Accepts Excel serial numbers without a date or time format.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowExcelSerialNumbers()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowExcelSerialNumbers = true;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<DateOnly?, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<DateOnly?, TBuilder>
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

        /// <summary>Accepts Excel serial numbers without a date or time format.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowExcelSerialNumbers()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowExcelSerialNumbers = true;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<TimeOnly, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<TimeOnly, TBuilder>
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

        /// <summary>Accepts Excel serial numbers without a date or time format.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowExcelSerialNumbers()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowExcelSerialNumbers = true;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<TimeOnly?, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<TimeOnly?, TBuilder>
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

        /// <summary>Accepts Excel serial numbers without a date or time format.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder AllowExcelSerialNumbers()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AllowExcelSerialNumbers = true;

            return builder.This;
        }
    }
}
