namespace Doka.Tabular.Tests;

public sealed partial class TabularOverloadTests
{
    private static readonly TabularImport s_import = new(
        new CsvFormat(new CsvFormatOptions()),
        new ExcelFormat(new ExcelFormatOptions()));

    private static readonly ImportCase<TabularImportOptions>[] s_imports =
    [
        new(
            ImportSourceKind.Stream,
            ImportShape.TabularMap,
            false,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Source, c.Type, c.Map, c.Token))),
        new(
            ImportSourceKind.Stream,
            ImportShape.TabularMap,
            true,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Source, c.Type, c.Map, c.Options, c.Token))),
        new(
            ImportSourceKind.Stream,
            ImportShape.RowMap,
            false,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Source, c.Type, c.RowMap, c.Token))),
        new(
            ImportSourceKind.Stream,
            ImportShape.RowMap,
            true,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Source, c.Type, c.RowMap, c.Options, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.TabularMap,
            false,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Path, c.Type, c.Map, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.TabularMap,
            true,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Path, c.Type, c.Map, c.Options, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.RowMap,
            false,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Path, c.Type, c.RowMap, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.RowMap,
            true,
            c => ImportOutcome.Of(s_import.ImportAsync(c.Path, c.Type, c.RowMap, c.Options, c.Token))),
    ];
}
