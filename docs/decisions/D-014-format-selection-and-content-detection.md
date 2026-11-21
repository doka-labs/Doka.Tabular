---
id: D-014
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How the stated format is checked against the content and how paths are handled"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-014 -- Format selection and content detection

## Context and Problem Statement

Files arrive with wrong names, from streams without names, or as XLSX saved
under a CSV name. Guessing the format from names leads to misreads; reading a
ZIP container as CSV produces garbage records.

## Decision Drivers

- No guessing from names or MIME types.
- Mismatches are reported before the first record.
- Forward-only streams work.

## Considered Options

- Stated format with a content check
- Automatic detection

## Decision Outcome

Chosen option: "Stated format with a content check", because it makes the
caller's intent explicit and still protects against wrong files.

### Consequences

- Good, because checked bytes are passed on to the parser unchanged, also for
  forward-only streams.
- Good, because Excel exports reject target paths ending in `.xls`, `.xlsm`,
  or `.xlsb`.
- Bad, because a renamed file is not corrected automatically.

### Confirmation

- Performed 2026-11-17: the encoding, Excel read, and behavior tests check
  container signatures with two encodings and forward-only streams, CSV
  content as Excel, plain ZIP files, expected variants, and rejected export
  extensions.

## Pros and Cons of the Options

### Stated format with a content check

- Good, because ZIP and CFB signatures in a CSV import and non-containers in
  an Excel import are reported as `formatMismatch`.
- Good, because `ExpectedVariant` restricts Excel to XLSX or XLSM.
- Bad, because callers always state the format.

### Automatic detection

- Good, because callers need not know the format.
- Bad, because detection has edge cases and its errors appear as data errors.

## More Information

Content types are `text/csv` and
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` without
charset.

### Re-evaluation Triggers

- A new container format appears that shares a signature.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [CsvFormat.cs](../../src/Doka.Tabular/Csv/CsvFormat.cs)
- [ExcelPackage.cs](../../src/Doka.Tabular/Excel/ExcelPackage.cs) and its topic partials `ExcelPackage.*.cs`
- [CsvEncodingTests.cs](../../tests/Doka.Tabular.Csv.Tests/CsvEncodingTests.cs)

### Sources

- No external sources; repository evidence only.
