---
id: D-002
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Which packages Tabular ships, how CSV and Excel are organized, and which dependencies they may have"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-002 -- Package boundaries, format slices and allowed dependencies

## Context and Problem Statement

The library reads and writes CSV and Excel and can write imports into EF Core
targets. A common entry point chooses the format per call and needs both
format types and their options. Separate CSV and Excel packages above a shared
core would have to be referenced by that core, which is a cycle. At the same
time the formats should stay separable in the code, and further formats should
be addable without touching the existing ones. Excel support could rely on a
third-party library or be implemented on the base class library.

## Decision Drivers

- No package cycles and a small number of packages to version.
- CSV and Excel stay independent of each other in the code.
- Few third-party dependencies, so the supply chain stays reviewable.
- Exports from EF queries work without an EF reference in the core.

## Considered Options

- Core package with CSV and Excel slices, separate EF package
- Separate packages per format above a core
- Excel through a third-party library

## Decision Outcome

Chosen option: "Core package with CSV and Excel slices, separate EF package",
because it keeps the entry point free of cycles, keeps the formats separable,
and needs no third-party library for the formats.

### Consequences

- Good, because `Doka.Tabular` depends only on
  `Microsoft.Extensions.Logging.Abstractions` and the BCL.
- Good, because `Doka.Tabular.EntityFrameworkCore` depends on
  `Microsoft.EntityFrameworkCore.Relational` and the core only.
- Good, because exports accept `IQueryable<T>` in the core through
  `System.Linq`, so EF queries are exported without an EF reference.
- Bad, because the own XLSX reader and writer have to follow the format
  specification and are maintained in this repository.

### Confirmation

- Performed 2026-11-17: the solution builds; the project files reference only
  the packages named above.
- Performed 2026-11-17: no type in `Csv/` uses a type from `Excel/` and the
  reverse; both use only the shared pipeline.
- Performed 2026-11-17: the EF export specification exports a `DbSet` and a
  filtered query through the core overload on SQLite, PostgreSQL, and MariaDB.
- Performed 2026-11-18: the packed `Doka.Tabular` and
  `Doka.Tabular.EntityFrameworkCore` packages list only the dependencies named
  above, and their assemblies contain no `JetBrains.Annotations` attribute.

## Pros and Cons of the Options

### Core package with CSV and Excel slices, separate EF package

- Good, because the common entry point can reference both formats without a
  cycle.
- Good, because the slices `Csv/` and `Excel/` depend only on the shared
  pipeline, never on each other, and a further format can be added as another
  slice.
- Bad, because an application that needs only CSV still receives the Excel
  code in the same assembly.

### Separate packages per format above a core

- Good, because applications reference only the format they need.
- Bad, because the common entry point in the core would need the format
  packages, which creates a cycle or a fourth entry package.

### Excel through a third-party library

- Good, because reading and writing workbooks would need less own code.
- Bad, because the package would depend on a large external library with its
  own release cycle, license terms, and memory behavior that the resource
  contract cannot bound.

## More Information

The slices live in the namespaces `Doka.Tabular.Csv` and `Doka.Tabular.Excel`
inside the core assembly. The Excel slice uses `ZipArchive` and
`XmlReader`/`XmlWriter` from the BCL; DTDs and external references are
disabled for every XML read. The common entry point is decided in
[D-013](D-013-common-entry-point-and-per-call-options.md).

`JetBrains.Annotations` is a build-only reference (`PrivateAssets="all"`) of
the libraries, the test applications, the specification library, and the
samples. Its attributes are conditional on `JETBRAINS_ANNOTATIONS`, so they are
not compiled into the assemblies, and the packed packages do not list it as a
dependency. `[PublicAPI]` on the public types tells Rider that applications
outside the solution use them; `[UsedImplicitly]` marks types that Tabular or
JSON create through reflection. The declared public API remains the one in
`PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, checked by
`Microsoft.CodeAnalysis.PublicApiAnalyzers`; the annotations do not replace it.

### Re-evaluation Triggers

- A new format with its own large dependency is requested.
- Applications report the assembly size of the unused format as a problem.
- The BCL loses a capability the Excel slice relies on.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-18: `JetBrains.Annotations` added as a build-only reference for
  IDE annotations; the runtime dependencies are unchanged.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [Doka.Tabular.csproj](../../src/Doka.Tabular/Doka.Tabular.csproj)
- [Csv slice](../../src/Doka.Tabular/Csv/CsvFormat.cs)
- [Excel slice](../../src/Doka.Tabular/Excel/ExcelFormat.cs)
- [Doka.Tabular.EntityFrameworkCore.csproj](../../src/Doka.Tabular.EntityFrameworkCore/Doka.Tabular.EntityFrameworkCore.csproj)
- [ExportSpecification.cs](../../tests/Doka.Tabular.EntityFrameworkCore.Specification/Cases/ExportSpecification.cs)

### Sources

- [JetBrains.Annotations 2026.2.0 on NuGet, MIT license](https://www.nuget.org/packages/JetBrains.Annotations) (primary source; retrieved 2026-11-18)
- [Rider code annotation attributes](https://www.jetbrains.com/help/rider/Reference__Code_Annotation_Attributes.html) (primary source; retrieved 2026-11-18)
- [Public API analyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/src/PublicApiAnalyzers/PublicApiAnalyzers.Help.md) (primary source; retrieved 2026-11-18)
