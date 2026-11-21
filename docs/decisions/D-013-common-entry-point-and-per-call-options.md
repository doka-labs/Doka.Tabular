---
id: D-013
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Prepared format instances, the common entry point, and the precedence of call options"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-013 -- Common entry point and per-call options

## Context and Problem Statement

Applications prepare defaults, limits, logging, and the clock once and then
choose the format per request. Options of a call must be buildable before the
format is known. The library must not register itself in a dependency
container.

## Decision Drivers

- Configuration in one place per format.
- The format is always stated, never detected from names.
- Typed options per direction and format.
- No hidden registration.

## Considered Options

- Prepared instances and a dispatching entry point
- Static facade with global defaults

## Decision Outcome

Chosen option: "Prepared instances and a dispatching entry point", because it
keeps configuration explicit and options typed.

### Consequences

- Good, because precedence is: value in the inner type of the chosen format,
  then the shared value, then the prepared instance.
- Good, because parameters follow the data flow: source, type, map,
  destination, options, token.
- Bad, because applications register the instances themselves.

### Confirmation

- Performed 2026-11-17: the behavior tests check instance defaults, call
  overrides, an unprepared format, an undefined type, content type and
  extension, and the precedence between shared and inner strategies.

## Pros and Cons of the Options

### Prepared instances and a dispatching entry point

- Good, because `new CsvFormat(new CsvFormatOptions { ... })` and `new
  ExcelFormat(...)` hold everything long-lived.
- Good, because `TabularImportOptions` and `TabularExportOptions` carry shared
  settings and an inner part per format.
- Bad, because an unprepared format is only reported by the call.

### Static facade with global defaults

- Good, because no instances are needed.
- Bad, because global state makes tests and multiple configurations hard.

## More Information

`TabularType` has `Csv` and `Excel`; extension properties give `ContentType`
and `FileExtension`. Overloads for `IQueryable<T>`, `IAsyncEnumerable<T>`, and
`IEnumerable<T>` are ordered with `OverloadResolutionPriority`.

### Re-evaluation Triggers

- A third format is added.
- Applications need named configurations per format.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TabularImport.cs](../../src/Doka.Tabular/Formats/TabularImport.cs) and its topic partials `TabularImport.*.cs`
- [TabularExport.cs](../../src/Doka.Tabular/Formats/TabularExport.cs) and its topic partials `TabularExport.*.cs`
- [TabularOptions.cs](../../src/Doka.Tabular/Formats/TabularOptions.cs)
- [TabularType.cs](../../src/Doka.Tabular/Formats/TabularType.cs)

### Sources

- No external sources; repository evidence only.
