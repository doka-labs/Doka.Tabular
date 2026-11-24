namespace Doka.Tabular.Csv.Tests;

/// <summary>
/// Every public export overload and every import overload without a destination: each forwards its rows, map, target,
/// options, and cancellation token, and rejects a null argument by its name.
/// </summary>
public sealed partial class CsvOverloadTests : IDisposable
{
    private const string BothColumns = "Ada,Dresden\r\nBob,Bonn";

    private const string NameColumn = "Ada\r\nBob";

    private const string MissingCity = "tabular.conversion.missingValue row 2";

    private static readonly CsvExportOptions s_exportOptions = new() { ColumnKeys = [Residents.NameKey] };

    private static readonly CsvImportOptions s_importOptions = new() { Strategy = ImportStrategy.Partial };

    private readonly string _directory = Directory.CreateTempSubdirectory("tabular-csv-")
        .FullName;

    public static TheoryData<string> ExportOverloads => [.. s_exports.Select(call => call.Name)];

    public static TheoryData<string> StreamExportOverloads =>
    [
        .. s_exports
            .Where(call => call.Target == ExportTargetKind.Stream)
            .Select(call => call.Name),
    ];

    public static TheoryData<string> ImportOverloads => [.. s_imports.Select(call => call.Name)];

    public static TheoryData<string, string> ExportNullArguments
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var call in s_exports)
            {
                foreach (var parameter in call.Parameters)
                {
                    data.Add(call.Name, parameter);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string> ImportNullArguments
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var call in s_imports)
            {
                foreach (var parameter in call.Parameters)
                {
                    data.Add(call.Name, parameter);
                }
            }

            return data;
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void PublicOverloads_EachHaveOneCase()
    {
        // Arrange
        var exports = s_exports
            .Select(call => call.Name)
            .Order();

        var imports = s_imports
            .Select(call => call.Name)
            .Order();

        // Act
        var exportOverloads = OverloadSurface.Names(typeof(CsvFormat), nameof(CsvFormat.ExportAsync));
        var importOverloads = OverloadSurface.Names(typeof(CsvFormat), nameof(CsvFormat.ImportAsync));

        // Assert
        Assert.Equal(exportOverloads, exports);
        Assert.Equal(importOverloads, imports);
    }

    [Theory]
    [MemberData(nameof(ExportOverloads))]
    public async Task ExportOverload_WithValidArguments_WritesTheResidentsToItsTarget(
        string overload
    )
    {
        // Arrange
        var call = s_exports.Single(entry => entry.Name == overload);
        var arguments = await CreateExportAsync();

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, outcome.Report.Status);
        Assert.Equal(call.WithOptions
                ? NameColumn
                : BothColumns,
            Encoding.UTF8.GetString(await arguments.WrittenAsync(call.Target, outcome)));
    }

    [Theory]
    [MemberData(nameof(StreamExportOverloads))]
    public async Task ExportOverload_ToAStream_LeavesTheStreamOpen(
        string overload
    )
    {
        // Arrange
        var call = s_exports.Single(entry => entry.Name == overload);
        var arguments = await CreateExportAsync();

        // Act
        await call.Run(arguments);

        // Assert
        Assert.True(arguments.Stream.CanWrite);
    }

    [Theory]
    [MemberData(nameof(ExportOverloads))]
    public async Task ExportOverload_WithACanceledToken_ReportsTheCancellation(
        string overload
    )
    {
        // Arrange
        var call = s_exports.Single(entry => entry.Name == overload);
        var arguments = await CreateExportAsync() with { Token = new CancellationToken(canceled: true) };

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal(ReportStatus.Canceled, outcome.Report.Status);
    }

    [Theory]
    [MemberData(nameof(ExportNullArguments))]
    public async Task ExportOverload_WithANullArgument_RejectsItByName(
        string overload,
        string parameter
    )
    {
        // Arrange
        var call = s_exports.Single(entry => entry.Name == overload);
        var arguments = await CreateExportAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => call.Run(arguments.Without(parameter)));

        // Assert
        Assert.Equal(parameter,
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    [Theory]
    [MemberData(nameof(ImportOverloads))]
    public async Task ImportOverload_WithValidResidents_ImportsBoth(
        string overload
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        var arguments = await CreateImportAsync("Ada,Dresden\r\nBob,Bonn"u8.ToArray());

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, outcome.Status);
        Assert.Equal(2, outcome.RowsImported);
        Assert.Equal(["Ada", "Bob"], outcome.Names);
    }

    [Theory]
    [MemberData(nameof(ImportOverloads))]
    public async Task ImportOverload_WithAResidentWithoutCity_RejectsHimAsItsStrategyRequires(
        string overload
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        var arguments = await CreateImportAsync("Ada,Dresden\r\nBob,"u8.ToArray());

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal([MissingCity], outcome.Errors);
        Assert.Equal(call.WithOptions
                ? ["Ada"]
                : [],
            outcome.Names);

        Assert.Equal(call.WithOptions
                ? 1
                : 0,
            outcome.RowsImported);
    }

    [Theory]
    [MemberData(nameof(ImportOverloads))]
    public async Task ImportOverload_WithACanceledToken_ReportsTheCancellation(
        string overload
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        var arguments = await CreateImportAsync("Ada,Dresden\r\nBob,Bonn"u8.ToArray()) with
        {
            Token = new CancellationToken(canceled: true),
        };

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal(ReportStatus.Canceled, outcome.Status);
        Assert.Empty(outcome.Names);
    }

    [Theory]
    [MemberData(nameof(ImportNullArguments))]
    public async Task ImportOverload_WithANullArgument_RejectsItByName(
        string overload,
        string parameter
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        var arguments = await CreateImportAsync("Ada,Dresden"u8.ToArray());

        // Act
        var exception = await Record.ExceptionAsync(() => call.Run(arguments.Without(parameter)));

        // Assert
        Assert.Equal(parameter,
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    private Task<ExportArguments<CsvExportOptions>> CreateExportAsync()
        => ExportArguments<CsvExportOptions>.CreateAsync(s_exportOptions,
            Path.Combine(_directory, "residents.csv"),
            Token);

    private Task<ImportArguments<CsvImportOptions>> CreateImportAsync(
        byte[] input
    ) => ImportArguments<CsvImportOptions>.CreateAsync(s_importOptions,
        input,
        Path.Combine(_directory, "residents.csv"),
        Token);
}
