namespace Doka.Tabular.Excel.Tests;

public sealed partial class ExcelWorkbookOverloadTests
{
    private static readonly ExcelFormat s_format = new(new ExcelFormatOptions());

    private static readonly WorkbookExportCase[] s_exports =
    [
        new(
            ExportTargetKind.Stream,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.Stream, c.Cancellation))),
        new(
            ExportTargetKind.Stream,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.Stream, c.ExportOptions, c.Cancellation))),
        new(
            ExportTargetKind.Path,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.Path, c.Cancellation))),
        new(
            ExportTargetKind.Path,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.Path, c.ExportOptions, c.Cancellation))),
        new(
            ExportTargetKind.Buffer,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.Cancellation))),
        new(
            ExportTargetKind.Buffer,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.ExportOptions, c.Cancellation))),
        new(
            ExportTargetKind.FileStore,
            false,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.FileTarget, c.Cancellation))),
        new(
            ExportTargetKind.FileStore,
            true,
            c => ExportOutcome.Of(s_format.ExportAsync(c.Sheets, c.FileTarget, c.ExportOptions, c.Cancellation))),
    ];

    private static readonly WorkbookImportCase[] s_imports =
    [
        new(
            ImportSourceKind.Stream,
            false,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Source, c.ImportSheets, c.Cancellation), c.Sheet)),
        new(
            ImportSourceKind.Stream,
            true,
            c => ImportOutcome.Of(
                s_format.ImportAsync(c.Source, c.ImportSheets, c.ImportOptions, c.Cancellation),
                c.Sheet)),
        new(
            ImportSourceKind.Path,
            false,
            c => ImportOutcome.Of(s_format.ImportAsync(c.Path, c.ImportSheets, c.Cancellation), c.Sheet)),
        new(
            ImportSourceKind.Path,
            true,
            c => ImportOutcome.Of(
                s_format.ImportAsync(c.Path, c.ImportSheets, c.ImportOptions, c.Cancellation),
                c.Sheet)),
    ];

    /// <summary>One public workbook export overload, named by its parameter kinds.</summary>
    private sealed record WorkbookExportCase(
        ExportTargetKind Target,
        bool WithOptions,
        Func<WorkbookArguments, Task<ExportOutcome>> Run
    )
    {
        public string Name => $"Workbook {Target}"
            + (WithOptions
                ? " options"
                : string.Empty);

        /// <summary>Gets the reference parameters of the overload, each of which rejects null.</summary>
        public IEnumerable<string> Parameters
        {
            get
            {
                yield return "sheets";

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

    /// <summary>One public workbook import overload, named by its parameter kinds.</summary>
    private sealed record WorkbookImportCase(
        ImportSourceKind Source,
        bool WithOptions,
        Func<WorkbookArguments, Task<ImportOutcome>> Run
    )
    {
        public string Name => $"{Source} Workbook"
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

                yield return "sheets";

                if (WithOptions)
                {
                    yield return "options";
                }
            }
        }
    }

    /// <summary>
    /// The arguments of the workbook overloads: one sheet of residents to export, and the input of the case to import,
    /// as a stream and as a file. A negative case replaces the arguments of one parameter with null.
    /// </summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record WorkbookArguments
    {
        public required ExcelWorkbookExport Sheets { get; init; }

        public required ExcelWorkbookImport ImportSheets { get; init; }

        public required ExcelImportSheet<Resident> Sheet { get; init; }

        public required MemoryStream Stream { get; init; }

        public required MemoryStream Source { get; init; }

        public required string Path { get; init; }

        public required MemoryFileStore Store { get; init; }

        public required TabularFileTarget FileTarget { get; init; }

        public required ExcelWorkbookExportOptions ExportOptions { get; init; }

        public required ExcelWorkbookImportOptions ImportOptions { get; init; }

        public required CancellationToken Cancellation { get; init; }

        /// <summary>Creates the arguments; the input is written to <paramref name="path" /> as well.</summary>
        public static async Task<WorkbookArguments> CreateAsync(
            byte[] input,
            string path,
            CancellationToken cancellationToken
        )
        {
            await File.WriteAllBytesAsync(path, input, cancellationToken);
            var imports = ExcelImport.Create();
            var sheet = imports.Add(TabularSheetName.Create(SheetName), Residents.Map);
            var store = new MemoryFileStore();

            return new WorkbookArguments
            {
                Sheets = ExcelExport
                    .Create()
                    .Add(TabularSheetName.Create(SheetName), Residents.Records, Residents.Map)
                    .Build(),
                ImportSheets = imports.Build(),
                Sheet = sheet,
                Stream = new MemoryStream(),
                Source = new MemoryStream(input),
                Path = path,
                Store = store,
                FileTarget = TabularFileTarget.To(store, System.IO.Path.GetFileName(path)),
                ExportOptions = s_exportOptions,
                ImportOptions = s_importOptions,
                Cancellation = cancellationToken,
            };
        }

        public WorkbookArguments Without(
            string parameter
        ) => parameter switch
        {
            "sheets" => this with
            {
                Sheets = null!,
                ImportSheets = null!,
            },
            "target" => this with
            {
                Stream = null!,
                FileTarget = null!,
            },
            "source" => this with { Source = null! },
            "path" => this with { Path = null! },
            "options" => this with
            {
                ExportOptions = null!,
                ImportOptions = null!,
            },
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
                    return await File.ReadAllBytesAsync(Path, Cancellation);
                case ExportTargetKind.Buffer:
                    await using (var file = outcome.File!)
                    {
                        using var copy = new MemoryStream();
                        await file.CopyToAsync(copy, Cancellation);

                        return copy.ToArray();
                    }
                case ExportTargetKind.FileStore:
                    return Assert.Single(Store.Files)
                        .Value;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "The target is unknown.");
            }
        }
    }
}
