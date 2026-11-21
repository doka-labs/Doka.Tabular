namespace Doka.Tabular.Excel;

/// <summary>Per-call settings of an Excel import. Unset values come from the format instance, then from the defaults.</summary>
[PublicAPI]
public sealed record ExcelImportOptions
{
    /// <summary>Gets the error strategy; the default is <see cref="ImportStrategy.Strict" />.</summary>
    public ImportStrategy? Strategy { get; init; }

    /// <summary>Gets the column binding; the default is <see cref="ColumnBinding.ByIndex" />.</summary>
    public ColumnBinding? Binding { get; init; }

    /// <summary>Gets whether all text fields are trimmed; a field can override it with <c>Trim(bool)</c>.</summary>
    public bool? TrimFields { get; init; }

    /// <summary>Gets the culture of numbers and dates as text; the default is the invariant culture.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets the texts of the translation keys of the map. Each call takes them at its start; later changes of the
    /// dictionary apply to the next call.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Translations { get; init; }

    /// <summary>Gets the behavior for missing translation keys of headers.</summary>
    public MissingTranslationBehavior? MissingTranslation { get; init; }

    /// <summary>Gets a normalization of header texts for header-name binding; it replaces trimming on both sides.</summary>
    public Func<string, string>? NormalizeHeader { get; init; }

    /// <summary>Gets the limit of error details; <see langword="null" /> means no limit.</summary>
    public int? MaxErrorDetails { get; init; }

    /// <summary>Gets the source key for duplicate detection without a write target.</summary>
    public TabularSourceKey? SourceKey { get; init; }

    /// <summary>Gets the sheet to read; without it the only sheet is chosen.</summary>
    public TabularSheetName? SheetName { get; init; }

    /// <summary>Gets the first worksheet row of the table, starting at 1; for header binding the header row.</summary>
    public int? StartRow { get; init; }

    /// <summary>Gets the last worksheet row read, inclusive.</summary>
    public int? EndRow { get; init; }

    /// <summary>Gets whether only rows visible in the saved workbook are read.</summary>
    public bool? VisibleRowsOnly { get; init; }

    /// <summary>Gets the expected Excel variant; <see langword="null" /> accepts every supported variant.</summary>
    public ExcelVariant? ExpectedVariant { get; init; }
}

/// <summary>Per-call settings of an Excel export. Unset values come from the format instance, then from the defaults.</summary>
[PublicAPI]
public sealed record ExcelExportOptions
{
    /// <summary>Gets whether a header row is written; the default is <see langword="false" />.</summary>
    public bool? WriteHeader { get; init; }

    /// <summary>Gets the culture of numbers and dates as text; the default is the invariant culture.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets the texts of the translation keys of the map. Each call takes them at its start; later changes of the
    /// dictionary apply to the next call.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Translations { get; init; }

    /// <summary>Gets the behavior for missing translation keys.</summary>
    public MissingTranslationBehavior? MissingTranslation { get; init; }

    /// <summary>
    /// Gets the selected columns and their order; <see langword="null" /> writes the full positional layout, and an
    /// empty list writes an empty table without reading the source.
    /// </summary>
    public IReadOnlyList<ColumnKey>? ColumnKeys { get; init; }

    /// <summary>Gets the limit of error details.</summary>
    public int? MaxErrorDetails { get; init; }

    /// <summary>Gets the sheet name; the default is <c>Export</c>.</summary>
    public TabularSheetName? SheetName { get; init; }

    /// <summary>Gets the layout.</summary>
    public ExcelLayoutOptions? Layout { get; init; }
}

/// <summary>Call-wide settings of a multi-sheet import; sheet options override them per sheet.</summary>
[PublicAPI]
public sealed record ExcelWorkbookImportOptions
{
    /// <summary>Gets the error strategy; the default is <see cref="ImportStrategy.Strict" />.</summary>
    public ImportStrategy? Strategy { get; init; }

    /// <summary>Gets the column binding; the default is <see cref="ColumnBinding.ByIndex" />.</summary>
    public ColumnBinding? Binding { get; init; }

    /// <summary>Gets whether all text fields are trimmed; a field can override it with <c>Trim(bool)</c>.</summary>
    public bool? TrimFields { get; init; }

    /// <summary>Gets the culture of numbers and dates as text; the default is the invariant culture.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets the texts of the translation keys of the map. Each call takes them at its start; later changes of the
    /// dictionary apply to the next call.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Translations { get; init; }

    /// <summary>Gets the behavior for missing translation keys of headers.</summary>
    public MissingTranslationBehavior? MissingTranslation { get; init; }

    /// <summary>Gets a normalization of header texts for header-name binding; it replaces trimming on both sides.</summary>
    public Func<string, string>? NormalizeHeader { get; init; }

    /// <summary>Gets the limit of error details; <see langword="null" /> means no limit.</summary>
    public int? MaxErrorDetails { get; init; }

    /// <summary>Gets the expected Excel variant.</summary>
    public ExcelVariant? ExpectedVariant { get; init; }
}

/// <summary>Settings of one sheet of a multi-sheet import.</summary>
[PublicAPI]
public sealed record ExcelSheetImportOptions
{
    /// <summary>Gets the column binding; the default is <see cref="ColumnBinding.ByIndex" />.</summary>
    public ColumnBinding? Binding { get; init; }

    /// <summary>Gets whether all text fields are trimmed; a field can override it with <c>Trim(bool)</c>.</summary>
    public bool? TrimFields { get; init; }

    /// <summary>Gets the culture of numbers and dates as text; the default is the invariant culture.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets the texts of the translation keys of the map. Each call takes them at its start; later changes of the
    /// dictionary apply to the next call.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Translations { get; init; }

    /// <summary>Gets the source key for duplicate detection without a write target.</summary>
    public TabularSourceKey? SourceKey { get; init; }

    /// <summary>Gets the first worksheet row of the table, starting at 1; for header binding the header row.</summary>
    public int? StartRow { get; init; }

    /// <summary>Gets the last worksheet row read, inclusive.</summary>
    public int? EndRow { get; init; }

    /// <summary>Gets whether only rows visible in the saved workbook are read.</summary>
    public bool? VisibleRowsOnly { get; init; }
}

/// <summary>Call-wide settings of a multi-sheet export.</summary>
[PublicAPI]
public sealed record ExcelWorkbookExportOptions
{
    /// <summary>Gets whether a header row is written; the default is <see langword="false" />.</summary>
    public bool? WriteHeader { get; init; }

    /// <summary>Gets the culture of numbers and dates as text; the default is the invariant culture.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets the texts of the translation keys of the map. Each call takes them at its start; later changes of the
    /// dictionary apply to the next call.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Translations { get; init; }

    /// <summary>Gets the behavior for missing translation keys.</summary>
    public MissingTranslationBehavior? MissingTranslation { get; init; }

    /// <summary>Gets the limit of error details.</summary>
    public int? MaxErrorDetails { get; init; }

    /// <summary>Gets the layout of every sheet without own layout.</summary>
    public ExcelLayoutOptions? Layout { get; init; }
}

/// <summary>Settings of one sheet of a multi-sheet export.</summary>
[PublicAPI]
public sealed record ExcelSheetExportOptions
{
    /// <summary>Gets whether a header row is written; the default is <see langword="false" />.</summary>
    public bool? WriteHeader { get; init; }

    /// <summary>Gets the culture of numbers and dates as text; the default is the invariant culture.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets the texts of the translation keys of the map. Each call takes them at its start; later changes of the
    /// dictionary apply to the next call.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Translations { get; init; }

    /// <summary>
    /// Gets the selected columns and their order; <see langword="null" /> writes the full positional layout, and an
    /// empty list writes an empty table without reading the source.
    /// </summary>
    public IReadOnlyList<ColumnKey>? ColumnKeys { get; init; }

    /// <summary>Gets the layout of this sheet; it replaces the call layout completely.</summary>
    public ExcelLayoutOptions? Layout { get; init; }
}

/// <summary>Settings of an <see cref="ExcelFormat" /> instance: infrastructure, limits, and call defaults.</summary>
[PublicAPI]
public sealed record ExcelFormatOptions
{
    /// <summary>Gets the logging settings.</summary>
    public TabularDiagnosticsOptions? Diagnostics { get; init; } = new();

    /// <summary>Gets the time provider; the default is <see cref="TimeProvider.System" />.</summary>
    public TimeProvider? TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets the in-memory size of the import buffer before spilling; the default is 16 MiB.</summary>
    public long ImportBufferBytes { get; init; } = ResourceLimits.DefaultBufferBytes;

    /// <summary>Gets the in-memory size of the output buffer before spilling; the default is 16 MiB.</summary>
    public long OutputBufferBytes { get; init; } = ResourceLimits.DefaultBufferBytes;

    /// <summary>Gets whether buffers may spill to temporary files; the default is <see langword="true" />.</summary>
    public bool AllowSpill { get; init; } = true;

    /// <summary>Gets the directory of temporary files; the default is the operating system's temporary directory.</summary>
    public string? TemporaryDirectory { get; init; }

    /// <summary>Gets the limit of simultaneously existing temporary bytes of a call; <see langword="null" /> means no limit.</summary>
    public long? MaxTemporaryBytes { get; init; }

    /// <summary>Gets the limit of bytes read from the input; <see langword="null" /> means no limit.</summary>
    public long? MaxInputBytes { get; init; }

    /// <summary>Gets the limit of UTF-16 code units of one cell text; <see langword="null" /> means no limit.</summary>
    public long? MaxFieldCharacters { get; init; }

    /// <summary>Gets the limit of actually expanded bytes of an import; the default is 1 GiB.</summary>
    public long? MaxExpandedBytes { get; init; } = ResourceLimits.DefaultMaxExpandedBytes;

    /// <summary>Gets the defaults of import calls.</summary>
    public ExcelImportOptions? Import { get; init; }

    /// <summary>Gets the defaults of export calls.</summary>
    public ExcelExportOptions? Export { get; init; }
}
