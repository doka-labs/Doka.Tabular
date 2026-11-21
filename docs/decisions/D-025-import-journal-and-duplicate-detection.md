---
id: D-025
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Recording imports, detecting duplicate files, and clearing unresolved imports"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-025 -- Import journal and duplicate detection

## Context and Problem Statement

Files are delivered twice, imports crash after partial commits, and two
processes start with the same file. Applications need to know whether a file
was already imported, without their own bookkeeping. The history of failed
attempts must survive the rollback of the data.

## Decision Drivers

- Exactly one process imports a file per scope.
- Evidence of committed data travels with the data transaction.
- Unclear states are never resolved by age alone.
- The journal tables come from the application's migrations.

## Considered Options

- Claims per scope and hash with evidence in the data transaction
- Application-side duplicate checks

## Decision Outcome

Chosen option: "Claims per scope and hash with evidence in the data
transaction", because it makes duplicate protection and crash recovery
reliable and shared.

### Consequences

- Good, because `UseTabularJournal` registers the standard journal with prefix
  `DokaTabular_` without code in `OnModelCreating`, and named journals are
  added with `AddTabularImportJournal`.
- Good, because `RecoverImportAsync` and `ResolveImportAsync` report call
  status separate from `ImportRecoveryState`, and a resolution against
  evidence is a conflict.
- Bad, because `InProgress` is known only for imports running in the same
  process; elsewhere the state is unresolved.

### Confirmation

- Performed 2026-11-17: the journal specification runs on SQLite, PostgreSQL
  18.7, and MariaDB 12.3: renamed duplicates, other scopes, scope case by
  registration, reject behavior, released claims after strict rollback, named
  journals, unknown journals, joined transactions, completion function
  failures, recovery and resolution with and without evidence, and log
  deletion keeping claims.

## Pros and Cons of the Options

### Claims per scope and hash with evidence in the data transaction

- Good, because the database guarantees one claim per journal, scope, and
  hash; the key is a hex digest of the scope, independent of collations.
- Good, because each group commit marks the claim imported inside the data
  transaction.
- Bad, because the model of the data context must contain the journal.

### Application-side duplicate checks

- Good, because no library tables are needed.
- Bad, because concurrent processes and crashes need the same logic in every
  application.

## More Information

Hashes are `Sha256Hash` values stored as 64 lowercase hex characters. The
completion function runs once per started journal operation with its own time
limit. When the limit passes, its token is canceled, but the call still awaits
the function, because it uses the scope of the application; exceeding the
limit is a technical error. A deliberate reimport of the same bytes runs without journal.

### Re-evaluation Triggers

- Applications need journals with a borrowed context.
- Fencing of stale processes becomes necessary.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ImportJournalRun.cs](../../src/Doka.Tabular.EntityFrameworkCore/Journals/ImportJournalRun.cs)
- [TabularJournal.cs](../../src/Doka.Tabular.EntityFrameworkCore/Journals/TabularJournal.cs)
- [JournalModel.cs](../../src/Doka.Tabular.EntityFrameworkCore/Journals/JournalModel.cs)
- [JournalSpecification.cs](../../tests/Doka.Tabular.EntityFrameworkCore.Specification/Cases/JournalSpecification.cs)

### Sources

- No external sources; repository evidence only.
