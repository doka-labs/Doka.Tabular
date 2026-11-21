# Getting started

## Prepare the formats once

Defaults, limits, logging, and the clock live in the format instances. Create
them once and share them; they are immutable.

```csharp
var csv = new CsvFormat(new CsvFormatOptions
{
    Diagnostics = new TabularDiagnosticsOptions { LoggerFactory = loggerFactory },
    Import = new CsvImportOptions { Delimiter = ';' },
    Export = new CsvExportOptions { Delimiter = ';', WriteHeader = true }
});
var excel = new ExcelFormat(new ExcelFormatOptions());

// Optional: one entry point that chooses the format per call.
var import = new TabularImport(csv, excel);
var export = new TabularExport(csv, excel);
```

Tabular registers nothing in a dependency container; register the instances
yourself if you use one.

## Describe the columns

```csharp
public sealed record Contact(int Number, string Email, DateOnly Since);

var map = TabularMap.For<Contact>()
    .Column(x => x.Number, index: 0, configure: c => c.Header("Number"))
    .Column(x => x.Email, index: 1, configure: c => c.Header("Email").Required())
    .Column(x => x.Since, index: 2, configure: c => c.Header("Since").ExcelNumberFormat("dd.mm.yyyy"))
    .Build();
```

`.Build()` checks the structure and throws `TabularConfigurationException` with
all problems. A built map can be used by many calls at the same time.

## Import

```csharp
var report = await import.ImportAsync(stream, TabularType.Csv, map,
    new TabularImportOptions { Strategy = ImportStrategy.Partial, Binding = ColumnBinding.ByHeaderName },
    cancellationToken);

if (report.Status is ReportStatus.Succeeded or ReportStatus.CompletedWithErrors)
{
    Save(report.Items!);
}
```

The default strategy is strict: one error and no record is returned. The
default binding is `ByIndex`, so the first row is a record; choose
`ByIndexWithHeader` or `ByHeaderName` for files with a header.

## Export

```csharp
// Into a passed stream; it stays open.
var written = await export.ExportAsync(contacts, map, response.Body, TabularType.Excel, cancellationToken);

// Or buffered: the file is returned only on full success.
var (exportReport, file) = await export.ExportAsync(contacts, map, TabularType.Csv,
    new TabularExportOptions { WriteHeader = true }, cancellationToken);
```

Exports accept `IEnumerable<T>`, `IAsyncEnumerable<T>`, and `IQueryable<T>`;
queries must support asynchronous enumeration, such as EF Core queries.

## Next steps

- [Maps and values](maps-and-values.md)
- [Errors and reports](errors-and-reports.md)
