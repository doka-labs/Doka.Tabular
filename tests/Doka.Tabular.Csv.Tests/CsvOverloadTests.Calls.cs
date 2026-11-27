namespace Doka.Tabular.Csv.Tests;

public sealed partial class CsvOverloadTests
{
    private static readonly CsvFormat s_format = new(new CsvFormatOptions());

    private static readonly ExportCase<CsvExportOptions>[] s_exports =
    [
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.Stream, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.Stream, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.Path, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.Path, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.FileTarget, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.TabularMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Records, c.Map, c.FileTarget, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.Stream, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.Stream, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.Path, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.Path, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.FileTarget, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.ResidentRows, c.RowExport, c.FileTarget, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.Stream, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.Stream, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.Path, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.Path, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.Options, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.FileTarget, c.Token))),
        new(
            RowSource.Enumerable,
            ExportShape.RowMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.TabularRows, c.RowMap, c.FileTarget, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.Stream, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.Stream, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.Path, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.Path, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.FileTarget, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.TabularMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncRecords, c.Map, c.FileTarget, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncResidentRows, c.RowExport, c.Stream, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(
                s_format.ExportAsync(
                    c.AsyncResidentRows,
                    c.RowExport,
                    c.Stream,
                    c.Options,
                    c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncResidentRows, c.RowExport, c.Path, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncResidentRows, c.RowExport, c.Path, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncResidentRows, c.RowExport, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncResidentRows, c.RowExport, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncResidentRows, c.RowExport, c.FileTarget, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowExportMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(
                s_format.ExportAsync(
                    c.AsyncResidentRows,
                    c.RowExport,
                    c.FileTarget,
                    c.Options,
                    c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncTabularRows, c.RowMap, c.Stream, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncTabularRows, c.RowMap, c.Stream, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncTabularRows, c.RowMap, c.Path, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncTabularRows, c.RowMap, c.Path, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncTabularRows, c.RowMap, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncTabularRows, c.RowMap, c.Options, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.AsyncTabularRows, c.RowMap, c.FileTarget, c.Token))),
        new(
            RowSource.AsyncEnumerable,
            ExportShape.RowMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(
                s_format.ExportAsync(
                    c.AsyncTabularRows,
                    c.RowMap,
                    c.FileTarget,
                    c.Options,
                    c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.Stream, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.Stream, c.Options, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.Path, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.Path, c.Options, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.Options, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.FileTarget, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.TabularMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryRecords, c.Map, c.FileTarget, c.Options, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryResidentRows, c.RowExport, c.Stream, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(
                s_format.ExportAsync(
                    c.QueryResidentRows,
                    c.RowExport,
                    c.Stream,
                    c.Options,
                    c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryResidentRows, c.RowExport, c.Path, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryResidentRows, c.RowExport, c.Path, c.Options, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryResidentRows, c.RowExport, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryResidentRows, c.RowExport, c.Options, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.QueryResidentRows, c.RowExport, c.FileTarget, c.Token))),
        new(
            RowSource.Queryable,
            ExportShape.RowExportMap,
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(
                s_format.ExportAsync(
                    c.QueryResidentRows,
                    c.RowExport,
                    c.FileTarget,
                    c.Options,
                    c.Token))),
    ];

    private static readonly ImportCase<CsvImportOptions>[] s_imports =
    [
        new(
            ImportSourceKind.Stream,
            ImportShape.TabularMap,
            false,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Source, c.Map, c.Token))),
        new(
            ImportSourceKind.Stream,
            ImportShape.TabularMap,
            true,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Source, c.Map, c.Options, c.Token))),
        new(
            ImportSourceKind.Stream,
            ImportShape.RowMap,
            false,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Source, c.RowMap, c.Token))),
        new(
            ImportSourceKind.Stream,
            ImportShape.RowMap,
            true,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Source, c.RowMap, c.Options, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.TabularMap,
            false,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Path, c.Map, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.TabularMap,
            true,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Path, c.Map, c.Options, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.RowMap,
            false,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Path, c.RowMap, c.Token))),
        new(
            ImportSourceKind.Path,
            ImportShape.RowMap,
            true,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Path, c.RowMap, c.Options, c.Token))),
    ];
}
