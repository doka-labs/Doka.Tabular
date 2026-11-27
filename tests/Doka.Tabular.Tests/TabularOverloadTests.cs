using static Doka.Tabular.Tests.Infrastructure.TestFiles;

namespace Doka.Tabular.Tests;

/// <summary>
/// Every public overload of the common export and every import overload without a destination: each forwards its
/// rows, map, target, file type, options, and cancellation token in both file types, and rejects a null argument by
/// its name.
/// </summary>
public sealed partial class TabularOverloadTests : IDisposable
{
    private const string MissingCity = "tabular.conversion.missingValue row 2";

    private static readonly string?[][] s_bothColumns = [["Ada", "Dresden"], ["Bob", "Bonn"]];

    private static readonly string?[][] s_nameColumn = [["Ada"], ["Bob"]];

    private static readonly TabularExportOptions s_exportOptions = new() { ColumnKeys = [Residents.NameKey] };

    private static readonly TabularImportOptions s_importOptions = new() { Strategy = ImportStrategy.Partial };

    private readonly string _directory = Directory.CreateTempSubdirectory("tabular-overloads-")
        .FullName;

    public static TheoryData<string, TabularType> ExportOverloads => InBothTypes(s_exports.Select(call => call.Name));

    public static TheoryData<string, TabularType> StreamExportOverloads => InBothTypes(
        s_exports
            .Where(call => call.Target == ExportTargetKind.Stream)
            .Select(call => call.Name));

    public static TheoryData<string, TabularType> ImportOverloads => InBothTypes(s_imports.Select(call => call.Name));

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
        var exportOverloads = OverloadSurface.Names(typeof(TabularExport), nameof(TabularExport.ExportAsync));
        var importOverloads = OverloadSurface.Names(typeof(TabularImport), nameof(TabularImport.ImportAsync));

        // Assert
        Assert.Equal(exportOverloads, exports);
        Assert.Equal(importOverloads, imports);
    }

    [Theory]
    [MemberData(nameof(ExportOverloads))]
    public async Task ExportOverload_WithValidArguments_WritesTheResidentsToItsTarget(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_exports.Single(entry => entry.Name == overload);
        var arguments = await CreateExportAsync(type);

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, outcome.Report.Status);
        Assert.Equal(
            call.WithOptions
                ? s_nameColumn
                : s_bothColumns,
            Cells(type, new MemoryStream(await arguments.WrittenAsync(call.Target, outcome))));
    }

    [Theory]
    [MemberData(nameof(StreamExportOverloads))]
    public async Task ExportOverload_ToAStream_LeavesTheStreamOpen(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_exports.Single(entry => entry.Name == overload);
        var arguments = await CreateExportAsync(type);

        // Act
        await call.Run(arguments);

        // Assert
        Assert.True(arguments.Stream.CanWrite);
    }

    [Theory]
    [MemberData(nameof(ExportOverloads))]
    public async Task ExportOverload_WithACanceledToken_ReportsTheCancellation(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_exports.Single(entry => entry.Name == overload);
        var arguments = await CreateExportAsync(type) with { Token = new CancellationToken(canceled: true) };

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
        var arguments = await CreateExportAsync(TabularType.Csv);

        // Act
        var exception = await Record.ExceptionAsync(() => call.Run(arguments.Without(parameter)));

        // Assert
        Assert.Equal(
            parameter,
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    [Theory]
    [MemberData(nameof(ImportOverloads))]
    public async Task ImportOverload_WithValidResidents_ImportsBoth(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        var arguments = await CreateImportAsync(type, ["Ada", "Dresden"], ["Bob", "Bonn"]);

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
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        var arguments = await CreateImportAsync(type, ["Ada", "Dresden"], ["Bob", null]);

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal([MissingCity], outcome.Errors);
        Assert.Equal(
            call.WithOptions
                ? ["Ada"]
                : [],
            outcome.Names);

        Assert.Equal(
            call.WithOptions
                ? 1
                : 0,
            outcome.RowsImported);
    }

    [Theory]
    [MemberData(nameof(ImportOverloads))]
    public async Task ImportOverload_WithACanceledToken_ReportsTheCancellation(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        var arguments = await CreateImportAsync(type, ["Ada", "Dresden"], ["Bob", "Bonn"]) with
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
        var arguments = await CreateImportAsync(TabularType.Csv, ["Ada", "Dresden"]);

        // Act
        var exception = await Record.ExceptionAsync(() => call.Run(arguments.Without(parameter)));

        // Assert
        Assert.Equal(
            parameter,
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    private static TheoryData<string, TabularType> InBothTypes(
        IEnumerable<string> overloads
    )
    {
        var data = new TheoryData<string, TabularType>();
        foreach (var overload in overloads)
        {
            data.Add(overload, TabularType.Csv);
            data.Add(overload, TabularType.Excel);
        }

        return data;
    }

    private async Task<ExportArguments<TabularExportOptions>> CreateExportAsync(
        TabularType type
    ) => await ExportArguments<TabularExportOptions>.CreateAsync(
            s_exportOptions,
            Path.Combine(_directory, $"residents.{type}"),
            Token) with
        {
            Type = type,
        };

    private async Task<ImportArguments<TabularImportOptions>> CreateImportAsync(
        TabularType type,
        params object?[][] rows
    ) => await ImportArguments<TabularImportOptions>.CreateAsync(
            s_importOptions,
            Create(type, rows)
                .ToArray(),
            Path.Combine(_directory, $"residents.{type}"),
            Token) with
        {
            Type = type,
        };
}
