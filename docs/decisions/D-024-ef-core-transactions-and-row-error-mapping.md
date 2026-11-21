---
id: D-024
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Transaction modes, savepoints, commit groups, and the assignment of database errors to rows"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-024 -- EF Core transactions and row error mapping

## Context and Problem Statement

Imports must be all-or-nothing in strict mode and keep confirmed progress in
partial mode. A database error of one row must not abort the run if the row
can be reset, and the application knows which provider errors are expected.
Applications also import inside their own transaction together with their own
records.

## Decision Drivers

- A rejected row leaves no trace in the database or the change tracker.
- Confirmed groups stay; the open group is rolled back on aborts.
- Uncertain commits are reported, never guessed.
- No context options are changed by the library.

## Considered Options

- Savepoint per row with commit groups and joined transactions
- One SaveChanges per file

## Decision Outcome

Chosen option: "Savepoint per row with commit groups and joined transactions",
because it isolates rows safely and makes every commit outcome explicit.

### Consequences

- Good, because pending changes in a passed context, active transactions
  without join, retrying execution strategies, unsupported isolation levels,
  and missing savepoint support are reported before the first write.
- Good, because every failure of a group commit is uncertain with
  `RowsUncertain`; no provider-independent signal tells a rejected commit from
  an interceptor that fails after the data commit, so the report never claims
  that nothing was written.
- Bad, because a real rejection at commit time, such as a deferred constraint,
  must be clarified as well; with an import journal the commit evidence does it.
- Bad, because on MariaDB with snapshot isolation under `REPEATABLE READ` a
  concurrent change rolls back the whole transaction; the run then ends with
  `tabular.persistence.rollbackUnknown` instead of a row conflict. With
  `IsolationLevel.ReadCommitted` the row conflict applies there as well.

### Confirmation

- Performed 2026-11-17: the import specification runs on SQLite, PostgreSQL
  18.7, MariaDB 12.3, and MySQL 8.4: strict and partial imports, assigned and
  unassigned database errors, parser aborts with and without key, cancellation
  before the last commit, pending context changes, joined transactions with
  commit and with failure, group size with join, and concurrency conflicts;
  the conflict case is skipped visibly on SQLite and checks the MariaDB
  behavior explicitly.
- Performed 2026-11-18: an uncertain commit is reproduced by a failure after
  the physical commit (SQLite test) and by a lost connection during the commit
  (journal sample on MariaDB 12.3); a concurrency conflict under
  `ReadCommitted` is a row conflict on PostgreSQL, MariaDB 10.11 and 12.3, and
  MySQL 8.4.

## Pros and Cons of the Options

### Savepoint per row with commit groups and joined transactions

- Good, because each row is reset to its savepoint, and only the entities of
  the row are detached or restored.
- Good, because `JoinExistingTransaction` writes into the application's
  transaction behind an import savepoint and reports `RowsPendingCommit`.
- Good, because `errors.Database(ex => code)` assigns a row error only when
  the row was reset safely.
- Bad, because providers without savepoints support only partial imports with
  a group size of 1.

### One SaveChanges per file

- Good, because it is fast.
- Bad, because one bad row rolls back everything and the failing row cannot be
  identified.

## More Information

The default group size is 100 valid rows; rejected rows do not count. Groups
never cross a sheet boundary. A set group size with a joined transaction and a
journal with a joined transaction are configuration errors.

### Re-evaluation Triggers

- A provider reports transaction rollback on row errors in other cases.
- Applications need retrying execution strategies.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [WriteSession.cs](../../src/Doka.Tabular.EntityFrameworkCore/Writing/WriteSession.cs)
- [TabularWriteOptions.cs](../../src/Doka.Tabular.EntityFrameworkCore/Writing/TabularWriteOptions.cs)
- [ImportSpecification.cs](../../tests/Doka.Tabular.EntityFrameworkCore.Specification/Cases/ImportSpecification.cs)

### Sources

- No external sources; repository evidence only.
