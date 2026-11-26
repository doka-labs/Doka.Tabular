# Versioning

## Public API

The public API of Doka.Tabular is:

- every element listed in `PublicAPI.Shipped.txt` of `src/Doka.Tabular` and
  `src/Doka.Tabular.EntityFrameworkCore`, with the behavior its XML
  documentation describes;
- the error codes in `TabularErrorCodes`;
- the `EventId`s of the log events, the name of the `ActivitySource`, the
  activity names and their tags;
- the formats Doka.Tabular reads and writes, and the database tables of the
  journals.

Not part of the public API are `internal` types, also those that
`Doka.Tabular` makes visible to `Doka.Tabular.EntityFrameworkCore` with
`InternalsVisibleTo`, and the texts of error messages and log events. An
application that reacts to an error checks its code, not its message.

## Semantic versioning

Versions follow [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html):

- a major version changes the public API incompatibly;
- a minor version adds to the public API compatibly or deprecates an element;
- a patch version fixes behavior that contradicts the documentation, without
  changing the public API.

## Pre-releases

A pre-release carries the suffix `-rc.N`, for example `1.0.0-rc.1`. A
pre-release is not stable: its public API and behavior can change until the
stable version of the same number.

## Breaking changes

Each of these is a breaking change and needs a new major version:

- **Source:** code that compiled no longer compiles, for example a removed
  overload.
- **Binary:** an application built against the earlier version no longer runs
  without a rebuild, for example a changed return type.
- **Behavior:** the same call gives another result, for example a new error
  code for an input that was valid before.

Single changes are classified by the
[.NET rules for library changes](https://learn.microsoft.com/dotnet/core/compatibility/library-change-rules).

## Deprecation

An element is deprecated in a minor version with `ObsoleteAttribute` and a
message that names its replacement, listed under `Deprecated` in the
[changelog](CHANGELOG.md), and removed no earlier than the next major version.

## Public API change detection

`Microsoft.CodeAnalysis.PublicApiAnalyzers` compares every build with
`PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`; a public element missing
from them fails the build, so every change of the public API is visible in a
pull request. Package validation (`EnablePackageValidation`) runs at pack
time; from the first stable version on, it also compares each package with
the last stable version on nuget.org.

## Package versions

`Doka.Tabular` and `Doka.Tabular.EntityFrameworkCore` are released together
with the same version. `Doka.Tabular.EntityFrameworkCore` uses internals of
`Doka.Tabular` and therefore depends on exactly the `Doka.Tabular` version of
the same release, for example `[1.0.0-rc.1]`. The version of the packages
does not follow the version of EF Core; `.github/release-policy.json`
(`toolchain.coupling`) records them as independent.
