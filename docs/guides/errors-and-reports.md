# Errors and reports

## Exceptions and reports

`.Build()` throws `TabularConfigurationException` for structural map errors, and
a null required argument throws `ArgumentNullException`. Everything else is
part of the report.

## Strategies

| `Strategy` | Behavior |
| --- | --- |
| `ImportStrategy.Strict` (default) | One error and no record is taken over |
| `ImportStrategy.Partial` | Every valid logical record is taken over |

A parser or technical abort keeps the rows checked before it in a partial
import (EF: the confirmed commit groups); with a source key nothing is kept.

## The error object

| Property | Meaning |
| --- | --- |
| `Code` | Stable `TabularErrorCode`; translate and react by code |
| `Kind` | `Configuration`, `Data`, or `Technical` |
| `Message` | English text without source values |
| `Location` | `RowNumber`, `RecordNumber`, `ColumnIndex`, `ColumnKey`, `Header`, `ExpectedHeader`, `PropertyPath`, `SheetName` |
| `RelatedLocations` | Other occurrences, for example of a duplicate key |
| `SourceValue` | Source text, only for columns with `IncludeSourceValue()` |

`TabularErrorCodes.All` lists the 32 library codes. Own codes are created with
`TabularErrorCode.Create("...")`.

System.Text.Json writes `Code`, `ColumnKey`, `PropertyPath`, and `SheetName` as
strings, so errors can go to a frontend as they are, for example
`{"Code":"tabular.conversion.invalidFormat", ...}`. Reading them back applies
the rules of each type. Enums such as `Kind` follow the serializer options of
the application; without `JsonStringEnumConverter` they are numbers.

## Reports

| Property | Import | Export |
| --- | --- | --- |
| `Status` | Succeeded, CompletedWithErrors, Failed, Canceled | Succeeded, Failed, Canceled |
| `IsComplete` | All selected rows were checked | All sources were enumerated |
| Counters | `RowsRead`, `RowsImported`, `RowsRejected`, `RowsSkipped`, `RowsPendingCommit`, `RowsUncertain` | `RowsRead`, `RowsWritten` |
| `Errors`, `TotalErrorCount`, `OmittedErrorCount` | Limited by `MaxErrorDetails` | Same |
| `FatalError` | The error that ended the run | Same |
| Output | `Items`, `Journal` | `PathOutput`, `StreamOutput`, `FileId` |

Cancellation through the token is the status `Canceled` without an error.
