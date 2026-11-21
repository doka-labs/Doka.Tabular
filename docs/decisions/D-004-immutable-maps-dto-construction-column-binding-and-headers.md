---
id: D-004
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How maps are built and reused, how DTOs are created, and how columns are bound to fields"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-004 -- Immutable maps, DTO construction, column binding and headers

## Context and Problem Statement

A map describes the columns of a record type for import and export. It is
configured once and used by many concurrent calls. Records are often
positional records or classes with private setters, so the library must create
them without guessing. Files come with or without a header row, and header
texts may be translated, so position and header binding must be chosen
explicitly.

## Decision Drivers

- A built map can be shared safely across concurrent calls.
- DTO creation is predictable and type-safe; nothing is guessed from names
  beyond direct parameter binding.
- Header handling is explicit on import and export.
- Configuration errors are found before data is read.

## Considered Options

- Immutable maps, constructor or factory, explicit binding mode
- Mutable maps with constructor selection and header detection

## Decision Outcome

Chosen option: "Immutable maps, constructor or factory, explicit binding
mode", because it keeps runs reproducible, shares maps safely, and reports
ambiguity as a configuration error instead of guessing.

### Consequences

- Good, because `.Build()` reports all structural errors at once with
  `TabularConfigurationException`.
- Good, because call-specific problems, such as missing constructor binding,
  are reported before the first read.
- Good, because header-name import uses the same header text the export
  writes.
- Bad, because applications must state the header mode on both sides.

### Confirmation

- Performed 2026-11-17: the map construction tests cover the basic form,
  swapped declarations, negative indexes, several fitting constructors with
  and without factory, parameter binding by C# name, `init` and `set` binding,
  optional parameters, and concurrent exports of one map, in CSV and Excel.

## Pros and Cons of the Options

### Immutable maps, constructor or factory, explicit binding mode

- Good, because a built map never changes and can be shared.
- Good, because exactly one fitting public constructor is used, and a factory
  resolves every other case type-safely.
- Good, because `ColumnBinding.ByIndex`, `ByIndexWithHeader`, and
  `ByHeaderName` state the meaning of the first row.
- Bad, because records with several fitting constructors need a factory.

### Mutable maps with constructor selection and header detection

- Good, because simple cases need less configuration.
- Bad, because guessing whether the first row is a header and which
  constructor to use leads to silent misreads.

## More Information

Parameter names are compared with the names of mapped properties using
`OrdinalIgnoreCase`; a match requires a directly assignable type. `Required()`
makes an empty value a field error for every type. A missing required header
is one schema error per file; `OptionalHeader()` makes a header optional for
nullable fields.

### Re-evaluation Triggers

- Applications need constructor choice without a factory.
- Header detection becomes a recurring request with reliable rules.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TabularMapBuilder.cs](../../src/Doka.Tabular/Mapping/TabularMapBuilder.cs)
- [NodePlan.cs](../../src/Doka.Tabular/Import/NodePlan.cs)
- [ImportPlan.cs](../../src/Doka.Tabular/Import/ImportPlan.cs)
- [MapConstructionTests.cs](../../tests/Doka.Tabular.Tests/MapConstructionTests.cs)

### Sources

- No external sources; repository evidence only.
