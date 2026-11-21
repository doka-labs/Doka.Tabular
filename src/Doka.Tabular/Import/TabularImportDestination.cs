namespace Doka.Tabular;

/// <summary>
/// Write target of an import, created by the EF package (for example <c>TabularWriteTarget&lt;TDto&gt;</c>). Without a
/// destination an import returns its records in the report.
/// </summary>
/// <typeparam name="T">The record type of the map; <see cref="TabularRow" /> for a <see cref="RowMap" />.</typeparam>
[PublicAPI]
public abstract class TabularImportDestination<T>
{
    private protected TabularImportDestination() { }

    /// <summary>Gets the source keys of the bound matches for duplicate detection in the file.</summary>
    internal virtual IReadOnlyList<TabularSourceKey> SourceKeys => [];

    /// <summary>
    /// The configuration error of a destination that was prepared for another map than the one of the call;
    /// <see langword="null" /> when it fits.
    /// </summary>
    internal virtual TabularError? MapProblem(
        object map
    ) => null;

    /// <summary>Creates the sink of one call; sinks of the same call share state through <paramref name="call" />.</summary>
    internal abstract IImportSink<T> CreateSink(
        ImportCall call
    );
}

/// <summary>State shared by the sinks of one import call, such as one context and transaction per database description.</summary>
internal sealed class ImportCall
{
    public Dictionary<object, object> Shared { get; } = [];
}
