# Doka.Tabular

Typed CSV and Excel import and export for .NET 10, with optional EF Core targets
and journals.

- One immutable map per record type describes columns for import and export.
- CSV follows RFC 4180 with explicit dialect rules; Excel reads XLSX
  Transitional, Strict, and XLSM and writes XLSX, without third-party libraries.
- Every call returns a report with status, counters, and stable error codes;
  only programming errors throw.
- Imports can be written to EF Core entities with insert, upsert, and lookup
  targets, savepoints per row, commit groups, and duplicate-file protection.

## Packages

| Package | Content |
| --- | --- |
| `Doka.Tabular` | Maps, conversion, CSV, Excel, reports, diagnostics |
| `Doka.Tabular.EntityFrameworkCore` | EF Core targets, transactions, import and export journals |

## Quick start

```csharp
public sealed record Article(string Number, int Stock);

var map = TabularMap.For<Article>()
    .Column(x => x.Number, index: 0)
    .Column(x => x.Stock, index: 1, configure: c => c.Validate(stock => stock >= 0))
    .Build();

var csv = new CsvFormat(new CsvFormatOptions());
var report = await csv.ImportAsync(stream, map,
    new CsvImportOptions { Strategy = ImportStrategy.Partial }, cancellationToken);

foreach (var error in report.Errors)
{
    Console.WriteLine($"{error.Code} in record {error.Location.RecordNumber}");
}
```

## Documentation

- [Guides](docs/README.md)
- [Architecture decisions](docs/decisions/README.md)
- [Samples](samples)

## Requirements

.NET 10 SDK 10.0.404 as pinned in `global.json`. The database tests and the EF
samples start servers with Testcontainers and need Docker.

Maps compile their accessors and close generic types at run time. The entry
points that build maps or run imports and exports are marked with
`RequiresDynamicCode` and `RequiresUnreferencedCode`, so an application that
trims or publishes native AOT gets the warnings at its call sites. Native AOT
is not supported; see
[D-029](docs/decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md).

## License

[MIT](LICENSE)
