# Contributing

General contribution rules are in the [Doka Labs contributing guide](https://github.com/doka-labs/.github/blob/main/CONTRIBUTING.md).
This file adds what applies to Doka.Tabular only.

## Prerequisites

- The .NET SDK that `global.json` pins (10.0.404, without roll-forward).
- Docker, for the database tests, the EF Core samples and the checks that run
  as containers. Testcontainers starts and removes the servers itself.

## Build

```sh
dotnet restore Doka.Tabular.slnx --locked-mode
dotnet build Doka.Tabular.slnx --configuration Release --no-restore
```

The build treats warnings as errors and ends with `0 Warning(s)` and
`0 Error(s)`.

## Tests

Each test project runs in its own process:

```sh
dotnet test --project tests/Doka.Tabular.Tests --configuration Release
```

The MySQL tests run once per engine stage of
`docker/database-images.Dockerfile`, selected with
`DOKA_TABULAR_MYSQL_TARGET`:

```sh
DOKA_TABULAR_MYSQL_TARGET=mysql84 dotnet test --project tests/Doka.Tabular.EntityFrameworkCore.MySql.Tests --configuration Release
```

The [test ownership](docs/support-and-qualification.md#test-ownership) lists
every project, and the
[local verification levels](docs/support-and-qualification.md#local-verification-levels)
list the commands from a quick check to the full run.

## Required engineering gates

The required check `required / Required` judges these jobs of
`.github/workflows/ci.yml`; `dependency-review / Review` is required as well.
The commands reproduce them locally from the repository root.

| Gate | Local command |
| --- | --- |
| Code style | `dotnet format Doka.Tabular.slnx style --severity warn --verify-no-changes --no-restore` and `dotnet format Doka.Tabular.slnx style --diagnostics IDE0005 --severity hidden --verify-no-changes --no-restore` |
| Tests | the commands under [Tests](#tests), one per project and engine |
| Coverage | run every test command with `--results-directory coverage -- --coverlet`, then from a checkout of `doka-labs/workflows`: `python3 -I tools/coverage/coverage.py --reports /path/to/Doka.Tabular/coverage --policy /path/to/Doka.Tabular/.github/coverage-policy.json --release-policy /path/to/Doka.Tabular/.github/release-policy.json` |
| Decision records | `docker run --rm --network none --volume "$PWD:/work:ro" ghcr.io/kdominic89/madr-check@sha256:933d050d1e8d7fa00b5b13191a26c19bf40fcbeb0e2a0c4e7e6384548a993a6d validate --profile doka docs/decisions` |
| Architecture document | after the restore under [Build](#build): `docker run --rm --network none --volume "$PWD:$PWD:ro" --volume "$HOME/.nuget/packages:$HOME/.nuget/packages:ro" --workdir "$PWD" ghcr.io/kdominic89/as-built-dotnet@sha256:67acbabae5a3d0d299c9697d82f4b5fe3108c3bc478a5f78c5e1bdf565759720 check docs/architecture.md`; with `write` instead of `check` and a writable mount of the checkout, it updates the blocks it owns |
| Release policy | from a checkout of `doka-labs/workflows` next to this one: `python3 -E -s -m tools.release_lines check --root ../Doka.Tabular --line refs/heads/main` |
| Workflow lint | `actionlint` and `zizmor --offline --persona=regular .github/workflows` |
| Pack and package check | `dotnet pack src/Doka.Tabular/Doka.Tabular.csproj --configuration Release` and the same for `src/Doka.Tabular.EntityFrameworkCore`; the package check itself runs only in CI |

## Product rules

- The projects under `src/` take no dependency beyond the ones
  [D-002](docs/decisions/D-002-package-boundaries-format-slices-and-allowed-dependencies.md)
  allows: `Microsoft.Extensions.Logging.Abstractions` for `Doka.Tabular` and
  `Microsoft.EntityFrameworkCore.Relational` for
  `Doka.Tabular.EntityFrameworkCore`.
- `Csv/` and `Excel/` never use each other, and neither uses `Formats/`; the
  [architectural invariants](docs/architecture.md#architectural-invariants)
  hold these boundaries, and `as-built check` fails when one breaks.
- A change of the public API updates `PublicAPI.Unshipped.txt` of its project
  in the same pull request.
- A new library error code is added to `TabularErrorCodes.All` and documented
  with `<summary>` (its meaning) and `<remarks>` (trigger and reaction).
- A change of the code that changes a block `as-built` owns in
  `docs/architecture.md` updates the document with `as-built write` in the same
  pull request.
- A decision about a contract is recorded under `docs/decisions/` before the
  code that implements it.
