# Entity Framework Core

## Targets

A database description creates targets; preparing does not touch the database.

```csharp
var database = TabularDatabase.ForFactory(contextFactory); // or TabularDatabase.ForContext(context)

var match = TabularMatch.For<UserDto, User>(userMap)
    .Part(dto => dto.PersonnelNumber, user => user.PersonnelNumber)
    .UseUniqueKey()
    .Build();

var users = database.PrepareUpsert<UserDto, User>(
    set: context => context.Users,
    match: match,
    create: dto => User.Create(dto.PersonnelNumber, dto.Email),
    update: (user, dto) => user.ChangeEmail(dto.Email),
    errors: errors => errors
        .Expect<InvalidUserException>(TabularErrorCode.Create("user.invalid"))
        .Database(exception => IsUniqueEmailViolation(exception) ? TabularErrorCode.Create("user.duplicateEmail") : null));

var report = await csv.ImportAsync(stream, userMap, users, cancellationToken);
```

| Target | Behavior |
| --- | --- |
| `PrepareInsert` | Inserts; with a match, existing records are skipped and counted in `RowsSkipped` |
| `PrepareUpsert` | `create` for new records, `update` for existing ones |
| `PrepareLookup` | Reads only; no match is `tabular.lookup.notFound` |

A match covers the primary key, or with `UseUniqueKey()` a unique key or
unfiltered unique index. It also supplies the source key: repeated keys in one
sheet reject all occurrences.

The database finds the candidates with its own equality, that is the column
collation; the comparer of a pair then checks them. A comparer can therefore
narrow the match, for example `Ordinal` on a case-insensitive collation, but
not widen it: `OrdinalIgnoreCase` on a binary collation does not find `ABC` for
`abc`. Use a collation that compares as the match should.

## Several targets per row

```csharp
var userId = users.Value(userRole, user => user.Id);
var addresses = database.PrepareUpsert<AddressDto, Address>(
    set: context => context.Addresses,
    match: TabularMatch.For<AddressDto, Address>(addressMap)
        .Part(dto => dto.PersonnelNumber, address => address.UserId, valueFrom: userId).UseUniqueKey().Build(),
    create: (dto, values) => Address.Create(values.Get(userId), dto.City),
    update: (address, dto) => address.Move(dto.City));

var destination = TabularDestination.For(rowMap)
    .Bind(userRole, users)
    .Bind(addressRole, addresses)
    .Build()
    .WithOptions(new TabularWriteOptions { ReturnItems = true });
```

Bind order is write order. A new entity is saved inside the transaction before
its value is read. If one target of a row fails, the whole row is reset.

## Transactions

| Mode | Behavior |
| --- | --- |
| Strict | One transaction; any error rolls back everything |
| Partial | Commit groups of `CommitGroupSize` valid rows (default 100) |
| `JoinExistingTransaction` | Writes into the passed context's transaction behind a savepoint; Tabular never commits |

Every row runs in a savepoint and is reset on a row error, including its
change-tracker state. Before the first write the import checks pending changes
of a passed context, active transactions, savepoint support, retrying execution
strategies, and the isolation level.

Every failure of a group commit leaves the outcome unknown: the rows are counted
in `RowsUncertain` and the run ends with `tabular.persistence.commitUnknown`.
From outside, a rejected commit looks the same as an interceptor that fails after
the data was committed. With an import journal, `RecoverImportAsync` clarifies
the state from the commit evidence. After a failed import with a passed
context, discard that context.

On MariaDB with `innodb_snapshot_isolation` enabled (the default since 11.6.2),
writing a row that another process changed after the snapshot of a
`REPEATABLE READ` transaction rolls back the whole transaction; the run then
ends with `tabular.persistence.rollbackUnknown` instead of a row conflict. Set
`TabularWriteOptions.IsolationLevel = IsolationLevel.ReadCommitted`, or turn the
variable off, to get the row conflict there as on the other engines.

## Exports from queries

Pass EF queries directly; they run asynchronously:

```csharp
var query = context.Users.AsNoTracking().OrderBy(user => user.PersonnelNumber)
    .Select(user => new UserRow(user.PersonnelNumber, user.Email));
var report = await csv.ExportAsync(query, rowMap, path, cancellationToken);
```
