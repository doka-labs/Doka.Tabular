---
id: D-012
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "The content and status of the reports returned by every call"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-012 -- Import and export reports

## Context and Problem Statement

Callers need to know whether a run succeeded, how many rows were taken over,
why it ended, and what reached the target. Committed groups, pending rows in
an application transaction, and uncertain commits have to be distinguishable.
Cancellation is a normal outcome, not an error.

## Decision Drivers

- One report shape for every call.
- Counters that do not mix committed, pending, and uncertain rows.
- The cause of an abort is visible even with an error limit of zero.

## Considered Options

- Immutable reports with status, counters, and FatalError
- Exceptions for aborts and a result for data errors

## Decision Outcome

Chosen option: "Immutable reports with status, counters, and FatalError",
because it shows every outcome, including partial ones, in one place.

### Consequences

- Good, because `ImportReport<T>` carries typed `Items` and the journal
  decision.
- Good, because `ExportReport` states `PathOutput` or `StreamOutput` and the
  stored `FileId`.
- Bad, because applications check the status instead of catching exceptions.

### Confirmation

- Performed 2026-11-17: the behavior, stream, EF, and journal tests check
  statuses, counters, error limits, fatal errors, cancellation without errors,
  and output states.

## Pros and Cons of the Options

### Immutable reports with status, counters, and FatalError

- Good, because `ReportStatus` has Succeeded, CompletedWithErrors, Failed, and
  Canceled, plus NotStarted for sheets.
- Good, because `FatalError` names the error that ended the run.
- Bad, because counters need not add up to the rows read.

### Exceptions for aborts and a result for data errors

- Good, because aborts stop the caller immediately.
- Bad, because confirmed commits before an abort would be lost from view.

## More Information

A token cancellation creates no error and no fatal error. A technical error is
never turned into Canceled afterwards.

### Re-evaluation Triggers

- Applications need per-sheet error lists.
- A new output target needs a new output state.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ImportReport.cs](../../src/Doka.Tabular/Reports/ImportReport.cs)
- [ExportReport.cs](../../src/Doka.Tabular/Reports/ExportReport.cs)
- [ReportTypes.cs](../../src/Doka.Tabular/Reports/ReportTypes.cs)

### Sources

- No external sources; repository evidence only.
