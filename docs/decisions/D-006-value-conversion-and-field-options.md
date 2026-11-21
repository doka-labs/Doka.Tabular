---
id: D-006
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Built-in conversions, custom conversions, and the per-field options for text and numbers"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-006 -- Value conversion and field options

## Context and Problem Statement

Cells and fields contain text, numbers, dates, booleans, and identifiers.
Silent rounding or truncation would change data unnoticed. Applications have
own value types such as customer numbers, and Excel may hold a number where
the application expects text, such as a postal code without leading zeros.

## Decision Drivers

- No silent loss of value or precision.
- Custom types reuse the built-in rules instead of reading raw cells.
- Text fields keep their content exactly, including leading zeros and
  whitespace.
- Options are typed per value type.

## Considered Options

- Exact built-in conversion with a base-type converter
- Raw cell converters

## Decision Outcome

Chosen option: "Exact built-in conversion with a base-type converter", because
it keeps one set of conversion rules and still supports own types.

### Consequences

- Good, because `PadLeft` restores leading zeros of text fields read from
  number cells.
- Good, because masking is available on export-only columns and rejected
  elsewhere at `.Build()`.
- Good, because negative zero becomes zero in both directions.
- Bad, because an empty text field is the empty string, also for `string?`,
  unless `EmptyStringAsNull()` is set.

### Confirmation

- Performed 2026-11-17: the conversion tests cover integer ranges, cultures,
  decimal points and exponents, decimal precision with rounding, doubles,
  native Excel numbers, text from number cells, empty values, null texts, Guid
  formats, runes, custom conversions, and booleans.

## Pros and Cons of the Options

### Exact built-in conversion with a base-type converter

- Good, because `Convert<TBase, TCustom>` reuses all rules of the base type,
  including empty values and Excel cell types.
- Good, because precision loss without explicit rounding is a field error.
- Bad, because raw cell access for unusual cases is not available.

### Raw cell converters

- Good, because every case could be handled by the application.
- Bad, because every converter would repeat empty-value, culture, and
  cell-type handling, and the error contract would differ per converter.

## More Information

`EmptyStringAsNull()` applies to nullable text fields only, because null
contradicts a non-nullable `string`. `PadLeft` applies to `string` and
`string?`. Native integer types need an explicit conversion and are rejected
at `.Build()`.

### Re-evaluation Triggers

- Applications need raw cell values for a recurring case.
- A new BCL numeric type should be supported.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [NumberRules.cs](../../src/Doka.Tabular/Conversion/NumberRules.cs), [IntegerConverter.cs](../../src/Doka.Tabular/Conversion/IntegerConverter.cs), [BigIntegerConverter.cs](../../src/Doka.Tabular/Conversion/BigIntegerConverter.cs), [DecimalConverter.cs](../../src/Doka.Tabular/Conversion/DecimalConverter.cs), and [FloatingConverter.cs](../../src/Doka.Tabular/Conversion/FloatingConverter.cs)
- [BasicConverters.cs](../../src/Doka.Tabular/Conversion/BasicConverters.cs)
- The column settings per value kind in `src/Doka.Tabular/Mapping/Tabular*ColumnExtensions*.cs`, for example [TabularNumberColumnExtensions.cs](../../src/Doka.Tabular/Mapping/TabularNumberColumnExtensions.cs) and [TabularTimeColumnExtensions.cs](../../src/Doka.Tabular/Mapping/TabularTimeColumnExtensions.cs)
- [ConversionTests.cs](../../tests/Doka.Tabular.Tests/ConversionTests.cs)

### Sources

- No external sources; repository evidence only.
