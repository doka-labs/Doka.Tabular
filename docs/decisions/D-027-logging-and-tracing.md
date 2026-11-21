---
id: D-027
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Log events, activities, and correlation of calls"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-027 -- Logging and tracing

## Context and Problem Statement

Operators correlate imports and exports with logs and traces. Logs must not
contain personal data from files. Logging must never change results, and
without listeners it should cost nothing.

## Decision Drivers

- Stable event ids and scalar properties only.
- No source values, headers, paths, or messages in telemetry.
- A failing logger changes nothing.

## Considered Options

- Fixed events and one activity per call
- Detailed logging per row

## Decision Outcome

Chosen option: "Fixed events and one activity per call", because it gives
correlation and failure signals without data in the logs.

### Consequences

- Good, because `report.Diagnostics.OperationId` appears in every event and
  tag.
- Good, because a missing logger factory is a configuration error of the
  format instance.
- Bad, because metrics need the application's own instrumentation.

### Confirmation

- Performed 2026-11-17: the behavior tests check start and completion events
  with operation id and no source values, failure events, a throwing logger
  without effect, a missing factory, and a single activity with error status.

## Pros and Cons of the Options

### Fixed events and one activity per call

- Good, because `OperationStarted`, `OperationCompleted`, `OperationFailed`,
  `PersistenceUncertain`, `CleanupFailed`, `JournalUpdateFailed`, and
  `CompletionFailed` cover the run with ids in `TabularLogEvents`.
- Good, because the `ActivitySource` named `Doka.Tabular` creates
  `tabular.import` or `tabular.export` only with a listener.
- Bad, because there are no events per row or error; the report carries those.

### Detailed logging per row

- Good, because every step would be visible.
- Bad, because logs would grow with the data and risk personal data.

## More Information

Categories are `Doka.Tabular.Csv`, `Doka.Tabular.Excel`,
`Doka.Tabular.EntityFrameworkCore`, and `Doka.Tabular.Journal`.

### Re-evaluation Triggers

- Applications need metrics from the library.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TabularDiagnostics.cs](../../src/Doka.Tabular/Diagnostics/TabularDiagnostics.cs)
- [BehaviorTests.cs](../../tests/Doka.Tabular.Tests/BehaviorTests.cs)

### Sources

- No external sources; repository evidence only.
