using System.Text;

namespace Doka.Tabular.Csv;

/// <summary>Per-call settings of a CSV import. Unset values come from the format instance, then from the defaults.</summary>
[PublicAPI]
public sealed record CsvImportOptions
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

    /// <summary>Gets the field delimiter; the default is the comma.</summary>
    public char? Delimiter { get; init; }

    /// <summary>Gets the encoding; the default is UTF-8 without replacement of invalid bytes.</summary>
    public Encoding? Encoding { get; init; }

    /// <summary>Gets whether a line break inside a quoted field is field content; the default is <see langword="true" />.</summary>
    public bool? AllowLineBreaksInFields { get; init; }

    /// <summary>Gets whether only CRLF separates records; a single LF is then a syntax error.</summary>
    public bool? RequireCrLf { get; init; }

    /// <summary>Gets whether empty physical lines are records with one empty field instead of being skipped.</summary>
    public bool? EmptyLinesAsRecords { get; init; }
}

/// <summary>Per-call settings of a CSV export. Unset values come from the format instance, then from the defaults.</summary>
[PublicAPI]
public sealed record CsvExportOptions
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

    /// <summary>Gets the field delimiter; the default is the comma.</summary>
    public char? Delimiter { get; init; }

    /// <summary>Gets the encoding; the default is UTF-8.</summary>
    public Encoding? Encoding { get; init; }

    /// <summary>Gets whether a UTF-8 byte order mark is written; the default is <see langword="false" />.</summary>
    public bool? WriteBom { get; init; }
}

/// <summary>Settings of a <see cref="CsvFormat" /> instance: infrastructure, limits, and call defaults.</summary>
[PublicAPI]
public sealed record CsvFormatOptions
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

    /// <summary>Gets the limit of UTF-16 code units of one field before trimming; <see langword="null" /> means no limit.</summary>
    public long? MaxFieldCharacters { get; init; }

    /// <summary>Gets the defaults of import calls.</summary>
    public CsvImportOptions? Import { get; init; }

    /// <summary>Gets the defaults of export calls.</summary>
    public CsvExportOptions? Export { get; init; }
}
