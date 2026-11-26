# Deployment and recovery

## Before deployment

- The limits for untrusted input are set as the
  [secure deployment requirements](../security/security-design.md#secure-deployment-requirements)
  list them.
- `TemporaryDirectory` exists, is writable and readable only by the
  application, and has the free space of `MaxTemporaryBytes` per parallel call.
- The migrations of the application contain the journal tables when imports
  or exports use a journal.
- The EF Core contexts of imports use no retrying execution strategy.
- The application logs the events of the logger categories
  `Doka.Tabular.Csv`, `Doka.Tabular.Excel` and `Doka.Tabular.Journal`, so that
  an `OperationId` can be traced.

## Incident evidence

Before any change, save:

- the report of the affected call, especially `Status`, `FatalError`,
  `RowsUncertain`, `Journal` and `Diagnostics.OperationId`;
- the log events and traces with that `OperationId`;
- the import id from `Journal` and the rows of the journal tables for it;
- the name and SHA-256 of the input file, when it can be read again.

## Routing table

| Symptom | Case |
| --- | --- |
| `tabular.persistence.commitUnknown` or `tabular.persistence.rollbackUnknown`, rows in `RowsUncertain` | [Import ended uncertain](#import-ended-uncertain) |
| An import of a file is reported as `Running` although no process imports it | [Import claim left running](#import-claim-left-running) |
| An export log stays `Storing` after the export ended | [Export log left storing](#export-log-left-storing) |
| Files `doka-tabular-*.tmp` stay in the temporary directory or next to an export target | [Temporary files left after a crash](#temporary-files-left-after-a-crash) |
| `tabular.resource.limitExceeded` | [Resource limit reached](#resource-limit-reached) |

## Import ended uncertain

**Detect.** The report has `FatalError.Code`
`tabular.persistence.commitUnknown` or `tabular.persistence.rollbackUnknown`
and rows in `RowsUncertain`.

**Collect evidence.** The [incident evidence](#incident-evidence), with the
import id from `report.Journal`.

**Act.**

1. Discard a context that the application passed to the import.
2. With an import journal, read the state from the commit evidence:

   ```csharp
   var recovery = await journal.RecoverImportAsync(importId, cancellationToken);
   ```

3. For `Imported` or `NoChanges`, nothing more is needed; a new attempt of a
   `NoChanges` import imports the file again.
4. For `Unresolved`, check the data in the database and record the decision:

   ```csharp
   await journal.ResolveImportAsync(importId,
       new ImportResolution { Outcome = ImportResolutionOutcome.NoChanges, Reason = "Rollback confirmed." },
       cancellationToken);
   ```

5. Without a journal, compare the database with the file and remove or
   complete the rows of the uncertain commit groups.

**Verify.** `RecoverImportAsync` returns `Imported` or `NoChanges`, and a new
import of the file gives the expected journal decision.

## Import claim left running

**Detect.** A new import of a file reports the decision `Running`, although
the process of the first import stopped.

**Collect evidence.** The import id and the log events of the first attempt.

**Act.**

1. Confirm that no process still imports the file.
2. Call `RecoverImportAsync` for the import id; committed evidence returns
   `Imported`.
3. Once it is certain that the attempt stopped, end it with
   `ResolveImportAsync` and the outcome the database shows: `Imported` when the
   data was committed, otherwise `NoChanges`.

**Verify.** A new import of the file is `Skipped` or `RejectedDuplicate` after
`Imported`, or `Processed` after `NoChanges`.

## Export log left storing

**Detect.** An export log stays `Storing` although its export ended, after
the journal database failed during the last steps of the export.

**Collect evidence.** The export id and the log events of the export.

**Act.**

1. Confirm that the export ended.
2. Delete it with the unfinished state allowed:

   ```csharp
   await journal.DeleteExportAsync(exportId, store,
       new ExportDeletionOptions { AllowUnfinished = true }, cancellationToken);
   ```

**Verify.** The export no longer appears in `journal.QueryExports(context)`.

## Temporary files left after a crash

**Detect.** Files named `doka-tabular-<32 hexadecimal digits>.tmp` stay in
`TemporaryDirectory` or next to an export target after the process ended
abnormally.

**Collect evidence.** The list of files with their size and time. The command
lists them for the system temporary directory, on Linux and macOS alike; check
the configured `TemporaryDirectory` and the directories of export targets the
same way:

```sh
find "${TMPDIR:-/tmp}" -maxdepth 1 -name 'doka-tabular-*.tmp' -exec ls -l {} +
```

**Act.**

1. Confirm that no process of the application runs any more that started
   before the oldest file.
2. Delete the files.

**Verify.** The command above lists no file older than the running processes.

## Resource limit reached

**Detect.** A call ends with `tabular.resource.limitExceeded`.

**Collect evidence.** The report, the size of the input and the limits of the
format options.

**Act.**

1. Decide whether the input is legitimate.
2. For legitimate input, raise the limit in the format options and deploy.
3. Otherwise reject the input to its sender.

**Verify.** A legitimate input of that size succeeds, and the process keeps
its memory and disk usage within its budget.

## Resume criteria

Normal operation has resumed when calls end with `Succeeded` or with data
errors only, no report shows `RowsUncertain`, no journal entry stays
`Running` or `Storing` without a running process, and the temporary
directory holds no file older than the running processes.
