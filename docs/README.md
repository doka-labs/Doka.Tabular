# Documentation

This documentation describes how to use Doka.Tabular, what it supports and
guarantees, and how it is released and secured; the
[API reference](https://doka-labs.github.io/Doka.Tabular/) describes every
public type, member and error code.

## Tutorials

| Document | Purpose |
| --- | --- |
| [Getting started](guides/getting-started.md) | Create the formats, describe the columns, and run a first import and export. |

## How-to guides

| Document | Purpose |
| --- | --- |
| [Maps and values](guides/maps-and-values.md) | Map columns, create records, and convert values, enums, dates and translations. |
| [CSV](guides/csv.md) | Read and write CSV with the dialect, header and record options. |
| [Excel](guides/excel.md) | Read and write workbooks, choose sheets, and lay out exports. |
| [Entity Framework Core](guides/entity-framework-core.md) | Write imports into EF Core targets with matches and transactions, and export queries. |
| [Journals](guides/journals.md) | Detect repeated files, recover uncertain imports, and store exports. |
| [Errors and reports](guides/errors-and-reports.md) | Choose a strategy and react to errors and report fields. |
| [Resources and limits](guides/resources-and-limits.md) | Set buffers, temporary files and limits, and pass streams and paths. |
| [Logging and tracing](guides/logging-and-tracing.md) | Connect log events and activities and correlate a call. |
| [Web and file transfer](guides/web-and-file-transfer.md) | Serve downloads, accept uploads, and transfer files over SFTP. |
| [Troubleshooting](troubleshooting.md) | Find the cause and fix of a configuration or technical error. |
| [Deployment and recovery](runbooks/deployment-and-recovery.md) | Prepare a deployment and recover from uncertain imports and left-over state. |
| [Release lines](operations/release-lines.md) | Prepare, activate and service a release line. |
| [Release publication](operations/release-publication.md) | Publish a version and recover from a failed publication. |
| [Verify a release](security/release-verification.md) | Check the signature, provenance and packages of a published release. |

## Reference

| Document | Purpose |
| --- | --- |
| [Support and qualification](support-and-qualification.md) | Supported platforms, release lines, maintenance and what the tests cover. |
| [Limitations](limitations.md) | Unsupported features and known limits by responsibility. |
| [Repository settings](operations/repository-settings.md) | The settings of the repository, the environment, Pages and nuget.org. |

## Explanation

| Document | Purpose |
| --- | --- |
| [Architecture](architecture.md) | How the packages and their areas work together. |
| [Release governance](release-governance.md) | The rules that every release follows. |
| [Release process](release-process.md) | The stages, attestations and guarantees of a release. |
| [Security design](security/security-design.md) | Trust boundaries, threats, controls and assurance cases. |
| [Secure development](security/secure-development.md) | The security checks of development and review. |
| [Architecture decisions](decisions/README.md) | The decisions behind the contracts. |
