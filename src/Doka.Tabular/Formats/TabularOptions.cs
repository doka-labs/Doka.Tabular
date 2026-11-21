using Doka.Tabular.Csv;
using Doka.Tabular.Excel;

namespace Doka.Tabular;

/// <summary>
/// Per-call settings at the common import entry point: shared settings plus one inner type per format. A value set
/// in the inner type of the chosen format wins, then a value set here, then the prepared format instance.
/// </summary>
[PublicAPI]
public sealed record TabularImportOptions
{
    /// <summary>Gets the error strategy.</summary>
    public ImportStrategy? Strategy { get; init; }

    /// <summary>Gets the column binding.</summary>
    public ColumnBinding? Binding { get; init; }

    /// <summary>Gets whether all text fields are trimmed.</summary>
    public bool? TrimFields { get; init; }

    /// <summary>Gets the culture of numbers and dates as text.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets the texts of the translation keys of the map. Each call takes them at its start; later changes of the
    /// dictionary apply to the next call.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Translations { get; init; }

    /// <summary>Gets the behavior for missing translation keys of headers.</summary>
    public MissingTranslationBehavior? MissingTranslation { get; init; }

    /// <summary>Gets a normalization of header texts for header-name binding.</summary>
    public Func<string, string>? NormalizeHeader { get; init; }

    /// <summary>Gets the limit of error details.</summary>
    public int? MaxErrorDetails { get; init; }

    /// <summary>Gets the source key for duplicate detection without a write target.</summary>
    public TabularSourceKey? SourceKey { get; init; }

    /// <summary>Gets settings used only when the call imports CSV.</summary>
    public CsvImportOptions? Csv { get; init; }

    /// <summary>Gets settings used only when the call imports Excel.</summary>
    public ExcelImportOptions? Excel { get; init; }

    internal CsvImportOptions ForCsv()
    {
        var inner = Csv ?? new CsvImportOptions();

        return inner with
        {
            Strategy = inner.Strategy ?? Strategy,
            Binding = inner.Binding ?? Binding,
            TrimFields = inner.TrimFields ?? TrimFields,
            Culture = inner.Culture ?? Culture,
            Translations = inner.Translations ?? Translations,
            MissingTranslation = inner.MissingTranslation ?? MissingTranslation,
            NormalizeHeader = inner.NormalizeHeader ?? NormalizeHeader,
            MaxErrorDetails = inner.MaxErrorDetails ?? MaxErrorDetails,
            SourceKey = inner.SourceKey ?? SourceKey,
        };
    }

    internal ExcelImportOptions ForExcel()
    {
        var inner = Excel ?? new ExcelImportOptions();

        return inner with
        {
            Strategy = inner.Strategy ?? Strategy,
            Binding = inner.Binding ?? Binding,
            TrimFields = inner.TrimFields ?? TrimFields,
            Culture = inner.Culture ?? Culture,
            Translations = inner.Translations ?? Translations,
            MissingTranslation = inner.MissingTranslation ?? MissingTranslation,
            NormalizeHeader = inner.NormalizeHeader ?? NormalizeHeader,
            MaxErrorDetails = inner.MaxErrorDetails ?? MaxErrorDetails,
            SourceKey = inner.SourceKey ?? SourceKey,
        };
    }
}

/// <summary>Per-call settings at the common export entry point; precedence as for <see cref="TabularImportOptions" />.</summary>
[PublicAPI]
public sealed record TabularExportOptions
{
    /// <summary>Gets whether a header row is written.</summary>
    public bool? WriteHeader { get; init; }

    /// <summary>Gets the culture of numbers and dates as text.</summary>
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

    /// <summary>Gets settings used only when the call exports CSV.</summary>
    public CsvExportOptions? Csv { get; init; }

    /// <summary>Gets settings used only when the call exports Excel.</summary>
    public ExcelExportOptions? Excel { get; init; }

    internal CsvExportOptions ForCsv()
    {
        var inner = Csv ?? new CsvExportOptions();

        return inner with
        {
            WriteHeader = inner.WriteHeader ?? WriteHeader,
            Culture = inner.Culture ?? Culture,
            Translations = inner.Translations ?? Translations,
            MissingTranslation = inner.MissingTranslation ?? MissingTranslation,
            ColumnKeys = inner.ColumnKeys ?? ColumnKeys,
            MaxErrorDetails = inner.MaxErrorDetails ?? MaxErrorDetails,
        };
    }

    internal ExcelExportOptions ForExcel()
    {
        var inner = Excel ?? new ExcelExportOptions();

        return inner with
        {
            WriteHeader = inner.WriteHeader ?? WriteHeader,
            Culture = inner.Culture ?? Culture,
            Translations = inner.Translations ?? Translations,
            MissingTranslation = inner.MissingTranslation ?? MissingTranslation,
            ColumnKeys = inner.ColumnKeys ?? ColumnKeys,
            MaxErrorDetails = inner.MaxErrorDetails ?? MaxErrorDetails,
        };
    }
}
