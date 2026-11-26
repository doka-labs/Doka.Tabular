# Troubleshooting

The sections cover the error codes of the kinds `Configuration` and
`Technical`, whose cause lies in the configuration, the environment or the
operation. Codes of the kind `Data` describe a single value or record and
belong in the report the application shows; the
[API reference](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.html)
describes every code. To follow a single call through logs and traces, use the
[logging and tracing guide](guides/logging-and-tracing.md).

## A call or a map is rejected as invalid

**Symptom.** `Build()` throws `TabularConfigurationException`, or a report has
status `Failed` without a read row and `FatalError.Code` is
[`tabular.configuration.invalid`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Configuration.html).

**Cause.** The map has structural errors, such as duplicate keys or colliding
enum texts, or the options of the call, the format instance, the EF Core
model, the transaction or the journal do not allow the call, for example a
missing logger factory, an unknown column key or a context with a retrying
execution strategy.

**Fix.**

1. Read the errors of the exception or `FatalError.Message`; they name the
   setting.
2. Correct the map or the setting in the code of the application.
3. Repeat the call.

## A translation is missing

**Symptom.** An export or import with `Translations` fails with
[`tabular.translation.missing`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Translation.html).

**Cause.** The dictionary of the call has no entry for a key the map needs,
and `MissingTranslation` is `Error`.

**Fix.**

1. Add the key that the error location names to the dictionary, or
2. set `MissingTranslation` to a fallback such as `UseFixedText`.

## An export exceeds the sheet layout

**Symptom.** An Excel export fails with [`tabular.schema.limitExceeded`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Schema.html) of the
kind `Configuration`.

**Cause.** The start cell and the columns of the layout do not fit into a
worksheet.

**Fix.**

1. Move the start cell, or select fewer columns.

## A lookup finds several entities

**Symptom.** An import ends with [`tabular.lookup.notUnique`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Lookup.html).

**Cause.** The key of the match is declared unique, but the database returns
several entities, for example because the collation of a column compares
case-insensitively while the match does not.

**Fix.**

1. Find the duplicates in the table with the key of the error location.
2. Add a unique index on the key columns, or align the collation with the
   match.

## A function of the application throws

**Symptom.** A call ends with [`tabular.callback.failed`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Callback.html).

**Cause.** A rule, constructor, factory, conversion, comparer, export source
or completion function threw an exception that the map did not assign to a
code, or a completion function exceeded `CompletionTimeout`.

**Fix.**

1. Find the function from the error location and the logs of the application.
2. Fix the function, or assign the expected exception with
   `Expect<TException>(code)`.

## A function of the application returns an invalid result

**Symptom.** A call ends with [`tabular.callback.invalidResult`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Callback.html).

**Cause.** A function or an `ITabularFileStore` returned a result its contract
does not allow, such as `null` instead of a value or wrong file metadata.

**Fix.**

1. Fix the function or the storage adapter that the message names.

## A row conflicts with another writer

**Symptom.** An import reports [`tabular.persistence.conflict`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Persistence.html) of the kind
`Technical`.

**Cause.** Another process changed the data, another run took over the claim
of the journal, or a resolution contradicts the commit evidence.

**Fix.**

1. Recover the import with the journal as
   [Deployment and recovery](runbooks/deployment-and-recovery.md#import-ended-uncertain)
   describes.
2. Repeat the import once the other writer finished.

## Writing to the database fails

**Symptom.** An import ends with [`tabular.persistence.writeFailed`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Persistence.html).

**Cause.** The database rejected a write that the database errors of the
target do not assign to a code, or the start or completion of a journal entry
was not saved.

**Fix.**

1. Check the permissions and the constraints of the tables.
2. Assign expected constraint violations to a code with
   `errors.Database(...)`.
3. Repeat the import.

## A database operation fails

**Symptom.** An import ends with [`tabular.persistence.operationFailed`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Persistence.html).

**Cause.** A query, a transaction, a savepoint or the release of an own
context failed, for example because of a lost connection.

**Fix.**

1. Check the connection and the state of the database server.
2. Repeat the import.

## The outcome of a commit is unknown

**Symptom.** An import ends with [`tabular.persistence.commitUnknown`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Persistence.html) and counts
rows in `RowsUncertain`.

**Cause.** A commit of a group failed, and no signal of the provider tells
whether the data was stored.

**Fix.**

1. Discard a context that the application passed to the import.
2. Clarify the stored state as
   [Deployment and recovery](runbooks/deployment-and-recovery.md#import-ended-uncertain)
   describes before any retry.

## The outcome of a rollback is unknown

**Symptom.** An import ends with [`tabular.persistence.rollbackUnknown`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Persistence.html).

**Cause.** A reset to a savepoint or a rollback was not confirmed, for
example because the connection failed, or because MariaDB rolled back the
whole transaction under `innodb_snapshot_isolation`.

**Fix.**

1. Discard a context that the application passed to the import.
2. Clarify the stored state as
   [Deployment and recovery](runbooks/deployment-and-recovery.md#import-ended-uncertain)
   describes.
3. On MariaDB, set `TabularWriteOptions.IsolationLevel` to `ReadCommitted`
   when the rollbacks come from snapshot isolation.

## The change tracker cannot be reset

**Symptom.** An import ends with [`tabular.persistence.contextRestoreFailed`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Persistence.html).

**Cause.** After a row error, the change tracker of the context could not be
returned to its state before the row.

**Fix.**

1. Discard the context.
2. Repeat the import with a new context.

## Reading or writing a file fails

**Symptom.** A call ends with [`tabular.io.failed`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Io.html).

**Cause.** Opening, reading, writing, publishing or removing a stream or file
failed, or a storage adapter failed.

**Fix.**

1. Check the path, the permissions, the free space and the temporary
   directory.
2. Repeat the call.

## A resource limit is reached

**Symptom.** A call ends with [`tabular.resource.limitExceeded`](https://doka-labs.github.io/Doka.Tabular/api/Doka.Tabular.TabularErrorCodes.Resource.html).

**Cause.** The input, a field, the unpacked workbook or the temporary files
reached a limit of the format options.

**Fix.**

1. Check whether the input is expected to be that large.
2. Raise the limit for trusted input, or reject the input, as
   [Deployment and recovery](runbooks/deployment-and-recovery.md#resource-limit-reached)
   describes.
