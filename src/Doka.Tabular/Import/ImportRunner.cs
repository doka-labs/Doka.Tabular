namespace Doka.Tabular;

/// <summary>Input of an import: a caller-owned stream or a local path opened by the library.</summary>
internal sealed record ImportInput(
    Stream? Stream,
    string? Path
)
{
    /// <summary>
    /// The configuration error of a stream that cannot be read; it is known before the input, a journal, or the
    /// target is touched.
    /// </summary>
    public TabularError? Problem => Stream is { CanRead: false }
        ? TabularError.Configuration("The input stream is not readable.")
        : null;
}

/// <summary>Format-specific part of a single-table import.</summary>
internal interface IImportFormat
{
    bool NeedsSeekableInput { get; }

    /// <summary>Checks the content signature and creates the record source; returns an error before the first data row.</summary>
    ValueTask<(IRecordSource? Source, TabularError? Error, IAsyncDisposable? Owned)> OpenAsync(
        PreparedInput input,
        ConversionContext context,
        CancellationToken cancellationToken
    );
}

/// <summary>Entry point of a single-table import.</summary>
internal static class ImportRunner
{
    [RequiresDynamicCode(
        "Tabular compiles typed accessors and closes generic types over the mapped types at run time.")]
    [RequiresUnreferencedCode("Tabular binds the properties and constructors of the mapped types by reflection.")]
    public static async Task<ImportReport<TItem>> RunAsync<TItem>(
        ImportInput input,
        IImportFormat format,
        ImportShape<TItem> shape,
        ImportSettings settings,
        ConversionContext context,
        IImportSink<TItem>? sink,
        ResourceLimits limits,
        OperationScope scope,
        IReadOnlyList<TabularError> preconditions,
        CancellationToken cancellationToken
    )
    {
        var run = new ImportRun<TItem>(input,
            format,
            shape,
            settings,
            context,
            sink,
            limits,
            scope,
            preconditions,
            cancellationToken);

        await using (run.ConfigureAwait(false))
        {
            return await run
                .RunAsync()
                .ConfigureAwait(false);
        }
    }
}
