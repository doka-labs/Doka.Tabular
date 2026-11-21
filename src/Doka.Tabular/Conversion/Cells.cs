namespace Doka.Tabular;

internal enum CellKind
{
    /// <summary>The position does not exist in the record (CSV) or the cell element is absent (Excel).</summary>
    Missing,

    /// <summary>An Excel cell without value and without formula.</summary>
    Empty,

    Text,

    Number,

    Boolean,

    /// <summary>An Excel error value such as <c>#DIV/0!</c>.</summary>
    Error,

    /// <summary>An Excel formula without a stored result.</summary>
    FormulaWithoutResult,
}

internal enum NumberFormatKind
{
    General,
    Date,
    Time,
    DateTime,
    Other,
}

/// <summary>
/// One source cell as delivered by a format reader. <see cref="Text" /> may be a view into the reader's record
/// buffer; it is valid until the reader reads the next record and is copied when it must outlive the record.
/// </summary>
internal readonly record struct SourceCell(
    CellKind Kind,
    ReadOnlyMemory<char> Text,
    double Number,
    bool Boolean,
    bool IsFormula,
    NumberFormatKind NumberFormat
)
{
    public static SourceCell Missing { get; } =
        new(CellKind.Missing, default, 0, false, false, NumberFormatKind.General);

    public static SourceCell Empty { get; } = new(CellKind.Empty, default, 0, false, false, NumberFormatKind.General);

    /// <summary>Gets a formula whose stored result is empty, such as <c>&lt;f&gt;IF(...)&lt;/f&gt;&lt;v/&gt;</c>.</summary>
    public static SourceCell EmptyFormula { get; } =
        new(CellKind.Empty, default, 0, false, true, NumberFormatKind.General);

    public static SourceCell FormulaWithoutResult { get; } =
        new(CellKind.FormulaWithoutResult, default, 0, false, true, NumberFormatKind.General);

    /// <summary>
    /// Gets whether a native date cell stated a time zone; its number is then the UTC time of the stated instant.
    /// </summary>
    public bool IsUtc { get; init; }

    /// <summary>
    /// Gets whether the cell has no value; a formula with an empty result is blank as a value but still makes its row a
    /// data row.
    /// </summary>
    public bool IsBlank => Kind is CellKind.Missing or CellKind.Empty;

    /// <summary>Gets whether the cell makes its row a data row: a value or a formula, also one with an empty result.</summary>
    public bool HasContent => !IsBlank || IsFormula;

    public static SourceCell FromText(
        ReadOnlyMemory<char> text,
        bool isFormula = false
    ) => new(CellKind.Text, text, 0, false, isFormula, NumberFormatKind.General);

    public static SourceCell FromNumber(
        double number,
        NumberFormatKind format,
        bool isFormula = false
    ) => new(CellKind.Number, default, number, false, isFormula, format);

    public static SourceCell FromBoolean(
        bool value,
        bool isFormula = false
    ) => new(CellKind.Boolean, default, 0, value, isFormula, NumberFormatKind.General);

    public static SourceCell FromError(
        string errorText,
        bool isFormula
    ) => new(CellKind.Error, errorText.AsMemory(), 0, false, isFormula, NumberFormatKind.General);
}

internal enum OutputKind
{
    Empty,
    Text,
    Number,
    Boolean,
}

/// <summary>
/// One formatted output value. CSV writers use <see cref="Text" />; Excel writers use the native kind. The text
/// may be a view into the row buffer of the export and is valid until the next row is formatted.
/// </summary>
internal readonly record struct OutputCell(
    OutputKind Kind,
    ReadOnlyMemory<char> Text,
    double Number,
    bool Boolean,
    string? NumberFormat
)
{
    public static OutputCell Empty { get; } = new(OutputKind.Empty, default, 0, false, null);

    public static OutputCell FromText(
        ReadOnlyMemory<char> text
    ) => new(OutputKind.Text, text, 0, false, null);

    public static OutputCell FromNumber(
        double number,
        string? numberFormat
    ) => new(OutputKind.Number, default, number, false, numberFormat);

    public static OutputCell FromBoolean(
        bool value
    ) => new(OutputKind.Boolean,
        value
            ? "true".AsMemory()
            : "false".AsMemory(),
        0,
        value,
        null);
}
