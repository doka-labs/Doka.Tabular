---
id: D-019
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Which workbook formats are read and how sheets, rows, cells, and formulas are interpreted"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-019 -- Excel import formats and reading rules

## Context and Problem Statement

Workbooks come from Excel and from other tools, in Transitional and Strict
markup, sometimes with macros. Binary and legacy formats and encrypted
packages need different parsers. Formulas store results that may be outdated,
missing, or error values. Hidden and formatted-only rows are common in
reports.

## Decision Drivers

- Support the open XML formats that Excel saves by default.
- Never execute or keep macros.
- Reproducible results without formula calculation.
- Explicit sheet choice.

## Considered Options

- Open XML formats with fixed reading rules
- All Excel formats including binary ones

## Decision Outcome

Chosen option: "Open XML formats with fixed reading rules", because it covers
the default formats with a bounded, reviewable reader.

### Consequences

- Good, because rows with only formatting are skipped and a value or formula
  anywhere makes a row a record.
- Good, because `ExcelAsText()` keeps text columns as text cells on export.
- Bad, because formula results are never recalculated.

### Confirmation

- Performed 2026-11-17: the Excel read tests cover format checks, sheet
  selection, start and end rows, empty and missing cells, formatted-only rows,
  formulas, hidden rows, escaped strings, DTDs, expansion limits, and
  forward-only input; workbooks written by the Open XML SDK are read as well.
- Performed 2026-11-21: the reference workbooks saved by Excel (R01 to R12, see
  the fixture README) are provided for Mac English and Windows German; all 63
  tests in `ReferenceFileTests` run and pass, none is skipped.

## Pros and Cons of the Options

### Open XML formats with fixed reading rules

- Good, because the reader follows the Open Packaging Conventions, including
  content types from overrides and extension defaults.
- Good, because stored formula results are read like values; missing results
  and error values are field errors.
- Bad, because XLS, XLSB, and encrypted packages are rejected with
  `unsupportedFormat`.

### All Excel formats including binary ones

- Good, because every file Excel can save would be readable.
- Bad, because two more binary parsers would be maintained without a library.

## More Information

Shared strings, rich text runs, and phonetic runs are read; phonetic text is
not part of the cell value. The variant is detected from the content, never
from the file name.

### Re-evaluation Triggers

- Excel changes its default save format.
- Applications need XLSB.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Confirmation updated: the reference workbooks are provided and
  their tests run.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ExcelPackage.cs](../../src/Doka.Tabular/Excel/ExcelPackage.cs) and its topic partials `ExcelPackage.*.cs`
- [ExcelSheetSource.cs](../../src/Doka.Tabular/Excel/ExcelSheetSource.cs)
- [ExcelReadTests.cs](../../tests/Doka.Tabular.Excel.Tests/ExcelReadTests.cs)
- [Fixtures README](../../tests/Doka.Tabular.Excel.Tests/Fixtures/README.md)

### Sources

- [ECMA-376, Office Open XML File Formats](https://ecma-international.org/publications-and-standards/standards/ecma-376/) (primary source; retrieved 2026-11-17)
