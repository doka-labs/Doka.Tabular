namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Read target of an import: a row without a matching record is a row error.</summary>
/// <typeparam name="TDto">The record type.</typeparam>
[PublicAPI]
public abstract class TabularLookupTarget<TDto> : TabularImportDestination<TDto>
{
    private protected TabularLookupTarget(
        TargetView view
    )
    {
        View = view;
    }

    internal TargetView View { get; }

    /// <summary>Returns a view with other write settings.</summary>
    /// <param name="options">The write settings.</param>
    /// <returns>The new view.</returns>
    public TabularLookupTarget<TDto> WithOptions(
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
    public TabularLookupTarget<TDto> WithImportLog(
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
    ) => new WriteSink<TDto>(call,
        View.Definition.Database,
        [new BoundTarget(View.Definition, null, 0)],
        View.Options,
        View.Journal,
        item => [item!]);

    private protected abstract TabularLookupTarget<TDto> With(
        TargetView view
    );
}

/// <summary>Read target with the entity type, for values of the found entity used by other targets of the row.</summary>
/// <typeparam name="TDto">The record type.</typeparam>
/// <typeparam name="TEntity">The entity type.</typeparam>
[PublicAPI]
public sealed class TabularLookupTarget<TDto, TEntity> : TabularLookupTarget<TDto>
    where TEntity : class
{
    internal TabularLookupTarget(
        TargetView view,
        TabularSourceKey<TDto> sourceKey
    )
        : base(view)
    {
        LookupKey = sourceKey;
    }

    private TabularSourceKey<TDto> LookupKey { get; }

    /// <summary>Describes the value of an entity property of the found record in the same row.</summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="role">The role this target is bound to.</param>
    /// <param name="value">Reads the value from the entity, for example <c>alias => alias.UserId</c>.</param>
    /// <returns>The value handle for <c>valueFrom</c> and <see cref="TabularTargetValues.Get{TValue}" />.</returns>
    public TabularTargetValue<TValue> Value<TValue>(
        TabularRole<TDto> role,
        Expression<Func<TEntity, TValue>> value
    ) => TargetValueFactory.Create(View.Definition, role, value);

    private protected override TabularLookupTarget<TDto> With(
        TargetView view
    ) => new TabularLookupTarget<TDto, TEntity>(view, LookupKey);
}
