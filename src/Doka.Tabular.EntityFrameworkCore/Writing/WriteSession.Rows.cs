using System.Collections;

namespace Doka.Tabular.EntityFrameworkCore;

internal sealed partial class WriteSession
{
    /// <summary>Writes the rows of one table according to strategy and transaction mode.</summary>
    public async Task<WriteResult> WriteAsync(
        IReadOnlyList<RowWork> rows,
        IReadOnlyList<BoundTarget> targets,
        ImportSinkWrite write,
        CancellationToken cancellationToken
    )
    {
        var result = new WriteResult();
        var strict = write.Strategy == ImportStrategy.Strict;
        var group = new List<(int Index, bool Skipped)>();
        try
        {
            if (Joined && (!_importSavepointSet || Options.TransactionPerSheet))
            {
                await _transaction!
                    .CreateSavepointAsync(ImportSavepoint, cancellationToken)
                    .ConfigureAwait(false);

                _importSavepointSet = true;
                _tablesInImportSavepoint = 0;
            }

            if (Joined)
            {
                _tablesInImportSavepoint++;
            }

            if (!await WriteRowsAsync(rows, targets, strict, group, result, cancellationToken)
                .ConfigureAwait(false))
            {
                return result;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.Canceled = true;
        }

        // WHY: The requested cancellation is caught above. EF passes exceptions of interceptors, providers, and
        // factories through unchanged, so any other exception is a failure of the database operation.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result.Fatal = TabularError.Create(
                TabularErrorCodes.Persistence.OperationFailed,
                TabularErrorKind.Technical,
                "A transaction or savepoint operation failed.");
        }

        await FinishTableAsync(write, strict, group, result)
            .ConfigureAwait(false);

        return result;
    }

    /// <summary>Writes the rows; <see langword="false" /> when a failed group commit already ended the table.</summary>
    private async Task<bool> WriteRowsAsync(
        IReadOnlyList<RowWork> rows,
        IReadOnlyList<BoundTarget> targets,
        bool strict,
        List<(int Index, bool Skipped)> group,
        WriteResult result,
        CancellationToken cancellationToken
    )
    {
        var groupSize = strict
            ? int.MaxValue
            : Options.CommitGroupSize;

        var rowSavepoints = Joined || strict || groupSize > 1;

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (cancellationToken.IsCancellationRequested)
            {
                result.Canceled = true;

                return true;
            }

            // WHY: A confirmed commit of an earlier group says nothing about an error of a later row, also not about
            // one while its transaction starts.
            result.CommitConfirmed = false;
            await BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var outcome = await ExecuteRowAsync(row, targets, rowSavepoints, cancellationToken)
                .ConfigureAwait(false);

            if (!Take(outcome, result))
            {
                // WHY: Written parts of a row whose reset failed stay in the open group; its rollback then covers them,
                // and an unconfirmed rollback counts the row as uncertain.
                if (outcome.ResetUnconfirmed)
                {
                    group.Add((row.Index, false));
                }

                return true;
            }

            // WHY: A saved row belongs to the open group before the cancellation is checked; its rollback then
            // covers it, and an unconfirmed rollback counts it as uncertain.
            if (outcome.Error is null)
            {
                group.Add((row.Index, outcome.Skipped));
            }

            // WHY: A cancellation during a row, accepted or rejected, must not let the open group commit afterwards.
            if (cancellationToken.IsCancellationRequested)
            {
                result.Canceled = true;

                return true;
            }

            if (outcome.Error is not null)
            {
                continue;
            }

            if (!strict
                && !Joined
                && group.Count >= groupSize
                && !await CommitGroupAsync(group, result)
                    .ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Starts the own transaction of the next rows, unless one is open or the import joins one.</summary>
    private async Task BeginTransactionAsync(
        CancellationToken cancellationToken
    )
    {
        if (Joined
            || _transaction is not null)
        {
            return;
        }

        _transaction = Options.IsolationLevel is { } level
            ? await Context!
                .Database
                .BeginTransactionAsync(level, cancellationToken)
                .ConfigureAwait(false)
            : await Context!
                .Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>Records a row outcome; <see langword="false" /> when cancellation or a fatal error ends the table.</summary>
    private static bool Take(
        RowOutcome outcome,
        WriteResult result
    )
    {
        if (outcome.Canceled)
        {
            result.Canceled = true;

            return false;
        }

        if (outcome.Fatal is not null)
        {
            result.Fatal = outcome.Fatal;

            return false;
        }

        if (outcome.Error is not null)
        {
            result.Errors.Add(outcome.Error);
            result.RowsRejected++;
        }

        return true;
    }

    private async Task<RowOutcome> ExecuteRowAsync(
        RowWork row,
        IReadOnlyList<BoundTarget> targets,
        bool savepoint,
        CancellationToken cancellationToken
    )
    {
        var context = Context!;
        if (savepoint)
        {
            await _transaction!
                .CreateSavepointAsync(RowSavepoint, cancellationToken)
                .ConfigureAwait(false);
        }

        using var tracking = new RowTracking(context);

        // WHY: Several targets may write the same entity type; a database error belongs to the target whose entity it
        // names, not to the first target of that type.
        var owners = new Dictionary<object, BoundTarget>(ReferenceEqualityComparer.Instance);
        try
        {
            var (skipped, failure) =
                await ApplyTargetsAsync(
                        context,
                        row,
                        targets,
                        owners,
                        tracking,
                        _matches,
                        cancellationToken)
                    .ConfigureAwait(false);

            failure ??= await SaveAsync(context, targets, owners, row.Location, cancellationToken)
                .ConfigureAwait(false);

            if (failure is { } rejected)
            {
                return await RejectAsync(tracking, savepoint, rejected.Error, rejected.Fatal)
                    .ConfigureAwait(false);
            }

            if (savepoint)
            {
                await ReleaseSavepointAsync()
                    .ConfigureAwait(false);
            }

            tracking.Release();

            return new RowOutcome(skipped, null, null, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var rejected = await RejectAsync(tracking, savepoint, null, null)
                .ConfigureAwait(false);

            return rejected with { Canceled = rejected.Fatal is null };
        }

        // WHY: The requested cancellation is caught above. EF passes exceptions of interceptors, providers, and
        // factories through unchanged, so any other exception is a failure of the database operation.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return await RejectAsync(
                    tracking,
                    savepoint,
                    null,
                    TabularError.Create(
                        TabularErrorCodes.Persistence.OperationFailed,
                        TabularErrorKind.Technical,
                        "A query of a target failed.",
                        row.Location))
                .ConfigureAwait(false);
        }
    }

    /// <summary>Applies the row to every target in order; the first failure stops the row.</summary>
    private static async Task<(bool Skipped, (TabularError? Error, TabularError? Fatal)? Failure)> ApplyTargetsAsync(
        DbContext context,
        RowWork row,
        IReadOnlyList<BoundTarget> targets,
        Dictionary<object, BoundTarget> owners,
        RowTracking tracking,
        MatchQueries matches,
        CancellationToken cancellationToken
    )
    {
        var values = new TabularTargetValues();
        var skipped = true;

        // WHY: Indexed access reads the list without an enumerator through the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var target = targets[targetIndex];
            var step = await target
                .Definition
                .ApplyAsync(
                    context,
                    row.Records[target.Ordinal],
                    values,
                    tracking,
                    matches,
                    row.Location,
                    cancellationToken)
                .ConfigureAwait(false);

            if (step.Fatal is not null
                || step.RowError is not null)
            {
                return (false, (step.RowError, step.Fatal));
            }

            skipped &= step.Skipped;

            // WHY: A save error belongs to the target that wrote the entity; a target that only found it, a lookup or
            // an insert that matched an existing record, takes it only while no writer has.
            var writes = target.Definition.Writes && !step.Skipped;
            if (writes || !owners.ContainsKey(step.Entity!))
            {
                owners[step.Entity!] = target;
            }

            RegisterDependents(context, step.Entity!, target, owners, writes);
            if (await ShareValuesAsync(
                        context,
                        row,
                        targets,
                        owners,
                        target,
                        step.Entity!,
                        values,
                        cancellationToken)
                    .ConfigureAwait(false) is { } failure)
            {
                return (false, failure);
            }
        }

        return (skipped, null);
    }

    /// <summary>
    /// Assigns the entities the target wrote along with its own to the target: owned entities and new, changed, or
    /// deleted entities in its graph, such as an owned collection in its own table. A save error naming only such an
    /// entity then belongs to this target. A writing target takes such entities from a target that only found them.
    /// </summary>
    private static void RegisterDependents(
        DbContext context,
        object entity,
        BoundTarget target,
        Dictionary<object, BoundTarget> owners,
        bool writes
    )
    {
        var pending = new Stack<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { entity };
        pending.Push(entity);
        while (pending.TryPop(out var current))
        {
            // WHY: Navigations are read through the change tracker; with the default field access no lazy load runs.
            foreach (var navigation in context.Entry(current)
                .Navigations)
            {
                var values = navigation.CurrentValue switch
                {
                    null => [],
                    IEnumerable items and not string => items.Cast<object>(),
                    var single => [single],
                };

                foreach (var dependent in values)
                {
                    var entry = context.Entry(dependent);
                    if ((!navigation.Metadata.TargetEntityType.IsOwned()
                            && entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                        || !visited.Add(dependent))
                    {
                        continue;
                    }

                    if (writes || !owners.ContainsKey(dependent))
                    {
                        owners[dependent] = target;
                    }

                    pending.Push(dependent);
                }
            }
        }
    }

    /// <summary>Reads the values later targets use; a new entity is saved first so that generated keys exist.</summary>
    private static async Task<(TabularError? Error, TabularError? Fatal)?> ShareValuesAsync(
        DbContext context,
        RowWork row,
        IReadOnlyList<BoundTarget> targets,
        Dictionary<object, BoundTarget> owners,
        BoundTarget target,
        object entity,
        TabularTargetValues values,
        CancellationToken cancellationToken
    )
    {
        var handles = target
            .Definition
            .Values
            .Where(value => ReferenceEquals(value.Role, target.Role) || target.Role is null)
            .ToList();

        if (handles.Count == 0)
        {
            return null;
        }

        if (context.Entry(entity)
                .State
            == EntityState.Added
            && await SaveAsync(context, targets, owners, row.Location, cancellationToken)
                .ConfigureAwait(false) is { } saveError)
        {
            return saveError;
        }

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var handleIndex = 0; handleIndex < handles.Count; handleIndex++)
        {
            var handle = handles[handleIndex];
            try
            {
                values.Set(handle, handle.Read(entity));
            }

            // WHY: Application code receives no token; its cancellation counts as the call's only while the call is
            // canceled.
            catch (Exception exception) when (Callbacks.Failed(exception, cancellationToken))
            {
                return (null, TabularError.Create(
                    TabularErrorCodes.Callback.Failed,
                    TabularErrorKind.Technical,
                    $"Reading a value of {target.Definition.EntityType.Name} threw an exception.",
                    row.Location));
            }
        }

        return null;
    }

    private static async Task<(TabularError? Error, TabularError? Fatal)?> SaveAsync(
        DbContext context,
        IReadOnlyList<BoundTarget> targets,
        Dictionary<object, BoundTarget> owners,
        TabularErrorLocation location,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await context
                .SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return (
                TabularError.Create(
                    TabularErrorCodes.Persistence.Conflict,
                    TabularErrorKind.Data,
                    "The record was changed by another process since it was read.",
                    location), null);
        }
        catch (DbUpdateException exception)
        {
            // WHY: The entity of a target identifies it; an entity no target wrote directly, such as a dependent one,
            // falls back to the first target of its type.
            var target = exception
                    .Entries
                    .Select(entry => owners.GetValueOrDefault(entry.Entity))
                    .FirstOrDefault(owner => owner is not null)
                ?? targets.FirstOrDefault(bound
                    => exception.Entries.Any(entry => bound.Definition.EntityType.IsInstanceOfType(entry.Entity)))
                ?? targets[^1];

            TabularErrorCode? code;
            try
            {
                code = target.Definition.Errors.DatabaseMapping?.Invoke(exception);
            }

            // WHY: Application code receives no token; its cancellation counts as the call's only while the call is
            // canceled.
            catch (Exception callback) when (Callbacks.Failed(callback, cancellationToken))
            {
                return (null,
                    TabularError.Create(
                        TabularErrorCodes.Callback.Failed,
                        TabularErrorKind.Technical,
                        $"The database error assignment of {target.Definition.EntityType.Name} threw an exception.",
                        location));
            }

            return code is null
                ? (null,
                    TabularError.Create(
                        TabularErrorCodes.Persistence.WriteFailed,
                        TabularErrorKind.Technical,
                        $"Saving {target.Definition.EntityType.Name} failed without an assigned code.",
                        location))
                : (
                    TabularError.Create(
                        code,
                        TabularErrorKind.Data,
                        $"The database rejected {target.Definition.EntityType.Name}.",
                        location), null);
        }
    }
}
