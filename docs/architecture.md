# Architecture

## System context

```mermaid
flowchart LR
  accTitle: System context
  accDescr: The shipped packages and the registry packages each takes directly.
  pkg_Doka_Tabular["Doka.Tabular"]
  pkg_Doka_Tabular_EntityFrameworkCore["Doka.Tabular.EntityFrameworkCore"]
  reg_Microsoft_EntityFrameworkCore_Relational(["Microsoft.EntityFrameworkCore.Relational"])
  reg_Microsoft_Extensions_Logging_Abstractions(["Microsoft.Extensions.Logging.Abstractions"])
  pkg_Doka_Tabular --> reg_Microsoft_Extensions_Logging_Abstractions
  pkg_Doka_Tabular_EntityFrameworkCore --> reg_Microsoft_EntityFrameworkCore_Relational
```

## Shipped packages

| Package | Responsibility |
| --- | --- |
| `Doka.Tabular` | maps, conversion, the CSV and Excel slices, the common entry point, reports, resources and diagnostics |
| `Doka.Tabular.EntityFrameworkCore` | EF Core write targets, transactions, and the import and export journals |

## Runtime composition

An application creates `CsvFormat` and `ExcelFormat` once with their options
and shares them; they are immutable and hold the defaults, limits, logger
factory and clock of every call. `TabularImport` and `TabularExport` are thin
entry points over these two instances. A `TabularMap` is built once with
`Build()` and is immutable, so many calls use it at the same time. Each call
owns its run: buffers, temporary files and the report live from the start to
the end of the call, and the library removes its own temporary files then.
`TabularDatabase` and `TabularJournal` are created over a context factory or a
passed context; a target they prepare describes the writes, and each import
opens its own write session with a context from the factory or the context
that the application passed in.

## Import flow

```mermaid
flowchart LR
  accTitle: Import flow
  accDescr: An import reads records through a format, converts them with a map, and hands valid records to a destination before it returns the report.
  source["Stream or path"]
  format["CsvFormat or ExcelFormat"]
  map["TabularMap"]
  destination["Destination or target"]
  report["ImportReport"]
  source --> format
  map --> format
  format --> destination
  format --> report
```

1. The application calls `ImportAsync` with the input, the map, and options
   or a destination.
2. The format checks the configuration of the call and the first bytes of the
   content before the first record.
3. The input is buffered within the limits of the call when a second read is
   needed.
4. The reader yields records; every field is converted and validated as the
   map describes, and errors are collected per record.
5. Valid records are created through their constructor or factory, and the
   strategy decides which records are taken over.
6. A destination receives the records that were taken over.
7. The call returns the report and removes its temporary files.

## Export flow

```mermaid
flowchart LR
  accTitle: Export flow
  accDescr: An export checks its plan, enumerates the source, and writes the cells into a path, a stream, a buffer or a file target.
  rows["IEnumerable, IAsyncEnumerable or IQueryable"]
  map["TabularMap or RowMap"]
  format["CsvFormat or ExcelFormat"]
  output["Path, stream, buffer or TabularFileTarget"]
  report["ExportReport"]
  rows --> format
  map --> format
  format --> output
  format --> report
```

1. The application calls `ExportAsync` with the source, the map, and a path,
   stream, file target or no output for a buffered export.
2. The format checks columns, translations and layout before the first byte.
3. The source is enumerated, and each record is converted into cells.
4. A path receives a temporary file next to it that is renamed on success; a
   stream receives the bytes while the source is read; a buffered export
   returns its file only on full success; a file target hands the finished file
   to its store.
5. The call returns the report.

## Database write flow

```mermaid
flowchart LR
  accTitle: Database write flow
  accDescr: An import into EF Core checks the context, writes each row in a savepoint through its targets, and commits in groups or once.
  format["CsvFormat or ExcelFormat"]
  destination["TabularDestination"]
  target["TabularWriteTarget"]
  context["DbContext of the application"]
  journal["TabularJournal"]
  format --> destination
  destination --> target
  target --> context
  journal --> context
```

1. Before the first write, the session checks pending changes of a passed
   context, the transaction, savepoint support, the execution strategy and the
   isolation level.
2. An import journal claims the hash of the input; a claimed file is skipped
   or rejected as the options decide.
3. Each row runs in a savepoint; its targets write in bind order, and a row
   error resets the row and its change-tracker state.
4. A strict import commits once; a partial import commits every
   `CommitGroupSize` valid rows. A failed commit is uncertain and ends the run.
5. The journal writes its commit evidence in the data transaction, so a later
   recovery can decide the outcome.

## Cross-cutting boundaries

### Security and privacy

Input files are untrusted; the
[security design](security/security-design.md) describes the trust
boundaries, threats and controls.

### Diagnostics

Every call logs its start and end and creates an activity when a listener is
registered, without values of the file, as the
[logging and tracing guide](guides/logging-and-tracing.md) describes.

### Trimming and deployment

Maps compile accessors at run time; the entry points carry the trimming and
AOT annotations of
[D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md),
and native AOT is a [limitation](limitations.md).

## Verification architecture

### Source responsibilities

#### Doka.Tabular

```mermaid
flowchart LR
  accTitle: Areas of Doka.Tabular
  accDescr: The areas of Doka.Tabular and which area depends on which.
  area__root_["(root)"]
  area_Conversion["Conversion"]
  area_Csv["Csv"]
  area_Diagnostics["Diagnostics"]
  area_Errors["Errors"]
  area_Excel["Excel"]
  area_Export["Export"]
  area_Formats["Formats"]
  area_Import["Import"]
  area_Mapping["Mapping"]
  area_Primitives["Primitives"]
  area_Properties["Properties"]
  area_Reports["Reports"]
  area_Resources["Resources"]
  area_Storage["Storage"]
  area_Conversion --> area_Errors
  area_Conversion --> area_Mapping
  area_Conversion --> area_Primitives
  area_Csv --> area_Conversion
  area_Csv --> area_Diagnostics
  area_Csv --> area_Errors
  area_Csv --> area_Export
  area_Csv --> area_Import
  area_Csv --> area_Mapping
  area_Csv --> area_Primitives
  area_Csv --> area_Reports
  area_Csv --> area_Resources
  area_Csv --> area_Storage
  area_Diagnostics --> area_Errors
  area_Diagnostics --> area_Primitives
  area_Diagnostics --> area_Reports
  area_Diagnostics --> area_Resources
  area_Errors --> area_Primitives
  area_Excel --> area_Conversion
  area_Excel --> area_Diagnostics
  area_Excel --> area_Errors
  area_Excel --> area_Export
  area_Excel --> area_Import
  area_Excel --> area_Mapping
  area_Excel --> area_Primitives
  area_Excel --> area_Reports
  area_Excel --> area_Resources
  area_Excel --> area_Storage
  area_Export --> area_Conversion
  area_Export --> area_Diagnostics
  area_Export --> area_Errors
  area_Export --> area_Import
  area_Export --> area_Mapping
  area_Export --> area_Primitives
  area_Export --> area_Reports
  area_Export --> area_Resources
  area_Export --> area_Storage
  area_Formats --> area_Csv
  area_Formats --> area_Errors
  area_Formats --> area_Excel
  area_Formats --> area_Export
  area_Formats --> area_Import
  area_Formats --> area_Mapping
  area_Formats --> area_Primitives
  area_Formats --> area_Reports
  area_Formats --> area_Storage
  area_Import --> area_Conversion
  area_Import --> area_Diagnostics
  area_Import --> area_Errors
  area_Import --> area_Mapping
  area_Import --> area_Primitives
  area_Import --> area_Reports
  area_Import --> area_Resources
  area_Mapping --> area_Conversion
  area_Mapping --> area_Errors
  area_Mapping --> area_Export
  area_Mapping --> area_Import
  area_Mapping --> area_Primitives
  area_Reports --> area_Errors
  area_Resources --> area_Errors
  area_Resources --> area_Primitives
  area_Storage --> area_Errors
  area_Storage --> area_Primitives
  area_Storage --> area_Reports
```

| Area | Uses |
| --- | --- |
| `(root)` | - |
| `Conversion` | `Errors`, `Mapping`, `Primitives` |
| `Csv` | `Conversion`, `Diagnostics`, `Errors`, `Export`, `Import`, `Mapping`, `Primitives`, `Reports`, `Resources`, `Storage` |
| `Diagnostics` | `Errors`, `Primitives`, `Reports`, `Resources` |
| `Errors` | `Primitives` |
| `Excel` | `Conversion`, `Diagnostics`, `Errors`, `Export`, `Import`, `Mapping`, `Primitives`, `Reports`, `Resources`, `Storage` |
| `Export` | `Conversion`, `Diagnostics`, `Errors`, `Import`, `Mapping`, `Primitives`, `Reports`, `Resources`, `Storage` |
| `Formats` | `Csv`, `Errors`, `Excel`, `Export`, `Import`, `Mapping`, `Primitives`, `Reports`, `Storage` |
| `Import` | `Conversion`, `Diagnostics`, `Errors`, `Mapping`, `Primitives`, `Reports`, `Resources` |
| `Mapping` | `Conversion`, `Errors`, `Export`, `Import`, `Primitives` |
| `Primitives` | - |
| `Properties` | - |
| `Reports` | `Errors` |
| `Resources` | `Errors`, `Primitives` |
| `Storage` | `Errors`, `Primitives`, `Reports` |

| Area | Decisions |
| --- | --- |
| `(root)` | [D-002](decisions/D-002-package-boundaries-format-slices-and-allowed-dependencies.md), [D-017](decisions/D-017-cancellation-tokens-and-optional-parameters.md) |
| `Conversion` | [D-006](decisions/D-006-value-conversion-and-field-options.md), [D-007](decisions/D-007-enums-and-flags.md), [D-008](decisions/D-008-date-and-time-rules.md), [D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md) |
| `Csv` | [D-002](decisions/D-002-package-boundaries-format-slices-and-allowed-dependencies.md), [D-014](decisions/D-014-format-selection-and-content-detection.md), [D-018](decisions/D-018-csv-dialect-and-content.md), [D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md) |
| `Diagnostics` | [D-027](decisions/D-027-logging-and-tracing.md) |
| `Errors` | [D-010](decisions/D-010-error-model-and-error-codes.md) |
| `Excel` | [D-002](decisions/D-002-package-boundaries-format-slices-and-allowed-dependencies.md), [D-014](decisions/D-014-format-selection-and-content-detection.md), [D-016](decisions/D-016-resource-contract-streams-and-temporary-files.md), [D-019](decisions/D-019-excel-import-formats-and-reading-rules.md), [D-020](decisions/D-020-excel-export-layout-and-cell-types.md), [D-021](decisions/D-021-escaped-strings-in-excel-cells.md), [D-022](decisions/D-022-multi-sheet-import-and-export.md), [D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md) |
| `Export` | [D-015](decisions/D-015-export-into-the-output-buffer.md), [D-028](decisions/D-028-json-conversion-of-own-value-types.md), [D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md) |
| `Formats` | [D-013](decisions/D-013-common-entry-point-and-per-call-options.md) |
| `Import` | [D-004](decisions/D-004-immutable-maps-dto-construction-column-binding-and-headers.md), [D-009](decisions/D-009-translation-of-headers-and-values.md), [D-011](decisions/D-011-validation-and-pre-checks.md), [D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md) |
| `Mapping` | [D-004](decisions/D-004-immutable-maps-dto-construction-column-binding-and-headers.md), [D-005](decisions/D-005-row-maps-nested-maps-and-composed-values.md), [D-006](decisions/D-006-value-conversion-and-field-options.md), [D-011](decisions/D-011-validation-and-pre-checks.md), [D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md) |
| `Primitives` | [D-028](decisions/D-028-json-conversion-of-own-value-types.md) |
| `Properties` | - |
| `Reports` | [D-012](decisions/D-012-import-and-export-reports.md) |
| `Resources` | [D-015](decisions/D-015-export-into-the-output-buffer.md), [D-016](decisions/D-016-resource-contract-streams-and-temporary-files.md) |
| `Storage` | [D-026](decisions/D-026-export-journal-and-stored-files.md) |

The slices `Csv` and `Excel` read and write their formats over the shared
pipeline of `Mapping`, `Conversion`, `Import` and `Export`; `Formats` holds the
common entry point above both slices.

#### Doka.Tabular.EntityFrameworkCore

```mermaid
flowchart LR
  accTitle: Areas of Doka.Tabular.EntityFrameworkCore
  accDescr: The areas of Doka.Tabular.EntityFrameworkCore and which area depends on which.
  area__root_["(root)"]
  area_Journals["Journals"]
  area_Matching["Matching"]
  area_Properties["Properties"]
  area_Writing["Writing"]
  area__root_ --> area_Matching
  area__root_ --> area_Writing
  area_Journals --> area_Writing
  area_Matching --> area_Writing
  area_Writing --> area__root_
  area_Writing --> area_Journals
  area_Writing --> area_Matching
```

| Area | Uses |
| --- | --- |
| `(root)` | `Matching`, `Writing` |
| `Journals` | `Writing` |
| `Matching` | `Writing` |
| `Properties` | - |
| `Writing` | `(root)`, `Journals`, `Matching` |

| Area | Decisions |
| --- | --- |
| `(root)` | [D-002](decisions/D-002-package-boundaries-format-slices-and-allowed-dependencies.md), [D-017](decisions/D-017-cancellation-tokens-and-optional-parameters.md), [D-023](decisions/D-023-ef-core-write-targets-insert-upsert-and-lookup.md) |
| `Journals` | [D-025](decisions/D-025-import-journal-and-duplicate-detection.md), [D-026](decisions/D-026-export-journal-and-stored-files.md) |
| `Matching` | [D-023](decisions/D-023-ef-core-write-targets-insert-upsert-and-lookup.md) |
| `Properties` | - |
| `Writing` | [D-023](decisions/D-023-ef-core-write-targets-insert-upsert-and-lookup.md), [D-024](decisions/D-024-ef-core-transactions-and-row-error-mapping.md) |

`Writing` holds the write session and the targets, `Matching` the key
matches, and `Journals` the import and export journals that run inside the
write session.

### Test responsibilities and fixture ownership

```mermaid
flowchart LR
  accTitle: Verification
  accDescr: The packages that verify shipped packages, and the shipped packages their code depends on.
  pkg_Doka_Tabular["Doka.Tabular"]
  pkg_Doka_Tabular_Benchmarks["Doka.Tabular.Benchmarks"]
  pkg_Doka_Tabular_Benchmarks_Tests["Doka.Tabular.Benchmarks.Tests"]
  pkg_Doka_Tabular_Csv_Tests["Doka.Tabular.Csv.Tests"]
  pkg_Doka_Tabular_EntityFrameworkCore["Doka.Tabular.EntityFrameworkCore"]
  pkg_Doka_Tabular_EntityFrameworkCore_MySql_Tests["Doka.Tabular.EntityFrameworkCore.MySql.Tests"]
  pkg_Doka_Tabular_EntityFrameworkCore_PostgreSql_Tests["Doka.Tabular.EntityFrameworkCore.PostgreSql.Tests"]
  pkg_Doka_Tabular_EntityFrameworkCore_Specification["Doka.Tabular.EntityFrameworkCore.Specification"]
  pkg_Doka_Tabular_EntityFrameworkCore_Tests["Doka.Tabular.EntityFrameworkCore.Tests"]
  pkg_Doka_Tabular_Excel_Tests["Doka.Tabular.Excel.Tests"]
  pkg_Doka_Tabular_Samples_Controller["Doka.Tabular.Samples.Controller"]
  pkg_Doka_Tabular_Samples_EntityFrameworkCore["Doka.Tabular.Samples.EntityFrameworkCore"]
  pkg_Doka_Tabular_Samples_Journals["Doka.Tabular.Samples.Journals"]
  pkg_Doka_Tabular_Samples_MinimalApi["Doka.Tabular.Samples.MinimalApi"]
  pkg_Doka_Tabular_Samples_MultiSheet["Doka.Tabular.Samples.MultiSheet"]
  pkg_Doka_Tabular_Samples_MultiTarget["Doka.Tabular.Samples.MultiTarget"]
  pkg_Doka_Tabular_Samples_NestedMaps["Doka.Tabular.Samples.NestedMaps"]
  pkg_Doka_Tabular_Samples_Sftp["Doka.Tabular.Samples.Sftp"]
  pkg_Doka_Tabular_Samples_Tests["Doka.Tabular.Samples.Tests"]
  pkg_Doka_Tabular_Tests["Doka.Tabular.Tests"]
  pkg_Doka_Tabular_Benchmarks --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Benchmarks --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_Benchmarks_Tests --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Csv_Tests --> pkg_Doka_Tabular
  pkg_Doka_Tabular_EntityFrameworkCore_MySql_Tests --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_EntityFrameworkCore_PostgreSql_Tests --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_EntityFrameworkCore_Specification --> pkg_Doka_Tabular
  pkg_Doka_Tabular_EntityFrameworkCore_Specification --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_EntityFrameworkCore_Tests --> pkg_Doka_Tabular
  pkg_Doka_Tabular_EntityFrameworkCore_Tests --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_Excel_Tests --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_Controller --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_EntityFrameworkCore --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_EntityFrameworkCore --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_Samples_Journals --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_Journals --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_Samples_MinimalApi --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_MultiSheet --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_MultiSheet --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_Samples_MultiTarget --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_MultiTarget --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_Samples_NestedMaps --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_Sftp --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_Tests --> pkg_Doka_Tabular
  pkg_Doka_Tabular_Samples_Tests --> pkg_Doka_Tabular_EntityFrameworkCore
  pkg_Doka_Tabular_Tests --> pkg_Doka_Tabular
```

`tests/Shared` holds the fixtures that every test application compiles, and
`tests/Shared/Database` the image manifest reader and the MariaDB server that
the MySQL and PostgreSQL tests and the database samples use.
`Doka.Tabular.EntityFrameworkCore.Specification` owns the EF Core cases that
the SQLite, PostgreSQL and MySQL test projects run.
`tests/Doka.Tabular.Excel.Tests/Fixtures` owns the reference workbooks.

## Architectural invariants

1. `Csv` does not use `Excel`.
2. `Excel` does not use `Csv`.
3. `Conversion` does not use `Excel`.
4. `Csv` does not use `Formats`.
5. `Excel` does not use `Formats`.
6. `Doka.Tabular` references no EF Core package, so the core package stays
   usable without a database.

## Related documentation

- [D-002](decisions/D-002-package-boundaries-format-slices-and-allowed-dependencies.md):
  package boundaries and format slices.
- [D-013](decisions/D-013-common-entry-point-and-per-call-options.md): the
  common entry point.
- [D-016](decisions/D-016-resource-contract-streams-and-temporary-files.md):
  streams and temporary files.
- [D-023](decisions/D-023-ef-core-write-targets-insert-upsert-and-lookup.md)
  and [D-024](decisions/D-024-ef-core-transactions-and-row-error-mapping.md):
  EF Core targets and transactions.
- [D-025](decisions/D-025-import-journal-and-duplicate-detection.md) and
  [D-026](decisions/D-026-export-journal-and-stored-files.md): the journals.
- [Getting started](guides/getting-started.md) and the other
  [guides](README.md).
