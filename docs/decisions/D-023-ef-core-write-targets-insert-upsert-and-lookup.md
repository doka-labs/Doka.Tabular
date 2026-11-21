---
id: D-023
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How imports are written to EF Core entities and how targets of one row share values"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-023 -- EF Core write targets, insert, upsert and lookup

## Context and Problem Statement

Imports create or update entities whose constructors and setters are not
public, match existing rows over business keys, and read reference tables. A
row often writes several entities whose foreign keys come from generated ids
of the same row. Names and types must stay compile-time checked.

## Decision Drivers

- Entity construction stays in the application's domain code.
- Matches are explicit; nothing is derived from names.
- Values between targets are typed and checked at compile time.
- Preparing targets does not touch the database.

## Considered Options

- Typed targets prepared from a database description
- Convention-based mapping from DTO to entity

## Decision Outcome

Chosen option: "Typed targets prepared from a database description", because
it keeps domain construction and matching explicit and typed.

### Consequences

- Good, because the match also supplies the source key for duplicate detection
  in the file; lookups do not.
- Good, because `create: (dto, values) => ...` receives values of earlier
  targets, and a new entity is saved inside the transaction before its value
  is read.
- Good, because targets of one row are bound in write order with
  `TabularDestination.For(rowMap).Bind(...)`.
- Bad, because `Value` exists only on the derived target types; a method that
  returns only `TabularWriteTarget<TDto>` cannot offer it.

### Confirmation

- Performed 2026-11-17: the import and multi-target specifications run on
  SQLite, PostgreSQL 18.7, and MariaDB 12.3, and on MySQL 8.4: insert with and
  without match, upsert keeping ids, update exceptions, unassigned exceptions,
  repeated source keys, returned items, generated ids passed to addresses,
  rejected rows as a unit, and lookups.

## Pros and Cons of the Options

### Typed targets prepared from a database description

- Good, because `PrepareInsert`, `PrepareUpsert`, and `PrepareLookup` return
  `TabularWriteTarget<TDto, TEntity>` or `TabularLookupTarget<TDto, TEntity>`,
  and `Value(role, e => e.Id)` is checked by the compiler.
- Good, because `TabularMatch` pairs each DTO field with an entity property
  and covers the primary key or, with `UseUniqueKey()`, a unique key or index.
- Bad, because applications declare each pair.

### Convention-based mapping from DTO to entity

- Good, because less code is needed for simple cases.
- Bad, because matching by names and setting private members bypasses domain
  rules.

## More Information

Comparison of match values uses the pair's comparer, by default type equality
and ordinal text. The database selects the candidates with the column
collation and the comparer checks them, so a comparer narrows the match but
cannot widen it. A database collation is not reproduced; the unique index
remains the final safeguard. Several rows that hit the same record are applied
in file order.

### Re-evaluation Triggers

- Applications need matches computed from several DTO fields.
- EF Core adds bulk APIs that change the per-row unit.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TabularDatabase.cs](../../src/Doka.Tabular.EntityFrameworkCore/TabularDatabase.cs)
- [TabularMatch.cs](../../src/Doka.Tabular.EntityFrameworkCore/Matching/TabularMatch.cs)
- [TabularDestinationBuilder.cs](../../src/Doka.Tabular.EntityFrameworkCore/Writing/TabularDestinationBuilder.cs), [TabularWriteTarget.cs](../../src/Doka.Tabular.EntityFrameworkCore/Writing/TabularWriteTarget.cs), and [TabularLookupTarget.cs](../../src/Doka.Tabular.EntityFrameworkCore/Writing/TabularLookupTarget.cs)
- [TargetDefinition.cs](../../src/Doka.Tabular.EntityFrameworkCore/Writing/TargetDefinition.cs)
- [MultiTargetSpecification.cs](../../tests/Doka.Tabular.EntityFrameworkCore.Specification/Cases/MultiTargetSpecification.cs)

### Sources

- No external sources; repository evidence only.
