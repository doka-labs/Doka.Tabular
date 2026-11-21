namespace Doka.Tabular;

/// <summary>How numbers are written and read as text.</summary>
[PublicAPI]
public enum NumberTextStyle
{
    /// <summary>Plain decimal text.</summary>
    Plain,

    /// <summary>The culture's percent sign is required; the value is divided by 100 exactly once.</summary>
    Percent,

    /// <summary>The culture's currency symbol is required; no currency conversion takes place.</summary>
    Currency,
}

/// <summary>Resolution of a local time that occurs twice when clocks are turned back.</summary>
[PublicAPI]
public enum AmbiguousTimeResolution
{
    /// <summary>Use the earlier occurrence.</summary>
    Earlier,

    /// <summary>Use the later occurrence.</summary>
    Later,
}

/// <summary>Unit of a duration stored as an Excel number.</summary>
[PublicAPI]
public enum ExcelDurationUnit
{
    /// <summary>Days; displayed as <c>[h]:mm:ss</c>.</summary>
    Days,

    /// <summary>Hours.</summary>
    Hours,

    /// <summary>Minutes.</summary>
    Minutes,

    /// <summary>Seconds.</summary>
    Seconds,

    /// <summary>Milliseconds.</summary>
    Milliseconds,

    /// <summary>Ticks of 100 nanoseconds.</summary>
    Ticks,
}
