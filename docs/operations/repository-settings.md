# Repository settings

## Repository and default branch

| Setting | Value | Reason |
| --- | --- | --- |
| Visibility | public | secret scanning, private vulnerability reporting, dependency review and CodeQL are free only for public repositories |
| Default branch | `main` | the only release line |
| Merge methods | squash only, title and description of the pull request | one commit per pull request on a linear history |
| Delete branch after merge | on | merged branches leave no stale refs |
| Auto-merge, update branch | on | a pull request can wait for its checks and catch up with `main` |
| Wiki, projects, discussions | off | issues and the documentation in the repository are the only channels |
| Issues | on | the channel of the [support guide](../../SUPPORT.md) |

## Code scanning

| Setting | Value | Reason |
| --- | --- | --- |
| CodeQL | default setup for `actions` and `csharp`, query suite `default`, weekly and on pull requests | finds vulnerable code in the library and the workflows |
| Secret scanning | on, with push protection | a pushed credential is blocked or reported |
| Private vulnerability reporting | on | the private channel of the [security policy](../../SECURITY.md) |
| Dependabot alerts and security updates | on | vulnerable dependencies are reported and updated |

## Actions policy

| Setting | Value | Reason |
| --- | --- | --- |
| Allowed actions | GitHub-owned actions, actions and reusable workflows of `doka-labs`, and the listed patterns, among them `NuGet/login@*` | the workflows use only `actions/*`, `doka-labs/workflows` and `NuGet/login` |
| SHA pinning required | on | a workflow cannot run an action by a movable tag |

## Rulesets and required checks

| Setting | Value | Reason |
| --- | --- | --- |
| Ruleset `main` on the default branch | no deletion, no force push, linear history; pull request with one approval, approval of the last push, dismissal of stale approvals, resolved threads, squash only; CodeQL without errors and without high or critical alerts | every change of `main` is reviewed and checked |
| Required checks of `main` | `required / Required` and `dependency-review / Review`, against the current `main` | the gate judges every job of the CI; dependency review runs on every pull request |
| Ruleset `release-tags` on `refs/tags/v*` | no creation, update, deletion or force push | release tags come only from a maintainer |
| Bypass of both rulesets | organization admins, always | the maintainer creates release tags; the push records the bypass |

## NuGet environment

| Setting | Value | Reason |
| --- | --- | --- |
| Environment | `nuget` | `release.environment` of `.github/release-policy.json` and of the Trusted Publishing policy |
| Required reviewers | `kdominic89`, self-review allowed | a publication waits for an explicit approval; the only maintainer approves their own runs |
| Deployment branches | selected branches, exactly `main` | only the release line can publish |
| Environment secret | `NUGET_USER`, the nuget.org profile name | the user name of the Trusted Publishing policy; no API key is stored |

## NuGet trusted publishing

| Setting | Value | Reason |
| --- | --- | --- |
| Policy owner on nuget.org | `doka-labs` | the owner of every Doka Labs package on nuget.org; the first publication creates both packages under it |
| Repository owner | `doka-labs` | the owner in the token of the publish job |
| Repository | `Doka.Tabular` | the repository in the token |
| Workflow file | `release-candidate.yml` | the file that defines the publish job |
| Environment | `nuget` | the environment of the publish job |
| Scopes | publishing new packages and new versions of existing packages | `1.0.0-rc.1` creates both package IDs on nuget.org; every later release adds a version |
| State | active, and still active when the release run starts | a policy can start [temporarily active for 7 days](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) and becomes inactive if nothing is published in that time; the first successful publication makes it permanently active |

## Release settings

| Setting | Value | Reason |
| --- | --- | --- |
| Immutable releases | on, enforced by the organization | a published release cannot change its tag or assets |

## SSH release signer

| Setting | Value | Reason |
| --- | --- | --- |
| `.github/allowed_signers` | the ED25519 key of the maintainer with the principal `54367198+kdominic89@users.noreply.github.com` | `git verify-tag` and the publish job accept only tags signed with a listed key; the release tags carry that address |

## GitHub Pages

| Setting | Value | Reason |
| --- | --- | --- |
| Source | GitHub Actions | `api-reference.yml` deploys the API reference site from the release workflow |
| Environment `github-pages`, deployment branches | selected branches, exactly `main` | the release workflow runs on `main`; no other branch can deploy the site |

## Read-only confirmation

Each command only reads; the output after it is what the settings above give.

```sh
gh api repos/doka-labs/Doka.Tabular --jq '{visibility, default_branch, allow_squash_merge, allow_merge_commit, allow_rebase_merge, delete_branch_on_merge, allow_auto_merge, allow_update_branch, has_wiki, has_projects}'
```

Expected: `"visibility":"public"`, `"default_branch":"main"`,
`"allow_squash_merge":true`, `"allow_merge_commit":false`,
`"allow_rebase_merge":false`, and `true` for the branch deletion, auto-merge
and branch update, `false` for wiki and projects.

```sh
gh api repos/doka-labs/Doka.Tabular --jq '.security_and_analysis | {secret_scanning: .secret_scanning.status, push_protection: .secret_scanning_push_protection.status, dependabot_security_updates: .dependabot_security_updates.status}'
gh api repos/doka-labs/Doka.Tabular/private-vulnerability-reporting --jq .enabled
gh api repos/doka-labs/Doka.Tabular/code-scanning/default-setup --jq '{state, languages}'
```

Expected: `enabled` three times, `true`, and `"state":"configured"` with
`actions` and `csharp`.

```sh
gh api repos/doka-labs/Doka.Tabular/actions/permissions --jq '{allowed_actions, sha_pinning_required}'
gh api repos/doka-labs/Doka.Tabular/rulesets --jq '[.[] | {name, target, enforcement}]'
```

Expected: `"allowed_actions":"selected"`, `"sha_pinning_required":true`, and
the rulesets `main` (branch) and `release-tags` (tag), both `active`.

```sh
gh api repos/doka-labs/Doka.Tabular/environments/nuget --jq '{name, reviewers: [.protection_rules[] | select(.type == "required_reviewers") | .reviewers[].reviewer.login], branch_policy: .deployment_branch_policy}'
gh api repos/doka-labs/Doka.Tabular/environments/nuget/deployment-branch-policies --jq '[.branch_policies[] | {name, type}]'
gh api repos/doka-labs/Doka.Tabular/environments/nuget/secrets --jq '[.secrets[].name]'
```

Expected: the reviewer `kdominic89`, `"custom_branch_policies":true`, the
branch policy `main` of type `branch`, and `["NUGET_USER"]`.

```sh
gh api repos/doka-labs/Doka.Tabular/immutable-releases --jq .
gh api repos/doka-labs/Doka.Tabular/pages --jq .build_type
gh api repos/doka-labs/Doka.Tabular/environments/github-pages/deployment-branch-policies --jq '[.branch_policies[] | {name, type}]'
```

Expected: `{"enabled":true,"enforced_by_owner":true}`, `workflow`, and the
branch policy `main` of type `branch`.

The Trusted Publishing policy is shown on nuget.org under **Trusted
Publishing** of the account, with its scopes and whether it is active or only
temporarily active; it has no public API.

## First-publication checklist

- [x] The rulesets `main` and `release-tags` are active as listed.
- [x] Immutable releases are enforced.
- [x] The environment `nuget` has the reviewer, exactly the branch `main` and
      the secret `NUGET_USER`.
- [x] GitHub Pages uses GitHub Actions as its source, and the environment
      `github-pages` allows exactly the branch `main`.
- [x] The Trusted Publishing policy on nuget.org names `doka-labs`,
      `Doka.Tabular`, `release-candidate.yml` and `nuget`, allows publishing
      new packages, and is active when the release run starts.
- [x] The signing key of the maintainer is listed in `.github/allowed_signers`.
- [x] Every command of the read-only confirmation shows its expected output.
