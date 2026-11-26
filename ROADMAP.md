# Roadmap

This roadmap describes intent, not a commitment. Items can change or be dropped; commitments are made only in [Support and qualification](docs/support-and-qualification.md).

## Direction

The first stable version is `1.0.0`, after the release candidates. No further
item is planned yet; a planned item gets its own section here, with its goal
and the version before which it is expected.

## Explicit non-goals

- **Reading XLS and XLSB workbooks:** both are binary formats outside the
  Office Open XML packages that the Excel slice reads with the base class
  library alone
  ([D-019](docs/decisions/D-019-excel-import-formats-and-reading-rules.md)).
- **Reading encrypted workbooks:** Doka.Tabular takes no password and
  rejects encrypted packages as an unsupported format
  ([D-019](docs/decisions/D-019-excel-import-formats-and-reading-rules.md)).
- **Computing formulas:** imports take the stored results, and exports write
  values, so no formula engine is part of the library
  ([D-019](docs/decisions/D-019-excel-import-formats-and-reading-rules.md),
  [D-020](docs/decisions/D-020-excel-export-layout-and-cell-types.md)).
- **Formatting beyond the fixed table layout:** exports offer typed cells with
  a small layout surface, not a general workbook editor
  ([D-020](docs/decisions/D-020-excel-export-layout-and-cell-types.md)).
- **Excel without the core package:** the CSV and Excel slices stay in one
  package with the common entry point, so the entry point needs no package
  cycle ([D-002](docs/decisions/D-002-package-boundaries-format-slices-and-allowed-dependencies.md)).

## Review triggers

- A new major version of .NET or of EF Core.
- A request for one of the non-goals with a use case the current packages
  cannot serve.
- The release of `1.0.0`.
