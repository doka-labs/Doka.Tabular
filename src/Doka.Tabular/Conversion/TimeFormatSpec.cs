namespace Doka.Tabular;

/// <summary>The parts a .NET date and time format string contains, outside quoted and escaped literals.</summary>
internal readonly record struct TimeFormatSpec(
    bool Year,
    bool ShortYear,
    bool Month,
    bool Day,
    bool Hour,
    bool TwelveHour,
    bool Designator,
    bool Offset,
    bool UnsupportedStandard
)
{
    /// <summary>Gets whether year, month, and day are all present.</summary>
    public bool HasDate => Year && Month && Day;

    /// <summary>Gets whether a 12-hour clock lacks its AM/PM designator, which would guess the half of the day.</summary>
    public bool LacksDesignator => TwelveHour && !Designator;

    public static TimeFormatSpec Of(
        string format
    ) => format.Length == 1
        ? Standard(format[0])
        : Custom(format);

    /// <summary>The parts of a custom pattern, such as the actual pattern of a culture behind a standard format.</summary>
    public static TimeFormatSpec OfPattern(
        string pattern
    ) => Custom(pattern);

    /// <summary>The parts of a standard format; formats without a complete date or time are not supported.</summary>
    private static TimeFormatSpec Standard(
        char kind
    ) => kind switch
    {
        'd' or 'D' => new TimeFormatSpec(true,
            false,
            true,
            true,
            false,
            false,
            false,
            false,
            false),
        'f' or 'F' or 'g' or 'G' or 's' => new TimeFormatSpec(true,
            false,
            true,
            true,
            true,
            false,
            false,
            false,
            false),
        'o' or 'O' => new TimeFormatSpec(true,
            false,
            true,
            true,
            true,
            false,
            false,
            true,
            false),
        't' or 'T' => new TimeFormatSpec(false,
            false,
            false,
            false,
            true,
            false,
            false,
            false,
            false),
        _ => new TimeFormatSpec(false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            true),
    };

    private static TimeFormatSpec Custom(
        string format
    )
    {
        var spec = new TimeFormatSpec();
        var index = 0;
        while (index < format.Length)
        {
            var character = format[index];
            switch (character)
            {
                case '\'' or '"':
                    index = AfterLiteral(format, index);

                    continue;
                case '\\':
                    index += 2;

                    continue;
            }

            var run = 1;
            while (index + run < format.Length
                && format[index + run] == character)
            {
                run++;
            }

            spec = With(spec, character, run);
            index += run;
        }

        return spec;
    }

    /// <summary>
    /// The index after a quoted literal that starts at <paramref name="start" />; as in .NET, a backslash inside the
    /// literal escapes the next character, also the quote.
    /// </summary>
    private static int AfterLiteral(
        string format,
        int start
    )
    {
        var quote = format[start];
        var index = start + 1;
        while (index < format.Length)
        {
            if (format[index] == '\\')
            {
                index += 2;

                continue;
            }

            if (format[index] == quote)
            {
                return index + 1;
            }

            index++;
        }

        return format.Length;
    }

    private static TimeFormatSpec With(
        TimeFormatSpec spec,
        char character,
        int run
    ) => character switch
    {
        'y' when run >= 4 => spec with { Year = true },
        'y' => spec with { ShortYear = true },
        'M' => spec with { Month = true },

        // WHY: ddd and dddd are weekday names, which do not identify a day of the month.
        'd' when run <= 2 => spec with { Day = true },
        'H' => spec with { Hour = true },
        'h' => spec with
        {
            Hour = true,
            TwelveHour = true,
        },
        't' => spec with { Designator = true },
        'z' or 'K' => spec with { Offset = true },
        _ => spec,
    };
}
