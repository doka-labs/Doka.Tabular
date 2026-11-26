# Doka.Tabular

Typed CSV and Excel import and export for .NET, without third-party libraries.
One immutable map per record type drives import and export in both formats,
and every call returns a report with stable error codes instead of throwing.
For applications that exchange records with people or other systems through
files.

## Installation

The package targets `net10.0`. It depends only on
`Microsoft.Extensions.Logging.Abstractions`, which NuGet installs with it.

```sh
dotnet add package Doka.Tabular
```

## Usage

```csharp
var map = TabularMap.For<Article>()
    .Column(x => x.Number, index: 0)
    .Column(x => x.Stock, index: 1, configure: c => c.Validate(stock => stock >= 0))
    .Build();

var csv = new CsvFormat(new CsvFormatOptions());
var report = await csv.ImportAsync(source, map,
    new CsvImportOptions { Strategy = ImportStrategy.Partial }, cancellationToken);

foreach (var error in report.Errors)
{
    Console.WriteLine($"{error.Code} in record {error.Location.RecordNumber}");
}

var excel = new ExcelFormat(new ExcelFormatOptions());
await excel.ExportAsync(report.Items!, map, target, cancellationToken);
```

The example imports the valid records of a CSV stream, prints the error code of
each rejected record and exports the valid records to an XLSX workbook.

## Documentation

- [Repository README](https://github.com/doka-labs/Doka.Tabular/blob/main/README.md)
- [Getting started](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/getting-started.md)
- [Maps and values](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/maps-and-values.md)
- [CSV](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/csv.md)
- [Excel](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/excel.md)
- [Errors and reports](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/errors-and-reports.md)
- [API reference](https://doka-labs.github.io/Doka.Tabular/)

## Feedback and support

Report problems and ideas in the
[issues](https://github.com/doka-labs/Doka.Tabular/issues); the
[support policy](https://github.com/doka-labs/Doka.Tabular/blob/main/SUPPORT.md)
describes the channels and what is maintained.

## License

The package is licensed under `MIT`; see
[LICENSE](https://github.com/doka-labs/Doka.Tabular/blob/main/LICENSE).
