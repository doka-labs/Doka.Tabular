# Release lines

## Current configuration

The lines are listed in
[Support and qualification](../support-and-qualification.md#framework-release-lines);
`.github/release-policy.json` is their source.

## Normal release

A release of an active line follows
[Release publication](release-publication.md), started on the branch of the
line; the release policy on `main` decides whether the line may publish the
version.

## Prepare and activate a line

1. Create the branch of the line from the commit of the last release it
   maintains, here `v1.4.0`:

   ```sh
   git push origin "$(git rev-parse 'v1.4.0^{commit}')":refs/heads/maintenance/1.x
   ```

2. In a pull request into `main`, add the line to `lines` of
   `.github/release-policy.json` with state `prepared`:

   ```json
   {
     "sourceRef": "refs/heads/maintenance/1.x",
     "role": "maintenance",
     "packageMajor": 1,
     "channel": "stable-and-prerelease",
     "upstreamStage": "ga",
     "state": "prepared"
   }
   ```

   Raise `revision`. `.github/dependabot.yml` must list every active line;
   while it differs, the job `policy / Check` of the pull request
   fails and its job summary shows the expected file under
   `Expected .github/dependabot.yml`. Copy that file into
   `.github/dependabot.yml` and push it to the pull request.

3. Qualify the line without publishing:

   ```sh
   gh workflow run release-candidate.yml --repo doka-labs/Doka.Tabular --ref maintenance/1.x -f version=1.4.1 -f qualification-only=true
   ```

4. In a second pull request into `main`, set the state of the line to
   `active`.

A prepared line can be qualified, only an active line can publish. To end a
line, set its state to `inactive` in the same way.

## Stable status and Latest

GitHub marks a release Latest only when it is a stable version strictly newer
than the current Latest; a pre-release or a maintenance release of a lower
version never becomes Latest. So Latest always shows the newest stable
version, also when an older line publishes later.

## Failure and recovery

### Line rejected by the release policy

**Detect.** `release-policy-check.yml` or the preflight fails with a message
about the line.

**Collect evidence.** The message of the failed job and the policy on `main`:

```sh
gh api repos/doka-labs/Doka.Tabular/contents/.github/release-policy.json --jq .content | base64 --decode
```

**Act.**

1. Correct the entry of the line or the `VersionPrefix` of its branch in a
   pull request.
2. Start the failed run again.

**Verify.** The policy check passes for the line.

## Ongoing qualification and dependency servicing

Dependabot opens updates per active line from `.github/dependabot.yml`; each
update runs the CI of its line. `dependency-audit.yml` audits `main` every
night, and `release-lines.yml` dispatches the same audit on every other
active line at its protected head.

## Primary sources

- [GitHub: Get the latest release](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)
- [GitHub: Managing a branch protection rule](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets)
