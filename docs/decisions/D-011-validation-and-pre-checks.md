---
id: D-011
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Field rules, cross-field rules, source keys, and the check of a file without a write target"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-011 -- Validation and pre-checks

## Context and Problem Statement

Files must be checked before data is stored, often in a preview step. Rules
apply to single fields and to combinations such as a city that needs a postal
code. Duplicate keys inside a file must be detected. Asynchronous rules per
value would turn an import into many service calls.

## Decision Drivers

- One way to check a file, the same as importing it.
- Rules are typed and synchronous.
- Duplicates in a file reject all occurrences.

## Considered Options

- Synchronous typed rules and import without target
- Separate preflight API with asynchronous rules

## Decision Outcome

Chosen option: "Synchronous typed rules and import without target", because it
keeps one pipeline and predictable rules.

### Consequences

- Good, because `alsoOnExport: true` applies a rule to exports when its fields
  are selected.
- Good, because every cross-field rule whose fields converted and passed their
  field rules runs on import; a violated rule does not hide another one.
- Good, because a source key passed as option rejects repeated keys with
  related locations.
- Bad, because a throwing rule ends the run technically.

### Confirmation

- Performed 2026-11-17: the behavior tests cover field codes, the general
  validation code, throwing rules, export rules with selections, and source
  keys on forward-only streams.

## Pros and Cons of the Options

### Synchronous typed rules and import without target

- Good, because `Validate` per field and over two to eight named fields is
  typed.
- Good, because an import without target returns the valid records as the
  check.
- Bad, because database checks need lookups or write targets.

### Separate preflight API with asynchronous rules

- Good, because rules could call services.
- Bad, because the preflight would duplicate the import pipeline and its
  results could differ from the import.

## More Information

Source keys are created with `map.Key(...)` and passed as import option
`SourceKey`; EF matches supply their keys automatically, see
[D-023](D-023-ef-core-write-targets-insert-upsert-and-lookup.md).

### Re-evaluation Triggers

- Applications need asynchronous rules per value.
- More than eight fields per rule are requested.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TabularMapBuilder.cs](../../src/Doka.Tabular/Mapping/TabularMapBuilder.cs)
- [ImportEngine.cs](../../src/Doka.Tabular/Import/ImportEngine.cs)
- [BehaviorTests.cs](../../tests/Doka.Tabular.Tests/BehaviorTests.cs)

### Sources

- No external sources; repository evidence only.
