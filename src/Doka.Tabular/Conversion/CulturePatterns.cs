namespace Doka.Tabular;

/// <summary>
/// The composed culture patterns of a date-time format, rebuilt when one of its source patterns changes.
/// </summary>
/// <remarks>
/// WHY: The composed patterns would otherwise be concatenated for every cell that is not ISO text; a writable
/// culture can change its patterns, so the cached entry is checked against the current source patterns.
/// </remarks>
internal sealed class CulturePatterns
{
    private static readonly ConditionalWeakTable<DateTimeFormatInfo, CulturePatterns> s_cache = [];

    private readonly TimeFormatSpec _dateLongTime;
    private readonly TimeFormatSpec _dateShortTime;
    private readonly TimeFormatSpec _fullDateTime;
    private readonly TimeFormatSpec _longDate;
    private readonly TimeFormatSpec _longDateShortTime;
    private readonly TimeFormatSpec _longTime;
    private readonly TimeFormatSpec _shortDate;
    private readonly TimeFormatSpec _shortTime;

    private CulturePatterns(
        DateTimeFormatInfo format
    )
    {
        ShortDate = format.ShortDatePattern;
        LongDate = format.LongDatePattern;
        FullDateTime = format.FullDateTimePattern;
        LongTime = format.LongTimePattern;
        ShortTime = format.ShortTimePattern;
        DateLongTime = ShortDate + " " + LongTime;
        DateShortTime = ShortDate + " " + ShortTime;
        _shortDate = TimeFormatSpec.OfPattern(ShortDate);
        _longDate = TimeFormatSpec.OfPattern(LongDate);
        _longDateShortTime = TimeFormatSpec.OfPattern(LongDate + " " + ShortTime);
        _fullDateTime = TimeFormatSpec.OfPattern(FullDateTime);
        _dateShortTime = TimeFormatSpec.OfPattern(DateShortTime);
        _dateLongTime = TimeFormatSpec.OfPattern(DateLongTime);
        _shortTime = TimeFormatSpec.OfPattern(ShortTime);
        _longTime = TimeFormatSpec.OfPattern(LongTime);
    }

    private string LongDate { get; }

    private string FullDateTime { get; }

    /// <summary>
    /// Gets whether the short date pattern names year, month, and day; a culture pattern without them would let
    /// parsing take the missing parts from the clock.
    /// </summary>
    public bool DateIsComplete => _shortDate.HasDate;

    private string ShortDate { get; }

    private string LongTime { get; }

    private string ShortTime { get; }

    public string DateLongTime { get; }

    public string DateShortTime { get; }

    public string Date => ShortDate;

    /// <summary>The parts of a standard format as this culture spells it out.</summary>
    public TimeFormatSpec SpecOf(
        char standard
    ) => standard switch
    {
        'd' => _shortDate,
        'D' => _longDate,
        'f' => _longDateShortTime,
        'F' => _fullDateTime,
        'g' => _dateShortTime,
        'G' => _dateLongTime,
        't' => _shortTime,
        'T' => _longTime,
        _ => TimeFormatSpec.Of(standard.ToString()),
    };

    public static CulturePatterns For(
        DateTimeFormatInfo format
    )
    {
        if (s_cache.TryGetValue(format, out var patterns)
            && ReferenceEquals(patterns.ShortDate, format.ShortDatePattern)
            && ReferenceEquals(patterns.LongDate, format.LongDatePattern)
            && ReferenceEquals(patterns.FullDateTime, format.FullDateTimePattern)
            && ReferenceEquals(patterns.LongTime, format.LongTimePattern)
            && ReferenceEquals(patterns.ShortTime, format.ShortTimePattern))
        {
            return patterns;
        }

        patterns = new CulturePatterns(format);
        s_cache.AddOrUpdate(format, patterns);

        return patterns;
    }
}
