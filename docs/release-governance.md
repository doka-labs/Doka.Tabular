# Release governance

## Roles

Roles and decision rights follow the
[Doka Labs governance](https://github.com/doka-labs/.github/blob/main/GOVERNANCE.md);
releases are decided and tagged by a maintainer who is an organization
admin.

## Version policy

Versions, pre-releases, breaking changes and deprecation follow
[Versioning](../VERSIONING.md).

## Entry conditions

A release starts only when:

- the version has a dated section `## [X.Y.Z] - YYYY-MM-DD` in
  `CHANGELOG.md`, merged to the head of its release line together with
  `version` and `date-released` in `CITATION.cff`;
- `VersionPrefix` in `src/Directory.Build.props` has the major of the line;
- the last pull request into that head passed `required / Required` and was
  attested;
- the tag of the version, for example `v1.0.0-rc.1`, does not exist and the
  version is not on nuget.org.

## Separation of stages

Qualification, candidate, tag, approval and publication are separate stages
with separate identities: the release workflow qualifies and builds the
candidate without any right to publish and checks it before the tag; a
maintainer checks the release line before the run and signs, verifies and
pushes the tag on their own machine; a reviewer of the `nuget` environment
approves the publication; only the publish job obtains a short-lived nuget.org
credential. No single stage can publish a package that the others did not
check.

## Required evidence

Every release has:

- the TRX results of every test project and engine of the run;
- the release qualification and release line snapshot attestations of the
  qualified commit;
- the build provenance attestation of every package, symbol package and the
  candidate manifest;
- an SPDX 2.2 SBOM per package with its attestation;
- the SSH-signed annotated tag of the version with a key in
  `.github/allowed_signers`;
- the release admission attestation of the publish job;
- the packages read back from nuget.org with their repository signature and
  symbols, and the immutable GitHub release with exactly the candidate assets.

## Failure policy

A release stops at the first failed check and publishes nothing after it. A
candidate that failed before the tag is deleted with its draft release and
built again under the same version. Once the tag is pushed, the version is
taken: a defect found after that is fixed in a new version, and a faulty
version on nuget.org is deprecated or unlisted, never replaced. The
[publication runbook](operations/release-publication.md#failure-and-recovery)
describes each case.

## Release-line ownership

The maintainers create, activate and end release lines through
`.github/release-policy.json` on `main`; the
[release lines runbook](operations/release-lines.md) describes the steps.
Only `main` is a line today.

## Security fixes

A security fix is prepared privately as the
[security policy](../SECURITY.md) describes, released as a patch version of
every line that receives fixes, and named with its advisory under `Security`
in the changelog.

## Post-release review

After a release the maintainer checks that:

- the release is immutable, marked Latest only when it is the newest stable
  version, and lists exactly the candidate assets;
- `nuget.org` shows both packages with README, license and repository link;
- the API reference site shows the released version;
- every step of the run needed no manual repair; a repair becomes a change of
  the documentation or the workflows before the next release.
