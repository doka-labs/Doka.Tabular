---
id: D-005
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How several DTOs per row, included maps, and values from several columns are mapped"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-005 -- Row maps, nested maps and composed values

## Context and Problem Statement

Rows often carry more than one record, such as a user and an address, or
values that span columns, such as an amount in minor units and a currency.
Maps for the parts already exist and should be reused with different positions
and headers. Values that need a conversion between several columns need a
typed form without object casts.

## Decision Drivers

- Built maps are reused without copying configuration.
- Each inclusion has independent positions, keys, and headers.
- Combined values are typed end to end.
- The row is the unit of validation and of writing.

## Considered Options

- Nested maps, RowMap roles, and typed Compose
- Flat maps with custom converters for everything

## Decision Outcome

Chosen option: "Nested maps, RowMap roles, and typed Compose", because it
reuses maps safely and keeps combined values typed.

### Consequences

- Good, because two roles of the same type stay distinct through their typed
  role handles.
- Good, because an empty optional nested record is null and no child property
  is read on export.
- Bad, because a row map can describe overlapping export indexes, which the
  export reports before the first write.

### Confirmation

- Performed 2026-11-17: the structure tests cover offsets and minimum width,
  null nested records, header prefixes in both directions, translated
  prefixes, role identity, reversed role order, shared source columns with
  `ImportOnly()`, and all Compose cases in CSV and Excel.

## Pros and Cons of the Options

### Nested maps, RowMap roles, and typed Compose

- Good, because `.Nested` and `RowMap.Add` take built maps with `StartIndex`,
  `KeyPrefix`, and `HeaderPrefix`.
- Good, because `Compose` checks the number and types of `read` and `write` at
  compile time.
- Bad, because more than four parts need a nested map.

### Flat maps with custom converters for everything

- Good, because there is only one concept.
- Bad, because reuse requires copying columns and combined values need untyped
  converters.

## More Information

An exception in `write` of a composed value ends the export technically with
row and property; an export rule on the property runs before `write`. A
failing part skips `read` and reports only its field errors.

### Re-evaluation Triggers

- Applications need more than four parts with a conversion.
- Row maps need write order that differs from bind order.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [MapFlattening.cs](../../src/Doka.Tabular/Mapping/MapFlattening.cs)
- [RowMap.cs](../../src/Doka.Tabular/Mapping/RowMap.cs)
- [TabularComposeFields.cs](../../src/Doka.Tabular/Mapping/TabularComposeFields.cs)
- [StructureTests.cs](../../tests/Doka.Tabular.Tests/StructureTests.cs)

### Sources

- No external sources; repository evidence only.
