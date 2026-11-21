namespace Doka.Tabular.Csv;

internal sealed record CsvReadSettings(
    char Delimiter,
    bool AllowLineBreaksInFields,
    bool RequireCrLf,
    bool EmptyLinesAsRecords,
    long? MaxFieldCharacters
);
