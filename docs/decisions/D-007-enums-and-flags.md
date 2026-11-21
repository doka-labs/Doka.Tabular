---
id: D-007
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How enum and flags values are read and written as text and numbers"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-007 -- Enums and flags

## Context and Problem Statement

Enum values appear in files as names, as texts from
`JsonStringEnumMemberName`, as translated texts, or as numbers. Aliases with
the same value and flags combinations need a deterministic text. Ambiguous
texts would turn a file into wrong values silently.

## Decision Drivers

- Reading is tolerant to case but never ambiguous.
- Writing is deterministic.
- Numbers are accepted only when the application allows them.
- Configuration problems are reported before reading or writing.

## Considered Options

- Names and attribute texts with explicit numeric input
- Numbers and names without checks

## Decision Outcome

Chosen option: "Names and attribute texts with explicit numeric input",
because it accepts the established texts and rejects ambiguity early.

### Consequences

- Good, because flags are written as the exactly matching member, otherwise as
  single bits in ascending order, with a configurable separator, and read with
  trimming.
- Good, because zero without a member needs an explicit representation for
  export.
- Bad, because combined flags members must cover whole values; partial bits
  are errors.

### Confirmation

- Performed 2026-11-17: the enum tests cover attribute texts, numeric input,
  short-based enums, collisions, aliases, empty values, flags parsing and
  export, combined members, zero without member, and separators in
  translations.

## Pros and Cons of the Options

### Names and attribute texts with explicit numeric input

- Good, because the texts the application already uses for JSON are accepted.
- Good, because collisions and aliases without export choice are configuration
  errors.
- Bad, because an alias needs `ExportAlias`, `TranslationKeys`, or
  `ExportAsNumber`; `ExportAlias` names the member as text (with `nameof`),
  because aliases share one value and a typed value cannot tell them apart.

### Numbers and names without checks

- Good, because less configuration is needed.
- Bad, because ambiguous texts and stray numbers are read as some value.

## More Information

Collisions between member names and attribute texts are reported by
`.Build()`; collisions involving translations are reported before the first
read or write of the call.

### Re-evaluation Triggers

- Applications need culture-specific numeric enum input.
- The serializer attribute used for texts changes.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [EnumConverter.cs](../../src/Doka.Tabular/Conversion/EnumConverter.cs)
- [EnumTests.cs](../../tests/Doka.Tabular.Tests/EnumTests.cs)

### Sources

- No external sources; repository evidence only.
