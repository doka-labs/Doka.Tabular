namespace Doka.Tabular;

public static partial class TabularTimeColumnExtensions
{
    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<DateTimeOffset, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<DateTimeOffset, TBuilder>
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

        /// <summary>Sets the named time zone of local times in the file, used in both directions.</summary>
        /// <param name="timeZone">The time zone of the file.</param>
        /// <returns>The field builder.</returns>
        public TBuilder FileTimeZone(
            TimeZoneInfo timeZone
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(timeZone);
            builder.Settings.FileTimeZone = timeZone;

            return builder.This;
        }

        /// <summary>Resolves a local time that occurs twice in the file time zone.</summary>
        /// <param name="resolution">Earlier or later occurrence.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ResolveAmbiguousTime(
            AmbiguousTimeResolution resolution
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AmbiguousTimeResolution = resolution;

            return builder.This;
        }

        /// <summary>Shifts a local time in a daylight-saving gap by the length of the gap.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder ShiftInvalidTime()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ShiftInvalidTime = true;

            return builder.This;
        }
    }

    /// <param name="builder">The field builder.</param>
    extension<TBuilder>(
        TabularValueBuilder<DateTimeOffset?, TBuilder> builder
    )
        where TBuilder : TabularValueBuilder<DateTimeOffset?, TBuilder>
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

        /// <summary>Sets the named time zone of local times in the file, used in both directions.</summary>
        /// <param name="timeZone">The time zone of the file.</param>
        /// <returns>The field builder.</returns>
        public TBuilder FileTimeZone(
            TimeZoneInfo timeZone
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(timeZone);
            builder.Settings.FileTimeZone = timeZone;

            return builder.This;
        }

        /// <summary>Resolves a local time that occurs twice in the file time zone.</summary>
        /// <param name="resolution">Earlier or later occurrence.</param>
        /// <returns>The field builder.</returns>
        public TBuilder ResolveAmbiguousTime(
            AmbiguousTimeResolution resolution
        )
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.AmbiguousTimeResolution = resolution;

            return builder.This;
        }

        /// <summary>Shifts a local time in a daylight-saving gap by the length of the gap.</summary>
        /// <returns>The field builder.</returns>
        public TBuilder ShiftInvalidTime()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Settings.ShiftInvalidTime = true;

            return builder.This;
        }
    }
}
