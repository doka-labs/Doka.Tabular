# Secure development

## Review the boundary that changed

A pull request that changes a trust boundary of the
[security design](security-design.md#trust-boundaries) updates the threats,
controls and assurance cases there and the invariants in
[SECURITY.md](../../SECURITY.md) in the same pull request; the review checks
that the controls still hold. Every pull request needs one approval and
resolved review threads under the ruleset `main` before it can be merged.

## Developer knowledge

The [contributing guide](../../CONTRIBUTING.md) lists the product rules, the
[architecture decisions](../decisions/README.md) record the reasons behind
each contract, and the security design names the threats a change must keep
in mind.

## Static analysis and dependency review

| Check | Where it runs | What it blocks |
| --- | --- | --- |
| .NET analyzers at `latest-Recommended` with warnings as errors, and code style in the build (`Directory.Build.props`) | every build, locally and in CI | a build with an analyzer warning |
| Code style (`code-style.yml`) | `.github/workflows/ci.yml` | the required check `required / Required` |
| CodeQL default setup for C# and GitHub Actions | GitHub code scanning on pull requests and weekly | a merge with an error or a high or critical security alert (ruleset `main`) |
| Workflow lint: actionlint, zizmor and shellcheck (`workflow-lint.yml`) | `.github/workflows/ci.yml` | the required check `required / Required` |
| Dependency review (`dependency-review.yml`) | `.github/workflows/dependency-review.yml` on pull requests | the required check `dependency-review / Review` for a vulnerable or disallowed dependency |
| NuGet audit of every project for direct and transitive advisories (`dependency-audit.yml`) | every night on `main`, on other active lines through `release-lines.yml` | nothing in regular builds; the audit run fails and shows the advisory |
| Dependabot updates for NuGet, the .NET SDK, GitHub Actions and the database images | `.github/dependabot.yml` | nothing; each update runs the CI of its line |
| Actions pinned by full commit SHA | the Actions policy of the repository | a workflow that uses an action by tag |

## Dynamic analysis evidence

The test projects exercise the readers with malformed and limit-exceeding
inputs: workbooks with a DTD, packages that expand beyond their limit,
reference workbooks of several producers, and property-based tests with
FsCheck that generate new values on every run. The EF Core tests run against
real servers of every engine in containers. The
[test ownership](../support-and-qualification.md#test-ownership) lists the
projects. No fuzzing beyond the property-based tests runs.

## Cryptographic boundary

Doka.Tabular encrypts and decrypts nothing and takes no passwords; encrypted
workbooks are rejected. It uses SHA-256 from the base class library to
identify the bytes of an imported file and the scope of a journal claim; the
hash detects a repeated file, it does not protect secrets. Signing of tags,
packages and attestations happens in the release workflows, not in the
library.

## Reports, advisories, and credentials

- Vulnerabilities are reported privately as the
  [security policy](../../SECURITY.md) describes; private vulnerability
  reporting is enabled for the repository.
- Secret scanning with push protection is enabled for the repository.
- The repository holds no credentials. The release obtains a short-lived
  nuget.org API key through Trusted Publishing in the publish job; the only
  stored secret is the nuget.org user name `NUGET_USER` of the `nuget`
  environment.

## Primary sources

- [GitHub: About code scanning with CodeQL](https://docs.github.com/en/code-security/code-scanning/introduction-to-code-scanning/about-code-scanning-with-codeql)
- [GitHub: About dependency review](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/about-dependency-review)
- [NuGet: Auditing package dependencies](https://learn.microsoft.com/nuget/concepts/auditing-packages)
- [GitHub: About secret scanning](https://docs.github.com/en/code-security/secret-scanning/introduction/about-secret-scanning)
- [NuGet: Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
