# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow the rules in [Versioning](VERSIONING.md).

## [Unreleased]

## [1.0.0] - 2026-11-27

### Added

- `Doka.Tabular.EntityFrameworkCore`: `TabularDatabase.ForFactory` also takes a function `Func<CancellationToken, Task<TContext>>`, like `TabularJournal.ForFactory`.

### Changed

- `Doka.Tabular.EntityFrameworkCore`: `TabularJournalSettings.IgnoreScopeCase` and `TabularJournalTables.IgnoreScopeCase` are renamed to `IgnoreImportScopeCase`, because the setting applies to import scopes only.

## [1.0.0-rc.1] - 2026-11-26

### Added

- `Doka.Tabular`: typed CSV and Excel import and export with immutable maps per record type, nested maps, row maps, composed values, exact conversions, enums, flags, date and time rules, and translations of headers and values.
- `Doka.Tabular`: `CsvFormat` reads and writes RFC 4180 CSV with explicit dialect options; `ExcelFormat` reads XLSX Transitional, XLSX Strict and XLSM and writes XLSX Transitional, also with several sheets per workbook.
- `Doka.Tabular`: `TabularImport` and `TabularExport` choose the format per call and check the content against it.
- `Doka.Tabular`: every call returns a report with status, counters and errors with stable codes from `TabularErrorCodes`, under a strict or a partial strategy.
- `Doka.Tabular`: buffered and streamed exports, resource limits per call, temporary files that the library removes, log events and an `ActivitySource` per call.
- `Doka.Tabular.EntityFrameworkCore`: insert, upsert and lookup targets for EF Core with savepoints per row, commit groups, several targets per row and exports from queries.
- `Doka.Tabular.EntityFrameworkCore`: import journals that detect a file imported twice and recover uncertain imports from their commit evidence, and export journals for stored files with retention.

[Unreleased]: https://github.com/doka-labs/Doka.Tabular/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/doka-labs/Doka.Tabular/releases/tag/v1.0.0
[1.0.0-rc.1]: https://github.com/doka-labs/Doka.Tabular/releases/tag/v1.0.0-rc.1
