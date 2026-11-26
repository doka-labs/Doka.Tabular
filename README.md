# Doka.Tabular

[![CI](https://github.com/doka-labs/Doka.Tabular/actions/workflows/ci.yml/badge.svg)](https://github.com/doka-labs/Doka.Tabular/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Typed CSV and Excel import and export for .NET 10, with optional EF Core targets
and journals.

- One immutable map per record type describes columns for import and export.
- CSV follows RFC 4180 with explicit dialect rules; Excel reads XLSX
  Transitional, Strict, and XLSM and writes XLSX, without third-party libraries.
- Every call returns a report with status, counters, and stable error codes;
  only programming errors throw.
- Imports can be written to EF Core entities with insert, upsert, and lookup
  targets, savepoints per row, commit groups, and duplicate-file protection.

## Contents

- [When to consider](#when-to-consider)
- [Status](#status)
- [Packages](#packages)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Documentation](#documentation)
- [Community and support](#community-and-support)
- [License](#license)

## When to consider

Consider Doka.Tabular when an application exchanges records with people or
other systems through CSV and Excel files and needs:

- one typed map that drives import and export in both formats;
- a report with stable error codes per row instead of exceptions, with strict
  or partial import strategies;
- imports into EF Core entities with insert, upsert, and lookup, transactions,
  and protection against importing the same file twice; or
- no third-party dependency: the core package depends only on
  `Microsoft.Extensions.Logging.Abstractions`, the EF package only on
  `Microsoft.EntityFrameworkCore.Relational`.

Another library fits better when you need:

- formulas calculated, styling beyond a fixed table layout, or other workbook
  features: [ClosedXML](https://github.com/ClosedXML/ClosedXML) (MIT) or
  [EPPlus](https://github.com/EPPlusSoftware/EPPlus) (commercial license for
  commercial use);
- to read legacy `.xls` or `.xlsb` workbooks:
  [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) (MIT,
  read only);
- CSV access below a typed map, or .NET Framework and .NET Standard targets:
  [CsvHelper](https://github.com/JoshClose/CsvHelper) (MS-PL or Apache-2.0) or
  [Sylvan.Data.Csv](https://github.com/MarkPflug/Sylvan) (MIT); or
- Native AOT, which Doka.Tabular does not support; see
  [D-029](docs/decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md).

[Limitations](docs/limitations.md) lists every known limit by
responsibility.

## Status

The first version, `1.0.0-rc.1`, is a release candidate: its public API can
still change until `1.0.0`, as [Versioning](VERSIONING.md) describes. Until a
stable version exists, `dotnet add package` needs `--prerelease`.
[Support and qualification](docs/support-and-qualification.md) lists the
supported platforms and which versions receive fixes.

## Packages

| Package | Content |
| --- | --- |
| `Doka.Tabular` | Maps, conversion, CSV, Excel, reports, diagnostics |
| `Doka.Tabular.EntityFrameworkCore` | EF Core targets, transactions, import and export journals |

## Requirements

.NET 10. Building the repository uses the SDK pinned in `global.json`; the
database tests and the EF samples start servers with Testcontainers and need
Docker.

Maps compile their accessors and close generic types at run time. The entry
points that build maps or run imports and exports are marked with
`RequiresDynamicCode` and `RequiresUnreferencedCode`, so an application that
trims or publishes native AOT gets the warnings at its call sites.

## Installation

```sh
dotnet add package Doka.Tabular
dotnet add package Doka.Tabular.EntityFrameworkCore
```

The second package is only needed for EF Core targets and journals.

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

var excel = new ExcelFormat(new ExcelFormatOptions());
await excel.ExportAsync(report.Items!, map, "articles.xlsx", cancellationToken);
```

## Documentation

- [Documentation](docs/README.md): guides from getting started to journals,
  troubleshooting, limitations, architecture, security and the release
  process.
- [API reference](https://doka-labs.github.io/Doka.Tabular/): every public type
  and member and the meaning of every error code.
- [Architecture decisions](docs/decisions/README.md): the reasons behind the
  contracts.
- [Samples](samples/README.md): runnable programs for an ASP.NET Core
  controller and a minimal API, EF Core imports and exports, journals,
  multi-sheet workbooks, several targets per row, nested maps, and SFTP
  transfer.
- [Changelog](CHANGELOG.md) and [roadmap](ROADMAP.md).

## Community and support

Doka.Tabular follows the organization-wide policies of Doka Labs and adds
what applies to it only:

- [Support channels and maintenance](SUPPORT.md)
- [Contributing](CONTRIBUTING.md)
- [Code of Conduct](https://github.com/doka-labs/.github/blob/main/CODE_OF_CONDUCT.md)
- [Security policy and private vulnerability reporting](SECURITY.md)
- [Governance and responsibilities](https://github.com/doka-labs/.github/blob/main/GOVERNANCE.md)

The project is maintained by Dominic Kalkbrenner; roles and decisions are
described in the governance.

## License

The code is MIT-licensed. See [LICENSE](LICENSE). The
[Code of Conduct](https://github.com/doka-labs/.github/blob/main/CODE_OF_CONDUCT.md#attribution)
is separately licensed under
[CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/).
