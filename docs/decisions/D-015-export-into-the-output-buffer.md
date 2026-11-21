---
id: D-015
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Exports that return the finished file instead of writing into a passed stream"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-015 -- Export into the output buffer

## Context and Problem Statement

Web applications want to answer with an error status and the report instead of
sending half a file. They need the complete file before the first byte is
sent, without writing their own temporary file handling.

## Decision Drivers

- The decision about the response is possible before sending.
- No half files and no leftover temporary files.
- Large files can spill to disk.

## Considered Options

- Buffered result with report and file
- Only exports into passed streams

## Decision Outcome

Chosen option: "Buffered result with report and file", because it lets
applications decide before the first byte.

### Consequences

- Good, because `Results.File` and `File(...)` dispose the stream after
  sending.
- Good, because a failed export returns no stream and leaves no temporary
  file.
- Bad, because streaming directly into a response remains a separate overload.

### Confirmation

- Performed 2026-11-17: the stream tests check a failed buffered export
  without file and temporary file, a spilled file at position 0 that is
  deleted on dispose, and the resource error without spill.
- Performed 2026-11-17: the Minimal API and controller samples return a
  buffered file or 422.

## Pros and Cons of the Options

### Buffered result with report and file

- Good, because `var (report, file) = ...` returns a seekable, read-only
  stream at position 0 on success.
- Good, because the caller owns the stream after the handover; disposing it
  removes a spilled file.
- Bad, because the file is complete before sending starts.

### Only exports into passed streams

- Good, because the API is smaller.
- Bad, because every application would build its own buffering.

## More Information

The output buffer is 16 MiB in memory by default, then a temporary file when
allowed, see [D-016](D-016-resource-contract-streams-and-temporary-files.md).

### Re-evaluation Triggers

- Applications need partial file handover.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ExportEngine.cs](../../src/Doka.Tabular/Export/ExportEngine.cs)
- [SpillBuffer.cs](../../src/Doka.Tabular/Resources/SpillBuffer.cs)
- [CsvStreamAndResourceTests.cs](../../tests/Doka.Tabular.Csv.Tests/CsvStreamAndResourceTests.cs)

### Sources

- No external sources; repository evidence only.
