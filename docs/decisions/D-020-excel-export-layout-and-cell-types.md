---
id: D-020
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Cell types, range and table mode, formatting, widths, and limits of Excel exports"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-020 -- Excel export layout and cell types

## Context and Problem Statement

Exports should sort and filter correctly in Excel, look acceptable without
configuration, and support formatted reports. Unbounded styling options make
the API large, and silent text fallbacks for unrepresentable numbers hide data
problems. Excel has fixed limits for rows, columns, and cell text.

## Decision Drivers

- Typed values sort and filter correctly.
- A small layout surface.
- No silent fallback from number to text.
- Limits are reported, never cut.

## Considered Options

- Typed cells with a fixed layout surface
- Full styling API

## Decision Outcome

Chosen option: "Typed cells with a fixed layout surface", because it produces
useful workbooks with a small and checkable API.

### Consequences

- Good, because table mode creates a table object named `TabularExport`,
  `TabularExport_2`, and so on when no name is given, with `TableStyleMedium2`
  and banded rows by default.
- Good, because widths are estimated from at most 200 rows or 1 MiB of preview
  data without reading the source twice.
- Good, because 1,048,576 rows, 16,384 columns, 32,767 characters, and 253
  line breaks per cell are checked.
- Bad, because a value that is not exactly representable as a native number is
  an error unless the column is text or rounding is configured.

### Confirmation

- Performed 2026-11-17: the Excel export tests check native values and
  formats, filter and frozen panes, table names and styles, header style and
  widths, start positions, invalid layouts, table mode headers, empty sources,
  rejected extensions, text cells that look like formulas, the row limit with
  path publication, and the cell text limit; written workbooks pass the Open
  XML SDK validator.

## Pros and Cons of the Options

### Typed cells with a fixed layout surface

- Good, because numbers, dates, and times are native cells with the map's
  number format.
- Good, because `ExcelLayoutOptions` covers mode, table name and style,
  filter, freeze, frozen columns, header style, heights, and column width and
  alignment.
- Bad, because further formatting needs post-processing by the application.

### Full styling API

- Good, because any report could be produced.
- Bad, because the API would mirror a large part of the format and grow with
  every request.

## More Information

Without settings, the export adds no own formatting. Layout settings at a
sheet replace the call-wide layout completely.

### Re-evaluation Triggers

- Applications need conditional formatting or formulas.
- Excel limits change.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ExcelWorkbookWriter.cs](../../src/Doka.Tabular/Excel/ExcelWorkbookWriter.cs) and [ExcelSheetWriter.cs](../../src/Doka.Tabular/Excel/ExcelSheetWriter.cs) with its topic partials `ExcelSheetWriter.*.cs`
- [ExcelLayoutValidation.cs](../../src/Doka.Tabular/Excel/ExcelLayoutValidation.cs)
- [ExcelExportTests.cs](../../tests/Doka.Tabular.Excel.Tests/ExcelExportTests.cs)

### Sources

- [Excel specifications and limits](https://support.microsoft.com/en-us/office/excel-specifications-and-limits-1672b34d-7043-467e-8e27-269d656771c3) (primary source; retrieved 2026-11-17)
