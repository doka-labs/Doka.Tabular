// WHY: The core, the CSV, and the Excel test applications link this file with different global usings, so it imports
// the Excel namespace itself; where a project imports it globally the directive looks redundant, which the cleanup must
// not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005

// WHY: Each test application uses only some of these members.
// ReSharper disable UnusedMember.Global
using Doka.Tabular.Excel;

namespace Doka.Tabular.Tests.Infrastructure;

/// <summary>Where an import overload reads.</summary>
internal enum ImportSourceKind
{
    Stream,
    Path,
}

/// <summary>The map of an import overload.</summary>
internal enum ImportShape
{
    TabularMap,
    RowMap,
}

/// <summary>The status, counters, imported names, and errors of an import overload.</summary>
internal sealed record ImportOutcome(
    ReportStatus Status,
    long RowsImported,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Errors
)
{
    public static async Task<ImportOutcome> Of(
        Task<ImportReport<Resident>> report
    )
    {
        var result = await report;

        return new ImportOutcome(result.Status,
            result.RowsImported,
            [.. (result.Items ?? []).Select(record => record.Name)],
            Describe(result.Errors));
    }

    public static async Task<ImportOutcome> Of(
        Task<ImportReport<TabularRow>> report
    )
    {
        var result = await report;

        return new ImportOutcome(result.Status,
            result.RowsImported,
            [
                .. (result.Items ?? []).Select(row => row.Get(Residents.Role)
                    .Name),
            ],
            Describe(result.Errors));
    }

    public static async Task<ImportOutcome> Of(
        Task<ExcelWorkbookImportReport> report,
        ExcelImportSheet<Resident> sheet
    )
    {
        var result = await report;

        return new ImportOutcome(result.Status,
            result.RowsImported,
            [
                .. (result.Get(sheet)
                        .Items
                    ?? []).Select(record => record.Name),
            ],
            Describe(result.Errors));
    }

    /// <summary>Describes each error by its code and row, the parts a rejection test compares.</summary>
    private static IReadOnlyList<string> Describe(
        IEnumerable<TabularError> errors
    ) => [.. errors.Select(error => $"{error.Code.Value} row {error.Location.RowNumber}")];
}

/// <summary>One public import overload without a destination, named by its parameter kinds.</summary>
internal sealed record ImportCase<TOptions>(
    ImportSourceKind Source,
    ImportShape Shape,
    bool WithOptions,
    Func<ImportArguments<TOptions>, Task<ImportOutcome>> Run
)
    where TOptions : class
{
    public string Name => $"{Source} {Shape}"
        + (WithOptions
            ? " options"
            : string.Empty);

    /// <summary>Gets the reference parameters of the overload, each of which rejects null.</summary>
    public IEnumerable<string> Parameters
    {
        get
        {
            yield return Source == ImportSourceKind.Stream
                ? "source"
                : "path";

            yield return "map";

            if (WithOptions)
            {
                yield return "options";
            }
        }
    }
}

/// <summary>
/// The arguments of the import overloads: the input of the case, as a stream and as a file. A negative case replaces
/// the arguments of one parameter with null.
/// </summary>
internal sealed record ImportArguments<TOptions>
    where TOptions : class
{
    public required MemoryStream Source { get; init; }

    public required string Path { get; init; }

    public required TabularMap<Resident> Map { get; init; }

    public required RowMap RowMap { get; init; }

    public required TOptions Options { get; init; }

    /// <summary>Gets the file type of the common import.</summary>
    public TabularType Type { get; init; }

    public required CancellationToken Token { get; init; }

    /// <summary>Creates the arguments; the input is written to <paramref name="path" /> as well.</summary>
    public static async Task<ImportArguments<TOptions>> CreateAsync(
        TOptions options,
        byte[] input,
        string path,
        CancellationToken cancellationToken
    )
    {
        await File.WriteAllBytesAsync(path, input, cancellationToken);

        return new ImportArguments<TOptions>
        {
            Source = new MemoryStream(input),
            Path = path,
            Map = Residents.Map,
            RowMap = Residents.RowMap,
            Options = options,
            Token = cancellationToken,
        };
    }

    public ImportArguments<TOptions> Without(
        string parameter
    ) => parameter switch
    {
        "source" => this with { Source = null! },
        "path" => this with { Path = null! },
        "map" => this with
        {
            Map = null!,
            RowMap = null!,
        },
        "options" => this with { Options = null! },
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "The parameter is unknown."),
    };
}
