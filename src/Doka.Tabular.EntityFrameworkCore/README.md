# Doka.Tabular.EntityFrameworkCore

Writes the records of a [Doka.Tabular](https://www.nuget.org/packages/Doka.Tabular)
import into EF Core entities with insert, upsert and lookup targets, row
savepoints and commit groups, and records imports and exports in journals that
detect a file imported twice. For applications that store imported records
with EF Core on a relational database.

## Installation

The package targets `net10.0` and depends on `Doka.Tabular` and
`Microsoft.EntityFrameworkCore.Relational`, which NuGet installs with it. The
application adds the EF Core provider of its database.

```sh
dotnet add package Doka.Tabular.EntityFrameworkCore
```

## Usage

```csharp
var database = TabularDatabase.ForFactory(contextFactory);

var users = database.PrepareUpsert<UserDto, User>(
    set: context => context.Users,
    match: TabularMatch.For<UserDto, User>(map)
        .Part(dto => dto.PersonnelNumber, user => user.PersonnelNumber)
        .UseUniqueKey()
        .Build(),
    create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
    update: (user, dto) => user.Change(dto.Email, dto.Name));

var csv = new CsvFormat(new CsvFormatOptions());
var report = await csv.ImportAsync(source, map, users, cancellationToken);
Console.WriteLine($"{report.Status}: {report.RowsImported} rows imported");
```

The example imports a CSV stream into the users table: a record with a known
personnel number updates its user, any other record creates one.

## Documentation

- [Repository README](https://github.com/doka-labs/Doka.Tabular/blob/main/README.md)
- [Entity Framework Core](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/entity-framework-core.md)
- [Journals](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/journals.md)
- [Errors and reports](https://github.com/doka-labs/Doka.Tabular/blob/main/docs/guides/errors-and-reports.md)
- [API reference](https://doka-labs.github.io/Doka.Tabular/)

## Feedback and support

Report problems and ideas in the
[issues](https://github.com/doka-labs/Doka.Tabular/issues); the
[support policy](https://github.com/doka-labs/Doka.Tabular/blob/main/SUPPORT.md)
describes the channels and what is maintained.

## License

The package is licensed under `MIT`; see
[LICENSE](https://github.com/doka-labs/Doka.Tabular/blob/main/LICENSE).
