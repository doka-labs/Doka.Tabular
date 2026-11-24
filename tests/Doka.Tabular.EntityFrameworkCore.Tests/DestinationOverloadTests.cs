using System.Linq;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using Doka.Tabular.Tests.Infrastructure;
using JetBrains.Annotations;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// Every public import overload with a destination, of the CSV, the Excel, and the common import: each forwards its
/// source, map, destination, options, and cancellation token, and rejects a null argument by its name.
/// </summary>
public sealed partial class DestinationOverloadTests : IDisposable
{
    private const string InvalidPersonnelNumber = "user.invalid row 2";

    private static readonly UserDto[] s_validUsers =
        [new(1, "ada@example.org", "Ada"), new(2, "bob@example.org", "Bob")];

    private static readonly UserDto[] s_withInvalidUser =
        [new(1, "ada@example.org", "Ada"), new(0, "bob@example.org", "Bob")];

    private readonly string _directory = Directory.CreateTempSubdirectory("tabular-destinations-")
        .FullName;

    public static TheoryData<string, TabularType> Overloads
    {
        get
        {
            var data = new TheoryData<string, TabularType>();
            foreach (var call in s_imports)
            {
                foreach (var type in call.Types)
                {
                    data.Add(call.Name, type);
                }
            }

            return data;
        }
    }

    public static TheoryData<string, string> NullArguments
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
    public void PublicDestinationOverloads_EachHaveOneCase()
    {
        // Arrange
        var cases = s_imports
            .Select(call => call.Name)
            .Order();

        // Act
        var overloads = OverloadSurface
            .DestinationNames(typeof(CsvFormat))
            .Select(name => $"Csv {name}")
            .Concat(OverloadSurface
                .DestinationNames(typeof(ExcelFormat))
                .Select(name => $"Excel {name}"))
            .Concat(OverloadSurface
                .DestinationNames(typeof(TabularImport))
                .Select(name => $"Tabular {name}"))
            .Order();

        // Assert
        Assert.Equal(overloads, cases);
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task ImportOverload_WithValidUsers_WritesBoth(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        await using var database = await SqliteFile.OpenAsync(Token);
        await using var context = await CreateContextAsync(database);
        var arguments = await DestinationArguments.CreateAsync(context, type, s_validUsers, _directory, Token);

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, outcome.Status);
        Assert.Equal(2, outcome.RowsImported);
        Assert.Equal(["Ada", "Bob"], await NamesAsync(context));
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task ImportOverload_WithAnInvalidUser_RejectsHimAsItsStrategyRequires(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        await using var database = await SqliteFile.OpenAsync(Token);
        await using var context = await CreateContextAsync(database);
        var arguments = await DestinationArguments.CreateAsync(context, type, s_withInvalidUser, _directory, Token);

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal([InvalidPersonnelNumber], outcome.Errors);
        Assert.Equal(call.WithOptions
                ? 1
                : 0,
            outcome.RowsImported);

        Assert.Equal(call.WithOptions
                ? ["Ada"]
                : [],
            await NamesAsync(context));
    }

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task ImportOverload_WithACanceledToken_ReportsTheCancellationAndWritesNothing(
        string overload,
        TabularType type
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        await using var database = await SqliteFile.OpenAsync(Token);
        await using var context = await CreateContextAsync(database);
        var arguments = await DestinationArguments.CreateAsync(context, type, s_validUsers, _directory, Token) with
        {
            Cancellation = new CancellationToken(canceled: true),
        };

        // Act
        var outcome = await call.Run(arguments);

        // Assert
        Assert.Equal(ReportStatus.Canceled, outcome.Status);
        Assert.Empty(await NamesAsync(context));
    }

    [Theory]
    [MemberData(nameof(NullArguments))]
    public async Task ImportOverload_WithANullArgument_RejectsItByName(
        string overload,
        string parameter
    )
    {
        // Arrange
        var call = s_imports.Single(entry => entry.Name == overload);
        await using var database = await SqliteFile.OpenAsync(Token);
        await using var context = await CreateContextAsync(database);
        var arguments = await DestinationArguments.CreateAsync(context,
            TabularType.Csv,
            s_validUsers,
            _directory,
            Token);

        // Act
        var exception = await Record.ExceptionAsync(() => call.Run(arguments.Without(parameter)));

        // Assert
        Assert.Equal(parameter,
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    private static Task<List<string>> NamesAsync(
        TabularTestContext context
    ) => context
        .Users
        .OrderBy(user => user.Name)
        .Select(user => user.Name)
        .ToListAsync(Token);

    private static async Task<TabularTestContext> CreateContextAsync(
        SqliteFile database
    )
    {
        var context = new TabularTestContext(new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(database.Connection)
            .Options);

        await context.Database.EnsureCreatedAsync(Token);

        return context;
    }

    private enum ImportSourceKind
    {
        Stream,
        Path,
    }

    private enum ImportShape
    {
        TabularMap,
        RowMap,
    }

    /// <summary>One public import overload with a destination, named by its class and parameter kinds.</summary>
    private sealed record DestinationCase(
        string Format,
        ImportSourceKind Source,
        ImportShape Shape,
        bool WithOptions,
        Func<DestinationArguments, Task<DestinationOutcome>> Run
    )
    {
        public string Name => $"{Format} {Source} {Shape}"
            + (WithOptions
                ? " options"
                : string.Empty);

        /// <summary>Gets the file types the overload reads; the common import reads both.</summary>
        public IEnumerable<TabularType> Types => Format switch
        {
            "Csv" => [TabularType.Csv],
            "Excel" => [TabularType.Excel],
            _ => [TabularType.Csv, TabularType.Excel],
        };

        /// <summary>Gets the reference parameters of the overload, each of which rejects null.</summary>
        public IEnumerable<string> Parameters
        {
            get
            {
                yield return Source == ImportSourceKind.Stream
                    ? "source"
                    : "path";

                yield return "map";
                yield return "destination";

                if (WithOptions)
                {
                    yield return "options";
                }
            }
        }
    }

    /// <summary>The status, the number of written records, and the errors of an import overload.</summary>
    private sealed record DestinationOutcome(
        ReportStatus Status,
        long RowsImported,
        IReadOnlyList<string> Errors
    )
    {
        public static async Task<DestinationOutcome> Of(
            Task<ImportReport<UserDto>> report
        ) => From(await report);

        public static async Task<DestinationOutcome> Of(
            Task<ImportReport<TabularRow>> report
        ) => From(await report);

        private static DestinationOutcome From<T>(
            ImportReport<T> report
        ) => new(report.Status,
            report.RowsImported,
            [.. report.Errors.Select(error => $"{error.Code.Value} row {error.Location.RowNumber}")]);
    }

    /// <summary>
    /// The arguments of the destination overloads: the users of the case, exported in its file type. A negative case
    /// replaces the arguments of one parameter with null.
    /// </summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record DestinationArguments
    {
        public required MemoryStream Source { get; init; }

        public required string Path { get; init; }

        public required TabularMap<UserDto> Map { get; init; }

        public required RowMap RowMap { get; init; }

        public required TabularImportDestination<UserDto> Destination { get; init; }

        public required TabularImportDestination<TabularRow> RowDestination { get; init; }

        public required CsvImportOptions CsvOptions { get; init; }

        public required ExcelImportOptions ExcelOptions { get; init; }

        public required TabularImportOptions TabularOptions { get; init; }

        public required TabularType Type { get; init; }

        public required CancellationToken Cancellation { get; init; }

        public static async Task<DestinationArguments> CreateAsync(
            TabularTestContext context,
            TabularType type,
            UserDto[] users,
            string directory,
            CancellationToken cancellationToken
        )
        {
            var export = await new TabularExport(s_csv, s_excel).ExportAsync(users,
                TestMaps.Users,
                type,
                cancellationToken);

            await using var file = export.File!;
            var input = new MemoryStream();
            await file.CopyToAsync(input, cancellationToken);
            var path = System.IO.Path.Combine(directory, $"users.{type}");
            await File.WriteAllBytesAsync(path, input.ToArray(), cancellationToken);
            input.Position = 0;

            var rows = RowMap.Create();
            var role = rows.Add(TestMaps.Users);
            var rowMap = rows.Build();
            var database = TabularDatabase.ForContext(context);

            return new DestinationArguments
            {
                Source = input,
                Path = path,
                Map = TestMaps.Users,
                RowMap = rowMap,
                Destination = TestMaps.Upsert(database),
                RowDestination = TabularDestination
                    .For(rowMap)
                    .Bind(role, TestMaps.Upsert(database))
                    .Build(),
                CsvOptions = new CsvImportOptions { Strategy = ImportStrategy.Partial },
                ExcelOptions = new ExcelImportOptions { Strategy = ImportStrategy.Partial },
                TabularOptions = new TabularImportOptions { Strategy = ImportStrategy.Partial },
                Type = type,
                Cancellation = cancellationToken,
            };
        }

        public DestinationArguments Without(
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
            "destination" => this with
            {
                Destination = null!,
                RowDestination = null!,
            },
            "options" => this with
            {
                CsvOptions = null!,
                ExcelOptions = null!,
                TabularOptions = null!,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "The parameter is unknown."),
        };
    }
}
