namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Destination of a row-map import: every row writes its bound targets in one row unit.</summary>
[PublicAPI]
public sealed class TabularRowDestination : TabularImportDestination<TabularRow>
{
    private readonly ImportLogBinding? _journal;
    private readonly RowMap _map;
    private readonly TabularWriteOptions _options;
    private readonly IReadOnlyList<BoundTarget> _targets;

    internal TabularRowDestination(
        RowMap map,
        IReadOnlyList<BoundTarget> targets,
        IReadOnlyList<TabularSourceKey> keys,
        TabularWriteOptions options,
        ImportLogBinding? journal
    )
    {
        _map = map;
        _targets = targets;
        SourceKeys = keys;
        _options = options;
        _journal = journal;
    }

    internal override IReadOnlyList<TabularSourceKey> SourceKeys { get; }

    /// <summary>The roles of the destination are positions in its own row map; another map would mix up the targets.</summary>
    internal override TabularError? MapProblem(
        object map
    ) => ReferenceEquals(map, _map)
        ? null
        : TabularError.Configuration("The destination was prepared for another row map than the one of the import.");

    /// <summary>Returns a view with other write settings.</summary>
    /// <param name="options">The write settings.</param>
    /// <returns>The new view.</returns>
    public TabularRowDestination WithOptions(
        TabularWriteOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TabularRowDestination(_map, _targets, SourceKeys, options, _journal);
    }

    /// <summary>Returns a view that records the import in <paramref name="journal" /> and protects against duplicate files.</summary>
    /// <param name="journal">The journal.</param>
    /// <param name="options">Scope, file details, duplicate behavior, and completion function.</param>
    /// <returns>The new view.</returns>
    public TabularRowDestination WithImportLog(
        TabularJournal journal,
        TabularImportLogOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(options);

        return new TabularRowDestination(_map, _targets, SourceKeys, _options, new ImportLogBinding(journal, options));
    }

    internal override IImportSink<TabularRow> CreateSink(
        ImportCall call
    ) => new WriteSink<TabularRow>(call,
        _targets[0].Definition.Database,
        _targets,
        _options,
        _journal,
        row => row.Items);
}
