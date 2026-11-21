---
id: D-017
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "The form of cancellation tokens and optional parameters in the public API"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-017 -- Cancellation tokens and optional parameters

## Context and Problem Statement

Forgotten tokens make long imports uncancellable. Optional parameters keep
frequent calls short, such as `Validate(rule, code:)`. The public API analyzer
warns about several overloads with optional parameters because later overloads
can change binding.

## Decision Drivers

- Cancellation is never forgotten.
- Frequent calls stay short and readable.
- Public API changes stay visible in review.

## Considered Options

- Required token last and optional parameters with RS0026 disabled
- Optional tokens and overloads without optional parameters

## Decision Outcome

Chosen option: "Required token last and optional parameters with RS0026
disabled", because it keeps cancellation explicit and the API compact.

### Consequences

- Good, because `CancellationToken cancellationToken` is required and last in
  every asynchronous method.
- Good, because growing groups of options become options objects.
- Bad, because RS0026 is disabled for `src/**/*.cs` in `.editorconfig`; RS0027
  stays active.

### Confirmation

- Performed 2026-11-17: the solution builds with warnings as errors; the
  PublicAPI files list every public member.

## Pros and Cons of the Options

### Required token last and optional parameters with RS0026 disabled

- Good, because callers always pass a token and named optional parameters stay
  available.
- Good, because new overloads remain visible through the PublicAPI files.
- Bad, because a later overload could rebind existing calls; only review
  catches it.

### Optional tokens and overloads without optional parameters

- Good, because the analyzer stays fully active.
- Bad, because tokens are forgotten and the overload count roughly doubles.

## More Information

Optional parameters are used for single settings such as `code:`,
`alsoOnExport:`, `mode:`, `match:`, `errors:`, and `options:`.

### Re-evaluation Triggers

- An overload change rebinds existing calls.
- The analyzer gains a finer rule.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [.editorconfig](../../.editorconfig)
- [PublicAPI.Unshipped.txt](../../src/Doka.Tabular/PublicAPI.Unshipped.txt)
- [PublicAPI.Unshipped.txt
  (EF)](../../src/Doka.Tabular.EntityFrameworkCore/PublicAPI.Unshipped.txt)

### Sources

- No external sources; repository evidence only.
