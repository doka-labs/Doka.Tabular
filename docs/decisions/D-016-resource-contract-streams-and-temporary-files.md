---
id: D-016
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Buffers, limits, temporary files, and stream ownership"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-016 -- Resource contract, streams and temporary files

## Context and Problem Statement

Inputs come from forward-only streams such as SFTP, and processing sometimes
needs to read again, for hashes, duplicate overviews, or ZIP directories.
Unbounded buffering exhausts memory; ZIP bombs expand without bound.
Applications need predictable limits and cleanup.

## Decision Drivers

- Memory use of buffers is bounded and configurable.
- Every own temporary file is removed on success, failure, and cancellation.
- Limits are exact and reported as technical errors.

## Considered Options

- Configured buffers with spill and per-call limits
- Load everything into memory

## Decision Outcome

Chosen option: "Configured buffers with spill and per-call limits", because it
bounds stored data and cleans up in every case.

### Consequences

- Good, because passed streams stay open and paths are opened and closed by
  the library.
- Good, because path exports publish atomically by rename and leave the
  previous file on failure.
- Bad, because ZIP entry streams flush synchronously on dispose, so the Excel
  writer buffers archive output and passes it on asynchronously.

### Confirmation

- Performed 2026-11-17: the stream and resource tests check stream ownership,
  atomic publication, cancellation before publication, partial output, spill
  and handover, limits at and one above the boundary, and negative limits.
- Performed 2026-11-17: an Excel and a CSV export into a target that rejects
  synchronous writes receive bytes while the source still delivers.

## Pros and Cons of the Options

### Configured buffers with spill and per-call limits

- Good, because import and output buffers of 16 MiB spill to own temporary
  files when allowed.
- Good, because `MaxInputBytes`, `MaxFieldCharacters`, `MaxTemporaryBytes`,
  and `MaxExpandedBytes` (1 GiB) are checked while reading.
- Bad, because DTOs themselves are not bounded.

### Load everything into memory

- Good, because the code is simpler.
- Bad, because large and hostile inputs exhaust memory.

## More Information

A file handed over to the caller leaves the call's cleanup and is deleted when
its stream is disposed. Cleanup failures are reported as own technical errors
without local paths.

### Re-evaluation Triggers

- Applications need a process-wide budget.
- The BCL ZIP writer gains asynchronous entry disposal.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [ResourceLimits.cs](../../src/Doka.Tabular/Resources/ResourceLimits.cs)
- [SpillBuffer.cs](../../src/Doka.Tabular/Resources/SpillBuffer.cs), [PreparedInput.cs](../../src/Doka.Tabular/Resources/PreparedInput.cs), and [CountingReadStream.cs](../../src/Doka.Tabular/Resources/CountingReadStream.cs)
- [ExcelWorkbookWriter.cs](../../src/Doka.Tabular/Excel/ExcelWorkbookWriter.cs) and [AsyncDrainStream.cs](../../src/Doka.Tabular/Excel/AsyncDrainStream.cs)
- [CsvStreamAndResourceTests.cs](../../tests/Doka.Tabular.Csv.Tests/CsvStreamAndResourceTests.cs)

### Sources

- No external sources; repository evidence only.
