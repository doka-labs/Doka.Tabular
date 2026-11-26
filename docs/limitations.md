# Limitations

## Inventory by responsibility

### Doka.Tabular

| Limitation | Effect | Details |
| --- | --- | --- |
| Native AOT is not supported | Entry points that build maps or run imports and exports are marked `RequiresDynamicCode` and `RequiresUnreferencedCode`; a native AOT build warns at these call sites | [D-029](decisions/D-029-typed-pipeline-compiled-accessors-and-aot-marking.md) |
| XLS, XLSB and encrypted workbooks are not read | Such a file ends the import with `tabular.schema.unsupportedFormat` | [Excel](guides/excel.md#formats) |
| Formulas are not computed | Imports take the stored result; a formula without one is a field error | [Excel](guides/excel.md#reading) |
| Exports write XLSX Transitional only | XLSX Strict and XLSM are import formats; a target path ending in `.xls`, `.xlsm` or `.xlsb` is rejected | [Excel](guides/excel.md#writing) |
| The layout of exports is a fixed table layout | Number formats, widths, header style, filter, frozen panes and table mode; no other workbook features | [D-020](decisions/D-020-excel-export-layout-and-cell-types.md) |
| A workbook with several sheets needs a sheet choice | Without `SheetName`, a single-sheet import of such a workbook is `tabular.schema.ambiguousSheet` | [Excel](guides/excel.md#reading) |
| Conversions are exact | A value that the target type cannot hold exactly is a field error unless rounding is configured | [Maps and values](guides/maps-and-values.md#conversion) |
| Most resource limits have no default | Only `MaxExpandedBytes` (1 GiB) bounds an input by default | [Resources and limits](guides/resources-and-limits.md) |
| No registration in a dependency container | The application creates and registers the format instances itself | [Getting started](guides/getting-started.md#prepare-the-formats-once) |

### EF Core

| Limitation | Effect | Details |
| --- | --- | --- |
| A context with a retrying execution strategy is rejected | The import ends with `tabular.configuration.invalid` before the first write | [Entity Framework Core](guides/entity-framework-core.md#transactions) |
| A provider without savepoints needs commit groups of one row | Otherwise the import ends with `tabular.configuration.invalid` before the first write | [Entity Framework Core](guides/entity-framework-core.md#transactions) |
| A failed commit of a group is always uncertain | The rows count as `RowsUncertain` and the run ends with `tabular.persistence.commitUnknown` | [Entity Framework Core](guides/entity-framework-core.md#transactions) |
| A match finds candidates with the equality of the database | A comparer can narrow a match but not widen it beyond the column collation | [Entity Framework Core](guides/entity-framework-core.md#targets) |
| MariaDB with `innodb_snapshot_isolation` under `REPEATABLE READ` | A conflicting row rolls back the whole transaction with `tabular.persistence.rollbackUnknown` instead of a row conflict | [Entity Framework Core](guides/entity-framework-core.md#transactions) |
| Journal tables come from the migrations of the application | The model of the data context must contain the journal | [Journals](guides/journals.md#registration) |

### File formats

| Limitation | Effect | Details |
| --- | --- | --- |
| A worksheet holds 1,048,576 rows, 16,384 columns and 32,767 characters per cell | Larger exports end with `tabular.schema.limitExceeded` | [Excel](guides/excel.md#writing) |
| CSV has no cell types | A CSV field that starts with `=`, `+`, `-` or `@` can run as a formula in a spreadsheet program | [Security design](security/security-design.md#formula-injection-in-exported-files) |
| Windows-1252 and other code pages are not built into .NET | The application passes an `Encoding` from `CodePagesEncodingProvider` | [CSV](guides/csv.md#dialect) |

### Application

| Limitation | Effect | Details |
| --- | --- | --- |
| No scheduler | Expired exports and old journal entries are deleted only by jobs of the application | [Journals](guides/journals.md#cleanup) |
| Limits for untrusted input are the application's choice | Without them, an input is bounded only by `MaxExpandedBytes` and the resources of the process | [Security design](security/security-design.md#secure-deployment-requirements) |
| An uncertain import without a journal | The application clarifies the stored state itself | [Deployment and recovery](runbooks/deployment-and-recovery.md) |

## Maintenance contract

A limitation is added in the pull request that introduces it or finds it, and
changed or removed in the pull request that lifts it, together with its entry
in the changelog. A limitation that a supported platform imposes stays until
that platform changes.

## Primary sources

- [Excel specifications and limits](https://support.microsoft.com/office/excel-specifications-and-limits-1672b34d-7043-467e-8e27-269d656771c3)
- [RFC 4180](https://www.rfc-editor.org/rfc/rfc4180)
- [EF Core: Connection resiliency](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency)
- [MariaDB: innodb_snapshot_isolation](https://mariadb.com/docs/server/server-usage/storage-engines/innodb/innodb-system-variables#innodb_snapshot_isolation)
