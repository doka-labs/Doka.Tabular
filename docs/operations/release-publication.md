# Release publication

## Overview

A release takes these steps:

1. Merge a pull request with the changelog section of the version.
2. Update the local `main` and run `./eng/pre-tag-check.sh` with the version.
3. Start the `Release Candidate` workflow on `main` with the version.
4. Wait until every job up to `Tag check` passed and `publish` waits for its
   approval.
5. Create the signed tag on the candidate commit, verify it and push it.
6. Approve the waiting `publish` job; it publishes to nuget.org and then the
   GitHub release.
7. Confirm that the release and both packages are public.

`./eng/pre-tag-check.sh` and the `Tag check` job use the version of
`doka-labs/workflows` that `.github/workflows/release-candidate.yml` of the
checked-out release line pins. No checkout of `doka-labs/workflows` is needed,
also when several release lines pin different versions. The
[Appendix](#appendix-the-same-steps-with-the-github-cli) shows the GitHub
steps with the GitHub CLI.

## One-time configuration

Each item is checked with the command after it.

- Git signs tags with an SSH key and verifies them against a signer file. The
  commands print the signature format, the key Git signs with and the signer
  file:

  ```sh
  git config --get gpg.format
  git config --get user.signingkey
  git config --get gpg.ssh.allowedSignersFile
  ```

  Expected: `ssh`, the path of a `.pub` file, and the path of a file that
  lists the maintainer's key. If a value is missing, set it with
  `git config --global gpg.format ssh`,
  `git config --global user.signingkey ~/.ssh/id_ed25519.pub` and
  `git config --global gpg.ssh.allowedSignersFile ~/.config/git/allowed_signers`,
  with the paths of the maintainer's own files.
- The public key of that signing key is listed in `.github/allowed_signers`.
  The command looks up the key part of the `.pub` file in the list;
  `--path` expands a leading `~` in the configured path:

  ```sh
  grep -F "$(cut -d' ' -f2 "$(git config --get --path user.signingkey)")" .github/allowed_signers
  ```

  Expected: one line with the principal and the key. No line means the key
  must be added to `.github/allowed_signers` in a pull request first.
- `gh` is signed in, because `./eng/pre-tag-check.sh` reads the repository
  through it. The command shows the signed-in account:

  ```sh
  gh auth status
  ```

  Expected: `Logged in to github.com account` and the account of the
  maintainer.
- The `nuget` environment, `NUGET_USER`, GitHub Pages and the nuget.org
  Trusted Publishing policy are set as
  [Repository settings](repository-settings.md) lists them.

## Publication procedure

`release_version` never has a leading `v`; `release_tag` is the same version
with it.

### 1. Merge the release-ready commit

In a pull request into `main`, add the dated section of the version, for
example `## [1.0.0-rc.1] - 2026-11-26`, with its link reference to
`CHANGELOG.md`, and set `version` and `date-released` in `CITATION.cff`.
Merge it on GitHub after `required / Required` passed.

### 2. Check the release line

In the clone of Doka.Tabular, the commands set the version, update `main`,
record the candidate commit and run the check:

```sh
release_version='1.0.0-rc.1'
release_tag="v${release_version}"

git switch main
git pull --rebase
release_commit="$(git rev-parse HEAD)"

./eng/pre-tag-check.sh "${release_version}"
```

`git fetch origin` followed by `git merge --ff-only origin/main` works as
well as `git pull --rebase`.

The check writes nothing. It fetches `doka-labs/workflows` at the commit the
line pins into a temporary directory and checks the changelog section, the
repository, immutable releases, a clean checkout on the current head of
`main`, the tag and the version still unused on GitHub and nuget.org, no other
version tag on the commit, the pull request qualification of the commit, and
the signing setup of [One-time configuration](#one-time-configuration).
Expected: `All 13 checks passed.` A `FAIL` line names its remedy; fix it
before the candidate starts.

### 3. Start the candidate

On GitHub, open **Actions** > **Release Candidate**, choose **Run workflow**,
select the branch `main`, enter `release_version` without `v` as `version`
and start it.

The run checks again what the tag needs, because `main` or nuget.org can have
changed since the local check, builds and tests the candidate and stores it as
a draft release.

### 4. Wait for the candidate

Expected: every job up to `candidate / Tag check` succeeded, and `publish`
waits for its approval. The summary of `candidate / Tag check` shows
`All 7 checks passed.` It checks once more the changelog section, the tag and
the version unused and no other tag on the commit, and that the draft holds
this run's candidate and passes the verification the `publish` job runs. If a
job failed, see
[Candidate failed or draft incomplete](#candidate-failed-or-draft-incomplete).
Do not approve `publish` yet.

### 5. Create the signed identity

Only after every job up to `Tag check` is green, create one signed, annotated
tag on the candidate commit:

```sh
test "$(git rev-parse HEAD)" = "${release_commit}"
test "$(git branch --show-current)" = main
git fetch origin main
git merge-base --is-ancestor "${release_commit}" origin/main
git tag -s "${release_tag}" "${release_commit}" -m "Doka.Tabular ${release_version}"
git tag -v "${release_tag}"
test "$(git rev-list -n 1 "${release_tag}")" = "${release_commit}"
git push origin "refs/tags/${release_tag}"
```

`git tag -v` prints
`Good "git" signature for 54367198+kdominic89@users.noreply.github.com with ED25519 key SHA256:...`.
If it prints `No principal matched` or `no signature found` instead, do not
push: delete the local tag with `git tag -d "${release_tag}"`, fix the
signing setup from [One-time configuration](#one-time-configuration) and
create it again.

Push only that tag; never use `git push --tags`. The push succeeds with the
notice
`Bypassed rule violations for refs/tags/v1.0.0-rc.1: Cannot create ref due to creations being restricted.`;
the ruleset `release-tags` lets only organization admins create tags and
records the bypass. The tag push starts no second workflow. From the push on,
the version is taken; if the publication then cannot complete, see
[Tag pushed, publication not possible](#tag-pushed-publication-not-possible).

### 6. Approve the publication

In the run, choose **Review deployments**, select `nuget` and **Approve and
deploy**. Expected: the `publish` job starts, checks the signed tag again and
publishes. If it ends with the readback pending, see
[Publication waits for the nuget.org readback](#publication-waits-for-the-nugetorg-readback).

### 7. Confirm completion

The first command shows the state and the assets of the release, the second
lets GitHub verify the release attestation:

```sh
gh release view "${release_tag}" --repo doka-labs/Doka.Tabular --json isDraft,isPrerelease,isImmutable,assets --jq '{isDraft, isPrerelease, isImmutable, assets: [.assets[].name]}'
gh release verify "${release_tag}" --repo doka-labs/Doka.Tabular
```

Expected: `isDraft` false, `isImmutable` true, `isPrerelease` true exactly
for a version with a suffix, the candidate assets, and
`Release v1.0.0-rc.1 verified!`. Both packages show the version on
`https://www.nuget.org/packages/Doka.Tabular` and
`https://www.nuget.org/packages/Doka.Tabular.EntityFrameworkCore`, and the
`api-reference` job published the site. Verify the release as a consumer with
[Verify a release](../security/release-verification.md).

## Failure and recovery

The commands use `release_tag` from step 2 and `RUN_ID`, the number at the end
of the run's address on GitHub, for example `RUN_ID=45412041265`.

### Candidate failed or draft incomplete

**Detect.** A job up to `Tag check` failed, or `Tag check` reports the draft as
incomplete or not publishable.

**Collect evidence.** The first command prints the log of the failed jobs, the
second the assets of the draft, if one exists:

```sh
gh run view --repo doka-labs/Doka.Tabular --log-failed "${RUN_ID}"
gh release view "${release_tag}" --repo doka-labs/Doka.Tabular --json isDraft,assets --jq '[.assets[].name]'
```

**Act.**

1. Fix the cause in a pull request into `main`, or, if the cause was outside
   the repository, re-run the failed jobs, the same as **Re-run failed jobs**
   in the run:

   ```sh
   gh run rerun --repo doka-labs/Doka.Tabular --failed "${RUN_ID}"
   ```

2. If the fix changed the commit, delete the draft, which is not yet public:

   ```sh
   gh release delete "${release_tag}" --repo doka-labs/Doka.Tabular --yes
   ```

3. Start the candidate again.

**Verify.** `Tag check` prints `OK` for every check.

### Publication waits for the nuget.org readback

**Detect.** The `publish` job succeeded with the warning that the readback is
pending, and the job summary says
`Published to nuget.org, readback pending`.

**Collect evidence.** The job summary and the package pages on nuget.org.

**Act.**

1. Wait until nuget.org lists both packages of the version.
2. Re-run the `publish` job; it continues at the readback and publishes the
   release. The first command reads the ID of the `publish` job, the second
   re-runs that job only:

   ```sh
   JOB_ID="$(gh run view "${RUN_ID}" --repo doka-labs/Doka.Tabular --json jobs --jq '.jobs[] | select(.name == "publish") | .databaseId')"
   gh run rerun --repo doka-labs/Doka.Tabular --job "${JOB_ID}"
   ```

**Verify.** The steps of [Confirm completion](#7-confirm-completion) give the
expected output.

### Tag pushed, publication not possible

**Detect.** The tag of the version exists, but the publication fails for a
reason that a re-run does not fix, for example a defect found in the
candidate.

**Collect evidence.** The first command shows the tag on GitHub, the second
whether its release is still a draft:

```sh
git ls-remote --tags origin "${release_tag}"
gh release view "${release_tag}" --repo doka-labs/Doka.Tabular --json isDraft
```

**Act.**

1. Leave the tag in place; the version is taken.
2. Fix the cause in a pull request with the next version in `CHANGELOG.md`,
   for example `1.0.0-rc.2`.
3. Publish the next version with this procedure.

**Verify.** The next version passes [Confirm completion](#7-confirm-completion).

### Faulty version on nuget.org

**Detect.** A published version has a defect that applications must not get.

**Collect evidence.** The issue or advisory that describes the defect and the
affected versions.

**Act.**

1. On nuget.org, deprecate both packages of the version with the reason and
   the version to use, or unlist them.
2. Add ` [YANKED]` to the heading of the version in `CHANGELOG.md`.
3. Publish a fixed version with this procedure.

**Verify.** nuget.org shows the version as deprecated or unlisted, and the
fixed version is the newest one.

### Signing key compromised

**Detect.** A signing key in `.github/allowed_signers` is lost or known to
others.

**Collect evidence.** The key fingerprint and the tags it signed. With the
public key of the compromised key in `compromised-key.pub`, the first command
reads its fingerprint, the loop prints every tag whose signature names it:

```sh
fingerprint="$(ssh-keygen -lf compromised-key.pub | cut -d' ' -f2)"
git for-each-ref refs/tags --format='%(refname:short)' | while read -r tag; do
  git -c gpg.ssh.allowedSignersFile=.github/allowed_signers verify-tag "$tag" 2>&1 | grep -q "$fingerprint" && echo "$tag"
done
```

**Act.**

1. Remove the key from `.github/allowed_signers` and add a new one in a pull
   request into every release line.
2. Check each tag the key signed against its immutable release and its
   provenance with [Verify a release](../security/release-verification.md).
3. Report a tag that does not match through the
   [security policy](../../SECURITY.md).

**Verify.** `git verify-tag` accepts a tag signed with the new key, and every
earlier release passes its verification.

### Repository settings lost

**Detect.** A step fails because an environment, ruleset, secret, Pages or the
Trusted Publishing policy is missing or changed.

**Collect evidence.** The output of the commands under
[Read-only confirmation](repository-settings.md#read-only-confirmation).

**Act.**

1. Restore every setting that differs from
   [Repository settings](repository-settings.md).
2. Re-run the failed job.

**Verify.** Every command of the read-only confirmation shows its expected
output.

## Appendix: the same steps with the GitHub CLI

The commands do on the command line what steps 3, 4 and 6 do on GitHub. The
first starts the candidate on `main`, the second lists the latest runs of the
workflow; take the ID of the run just started from its list:

```sh
gh workflow run release-candidate.yml --repo doka-labs/Doka.Tabular --ref main -f version="${release_version}"
gh run list --repo doka-labs/Doka.Tabular --workflow release-candidate.yml --limit 3
```

With that ID in `RUN_ID`, the first command follows the run until it stops or
waits for the approval, the second opens it in the browser, where the summary
of `candidate / Tag check` and **Review deployments** are:

```sh
gh run watch --repo doka-labs/Doka.Tabular "${RUN_ID}"
gh run view --repo doka-labs/Doka.Tabular --web "${RUN_ID}"
```

The commands of [Failure and recovery](#failure-and-recovery) use the same
`RUN_ID`.

## Primary sources

- [GitHub: Reviewing deployments](https://docs.github.com/en/actions/managing-workflow-runs-and-deployments/managing-deployments/reviewing-deployments)
- [GitHub: Immutable releases](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/immutable-releases)
- [Git: Signing with SSH keys](https://git-scm.com/docs/git-config#Documentation/git-config.txt-gpgformat)
- [Git: git verify-tag](https://git-scm.com/docs/git-verify-tag)
- [NuGet: Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
- [NuGet: Deprecating packages](https://learn.microsoft.com/nuget/nuget-org/deprecate-packages)
