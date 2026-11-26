# Benchmarks

## Run settings

The benchmarks use BenchmarkDotNet with its default job and the memory
diagnoser. Run them in Release from the repository root; the switcher selects
classes and methods with `--filter`:

```sh
dotnet run --project benchmarks/Doka.Tabular.Benchmarks --configuration Release -- --filter '*'
```

Each report adds the profile parameters, the size of the generated input and
the versions of Doka.Tabular, the runtime, EF Core and the SQLite provider.

## Feature families

| Class | Measures |
| --- | --- |
| `FormatImportBenchmarks` | imports of every format profile, CSV, XLSX Transitional, XLSX Strict and XLSM, without a target |
| `FormatExportBenchmarks` | exports of every format profile that Doka.Tabular writes, CSV and XLSX Transitional |
| `StorageBenchmarks` | imports into an EF Core target on SQLite, each into a new database |
| `FileStoreBenchmarks` | buffered exports stored through `ITabularFileStore` in memory |

The data comes from reference profiles with a fixed identifier, version and
seed: standard, wide, text and conversion shapes, one or several sheets, and a
share of invalid records.

## Providers and resource ownership

`StorageBenchmarks` uses SQLite through `Microsoft.EntityFrameworkCore.Sqlite`
and creates a new database for every measured import; no server and no
container is needed. `FileStoreBenchmarks` keeps the stored files in memory.

## Measurement boundaries

The results are diagnostic: they depend on the machine, the runtime and the
load, and no build or release fails on them. A profile changes its version
whenever its generated data changes, so results of different versions are not
compared.

## Exports and comparisons

BenchmarkDotNet writes its reports to `BenchmarkDotNet.Artifacts/` in the
working directory, which Git ignores. To compare two states, run the same
filter on both on the same machine and compare the reports with the same
profile versions.
