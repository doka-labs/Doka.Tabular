---
id: D-009
status: implemented
date: 2026-11-21
decision-makers: ["Dominic Kalkbrenner"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "How headers and value texts are translated per call"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---

# D-009 -- Translation of headers and values

## Context and Problem Statement

Exports are produced in the language of the user, and imports read files in
that language. A map is shared by all languages and calls. The application
owns its translation source; the library should not know languages or
catalogs.

## Decision Drivers

- One map serves every language.
- Missing translations are visible, not silently replaced.
- Parallel calls with different languages do not influence each other.

## Considered Options

- Keys in the map, dictionary per call
- Translation callbacks registered on the map

## Decision Outcome

Chosen option: "Keys in the map, dictionary per call", because it keeps maps
free of application state and makes missing texts visible.

### Consequences

- Good, because only keys of selected fields and occurring values are needed.
- Good, because headers for header-name import are formed exactly as on
  export.
- Bad, because translated value collisions are reported only for the call that
  has them.

### Confirmation

- Performed 2026-11-17: the behavior tests cover translated headers and values
  in CSV and Excel, missing keys before writing and at the row, the fixed-text
  fallback, collisions, and import of translated and fixed texts.

## Pros and Cons of the Options

### Keys in the map, dictionary per call

- Good, because the map stays immutable and each call passes its texts.
- Good, because `MissingTranslation` chooses between an error and the fixed
  text.
- Bad, because applications provide every needed key per call.

### Translation callbacks registered on the map

- Good, because the application provides a function once.
- Bad, because the map would hold application state and calls could not differ
  by language.

## More Information

`tabular.translation.missing` has kind Configuration. A translated boolean or
enum is a text cell in Excel.

### Re-evaluation Triggers

- Applications need plural or format-aware translations.
- A standard localization abstraction becomes the common source.

### Decision History

- 2026-11-17: Decision recorded with status proposed.
- 2026-11-21: Status changed from proposed to accepted.
- 2026-11-21: Status changed from accepted to implemented.

### Implementation References

- [HeaderText in
  ImportSettings.cs](../../src/Doka.Tabular/Import/ImportSettings.cs)
- [BehaviorTests.cs](../../tests/Doka.Tabular.Tests/BehaviorTests.cs)

### Sources

- No external sources; repository evidence only.
