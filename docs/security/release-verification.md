# Verify a release

## Prerequisites

- `git` 2.34 or later, for SSH signature verification, with OpenSSH 8.8 or
  later.
- `gh` 2.81.0 or later, for `gh attestation verify`, `gh release verify` and
  `gh release verify-asset`.
- `dotnet` 10, for `dotnet nuget verify`.

Only public read access is needed; no account or token beyond an anonymous
`gh` is required for a public repository.

## Verify the source identity

The commands read the version from `VERSION`.

```sh
VERSION=1.0.0
git clone https://github.com/doka-labs/Doka.Tabular.git
cd Doka.Tabular
git fetch --tags origin
git show "v$VERSION^{commit}:.github/allowed_signers" > allowed_signers
git -c gpg.ssh.allowedSignersFile=allowed_signers verify-tag "v$VERSION"
```

Expected: `Good "git" signature for 54367198+kdominic89@users.noreply.github.com with ED25519 key SHA256:...`.
A key that is not in the list prints `No principal matched.` and the command
fails.

## Download the release assets

```sh
mkdir release && gh release download "v$VERSION" --repo doka-labs/Doka.Tabular --dir release
```

Expected: both `.nupkg` and `.snupkg` files, their `.spdx.json` SBOMs,
`release-notes.md`, `symbol-probes.json` and `candidate.json`.

## Verify build provenance

| Expectation | Value |
| --- | --- |
| Source repository | `doka-labs/Doka.Tabular` |
| Workflow and builder identity | `doka-labs/workflows/.github/workflows/release-candidate.yml`, called by `.github/workflows/release-candidate.yml` |
| Tag | `v$VERSION` |
| Commit | the commit of the tag |

```sh
for asset in release/*.nupkg release/*.snupkg release/candidate.json; do
  gh attestation verify "$asset" -R doka-labs/Doka.Tabular \
    --signer-workflow doka-labs/workflows/.github/workflows/release-candidate.yml \
    --source-digest "$(git rev-parse "v$VERSION^{commit}")"
done
for package in release/*.nupkg; do
  gh attestation verify "$package" -R doka-labs/Doka.Tabular \
    --signer-workflow doka-labs/workflows/.github/workflows/release-candidate.yml \
    --predicate-type https://spdx.dev/Document/v2.2
done
```

Expected: `Verification succeeded!` for each asset.

## Verify the NuGet packages

```sh
mkdir nuget
for id in Doka.Tabular Doka.Tabular.EntityFrameworkCore; do
  curl --fail --location --output "nuget/$id.$VERSION.nupkg" \
    "https://www.nuget.org/api/v2/package/$id/$VERSION"
  dotnet nuget verify --all "nuget/$id.$VERSION.nupkg"
  unzip -p "nuget/$id.$VERSION.nupkg" "$id.nuspec" | grep '<repository'
done
```

Expected: `Successfully verified package` for each package, and a repository
element with `url="https://github.com/doka-labs/Doka.Tabular"` and
the commit of the tag as `commit`.

## Verify the GitHub release

```sh
gh release verify "v$VERSION" --repo doka-labs/Doka.Tabular
gh release view "v$VERSION" --repo doka-labs/Doka.Tabular --json isImmutable --jq .isImmutable
for asset in release/*; do gh release verify-asset "v$VERSION" "$asset" --repo doka-labs/Doka.Tabular; done
```

Expected: `Release v$VERSION verified!`, `true`, and a verified line for
each asset.

## Accept the release only when

- the tag carries a good signature of a key in `.github/allowed_signers` of
  its commit ([source identity](#verify-the-source-identity));
- every asset was downloaded from the release
  ([download](#download-the-release-assets));
- every package, symbol package and the manifest has provenance from the
  release workflow at the commit of the tag, and every package an SBOM
  ([build provenance](#verify-build-provenance));
- the packages on nuget.org carry a valid repository signature and name the
  repository and commit of the tag ([NuGet packages](#verify-the-nuget-packages));
- the release is immutable and holds exactly the verified assets
  ([GitHub release](#verify-the-github-release)).

## Primary sources

- [GitHub CLI: gh attestation verify](https://cli.github.com/manual/gh_attestation_verify)
- [GitHub CLI: gh release verify](https://cli.github.com/manual/gh_release_verify)
- [NuGet: dotnet nuget verify](https://learn.microsoft.com/dotnet/core/tools/dotnet-nuget-verify)
- [SLSA 1.2: Verifying artifacts](https://slsa.dev/spec/v1.2/verifying-artifacts)
