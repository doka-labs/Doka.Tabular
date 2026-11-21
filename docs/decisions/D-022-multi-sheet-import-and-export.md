---
id: D-022
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Workbooks with several sheets on import and export"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-022 -- Multi-sheet import and export

## Context and Problem Statement

Reports have several sheets of different record types, and imports read
several sheets into several targets. Object lists and casts would lose typing.
A strict import over several sheets must decide whether one bad sheet rejects
all.

## Decision Drivers

- Typed definitions per sheet without casts.
- Clear transaction scope.
- Sheet order is explicit.

## Considered Options

- Typed workbook definitions on ExcelFormat
- Workbook as a list of untyped sheets

## Decision Outcome

Chosen option: "Typed workbook definitions on ExcelFormat", because it keeps
sheets typed and the transaction scope explicit.

### Consequences

- Good, because unmapped sheets are ignored and a missing configured sheet is
  a schema error before any write.
- Good, because a later failing sheet keeps an existing path unchanged and
  marks a stream as partial.
- Bad, because strict scope over several databases is rejected unless
  per-sheet transactions are chosen.

### Confirmation

- Performed 2026-11-17: the Excel export tests cover independent sheets, a
  later failure, invalid second definitions, configured import order with
  typed results, strict failure with NotStarted, missing sheets, and
  translations per sheet; the multi-sheet sample imports into EF with both
  scopes on MariaDB.

## Pros and Cons of the Options

### Typed workbook definitions on ExcelFormat

- Good, because `ExcelExport.Create().Add(...)` and
  `ExcelImport.Create().Add(...)` keep each sheet typed, and
  `report.Get(sheet)` returns the typed result.
- Good, because strict scope covers all sheets unless `TransactionPerSheet` is
  set.
- Bad, because the common entry point does not offer multi-sheet calls.

### Workbook as a list of untyped sheets

- Good, because one method handles any number of sheets.
- Bad, because results need casts and errors appear at run time.

## More Information

Budgets apply per call, not per sheet. Duplicates are checked per sheet.

### Re-evaluation Triggers

- Applications need multi-sheet calls at the common entry point.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ExcelExportBuilder.cs](../../src/Doka.Tabular/Excel/ExcelExportBuilder.cs), [ExcelImportBuilder.cs](../../src/Doka.Tabular/Excel/ExcelImportBuilder.cs), [WorkbookExportSheet.cs](../../src/Doka.Tabular/Excel/WorkbookExportSheet.cs), [WorkbookImportSheet.cs](../../src/Doka.Tabular/Excel/WorkbookImportSheet.cs), and [WorkbookImportRun.cs](../../src/Doka.Tabular/Excel/WorkbookImportRun.cs)
- [ExcelFormat.Workbook.cs](../../src/Doka.Tabular/Excel/ExcelFormat.Workbook.cs)
- [ExcelExportTests.cs](../../tests/Doka.Tabular.Excel.Tests/ExcelExportTests.cs)
- [MultiSheetSample.cs](../../samples/Doka.Tabular.Samples.MultiSheet/MultiSheetSample.cs)

### Sources

- No external sources; repository evidence only.
