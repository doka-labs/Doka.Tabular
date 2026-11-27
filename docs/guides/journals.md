# Journals

The journals are optional and live in `Doka.Tabular.EntityFrameworkCore`.

## Registration

```csharp
services.AddDbContextFactory<AppDbContext>(options => options
    .UseSqlite(connectionString)
    .UseTabularJournal(journal => journal.TablePrefix = "App_"));
```

The standard journal creates the tables `<prefix>ImportLogs`,
`<prefix>ImportClaims`, and `<prefix>ExportLogs` (default prefix
`DokaTabular_`) through your migrations. Named journals are added in
`OnModelCreating` with `modelBuilder.AddTabularImportJournal(JournalName.Create("users"))`
and `AddTabularExportJournal`. `IgnoreImportScopeCase` compares import scopes
without regard to case; export scopes are not affected.

## Duplicate files

```csharp
var journal = TabularJournal.ForFactory(contextFactory);
var destination = users.WithImportLog(journal, new TabularImportLogOptions
{
    Scope = JournalScope.Create("users"),
    FileName = "users.csv",
    DuplicateBehavior = ImportDuplicateBehavior.Skip,
    OnCompletedAsync = (completed, token) => publisher.PublishAsync(completed, token)
});
var report = await csv.ImportAsync(stream, userMap, destination, cancellationToken);
// report.Journal.Decision: Processed, Skipped, RejectedDuplicate, or Running
```

The completion function gets its own token; after `CompletionTimeout` the
token is canceled, and the call still waits until the function returns.

The SHA-256 hash of the input bytes is claimed per journal and scope; the
database guarantees one claim. The same bytes under another name are a
duplicate; the same bytes in another scope are a new import. Commit evidence is
written in the data transaction, so the model of the data context must contain
the journal.

## Unresolved imports

```csharp
var recovery = await journal.RecoverImportAsync(importId, cancellationToken);
if (recovery.State == ImportRecoveryState.Unresolved)
{
    await journal.ResolveImportAsync(importId,
        new ImportResolution { Outcome = ImportResolutionOutcome.NoChanges, Reason = "Rollback confirmed." },
        cancellationToken);
}
```

Committed evidence proves an import. Without evidence the state is unresolved
until the application decides. A resolution against evidence is a conflict.

## Stored exports

```csharp
var target = TabularFileTarget.To(store, fileName: "users.xlsx")
    .WithRetention(TimeSpan.FromDays(7))
    .WithExportLog(journal, new TabularExportLogOptions { Scope = JournalScope.Create("users") });
var report = await export.ExportAsync(rows, map, target, TabularType.Excel, cancellationToken);
```

`ITabularFileStore` stores the finished file only on full success;
`report.FileId` is set once the store confirmed it. An export is available while
its status is `Ready` and `ExpiresAt` is empty or later than now, which is a
plain query over `journal.QueryExports(context)`.

## Cleanup

Tabular has no scheduler. Call `DeleteExpiredExportsAsync(store, token)`,
`DeleteImportLogsAsync(completedBefore, token)`, and
`DeleteExportLogsAsync(completedBefore, token)` from your own jobs. Deleting
import logs keeps the claims, so files stay duplicates. An import log is deleted once it ended, by its own run or
by `ResolveImportAsync`, and its outcome is settled: the import ended safely, the application resolved it, or its
claim records it as imported or released. `RecoverImportAsync` ends no log, since imported data does not prove that
the import stopped. Once you know that a crashed import with committed data stopped, confirm it with
`ResolveImportAsync` and `ImportResolutionOutcome.Imported`; this ends its log. A log whose outcome is still open stays for `RecoverImportAsync`. Deleting export logs keeps every log
that still refers to a stored file, so `DeleteExportAsync` can delete it later. `DeleteExportAsync` on an
export that is still running returns `Conflict`; repeat it after the export
ended. If the journal database failed during the last steps of an export, its
log can stay `Storing` although the export ended; once you know it ended,
delete it with `new ExportDeletionOptions { AllowUnfinished = true }`.
