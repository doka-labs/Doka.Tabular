# Support and qualification

## Product baseline

Both packages target `net10.0` (`Directory.Build.props`). Their direct
dependencies, with the lowest version NuGet resolves and no upper bound:

| Package | Dependency | Version range |
| --- | --- | --- |
| `Doka.Tabular` | `Microsoft.Extensions.Logging.Abstractions` | `>= 10.0.15` |
| `Doka.Tabular.EntityFrameworkCore` | `Doka.Tabular` | exactly the version of the same release |
| `Doka.Tabular.EntityFrameworkCore` | `Microsoft.EntityFrameworkCore.Relational` | `>= 10.0.15` |

## Framework release lines

`.github/release-policy.json` is the source of this table.

| Line | Branch | Framework | Versions | Status |
| --- | --- | --- | --- | --- |
| main | `main` | .NET 10, EF Core 10 (generally available) | `1.x`, stable and pre-release | active |

## Platform matrix

| Component | Supported | Tested in CI |
| --- | --- | --- |
| .NET 10 | yes | yes, SDK 10.0.404 on Linux (`ubuntu-26.04`) |
| Operating systems that .NET 10 supports | yes | Linux only |
| EF Core 10 on SQLite (`Microsoft.EntityFrameworkCore.Sqlite` 10.0.15) | yes | yes |
| EF Core 10 on PostgreSQL 18.7 (`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.4) | yes | yes |
| EF Core 10 on MariaDB 10.11, 11.4, 11.8 and 12.3 (`Doka.EntityFrameworkCore.MySql` 10.7.0) | yes | yes, each engine |
| EF Core 10 on MySQL 8.4 and 9.7 (`Doka.EntityFrameworkCore.MySql` 10.7.0) | yes | yes, each engine |
| CSV after RFC 4180, read and write | yes | yes |
| XLSX Transitional, read and write | yes | yes |
| XLSX Strict and XLSM, read | yes | yes |
| XLS, XLSB and encrypted workbooks | no | yes, as rejected inputs |
| Native AOT | no | no |

The exact images of the database engines are pinned in
`docker/database-images.Dockerfile`.

## Maintenance

Only the latest release receives fixes.

## Test ownership

| Area | Test project | Runs in |
| --- | --- | --- |
| Maps, conversion, reports, resources, diagnostics | `tests/Doka.Tabular.Tests` | CI and release, job `Test Core` |
| CSV slice | `tests/Doka.Tabular.Csv.Tests` | CI and release, job `Test Csv` |
| Excel slice and reference workbooks | `tests/Doka.Tabular.Excel.Tests` | CI and release, job `Test Excel` |
| EF Core targets and journals on SQLite | `tests/Doka.Tabular.EntityFrameworkCore.Tests` | CI and release, job `Test EF Core` |
| EF Core targets and journals on PostgreSQL | `tests/Doka.Tabular.EntityFrameworkCore.PostgreSql.Tests` | CI and release, job `Test PostgreSQL` |
| EF Core targets and journals on MariaDB and MySQL | `tests/Doka.Tabular.EntityFrameworkCore.MySql.Tests` | CI and release, one job per engine |
| Shared EF Core cases of the three providers | `tests/Doka.Tabular.EntityFrameworkCore.Specification` | as part of the three EF Core test projects |
| Samples and package README examples | `tests/Doka.Tabular.Samples.Tests` | CI and release, job `Test Samples` |
| Benchmark profiles | `tests/Doka.Tabular.Benchmarks.Tests` | CI and release, job `Test Benchmarks` |

## Required behavioral coverage

Tests cover every public import and export overload; the conversion of every
supported type in both directions; the CSV dialect options; the Excel
formats, sheets, layout and limits, also against reference workbooks; every
library error code; the strict and partial strategies; the resource limits
and the removal of temporary files; the EF Core targets, transactions and
journals on every engine of the platform matrix; and the log events and
activities.

`.github/coverage-policy.json` sets the thresholds that the coverage gate of
every pull request enforces:

| Assembly | Minimum line coverage | Minimum branch coverage |
| --- | --- | --- |
| `Doka.Tabular` | 90 % | 85 % |
| `Doka.Tabular.EntityFrameworkCore` | 92 % | 81 % |

## Performance evidence

`benchmarks/Doka.Tabular.Benchmarks` measures imports and exports of
generated reference profiles in every format, the import into an EF Core
target on SQLite and the export into a file store
([benchmarks](../benchmarks/README.md)). The measurements are diagnostic and
no gate; the tests check the benchmark structure, the profile catalog, that
each profile generates the same input on every run, and that an import of it
returns the expected records and errors.

## Local verification levels

| Level | Command | Covers |
| --- | --- | --- |
| Quick | `dotnet build Doka.Tabular.slnx --configuration Release` | compilation, analyzers, the public API files |
| Core | `dotnet test --project tests/Doka.Tabular.Tests --configuration Release` | maps, conversion, reports, resources |
| Formats | the core command for `tests/Doka.Tabular.Csv.Tests` and `tests/Doka.Tabular.Excel.Tests` | the CSV and Excel slices |
| Database | the core command for each EF Core test project, the MySQL one once per `DOKA_TABULAR_MYSQL_TARGET` | targets and journals on every engine; needs Docker |
| Full | every command of the [required engineering gates](../CONTRIBUTING.md#required-engineering-gates) | what CI checks for a pull request |

## Evidence boundaries

- The tests run on Linux only; Windows and macOS are not exercised.
- The database tests cover the engine versions of the platform matrix, not
  other patch versions or cloud variants of the engines.
- The reference workbooks cover the files that the fixtures list; other
  producers of XLSX can write constructs no test has seen.
- Coverage shows which lines ran, not that every result was checked.
- The benchmarks show the cost on one machine, not on the machine of an
  application.

## Adding or updating support

1. Change the version in `Directory.Packages.props`, or the image tag and
   index digest in `docker/database-images.Dockerfile`.
2. For a new database engine, add its stage to
   `docker/database-images.Dockerfile`, `docker/compose.yml` and the matrix of
   `.github/workflows/tests.yml`.
3. Restore with `dotnet restore Doka.Tabular.slnx` to update the lock files,
   and run the tests of the affected projects.
4. Update the platform matrix of this document and, for a change of the
   dependencies of a package, the product baseline and the changelog.
