# Logging and tracing

## Logging

Pass a logger factory in the format options:

```csharp
new CsvFormatOptions { Diagnostics = new TabularDiagnosticsOptions { LoggerFactory = loggerFactory } }
```

The default is `NullLoggerFactory.Instance`; `null` is a configuration error.

| Event | Id | Level |
| --- | --- | --- |
| `OperationStarted` | 1000 | Debug |
| `OperationCompleted` | 1001 | Information, Warning with data errors, Error with technical errors |
| `OperationFailed` | 1002 | Error |
| `PersistenceUncertain` | 1200 | Error |
| `CleanupFailed` | 1300 | Error |
| `JournalUpdateFailed` | 1400 | Error |
| `CompletionFailed` | 1401 | Error |

Events carry `OperationId`, direction, and format, and the completion event the
status and counters. They never contain source values, headers, paths, or
messages. A throwing logger does not change processing.

## Tracing

The `ActivitySource` named `TabularDiagnostics.ActivitySourceName`
(`Doka.Tabular`) creates one `tabular.import` or `tabular.export` activity per
call when a listener is registered. Tags use the prefix `doka.tabular.`.

## Correlation

`report.Diagnostics.OperationId` is the same identifier as in the log events
and activity tags.
