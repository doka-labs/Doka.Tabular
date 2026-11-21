---
id: D-018
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "The CSV syntax, dialect rules, and field content"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-018 -- CSV dialect and content

## Context and Problem Statement

RFC 4180 defines the structure of CSV but limits field text to printable
ASCII. Real files contain umlauts, tabs, and control characters, use
semicolons, and end lines with LF. Some files use line breaks in quoted
fields, others never do.

## Decision Drivers

- The RFC structure is binding.
- Content is kept exactly.
- Deviations from the RFC are named rules.

## Considered Options

- RFC 4180 structure with named dialect rules
- Strict RFC 4180 only

## Decision Outcome

Chosen option: "RFC 4180 structure with named dialect rules", because it keeps
the RFC structure and handles real files explicitly.

### Consequences

- Good, because the writer writes CRLF, quotes as needed, and writes no BOM
  unless requested.
- Good, because invalid bytes and characters the encoding cannot represent are
  encoding errors, never silent replacements.
- Bad, because the last record has no trailing line break; readers must accept
  both forms as the RFC allows.

### Confirmation

- Performed 2026-11-17: the CSV syntax and encoding tests cover field content,
  quoting, semicolons, single-column empties, record boundaries, unclosed
  quotes in both line-break modes, empty lines, column selection, minimum
  width, encodings, and BOM.

## Pros and Cons of the Options

### RFC 4180 structure with named dialect rules

- Good, because quoting follows the RFC and only the delimiter, CR, LF, and
  quotes trigger it.
- Good, because `AllowLineBreaksInFields`, `RequireCrLf`,
  `EmptyLinesAsRecords`, `Delimiter`, `Encoding`, and `WriteBom` are explicit.
- Bad, because field content beyond printable ASCII is a deliberate extension
  of the RFC.

### Strict RFC 4180 only

- Good, because the rules are minimal.
- Bad, because common files with umlauts, semicolons, or LF would be rejected.

## More Information

An unclosed quote with line breaks allowed ends the run as parser abort;
without line breaks in fields it rejects the record and the parser continues
on the next line.

### Re-evaluation Triggers

- RFC 4180 is replaced by a standards-track document.
- Applications need other quote characters.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [CsvRecordSource.cs](../../src/Doka.Tabular/Csv/CsvRecordSource.cs) and [CharReader.cs](../../src/Doka.Tabular/Csv/CharReader.cs)
- [CsvWriter.cs](../../src/Doka.Tabular/Csv/CsvWriter.cs)
- [CsvSyntaxTests.cs](../../tests/Doka.Tabular.Csv.Tests/CsvSyntaxTests.cs)

### Sources

- [RFC 4180, Common Format and MIME Type for Comma-Separated Values (CSV) Files](https://www.rfc-editor.org/rfc/rfc4180.txt) (primary source; retrieved 2026-11-17)
