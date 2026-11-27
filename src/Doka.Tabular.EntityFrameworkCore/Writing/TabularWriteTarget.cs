namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>
/// Write target of an import (insert or upsert). Views created with <see cref="WithOptions" /> and
/// <see cref="WithImportLog" /> are immutable and share the target definition.
/// </summary>
/// <typeparam name="TDto">The record type.</typeparam>
[PublicAPI]
public abstract class TabularWriteTarget<TDto> : TabularImportDestination<TDto>
{
    private protected TabularWriteTarget(
        TargetView view,
        TabularSourceKey<TDto>? sourceKey
    )
    {
        View = view;
        Key = sourceKey;
    }

    internal TargetView View { get; }

    internal TabularSourceKey<TDto>? Key { get; }

    internal override IReadOnlyList<TabularSourceKey> SourceKeys => Key is null
        ? []
        : [Key];

    /// <summary>Returns a view with other write settings.</summary>
    /// <param name="options">The write settings.</param>
    /// <returns>The new view.</returns>
    public TabularWriteTarget<TDto> WithOptions(
        TabularWriteOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        return With(View with { Options = options });
    }

    /// <summary>Returns a view that records the import in <paramref name="journal" /> and protects against duplicate files.</summary>
    /// <param name="journal">The journal.</param>
    /// <param name="options">Scope, file details, duplicate behavior, and completion function.</param>
    /// <returns>The new view.</returns>
    public TabularWriteTarget<TDto> WithImportLog(
        TabularJournal journal,
        TabularImportLogOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(options);

        return With(View with { Journal = new ImportLogBinding(journal, options) });
    }

    internal override IImportSink<TDto> CreateSink(
        ImportCall call
    ) => new WriteSink<TDto>(
        call,
        View.Definition.Database,
        [new BoundTarget(View.Definition, null, 0)],
        View.Options,
        View.Journal,
        item => [item!]);

    private protected abstract TabularWriteTarget<TDto> With(
        TargetView view
    );
}

/// <summary>Write target with the entity type, for values of the entity used by other targets of the row.</summary>
/// <typeparam name="TDto">The record type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
[PublicAPI]
public sealed class TabularWriteTarget<TDto, TEntity> : TabularWriteTarget<TDto>
    where TEntity : class
{
    internal TabularWriteTarget(
        TargetView view,
        TabularSourceKey<TDto>? sourceKey
    )
        : base(view, sourceKey) { }

    /// <summary>
    /// Describes the actual value of an entity property of this target in the same row, also a database-generated
    /// value. A new entity is saved within the transaction before the value is read.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="role">The role this target is bound to.</param>
    /// <param name="value">Reads the value from the entity, for example <c>user => user.Id</c>.</param>
    /// <returns>The value handle for <c>valueFrom</c> and <see cref="TabularTargetValues.Get{TValue}" />.</returns>
    public TabularTargetValue<TValue> Value<TValue>(
        TabularRole<TDto> role,
        Expression<Func<TEntity, TValue>> value
    ) => TargetValueFactory.Create(View.Definition, role, value);

    private protected override TabularWriteTarget<TDto> With(
        TargetView view
    ) => new TabularWriteTarget<TDto, TEntity>(view, Key);
}
