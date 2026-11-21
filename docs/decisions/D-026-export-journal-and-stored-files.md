---
id: D-026
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Storing finished exports through an adapter, recording them, retention, and deletion"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-026 -- Export journal and stored files

## Context and Problem Statement

Applications prepare exports on the server for a later download. The file must
be stored only when complete, its availability must be checkable, and expired
files must be removed by the application's own jobs.

## Decision Drivers

- Stored only on full success.
- Availability is a plain query.
- No scheduler in the library.

## Considered Options

- Storage adapter with an optional export log
- Library-managed file storage

## Decision Outcome

Chosen option: "Storage adapter with an optional export log", because it keeps
storage with the application and records exports reliably.

### Consequences

- Good, because `ExportReport.FileId` is set only after the store confirmed
  the file.
- Good, because deleting first makes the export unavailable, then deletes the
  file, then marks the log deleted; an export that is still running is a
  conflict, so it cannot become ready again after its deletion. A log left
  `Storing` by a failing journal database is deleted with
  `ExportDeletionOptions.AllowUnfinished` once the application knows the
  export ended.
- Bad, because there is no reconciliation between store and journal.

### Confirmation

- Performed 2026-11-17: the journal specification checks a stored export with
  retention, availability before and at expiry, a failing and a repeated
  deletion, and storage without journal on three providers; the journals
  sample runs on MariaDB.

## Pros and Cons of the Options

### Storage adapter with an optional export log

- Good, because `ITabularFileStore` has only `SaveAsync` and `DeleteAsync`,
  and `TabularFileTarget` works with and without journal.
- Good, because `WithRetention` sets `ExpiresAt` and
  `DeleteExpiredExportsAsync` retries failed deletions.
- Bad, because a file stored without a successful log update stays and is
  reported, not removed.

### Library-managed file storage

- Good, because applications need no adapter.
- Bad, because storage, permissions, and tenants belong to the application.

## More Information

A retention without export log is a configuration error. The standard journal
uses the prefix `DokaTabular_`.

### Re-evaluation Triggers

- Applications need export deduplication.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ExportJournal.cs](../../src/Doka.Tabular.EntityFrameworkCore/Journals/ExportJournal.cs)
- [FileStore.cs](../../src/Doka.Tabular/Storage/FileStore.cs)
- [JournalsSample.cs](../../samples/Doka.Tabular.Samples.Journals/JournalsSample.cs)

### Sources

- No external sources; repository evidence only.
