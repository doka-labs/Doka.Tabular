# Release process

## Release identity

A release is identified by its version, for example `1.0.0-rc.1`, and by its
SSH-signed annotated tag, for example `v1.0.0-rc.1`, on a commit of a release
line. It consists of the packages `Doka.Tabular` and
`Doka.Tabular.EntityFrameworkCore` with their symbol packages on nuget.org and
the immutable GitHub release of the tag, which holds the same packages, their
SBOMs, the release notes and the candidate manifest.

## Qualification stages

The stages run in this order:

1. **Preflight** (`release-preflight.yml`, called by
   `.github/workflows/release-candidate.yml`): checks that the run is on the
   head of a line that may publish the version, that `CHANGELOG.md` has its
   dated section and that tag and version are unused; resumes with the
   publication when a complete draft exists. Started by `workflow_dispatch`
   with the version.
2. **Tests and pack** (`.github/workflows/tests.yml`,
   `.github/workflows/pack.yml`): run every test project and engine with TRX
   results, and pack both packages with the version. Started by the
   preflight.
3. **Qualification** (`release-qualification.yml`): checks the TRX results of
   every test project, the toolchain of the line and the gates of
   `.github/release-policy.json`, here `required / Required` of the pull
   request that produced the commit, and attests the qualified commit. Started
   after tests and pack.
4. **Candidate as draft release** (`release-candidate.yml` of
   `doka-labs/workflows`): checks identity and source binding of the
   packages and the exact pin of the EF package on the core package, restores
   each in an isolated consumer, verifies the Portable PDBs, generates and
   validates an SBOM per package, attests provenance and SBOMs, and stores
   everything as a draft release. Started after the qualification.
5. **Check before the tag** (`Tag check` job of the same workflow): checks the
   changelog section, the unused tag and version again, and that the draft
   holds this run's candidate and passes the publish verification. Started
   after the SBOM attestations. Before the run, the maintainer has checked
   the release line with `./eng/pre-tag-check.sh`, which runs the same
   checks without the draft, plus the pull request qualification and the
   local signing setup.
6. **Tag**: the maintainer creates the SSH-signed annotated tag of the version
   on the candidate commit in the checkout of the line, verifies it with
   `git tag -v` and pushes only that tag.
7. **Approval**: a reviewer of the `nuget` environment approves the publish
   job, which then rechecks candidate, authority, environment, release
   workflow and the signed tag, and attests the admission.
8. **Publication and readback** (`release-publish` action of
   `doka-labs/workflows`): pushes the packages to nuget.org with Trusted
   Publishing, reads them back signed with their symbols, restores them in an
   isolated consumer, and publishes the immutable GitHub release; afterwards
   `api-reference.yml` publishes the API reference site.

## Attestation

| Subject | Attestation | Created by |
| --- | --- | --- |
| Qualified commit | release qualification, release line snapshot | `release-qualification.yml` |
| Each `.nupkg` and `.snupkg`, and `candidate.json` | build provenance (`https://slsa.dev/provenance/v1`) | `release-candidate.yml` |
| Each `.nupkg` | SBOM (`https://spdx.dev/Document/v2.2`) | `release-candidate.yml` |
| `candidate.json` | release admission | the publish job of `.github/workflows/release-candidate.yml` |

## Publication boundary

Everything up to the draft release is private to the repository and can be
deleted. The pushed tag is the first public and permanent step: the version
cannot be reused. The push to nuget.org is irreversible; before it stands the
approval in the `nuget` environment, and the publish job checks everything
again right before the first push.

## Recovery

- A failed preflight, test, qualification or candidate leaves at most a draft
  release; see
  [Candidate failed or draft incomplete](operations/release-publication.md#candidate-failed-or-draft-incomplete).
- A publish job that ends with the readback pending leaves the packages on
  nuget.org and the release a draft; see
  [Publication waits for the nuget.org readback](operations/release-publication.md#publication-waits-for-the-nugetorg-readback).
- A pushed tag whose publication cannot complete leaves the version taken;
  see [Tag pushed, publication not possible](operations/release-publication.md#tag-pushed-publication-not-possible).
- A published version with a defect stays on nuget.org; see
  [Faulty version on nuget.org](operations/release-publication.md#faulty-version-on-nugetorg).

## Evidence limits

- The attestations prove which workflow built which bytes from which commit,
  not that the code is free of defects.
- The tests prove the behavior of the platform matrix, not of other engines
  or operating systems.
- The SBOM lists the packages that the isolated consumer restored, not
  dependencies an application adds.
- The release admission proves the approval in the environment, not a review
  of the code by a second person.
