# Doka MADR Enterprise Profile 1.0

This corpus records the relevant decisions of Doka.Tabular as Architecture
Decision Records. It uses MADR 4.0.0 together with the complete format and
content contract of Doka MADR Enterprise Profile 1.0 described below. The
profile name and `doka-profile-version` identify the shared document format;
they do not establish product affiliation. The additional metadata, status
rules, and required sections are extensions of the Doka profile; they are not
requirements of the unmodified MADR standard.

Relevant decisions include architecture and responsibilities, public API and
behavioral contracts, mapping and storage layout, provider differences,
transactions and consistency, and algorithms, optimizations, and their
prerequisites. Deliberately accepted limitations and trade-offs involving
maintainability, performance, and memory consumption are relevant as well.
Each record addresses one clearly bounded decision question. Related
provisions may form one record if each statement remains traceable.

Records name the accountable people, the actual approval history, and the
evidence without attributing authorship to an implementation assistant or a
model. Invalid approvals and their retractions remain visible as history.
Authorization to implement is not acceptance of a decision.

The [template](adr-template.md) is the starting point for a new record. The
[README](README.md) introduces this folder and lists no records, so no index is
maintained by hand.

## Files and identifiers

- Files use `D-NNN-lowercase-version-safe-slug.md`. The slug contains only
  ASCII lowercase letters, digits, hyphens, and version dots. The file name,
  the metadata `id`, and the H1 `# D-NNN -- Short decision title` agree.
- IDs start at `D-001`, are unique and contiguous, and are never reassigned or
  renumbered once they are part of the repository history.
- Records use ASCII, LF line endings, and a final newline. Every heading is
  preceded by a blank line.

## Metadata

Each record starts with these thirteen flat YAML keys, in this order:

```yaml
---
id: D-NNN
status: proposed
date: YYYY-MM-DD
decision-makers: ["Full name"]
consulted: []
informed: ["@doka-labs/core-maintainers"]
scope: "Clearly bounded decision question"
supersedes: []
superseded-by: []
amends: []
amended-by: []
madr-version: "4.0.0"
doka-profile-version: "1.0"
---
```

- Every key occurs exactly once. Further keys may follow the thirteen keys.
  They add information and never repeat or change the meaning of a key or
  section of this profile or of MADR 4.0.0. Each of the thirteen keys is
  written on one line. The title appears in the H1, not in the metadata.
- `id`, `status`, `date`, `scope`, `madr-version`, and `doka-profile-version`
  are non-empty scalar values. `madr-version` is `"4.0.0"` and
  `doka-profile-version` is `"1.0"`.
- `date` is a valid calendar date in the form `YYYY-MM-DD`: the date the
  decision was last updated, as in MADR 4.0.0. It equals the date of the last
  `Decision History` entry; the recording date stays in the first entry.
- The participant and relationship keys are single-line lists of unique
  entries. `decision-makers` names the actually accountable people and is
  never empty. `consulted` names actually consulted people and stays empty
  otherwise. `informed` names the intended audience and proves neither
  agreement nor a completed notification. Repositories of doka-labs name
  `"@doka-labs/core-maintainers"`; another project names its own audience or
  leaves the list empty.
- Relationship lists contain only record IDs and may be empty.
- The metadata block is the only source of current metadata. Status, date, and
  scope are not repeated as a labeled block in the body.

## Sections

Every record contains each of these sections exactly once, at the shown level
and in this order. Further sections may be added; they never replace a section
of this profile or change its meaning. Headings inside example code blocks do
not count.

```text
## Context and Problem Statement
## Decision Drivers
## Considered Options
## Decision Outcome
### Consequences
### Confirmation
## Pros and Cons of the Options
### Exact first considered option
### Exact second considered option
## More Information
### Re-evaluation Triggers
### Decision History
### Implementation References
### Sources
```

- Every required section has meaningful content.
- At least two credible options are evaluated with real advantages and
  disadvantages. Each option has exactly one H3 section with the same title, in
  the order of `Considered Options`.
- `Decision Outcome` states `Chosen option: "Exact option title", because ...`
  with a title that matches an entry of `Considered Options`.
- Each option and `Consequences` contain at least one `Good, because ...` and
  one `Bad, because ...` entry. `Neutral, because ...` entries are allowed.
  The entries may stand under headings of the record's own below the section,
  such as one per kind of consequence. Reasons name concrete effects on the
  decision drivers.
- `Confirmation` describes reproducible checks with expected results and the
  relevant negative and boundary cases. Planned checks and checks already
  performed stay distinguishable. For trigger-dependent implementation it is
  recorded whether the observable trigger has occurred; if it has, the
  required confirmation procedure is applied and its result is recorded,
  otherwise the pending state and the later procedure stay visible. Review
  approval alone does not confirm an implementation.
- `Re-evaluation Triggers` names concrete conditions for a new evaluation.
- `Implementation References` links the repository artifacts that actually
  exist. Pending implementation and evidence are stated as pending; paths that
  do not exist are never presented as evidence.
- Implementation details belong in `More Information` or
  `Implementation References`, history belongs in `Decision History`.

## Status

| Status | Meaning |
| --- | --- |
| `proposed` | Proposal under review; not yet binding. |
| `accepted` | Accepted by the accountable people; required implementation confirmation may still be pending. |
| `implemented` | Accepted and confirmed by suitable implementation evidence. |
| `rejected` | Reviewed and declined before acceptance; was never binding. |
| `deprecated` | Still historically relevant but no longer recommended. |
| `superseded` | Replaced by at least one named successor. |

Only these transitions are allowed:

- `proposed -> accepted`
- `proposed -> rejected`
- `accepted -> implemented`
- `accepted -> deprecated`
- `accepted -> superseded`
- `implemented -> deprecated`
- `implemented -> superseded`

Existing code, a compiler result, or a passing test does not grant approval. A
rationale written afterwards for an existing implementation also requires an
evidenced acceptance.

## Decision History

The metadata holds the current state. `Decision History` records the
recording, the evidenced acceptance, status changes, and significant later
changes as dated list entries:

```text
- YYYY-MM-DD: Decision recorded with status proposed.
- YYYY-MM-DD: Status changed from <old> to <new>.
- YYYY-MM-DD: Rationale, review, or another evidenced event.
```

- The first entry reads `Decision recorded with status <status>.` with the
  status the decision had when it was recorded; its date is the recording
  date. The date of the last entry equals `date`.
- All dates are valid and never decrease. Status changes use the shown phrase
  exactly; further reasons may get their own dated entries. The chain of
  status changes is complete and ends at the current metadata status.
- Later changes never overwrite the recording date; they are added as events
  with their own date, and `date` moves to the date of the new last entry.
  Historical dates, approvals, and implementation states are never invented.

## Relationships

- Actual replacements and amendments use the reciprocal relationships
  `supersedes` / `superseded-by` and `amends` / `amended-by`. Every ID resolves
  to an existing record; self-references are not allowed.
- A replacement sets the old record to `superseded` and names its successor in
  `superseded-by`. Every `superseded` record names at least one successor.
- `amends` extends a record that remains valid and does not change its status
  by itself. Both sides of a relationship and their histories are maintained
  together. Content changes get a history entry.
- Ordinary topical references stay plain relative links and never create an
  invented amendment or replacement.

## Sources and references

- External claims are checked against suitable primary sources. External URLs
  appear only under `Sources`, each with title, address, primary-source
  marker, and the actual retrieval date. A link is written inline or by
  reference; both forms may be used in one record:

  ```text
  - [MADR 4.0.0 full template](https://github.com/adr/madr/blob/4.0.0/template/adr-template.md) (primary source; retrieved 2026-11-17)
  - [MADR 4.0.0 full template][madr-template] (primary source; retrieved 2026-11-17)

  [madr-template]: https://github.com/adr/madr/blob/4.0.0/template/adr-template.md
  ```

- A label is defined exactly once and every definition is used. A definition
  that carries an external URL stands under `Sources` and belongs to an entry
  there. Text outside `Sources` may cite a source by its label; the URL itself
  still appears only under `Sources`.
- After the retrieval date an entry may say in words what the source shows
  and what it does not. It names no further URL; a second source gets an
  entry of its own.

- Versioned contracts use correspondingly versioned sources. The retrieval date
  is the date the claim was checked and is distinct from the recording date. A
  source must be authoritative for the specific claim; following the format
  alone does not confirm the claim.
- Records based only on repository evidence use exactly this entry and nothing
  else under `Sources`:

  ```text
  - No external sources; repository evidence only.
  ```

- Local implementation and test evidence appears under
  `Implementation References` as resolvable relative repository links.
- Every record is understandable and verifiable from this repository alone.
  Repository references use relative paths that resolve inside the repository.
  The only external references are publicly reachable primary sources. An
  external URL uses HTTPS, names a host, and carries no credentials.

## Upstream basis

The section structure follows MADR 4.0.0, used under its CC0-1.0 alternative.
The additional rules above are project requirements and are not attributed to
the unmodified upstream template.
