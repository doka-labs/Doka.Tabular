---
id: D-008
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Reading and writing DateOnly, TimeOnly, DateTime, DateTimeOffset, and TimeSpan in text and Excel cells"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-008 -- Date and time rules

## Context and Problem Statement

Time values appear as ISO texts, culture texts, and Excel serial numbers in
two date systems. Local times without offset are ambiguous across daylight
saving changes. Excel serial numbers are doubles that cannot hold every tick.
Journal times and expiry rules need a controllable clock for tests.

## Decision Drivers

- No silent shift of time values.
- Results do not depend on the time zone of the machine.
- Excel values read back unchanged after export.
- Time-dependent rules are testable without waiting.

## Considered Options

- Explicit zones and exact values with coarsest read-back
- Machine time zone and nearest tick

## Decision Outcome

Chosen option: "Explicit zones and exact values with coarsest read-back",
because it keeps results independent of the machine and makes Excel round
trips exact.

### Consequences

- Good, because two-digit years are rejected at `.Build()`.
- Good, because `TimeProvider` in the format and journal options controls
  every time the library takes.
- Bad, because a culture date text must reproduce exactly with the short date
  pattern of the culture.

### Confirmation

- Performed 2026-11-17: the time tests cover culture and ISO texts, offsets,
  file zones, daylight saving gaps and overlaps, kinds, rounding, both date
  systems, serial numbers without date format, offsets in Excel, durations,
  and sub-millisecond values.

## Pros and Cons of the Options

### Explicit zones and exact values with coarsest read-back

- Good, because `FileTimeZone`, `ResolveAmbiguousTime`, and `ShiftInvalidTime`
  make every local time explicit.
- Good, because a native serial of a point in time or time of day is read back
  as the coarsest tick value that gives the same double, so written values
  survive; durations use the nearest tick, which their small values already
  read back exactly.
- Bad, because values below the resolution of a serial need text or rounding.

### Machine time zone and nearest tick

- Good, because it needs no configuration.
- Bad, because results differ between machines and normal times do not
  round-trip through Excel.

## More Information

The export always writes the 1900 date system. Durations are written in days
by default or in a unit chosen with `ExcelDuration`; a negative duration in
the default format is an export error.

### Re-evaluation Triggers

- Excel changes the precision of serial numbers.
- Applications need other default zone handling.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [TimeConverterBase.cs](../../src/Doka.Tabular/Conversion/TimeConverterBase.cs)
- [TimestampConverterBase.cs](../../src/Doka.Tabular/Conversion/TimestampConverterBase.cs)
- [DateOnlyConverter.cs](../../src/Doka.Tabular/Conversion/DateOnlyConverter.cs), [TimeOnlyConverter.cs](../../src/Doka.Tabular/Conversion/TimeOnlyConverter.cs), [DateTimeConverter.cs](../../src/Doka.Tabular/Conversion/DateTimeConverter.cs), [DateTimeOffsetConverter.cs](../../src/Doka.Tabular/Conversion/DateTimeOffsetConverter.cs), [TimeSpanConverter.cs](../../src/Doka.Tabular/Conversion/TimeSpanConverter.cs)
- [TimeText.cs](../../src/Doka.Tabular/Conversion/TimeText.cs)
- [ExcelDates.cs](../../src/Doka.Tabular/Conversion/ExcelDates.cs)
- [TimeTests.cs](../../tests/Doka.Tabular.Tests/TimeTests.cs)

### Sources

- No external sources; repository evidence only.
