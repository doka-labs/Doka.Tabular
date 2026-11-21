namespace Doka.Tabular.Benchmarks;

/// <summary>The formats a profile runs with; buffers below the file size make import and export spill to disk.</summary>
public sealed record ProfileFormats(
    CsvFormat Csv,
    ExcelFormat Excel
)
{
    public static ProfileFormats Default { get; } =
        new(new CsvFormat(new CsvFormatOptions()), new ExcelFormat(new ExcelFormatOptions()));
}

/// <summary>What an import of a profile produced: status, rows, records in order, and invalid record numbers.</summary>
public sealed record ProfileOutcome(
    ReportStatus Status,
    long RowsRead,
    IReadOnlyList<object> Items,
    IReadOnlyList<long> ErrorRecords
);

/// <summary>
/// One profile prepared for measurement: the input file is generated once; export and import are the measured work,
/// and the expected result follows from the profile.
/// </summary>
public sealed class ProfileRun
{
    private const ColumnBinding Binding = ColumnBinding.ByIndexWithHeader;
    private readonly ProfileFormats _formats;

    private ProfileRun(
        ReferenceProfile profile,
        ProfileFormats formats
    )
    {
        Profile = profile;
        _formats = formats;
    }

    public ReferenceProfile Profile { get; }

    /// <summary>Gets the generated input file of the import.</summary>
    public byte[] Input { get; private set; } = [];

    private bool IsExcel => Profile.Format != ProfileFormat.Csv;

    public static async Task<ProfileRun> PrepareAsync(
        ReferenceProfile profile,
        ProfileFormats? formats = null,
        CancellationToken cancellationToken = default
    )
    {
        var run = new ProfileRun(profile, formats ?? ProfileFormats.Default);
        using var file = new MemoryStream();
        var report = await run.WriteAsync(file, profile.Format == ProfileFormat.Csv, cancellationToken);
        if (report.Status != ReportStatus.Succeeded)
        {
            throw new InvalidOperationException($"The input of {profile} could not be generated: {report.Status}.");
        }

        run.Input = profile.Format == ProfileFormat.Csv
            ? file.ToArray()
            : ExcelVariants.Convert(file.ToArray(), profile.Format);

        return run;
    }

    /// <summary>The records the import must return, in file order.</summary>
    public IReadOnlyList<object> ExpectedItems() => Profile switch
    {
        { ErrorsPerThousand: > 0 } =>
        [
            .. ProfileData.WithErrors(Profile)
                .Valid,
        ],
        { ColumnSelection: true } =>
        [
            .. ProfileData
                .Standard(Profile)
                .Select(row => new SelectedRow(row.Key, row.Name, row.Id)),
        ],
        { Shape: ProfileShape.Wide } => [.. ProfileData.Wide(Profile)],
        { Shape: ProfileShape.Conversions } => [.. ProfileData.Conversions(Profile)],
        _ => [.. ProfileData.Standard(Profile)],
    };

    /// <summary>The record numbers the import must report as invalid.</summary>
    public IReadOnlyList<long> ExpectedErrorRecords() => Profile.ErrorsPerThousand > 0
        ? ProfileData.WithErrors(Profile)
            .Invalid
        : [];

    /// <summary>Exports the records of the profile in its format; the measured export work.</summary>
    public Task<ExportReport> ExportAsync(
        Stream target,
        CancellationToken cancellationToken = default
    ) => Profile.Exports
        ? WriteAsync(target, Profile.Format == ProfileFormat.Csv, cancellationToken)
        : throw new InvalidOperationException($"{Profile} is an import profile; Tabular writes Transitional only.");

    /// <summary>Exports the standard records from an asynchronous source that keeps generating them.</summary>
    public Task<ExportReport> ExportStreamingAsync(
        Stream target,
        CancellationToken cancellationToken = default
    ) => Profile.Format == ProfileFormat.Csv
        ? _formats.Csv.ExportAsync(ProfileData.StandardAsync(Profile, cancellationToken),
            ProfileMaps.Standard,
            target,
            new CsvExportOptions { WriteHeader = true },
            cancellationToken)
        : _formats.Excel.ExportAsync(ProfileData.StandardAsync(Profile, cancellationToken),
            ProfileMaps.Standard,
            target,
            new ExcelExportOptions { WriteHeader = true },
            cancellationToken);

    /// <summary>
    /// Exports the standard records without a target into the output buffer of the format; the buffer spills to a
    /// temporary file when the format allows it and the file exceeds the buffer.
    /// </summary>
    public async Task<(ReportStatus Status, byte[] File)> ExportBufferedAsync(
        CancellationToken cancellationToken = default
    )
    {
        var (report, file) = Profile.Format == ProfileFormat.Csv
            ? await _formats.Csv.ExportAsync(ProfileData.Standard(Profile),
                ProfileMaps.Standard,
                new CsvExportOptions { WriteHeader = true },
                cancellationToken)
            : await _formats.Excel.ExportAsync(ProfileData.Standard(Profile),
                ProfileMaps.Standard,
                new ExcelExportOptions { WriteHeader = true },
                cancellationToken);

        if (file is null)
        {
            return (report.Status, []);
        }

        await using (file)
        {
            using var copy = new MemoryStream();
            await file.CopyToAsync(copy, cancellationToken);

            return (report.Status, copy.ToArray());
        }
    }

    /// <summary>Exports the standard records into a file target with a store in memory; the measured store work.</summary>
    public async Task<(ExportReport Report, MemoryStore Store)> ExportToStoreAsync(
        CancellationToken cancellationToken = default
    )
    {
        var store = new MemoryStore();
        var csv = Profile.Format == ProfileFormat.Csv;
        var target = TabularFileTarget.To(store,
            csv
                ? $"{Profile.Id}.csv"
                : $"{Profile.Id}.xlsx");

        var report = csv
            ? await _formats.Csv.ExportAsync(ProfileData.Standard(Profile),
                ProfileMaps.Standard,
                target,
                new CsvExportOptions { WriteHeader = true },
                cancellationToken)
            : await _formats.Excel.ExportAsync(ProfileData.Standard(Profile),
                ProfileMaps.Standard,
                target,
                new ExcelExportOptions { WriteHeader = true },
                cancellationToken);

        return (report, store);
    }

    /// <summary>Imports the generated input; the measured import work.</summary>
    public Task<ProfileOutcome> ImportAsync(
        CancellationToken cancellationToken = default
    ) => ImportAsync(new MemoryStream(Input, writable: false), cancellationToken);

    /// <summary>Imports a file of the profile, for example one written by <see cref="ExportAsync" />.</summary>
    public Task<ProfileOutcome> ImportAsync(
        Stream input,
        CancellationToken cancellationToken = default
    ) => Profile switch
    {
        { Sheets: > 1 } => ImportWorkbookAsync(input, cancellationToken),
        { ErrorsPerThousand: > 0 } => ImportTableAsync(input,
            ProfileMaps.Standard,
            ImportStrategy.Partial,
            Binding,
            cancellationToken),
        { ColumnSelection: true } => ImportTableAsync(input,
            ProfileMaps.Selected,
            ImportStrategy.Strict,
            ColumnBinding.ByHeaderName,
            cancellationToken),
        { Shape: ProfileShape.Wide } => ImportTableAsync(input,
            ProfileMaps.Wide,
            ImportStrategy.Strict,
            Binding,
            cancellationToken),
        { Shape: ProfileShape.Conversions } => ImportTableAsync(input,
            ProfileMaps.Conversion,
            ImportStrategy.Strict,
            Binding,
            cancellationToken),
        _ => ImportTableAsync(input, ProfileMaps.Standard, ImportStrategy.Strict, Binding, cancellationToken),
    };

    /// <summary>Imports the standard records of the input into a destination, such as an EF target.</summary>
    public async Task<ProfileOutcome> ImportIntoAsync(
        TabularImportDestination<ProfileRow> destination,
        CancellationToken cancellationToken = default
    )
    {
        var input = new MemoryStream(Input, writable: false);
        var report = IsExcel
            ? await _formats.Excel.ImportAsync(input,
                ProfileMaps.Standard,
                destination,
                new ExcelImportOptions { Binding = Binding },
                cancellationToken)
            : await _formats.Csv.ImportAsync(input,
                ProfileMaps.Standard,
                destination,
                new CsvImportOptions { Binding = Binding },
                cancellationToken);

        return new ProfileOutcome(report.Status, report.RowsImported, [], ErrorRecords(report.Errors));
    }

    private Task<ExportReport> WriteAsync(
        Stream target,
        bool csv,
        CancellationToken cancellationToken
    ) => Profile switch
    {
        { Sheets: > 1 } => WriteWorkbookAsync(target, cancellationToken),
        { ErrorsPerThousand: > 0 } => WriteTableAsync(ProfileData.WithErrors(Profile)
                .Rows,
            ProfileMaps.ErrorSource,
            target,
            csv,
            null,
            cancellationToken),
        { Shape: ProfileShape.Wide } => WriteTableAsync(ProfileData.Wide(Profile),
            ProfileMaps.Wide,
            target,
            csv,
            null,
            cancellationToken),
        { Shape: ProfileShape.Conversions } => WriteTableAsync(ProfileData.Conversions(Profile),
            ProfileMaps.Conversion,
            target,
            csv,
            null,
            cancellationToken),
        _ => WriteTableAsync(ProfileData.Standard(Profile),
            ProfileMaps.Standard,
            target,
            csv,
            Profile.ColumnSelection
                ? ProfileMaps.Selection
                : null,
            cancellationToken),
    };

    private Task<ExportReport> WriteTableAsync<T>(
        IEnumerable<T> rows,
        TabularMap<T> map,
        Stream target,
        bool csv,
        IReadOnlyList<ColumnKey>? columns,
        CancellationToken cancellationToken
    ) => csv
        ? _formats.Csv.ExportAsync(rows,
            map,
            target,
            new CsvExportOptions
            {
                WriteHeader = true,
                ColumnKeys = columns,
            },
            cancellationToken)
        : _formats.Excel.ExportAsync(rows,
            map,
            target,
            new ExcelExportOptions
            {
                WriteHeader = true,
                ColumnKeys = columns,
            },
            cancellationToken);

    private Task<ExportReport> WriteWorkbookAsync(
        Stream target,
        CancellationToken cancellationToken
    )
    {
        var rows = ProfileData
            .Standard(Profile)
            .ToList();

        var size = rows.Count / Profile.Sheets;
        var sheets = ExcelExport.Create();
        for (var sheet = 0; sheet < Profile.Sheets; sheet++)
        {
            sheets.Add(SheetName(sheet), rows.GetRange(sheet * size, size), ProfileMaps.Standard);
        }

        return _formats.Excel.ExportAsync(sheets.Build(),
            target,
            new ExcelWorkbookExportOptions { WriteHeader = true },
            cancellationToken);
    }

    private async Task<ProfileOutcome> ImportTableAsync<T>(
        Stream input,
        TabularMap<T> map,
        ImportStrategy strategy,
        ColumnBinding binding,
        CancellationToken cancellationToken
    )
    {
        var report = IsExcel
            ? await _formats.Excel.ImportAsync(input,
                map,
                new ExcelImportOptions
                {
                    Binding = binding,
                    Strategy = strategy,
                },
                cancellationToken)
            : await _formats.Csv.ImportAsync(input,
                map,
                new CsvImportOptions
                {
                    Binding = binding,
                    Strategy = strategy,
                },
                cancellationToken);

        return new ProfileOutcome(report.Status,
            report.RowsRead,
            [.. (report.Items ?? []).Cast<object>()],
            ErrorRecords(report.Errors));
    }

    private async Task<ProfileOutcome> ImportWorkbookAsync(
        Stream input,
        CancellationToken cancellationToken
    )
    {
        var import = ExcelImport.Create();
        var sheets = Enumerable
            .Range(0, Profile.Sheets)
            .Select(sheet => import.Add(SheetName(sheet),
                ProfileMaps.Standard,
                new ExcelSheetImportOptions { Binding = Binding }))
            .ToList();

        var report = await _formats.Excel.ImportAsync(input, import.Build(), cancellationToken);

        return new ProfileOutcome(report.Status,
            report.RowsRead,
            [
                .. sheets.SelectMany(sheet => (report.Get(sheet)
                        .Items
                    ?? []).Cast<object>()),
            ],
            ErrorRecords(report.Errors));
    }

    private static TabularSheetName SheetName(
        int sheet
    ) => TabularSheetName.Create($"Part {sheet + 1}");

    private static List<long> ErrorRecords(
        IReadOnlyList<TabularError> errors
    ) => [.. errors.Select(error => error.Location.RecordNumber ?? 0)];
}
