---
id: D-028
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Which own value types System.Text.Json reads and writes as strings"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-028 -- JSON conversion of own value types

## Context and Problem Statement

Column keys, error codes, property paths, and sheet names are own types without
an implicit conversion from `string`. Two cases cross a JSON boundary.
Applications let users choose the exported columns in a frontend that sends a
JSON array of strings, and a request type with `IReadOnlyList<ColumnKey>` did
not deserialize; an empty or padded text then threw `ArgumentException` in
application code. Applications also return import errors to the uploader, and
without a converter the serializer writes each own value as an object with a
`Value` property instead of the text.

## Decision Drivers

- Own types stay own types instead of `string`.
- A request type carries the selection without a conversion step.
- Errors reach a frontend with their codes, keys, paths, and sheet names as
  plain strings.
- Invalid input fails where input is read, not as an unexpected exception.
- No package outside the .NET shared framework, and no converter without a
  consumer.

## Considered Options

- Converters for the value types that cross a JSON boundary
- Converters for every own value type
- A TryCreate method for conversion in the application

## Decision Outcome

Chosen option: "Converters for the value types that cross a JSON boundary",
because column selections and error reports are the known JSON cases, and
both are covered without converters that nothing uses.

`ColumnKey`, `TabularErrorCode`, `TabularPropertyPath`, and `TabularSheetName`
carry `[JsonConverter]` with a public converter each. `ColumnKey` is also
supported as a dictionary key, as used by `ExcelLayoutOptions.Columns`.

### Consequences

- Good, because a converter on the type applies to properties, list elements,
  and nested report objects without registration.
- Good, because reading applies the rules of each type: `ColumnKey.Create`,
  `TabularErrorCode.Create` (library codes read back as written),
  `TabularSheetName.Create`, and a non-empty trimmed text for property paths.
  A rejected text and a value that is not a string fail with a `JsonException`
  that names the JSON path; the rejected rule is its inner exception.
- Neutral, because a JSON `null` element reaches a column selection as a null
  entry; the export reports it as a configuration error before writing.
- Neutral, because enums in reports follow the serializer options of the
  application.
- Bad, because other serializers such as Newtonsoft.Json need their own
  conversion.
- Bad, because `ExcelTableName`, `JournalName`, `JournalScope`, and
  `Sha256Hash` serialize as objects until a JSON case for them exists.

### Confirmation

- Performed 2026-11-18: the JSON tests read column keys from strings, keep a
  missing and a null selection, write keys as strings, reject empty, padded,
  and non-string values with their JSON path, round-trip dictionary keys,
  export with a selection read from JSON, and report a null entry without
  writing.
- Performed 2026-11-18: the report value tests round-trip library codes, own
  codes, sheet names, and property paths as strings, reject texts each type
  rejects with their JSON path, and serialize the code and location of a real
  import error as strings.

## Pros and Cons of the Options

### Converters for the value types that cross a JSON boundary

- Good, because the selection binds in the request type, and errors serialize
  as readable strings.
- Good, because each converter type is public, so an application can also
  register it explicitly in its serializer options.
- Bad, because the library carries one piece of serialization support.

### Converters for every own value type

- Good, because every own type serializes the same way.
- Bad, because four converters would have no known consumer.

### A TryCreate method for conversion in the application

- Good, because the library stays free of serialization concerns, and invalid
  text no longer throws.
- Bad, because every application writes the conversion and its error handling,
  and reports still serialize own values as objects.

## More Information

The serializer does not pass a JSON `null` to a converter of a reference type
and returns `null` instead. The shared reading rules live in one internal
helper used by all four converters.

`TryParse` methods are not part of this decision; they have no consumer while
selections arrive in a JSON body.

### Re-evaluation Triggers

- A column selection arrives through the query string or a route instead of a
  JSON body.
- Another own value type crosses a JSON boundary.
- Applications need the conversion with a serializer other than
  System.Text.Json.

### Decision History

- 2026-11-18: Decision recorded with status proposed.
- 2026-11-18: Scope extended from column keys to error codes, property paths,
  and sheet names.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TextValueJson.cs](../../src/Doka.Tabular/Primitives/TextValueJson.cs)
- [ColumnKeyJsonConverter.cs](../../src/Doka.Tabular/Primitives/ColumnKeyJsonConverter.cs)
- [TabularErrorCodeJsonConverter.cs](../../src/Doka.Tabular/Primitives/TabularErrorCodeJsonConverter.cs)
- [TabularPropertyPathJsonConverter.cs](../../src/Doka.Tabular/Primitives/TabularPropertyPathJsonConverter.cs)
- [TabularSheetNameJsonConverter.cs](../../src/Doka.Tabular/Primitives/TabularSheetNameJsonConverter.cs)
- [ExportPlan.cs](../../src/Doka.Tabular/Export/ExportPlan.cs)
- [ColumnKeyJsonTests.cs](../../tests/Doka.Tabular.Tests/ColumnKeyJsonTests.cs)
- [ReportValueJsonTests.cs](../../tests/Doka.Tabular.Tests/ReportValueJsonTests.cs)
- [Maps and values](../guides/maps-and-values.md)
- [Errors and reports](../guides/errors-and-reports.md)

### Sources

- [How to write custom converters for JSON serialization](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to) (primary source; retrieved 2026-11-18)
