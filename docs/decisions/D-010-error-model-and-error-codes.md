---
id: D-010
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "The shape of errors, the stable codes, and what becomes an exception or a report entry"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-010 -- Error model and error codes

## Context and Problem Statement

Applications react to errors programmatically and show them to users in their
language. Error messages must not leak source values or exception texts.
Programming errors in a map are different from problems of a particular file.
Applications also map their own domain exceptions from constructors,
factories, and targets to row errors.

## Decision Drivers

- Stable, machine-readable codes independent of messages.
- No source values, exception texts, or raw rows in messages.
- A clear line between exceptions and report entries.
- Codes are created explicitly, never from implicit strings.

## Considered Options

- One TabularError type with a closed code register
- Exception hierarchy for every problem

## Decision Outcome

Chosen option: "One TabularError type with a closed code register", because it
collects all problems of a run with stable codes and keeps exceptions for
programming errors.

### Consequences

- Good, because `.Build()` throws `TabularConfigurationException` with all
  structural errors in fixed order.
- Good, because `Expect<TException>(code)` turns assigned exceptions into row
  errors and leaves others technical.
- Good, because `TabularErrorCode.Create` is the only way to create a code.
- Bad, because a released source value must be enabled per column with
  `IncludeSourceValue()`.

### Confirmation

- Performed 2026-11-17: the behavior tests check the location fields, released
  source values, the error limit, and the list of 32 codes; factory and
  conversion tests check assigned and unassigned exceptions.

## Pros and Cons of the Options

### One TabularError type with a closed code register

- Good, because every error has `Code`, `Kind`, `Message`, `Location`,
  `RelatedLocations`, and an optional released `SourceValue`.
- Good, because the 32 library codes are listed in `TabularErrorCodes.All`.
- Bad, because applications translate codes themselves.

### Exception hierarchy for every problem

- Good, because .NET developers know exceptions.
- Bad, because data errors of many rows cannot be collected, and a partial
  import would need exception filtering.

## More Information

The strategy is strict by default: one error and nothing is taken over. A
partial import takes over every complete logical record and continues. Error
kinds are Configuration, Data, and Technical.

### Re-evaluation Triggers

- A code needs a different meaning.
- Applications need structured details beyond the location.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TabularError.cs](../../src/Doka.Tabular/Errors/TabularError.cs)
- [TabularErrorCodes.cs](../../src/Doka.Tabular/Errors/TabularErrorCodes.cs)
- [BehaviorTests.cs](../../tests/Doka.Tabular.Tests/BehaviorTests.cs)

### Sources

- No external sources; repository evidence only.
