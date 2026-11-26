# Security policy

Reporting channels, response targets, and disclosure follow the [Doka Labs security policy](https://github.com/doka-labs/.github/blob/main/SECURITY.md). This file adds what applies to Doka.Tabular only.

## System and scope

The scope covers the two packages that `.github/release-policy.json` ships:

- `Doka.Tabular`, which reads and writes CSV and Excel files in the process of
  an application, often with files that users or other systems provide;
- `Doka.Tabular.EntityFrameworkCore`, which writes imported records and
  journal rows into the database of the application through EF Core.

The samples, benchmarks, tests and release workflows of this repository are
not shipped.

## Threat model and trust boundaries

The [security design](docs/security/security-design.md#trust-boundaries)
describes each boundary:

- B1 file input: CSV text and workbook packages, untrusted.
- B2 configuration and functions: maps, options and functions of the
  application.
- B3 writes: entities and journal rows in the database of the application.
- B4 temporary and stored files: spilled buffers and stored exports.
- B5 file output: files the application hands on.
- B6 package supply: the packages on nuget.org.

## Security invariants

1. Reading a workbook never processes a DTD and never resolves an external
   reference ([assurance case](docs/security/security-design.md#no-external-resources-when-reading-a-workbook)).
2. The bytes unpacked from one workbook never exceed `MaxExpandedBytes`
   ([assurance case](docs/security/security-design.md#bounded-expansion-of-a-workbook)).
3. An Excel export writes no formula cell
   ([assurance case](docs/security/security-design.md#no-formulas-in-exported-workbooks)).
4. Error messages and log events contain no values of the file; `SourceValue`
   is set only for released columns
   ([assurance case](docs/security/security-design.md#no-source-values-in-messages-and-logs)).

## Reportable findings and severity context

Severities follow the qualitative scale of
[CVSS](https://www.first.org/cvss/v3.1/specification-document#Qualitative-Severity-Rating-Scale).

| Finding | Severity | Reason |
| --- | --- | --- |
| A workbook makes Doka.Tabular read a local file or a network resource | Critical | breaks invariant 1 and discloses data of the host |
| A workbook expands beyond `MaxExpandedBytes`, or an input exceeds a set limit without `tabular.resource.limitExceeded` | High | breaks invariant 2 or a configured limit; an upload can stop the process |
| An Excel export writes a value as a formula | High | breaks invariant 3; a user who opens the file runs it |
| A message, log event or activity tag contains a value of the file | Medium | breaks invariant 4 and discloses personal or business data |
| An import reports success although rows were not stored, or stores rows of a strict import with errors | Medium | the application acts on a wrong state of its database |
| Doka.Tabular runs a macro or computes a formula of a read workbook | Critical | runs content of an untrusted file |
| A published package differs from the source of its signed tag | Critical | the supply chain no longer proves what applications run |

## Exclusions and ownership

- Formula injection through CSV: CSV has no cell types and the writer keeps
  values unchanged; neutralizing values is the task of the application.
- Limits the application leaves unset: only `MaxExpandedBytes` has a default;
  the application chooses the other limits.
- Values the application logs itself, or releases with `IncludeSourceValue()`.
- Paths and file names of imports and exports, which the application chooses.
- Vulnerabilities of EF Core, the database provider or the database engine;
  they belong to their projects.
- Stored formula results that differ from the formula: Doka.Tabular reads
  the values the workbook stores and computes nothing.

## Limitations and compensating controls

- Most resource limits have no default; the
  [secure deployment requirements](docs/security/security-design.md#secure-deployment-requirements)
  list the values to set for untrusted input.
- Spilled buffers are plain files in the temporary directory; the application
  keeps that directory private to its process.
- Without an import journal an uncertain commit cannot be clarified by
  Doka.Tabular; the report states it with
  `tabular.persistence.commitUnknown`.

## Supported versions

Which versions receive security fixes is stated in
[Support and qualification](docs/support-and-qualification.md#maintenance).
