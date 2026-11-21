---
id: D-021
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How texts that XML cannot carry are written and read in workbooks"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-021 -- Escaped strings in Excel cells

## Context and Problem Statement

Field content may contain control characters, carriage returns, and texts that
look like escape sequences. XML 1.0 cannot carry every character, and Excel
uses an escape form for such characters in its text elements. Without a rule,
some texts would be lost or misread.

## Decision Drivers

- Every text value round-trips.
- Files from Excel are read as Excel writes them.

## Considered Options

- ST_Xstring escaping in both directions
- No escaping rule

## Decision Outcome

Chosen option: "ST_Xstring escaping in both directions", because it keeps
every text value and matches the type of the text elements in the format.

### Consequences

- Good, because LF and TAB stay unescaped in cell text and are escaped in
  attributes.
- Bad, because the escape form must be applied consistently in every text
  path.

### Confirmation

- Performed 2026-11-17: the Excel read tests export and reimport control
  characters, CRLF, TAB, and a literal escape text, check the escaped forms in
  the sheet XML, and decode foreign escapes from shared and inline strings.
- Performed 2026-11-21: the implemented rules were compared with ECMA-376
  Part 1, 5th edition, 22.9.2.19 ST_Xstring, and with the Office implementer
  notes for that clause. The standard escapes characters XML 1.0 cannot carry as
  `_xHHHH_`; Excel also escapes CR always, LF and TAB only in attributes, and an
  underscore only before an escape form. Writing follows these rules, and
  reading decodes escapes in upper and lower case. The schema types the text of
  shared and inline strings, cell values, sheet names, and table column names as
  ST_Xstring; each of these paths escapes or decodes. `ExcelEscapeTests` checks
  the rules, and each of its mutations of `ExcelXml` turns at least one test red.

## Pros and Cons of the Options

### ST_Xstring escaping in both directions

- Good, because characters XML 1.0 does not allow and CR are written as
  `_xHHHH_`, and a literal `_xHHHH_` text gets `_x005F_` for its underscore.
- Good, because shared strings, inline strings, and attributes are decoded on
  reading.
- Bad, because readers that ignore the escape form show the escape text.

### No escaping rule

- Good, because cell text is written as is.
- Bad, because control characters break the XML and literal escape-like texts
  change on reading.

## More Information

Text elements of the spreadsheet markup are typed as `ST_Xstring` (ECMA-376
Part 1, 22.9.2.19).

### Re-evaluation Triggers

- A reader in the ecosystem misinterprets the escape form.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Confirmation completed against the clause text of ECMA-376 Part 1
  and the Office implementer notes.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ExcelXml.cs](../../src/Doka.Tabular/Excel/ExcelXml.cs)
- [ExcelReadTests.cs](../../tests/Doka.Tabular.Excel.Tests/ExcelReadTests.cs)
- [ExcelEscapeTests.cs](../../tests/Doka.Tabular.Excel.Tests/ExcelEscapeTests.cs)

### Sources

- [ECMA-376, Office Open XML File Formats](https://ecma-international.org/publications-and-standards/standards/ecma-376/) (primary source; retrieved 2026-11-17)
- [Text Class (DocumentFormat.OpenXml.Spreadsheet)](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.text?view=openxml-3.0.1) (primary source; retrieved 2026-11-17)
- [ECMA-376 Part 1, 5th edition, 22.9.2.19 ST_Xstring](https://ecma-international.org/wp-content/uploads/ECMA-376-1_5th_edition_december_2016.zip) (primary source; retrieved 2026-11-21)
- [MS-OI29500, Part 1 Section 22.9.2.19, ST_Xstring](https://learn.microsoft.com/en-us/openspecs/office_standards/ms-oi29500/d34ae755-c53f-4a44-a363-c6dd3ee018a4) (primary source; retrieved 2026-11-21)
