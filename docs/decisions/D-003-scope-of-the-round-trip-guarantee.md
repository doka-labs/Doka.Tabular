---
id: D-003
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "What an export followed by an import is guaranteed to reproduce"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-003 -- Scope of the round-trip guarantee

## Context and Problem Statement

Applications export records, let users edit the file, and import it again.
Exports can select columns, translate texts, mask values, or contain computed
export-only columns. Users expect that what they exported comes back, but a
selection that omits a column cannot restore that column, and a masked value
cannot be read back.

## Decision Drivers

- The guarantee is testable and easy to explain.
- Column selection and masking stay available for exports.
- The library does not invent identifier handling that applications solve
  differently.

## Considered Options

- Guarantee only for complete exports with the same map
- Guarantee for every export, with a hidden identifier column

## Decision Outcome

Chosen option: "Guarantee only for complete exports with the same map",
because it is exact and testable and leaves identity to the application, which
knows its keys.

### Consequences

- Good, because round-trip tests compare equal records for CSV and Excel.
- Good, because an always visible identifier is a normal map column the
  application adds to its selection.
- Bad, because a filtered export is not a reimport template by itself.

### Confirmation

- Performed 2026-11-17: the conversion, structure, and enum tests export and
  reimport complete maps in CSV and Excel and compare equal records.
- Performed 2026-11-17: selected exports write only the requested keys in the
  requested order.

## Pros and Cons of the Options

### Guarantee only for complete exports with the same map

- Good, because the guarantee is exact: a complete export of a map reimports
  to equal records with that map.
- Bad, because applications that select columns must decide themselves how a
  reimport matches records.

### Guarantee for every export, with a hidden identifier column

- Good, because reimports could always find their records.
- Bad, because the library would have to add columns the application did not
  request and choose identifiers for every entity type.

## More Information

Columns that exist only for the export, such as computed or masked values, are
marked `ExportOnly()` and are ignored on import. Import-only columns are left
empty in a complete export. Selection with column keys is described in
[D-013](D-013-common-entry-point-and-per-call-options.md).

### Re-evaluation Triggers

- Applications need a reimport of selected exports without their own key
  column.
- A feature would hide data in an export file.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ConversionTests.cs](../../tests/Doka.Tabular.Tests/ConversionTests.cs)
- [StructureTests.cs](../../tests/Doka.Tabular.Tests/StructureTests.cs)
- [BehaviorTests.cs](../../tests/Doka.Tabular.Tests/BehaviorTests.cs)

### Sources

- No external sources; repository evidence only.
