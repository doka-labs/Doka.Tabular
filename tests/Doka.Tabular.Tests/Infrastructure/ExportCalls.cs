// WHY: The core, the CSV, and the Excel test applications link this file; each uses only some of its members.
// ReSharper disable UnusedMember.Global

using System.Collections;
using System.Linq.Expressions;

namespace Doka.Tabular.Tests.Infrastructure;

/// <summary>Where the rows of an export overload come from.</summary>
internal enum RowSource
{
    Enumerable,
    AsyncEnumerable,
    Queryable,
}

/// <summary>The map of an export overload.</summary>
internal enum ExportShape
{
    TabularMap,
    RowExportMap,
    RowMap,
}

/// <summary>Where an export overload writes.</summary>
internal enum ExportTargetKind
{
    Stream,
    Path,
    Buffer,
    FileStore,
}

/// <summary>The report of an export overload and, for a buffered export, its file.</summary>
internal sealed record ExportOutcome(
    ExportReport Report,
    Stream? File
)
{
    public static async Task<ExportOutcome> Of(
        Task<ExportReport> report
    ) => new(await report, null);

    public static async Task<ExportOutcome> Of(
        Task<BufferedExportResult> result
    )
    {
        var (report, file) = await result;

        return new ExportOutcome(report, file);
    }
}

/// <summary>One public export overload, named by its parameter kinds.</summary>
internal sealed record ExportCase<TOptions>(
    RowSource Source,
    ExportShape Shape,
    ExportTargetKind Target,
    bool WithOptions,
    Func<ExportArguments<TOptions>, Task<ExportOutcome>> Run
)
    where TOptions : class
{
    public string Name => $"{Source} {Shape} {Target}"
        + (WithOptions
            ? " options"
            : string.Empty);

    /// <summary>Gets the reference parameters of the overload, each of which rejects null.</summary>
    public IEnumerable<string> Parameters
    {
        get
        {
            yield return "rows";
            yield return "map";

            switch (Target)
            {
                case ExportTargetKind.Stream or ExportTargetKind.FileStore:
                    yield return "target";
                    break;
                case ExportTargetKind.Path:
                    yield return "path";
                    break;
            }

            if (WithOptions)
            {
                yield return "options";
            }
        }
    }
}

/// <summary>
/// The arguments of the export overloads: the same two residents for every source and map. A negative case replaces
/// the arguments of one parameter with null.
/// </summary>
internal sealed record ExportArguments<TOptions>
    where TOptions : class
{
    public required Resident[] Records { get; init; }

    public required ResidentRow[] ResidentRows { get; init; }

    public required TabularRow[] TabularRows { get; init; }

    public required TabularMap<Resident> Map { get; init; }

    public required RowExportMap<ResidentRow> RowExport { get; init; }

    public required RowMap RowMap { get; init; }

    public required MemoryStream Stream { get; init; }

    public required string Path { get; init; }

    public required MemoryFileStore Store { get; init; }

    public required TabularFileTarget FileTarget { get; init; }

    public required TOptions Options { get; init; }

    /// <summary>Gets the file type of the common export.</summary>
    public TabularType Type { get; init; }

    public required CancellationToken Token { get; init; }

    public IAsyncEnumerable<Resident> AsyncRecords => Yield(Records);

    public IAsyncEnumerable<ResidentRow> AsyncResidentRows => Yield(ResidentRows);

    public IAsyncEnumerable<TabularRow> AsyncTabularRows => Yield(TabularRows);

    public IQueryable<Resident> QueryRecords => Query(Records)!;

    public IQueryable<ResidentRow> QueryResidentRows => Query(ResidentRows)!;

    /// <summary>Creates the arguments; the tabular rows come from a CSV import with the row map.</summary>
    public static async Task<ExportArguments<TOptions>> CreateAsync(
        TOptions options,
        string path,
        CancellationToken cancellationToken
    )
    {
        var imported = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestInput.Utf8("Ada,Dresden\r\nBob,Bonn"),
            Residents.RowMap,
            cancellationToken);

        var store = new MemoryFileStore();

        return new ExportArguments<TOptions>
        {
            Records = Residents.Records,
            ResidentRows = [.. Residents.Records.Select(record => new ResidentRow(record))],
            TabularRows = [.. imported.Items!],
            Map = Residents.Map,
            RowExport = Residents.RowExport,
            RowMap = Residents.RowMap,
            Stream = new MemoryStream(),
            Path = path,
            Store = store,
            FileTarget = TabularFileTarget.To(store, System.IO.Path.GetFileName(path)),
            Options = options,
            Token = cancellationToken,
        };
    }

    public ExportArguments<TOptions> Without(
        string parameter
    ) => parameter switch
    {
        "rows" => this with
        {
            Records = null!,
            ResidentRows = null!,
            TabularRows = null!,
        },
        "map" => this with
        {
            Map = null!,
            RowExport = null!,
            RowMap = null!,
        },
        "target" => this with
        {
            Stream = null!,
            FileTarget = null!,
        },
        "path" => this with { Path = null! },
        "options" => this with { Options = null! },
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "The parameter is unknown."),
    };

    /// <summary>Reads the bytes the overload wrote to the target of its kind.</summary>
    public async Task<byte[]> WrittenAsync(
        ExportTargetKind target,
        ExportOutcome outcome
    )
    {
        switch (target)
        {
            case ExportTargetKind.Stream:
                return Stream.ToArray();
            case ExportTargetKind.Path:
                return await File.ReadAllBytesAsync(Path, Token);
            case ExportTargetKind.Buffer:
                await using (var file = outcome.File!)
                {
                    using var copy = new MemoryStream();
                    await file.CopyToAsync(copy, Token);

                    return copy.ToArray();
                }
            case ExportTargetKind.FileStore:
                return Assert.Single(Store.Files)
                    .Value;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "The target is unknown.");
        }
    }

    private static AsyncQuery<T>? Query<T>(
        T[]? items
    ) => items is null
        ? null
        : new AsyncQuery<T>(items);

    private static IAsyncEnumerable<T> Yield<T>(
        T[]? items
    )
    {
        return items is null
            ? null!
            : Iterate(items);

        static async IAsyncEnumerable<T> Iterate(
            T[] values
        )
        {
            foreach (var value in values)
            {
                await Task.Yield();

                yield return value;
            }
        }
    }
}

/// <summary>A query in memory that also enumerates asynchronously, as an EF Core query does.</summary>
internal sealed class AsyncQuery<T> : IQueryable<T>, IAsyncEnumerable<T>
{
    private readonly IQueryable<T> _inner;

    public AsyncQuery(
        IEnumerable<T> items
    )
    {
        _inner = items.AsQueryable();
    }

    public async IAsyncEnumerator<T> GetAsyncEnumerator(
        CancellationToken cancellationToken
    )
    {
        foreach (var item in _inner)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            yield return item;
        }
    }

    public Type ElementType => _inner.ElementType;

    public Expression Expression => _inner.Expression;

    public IQueryProvider Provider => _inner.Provider;

    public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A file store in memory.</summary>
internal sealed class MemoryFileStore : ITabularFileStore
{
    public Dictionary<Guid, byte[]> Files { get; } = [];

    public async Task<TabularStoredFile> SaveAsync(
        TabularFileWriteRequest request,
        Stream content,
        CancellationToken cancellationToken
    )
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[request.ExportId] = copy.ToArray();

        return new TabularStoredFile(request.ExportId, request.ExportId, request.ByteSize, request.Sha256);
    }

    public Task DeleteAsync(
        Guid exportId,
        CancellationToken cancellationToken
    )
    {
        Files.Remove(exportId);

        return Task.CompletedTask;
    }
}
