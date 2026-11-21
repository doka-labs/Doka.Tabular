---
id: D-029
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How import and export move values between cells and mapped properties, and how trimming and native AOT are handled"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-029 -- Typed pipeline, compiled accessors, and AOT marking

## Context and Problem Statement

The first implementation moved every value through `object` and `string`:
properties were read and set by reflection, converters returned boxed values,
cell texts were new strings, and rules received `object` arguments. Import and
export of a medium file allocated several hundred megabytes, and every value
type boxed at least once per cell. Maps are built once and used for many calls,
so the cost belongs into the build step, not into every row. The library also
has to state honestly what it needs at run time, because applications may trim
or publish native AOT, and a later `net11.0` target must not need a redesign.

## Decision Drivers

- No allocation per cell for value types, and no boxing between cell and
  property.
- Cell text stays a view into reusable buffers instead of a new string.
- The map API stays as it is; mapped types need no attributes and no partial
  declarations.
- Applications that trim or publish native AOT see warnings at build time
  instead of failures at run time.
- No package outside the .NET shared framework.
- The design carries over to an additional `net11.0` target unchanged.

## Considered Options

- Typed pipeline with compiled and cached accessors, marked for trimming and AOT
- Source generator that emits the accessors at compile time
- Keep reflection and `object`, reduce allocations locally

## Decision Outcome

Chosen option: "Typed pipeline with compiled and cached accessors, marked for
trimming and AOT", because it removes the per-cell allocations without
changing the map API, and it keeps the runtime requirements visible.

- Converters are typed (`ValueConverter<T>`), read from
  `ReadOnlySpan<char>`, and write into a reusable `TextBuffer`. Cell texts are
  `ReadOnlyMemory<char>` views that stay valid until the buffer of the row is
  cleared.
- Every mapped member owns a typed slot (`ValueSlot<T>`); a codec closed over
  converter type, base type, and property type moves the value between
  converter, custom conversion, and property without boxing.
- Property paths, record creation, and rules over several members are compiled
  from expression trees once per structure and cached; per call a plan only
  binds the compiled delegates to its slots.
- CSV reading scans buffered spans with `SearchValues`; CSV and Excel writing
  encode spans into reused buffers; Excel reading reuses the row, its cell
  list, and its text buffer, and a string cell that spans a whole shared
  string keeps that string.
- Excel serial numbers become ticks in exact 128-bit integer arithmetic; the
  exact decimal arithmetic remains for values outside that range.
- A factory prepares the fields it reads once in `Build()`
  (`UseFactory(fields => { var id = fields.Of(x => x.Id); return values =>
  new T(values.Get(id)); })`); per record it reads typed slots by position.
  A field that is not mapped is a configuration error of `Build()`, a field
  the call does not import a configuration error before the first row.
- Both libraries set `IsAotCompatible`. Entry points that build maps or plans
  carry `[RequiresDynamicCode]` and `[RequiresUnreferencedCode]`, so a trimmed
  or native AOT application gets the warning at its own call site.

### Consequences

- Good, because import and export allocate a fraction of the earlier amount;
  see Confirmation for the measured values.
- Good, because the public map API did not change, and no attribute or partial
  declaration is needed on mapped types.
- Good, because the analyzers keep every dynamic use marked; a new unmarked use
  fails the build.
- Neutral, because the first use of a map structure compiles its accessors;
  later maps of the same structure reuse the cache.
- Bad, because native AOT applications cannot use the library yet: expression
  trees are interpreted there, and closing generic types over value types at
  run time is not supported. The warnings say so instead of failing silently.
- Neutral, because a factory is written in two steps: it prepares its fields
  once and returns the function that creates each record.

### Confirmation

- Performed 2026-11-18: both libraries build with `IsAotCompatible` and with
  warnings as errors without trimming or AOT warnings.
- Performed 2026-11-18: all suites pass, including PostgreSQL and the MySQL
  and MariaDB matrix.
- Performed 2026-11-18: the reference benchmarks (100,000 rows, 7 columns,
  short job) allocate, before and after the change: CSV export 145.8 MB and
  0.27 MB, CSV import 305.5 MB and 22.7 MB, Excel export 312.0 MB and
  13.4 MB, Excel import 712.5 MB and 84.6 MB. The remaining Excel import
  allocation comes mainly from attribute strings that `XmlReader` creates.
- Performed 2026-11-18: the 128-bit conversion of serial numbers to ticks
  matched the decimal conversion for 400,000 random values over all rounding
  modes and steps it decides; a property test keeps comparing both paths with
  new values on every run.
- Performed 2026-11-18: an import of 100,000 CSV rows into a record with four
  fields allocated 17.7 MB with automatic constructor binding, 181.8 MB with
  the earlier factory that looked values up by property expression per row,
  and 17.7 MB with the prepared factory fields.

## Pros and Cons of the Options

### Typed pipeline with compiled and cached accessors, marked for trimming and AOT

- Good, because it needs no change of the public API and no build step in the
  application.
- Good, because a source generator can later replace the compiled accessors
  behind the same slots and codecs.
- Bad, because it requires dynamic code, so native AOT stays unsupported.

### Source generator that emits the accessors at compile time

- Good, because it would make native AOT and full trimming possible.
- Bad, because mapped types or maps would need declarations the generator can
  see, which changes the API.
- Bad, because it is a separate package with its own build and test effort,
  while no application needs native AOT yet.

### Keep reflection and `object`, reduce allocations locally

- Good, because the change would be small.
- Bad, because boxing and string creation per cell stay structural, and the
  measured allocations stay out of proportion.

## More Information

The slots, codecs, and compiled delegates are internal. The compiled accessors
are cached in static concurrent dictionaries keyed by structure, so maps built
repeatedly with the same members share them.

### Re-evaluation Triggers

- An application needs native AOT or full trimming.
- The `net11.0` target is added and changes the run-time cost of expression
  compilation or generic instantiation.
- Excel import allocation matters enough to replace `XmlReader` for cell
  attributes.

### Decision History

- 2026-11-18: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ConversionContext.cs](../../src/Doka.Tabular/Conversion/ConversionContext.cs)
- [TextBuffer.cs](../../src/Doka.Tabular/Conversion/TextBuffer.cs)
- [TypedValues.cs](../../src/Doka.Tabular/Mapping/TypedValues.cs)
- [TabularFactory.cs](../../src/Doka.Tabular/Mapping/TabularFactory.cs)
- [FieldCodec.cs](../../src/Doka.Tabular/Mapping/FieldCodec.cs)
- [Accessors.cs](../../src/Doka.Tabular/Mapping/Accessors.cs)
- [FieldReader.cs](../../src/Doka.Tabular/Import/FieldReader.cs)
- [FieldFormatter.cs](../../src/Doka.Tabular/Export/FieldFormatter.cs)
- [CsvRecordSource.cs](../../src/Doka.Tabular/Csv/CsvRecordSource.cs)
- [CsvWriter.cs](../../src/Doka.Tabular/Csv/CsvWriter.cs)
- [ExcelSheetWriter.cs](../../src/Doka.Tabular/Excel/ExcelSheetWriter.cs)
- [ExcelSheetSource.Cells.cs](../../src/Doka.Tabular/Excel/ExcelSheetSource.Cells.cs)
- [ExcelDates.cs](../../src/Doka.Tabular/Conversion/ExcelDates.cs)
- [Directory.Build.props](../../src/Directory.Build.props)

### Sources

- [Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) (primary source; retrieved 2026-11-18)
- [Introduction to AOT warnings](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings) (primary source; retrieved 2026-11-18)
- [Prepare .NET libraries for trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming) (primary source; retrieved 2026-11-18)
- [EF Core NativeAOT support and precompiled queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries) (primary source; retrieved 2026-11-18)
