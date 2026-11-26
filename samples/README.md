# Samples

## Prerequisites

- The .NET SDK that `global.json` pins.
- Docker for the samples that need a database. They start MariaDB 12.3 with
  Testcontainers and remove it at the end, or connect to a server of
  `docker/compose.yml` when `DOKA_TABULAR_SAMPLE_SERVER` names its stage, as
  [Database images](../docker/README.md#run-a-database-locally) describes.

Each sample is a console program; Ctrl+C cancels it through its token.

## Example catalog

| Sample | Shows | Needs | Run |
| --- | --- | --- | --- |
| `Doka.Tabular.Samples.Controller` | an ASP.NET Core controller with the common entry point, inner CSV options, a streamed export and a partial upload import | - | `dotnet run --project samples/Doka.Tabular.Samples.Controller` |
| `Doka.Tabular.Samples.EntityFrameworkCore` | a check without target, a strict and a partial upsert, an import inside the application's transaction, and an export of stored users | Docker | `dotnet run --project samples/Doka.Tabular.Samples.EntityFrameworkCore` |
| `Doka.Tabular.Samples.Journals` | an import protected against duplicate files, an unresolved import and its resolution, a stored export with retention, and the cleanup | Docker | `dotnet run --project samples/Doka.Tabular.Samples.Journals` |
| `Doka.Tabular.Samples.MinimalApi` | a minimal API with a buffered export decided before the first byte, a streamed export and an upload import | - | `dotnet run --project samples/Doka.Tabular.Samples.MinimalApi` |
| `Doka.Tabular.Samples.MultiSheet` | a formatted workbook with two sheets, and an import of two sheets into two EF Core targets, in one transaction and per sheet | Docker | `dotnet run --project samples/Doka.Tabular.Samples.MultiSheet` |
| `Doka.Tabular.Samples.MultiTarget` | several records of one row written to several targets, with a generated id passed on and a lookup over a two-part key | Docker | `dotnet run --project samples/Doka.Tabular.Samples.MultiTarget` |
| `Doka.Tabular.Samples.NestedMaps` | a nested map included twice, composed values, enums, flags and translations, exported and imported in CSV and Excel | - | `dotnet run --project samples/Doka.Tabular.Samples.NestedMaps` |
| `Doka.Tabular.Samples.Sftp` | import and export through streams that a transfer service of the application opens | - | `dotnet run --project samples/Doka.Tabular.Samples.Sftp` |
