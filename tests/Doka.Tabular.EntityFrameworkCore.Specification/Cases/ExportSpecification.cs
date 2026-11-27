using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>Export from EF queries: the query overload runs asynchronously and gives the same rows as an async sequence.</summary>
public abstract class ExportSpecification
{
    private readonly ITestDatabaseProvider _provider;

    protected ExportSpecification(
        ITestDatabaseProvider provider
    )
    {
        _provider = provider;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<TabularType, string> TypesAndShapes()
    {
        var data = new TheoryData<TabularType, string>();
        foreach (var type in new[] { TabularType.Csv, TabularType.Excel })
        {
            foreach (var shape in new[] { "query", "set", "async sequence" })
            {
                data.Add(type, shape);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TypesAndShapes))]
    public async Task EveryQueryShape_ExportsAndRunsOnlyAsynchronously(
        TabularType type,
        string shape
    )
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(
            User.Create(2, "b@example.org", "B"),
            User.Create(1, "a@example.org", "A"),
            User.Create(3, "c@example.org", "C"));

        var counter = new ReaderCounter();
        var options = new DbContextOptionsBuilder<TabularTestContext>(((OptionsContextFactory)database.Factory).Options)
            .AddInterceptors(counter)
            .Options;

        await using var context = new TabularTestContext(options);

        // Act
        var (report, _) = await ExportShapeAsync(context, type, shape);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(0, counter.Synchronous);
        Assert.True(counter.Asynchronous >= 1);
    }

    [Theory]
    [InlineData(TabularType.Csv)]
    [InlineData(TabularType.Excel)]
    public async Task Query_WritesOnlyItsFilteredRows(
        TabularType type
    )
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(
            User.Create(2, "b@example.org", "B"),
            User.Create(1, "a@example.org", "A"),
            User.Create(3, "c@example.org", "C"));

        await using var context = await database.ContextAsync();

        // Act
        var (report, _) = await ExportShapeAsync(context, type, "query");

        // Assert
        Assert.Equal(2, report.RowsWritten);
    }

    [Fact]
    public async Task AsyncSequenceOfAQuery_GivesTheSameFileAsTheQuery()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(
            User.Create(2, "b@example.org", "B"),
            User.Create(1, "a@example.org", "A"),
            User.Create(3, "c@example.org", "C"));

        await using var context = await database.ContextAsync();

        // Act
        // WHY: The equality of both shapes is the checked contract, so both exports form one action.
        var (_, queryFile) = await ExportShapeAsync(context, TabularType.Csv, "query");
        var (_, asyncFile) = await ExportShapeAsync(context, TabularType.Csv, "async sequence");

        // Assert
        Assert.Equal(Bytes(queryFile!), Bytes(asyncFile!));
    }

    /// <summary>Exports the users in one of the shapes: a query, the whole set, or the query as asynchronous sequence.</summary>
    private static Task<BufferedExportResult> ExportShapeAsync(
        TabularTestContext context,
        TabularType type,
        string shape
    )
    {
        var map = TabularMap
            .For<User>()
            .Column(
                x => x.PersonnelNumber,
                index: 0,
                configure: c => c
                    .Header("Nr")
                    .ExportOnly())
            .Column(
                x => x.Email,
                index: 1,
                configure: c => c
                    .Header("E-Mail")
                    .ExportOnly())
            .Build();

        var query = context
            .Users
            .AsNoTracking()
            .Where(user => user.PersonnelNumber > 1)
            .OrderBy(user => user.PersonnelNumber);

        var export = new TabularExport(
            new CsvFormat(new CsvFormatOptions()),
            new ExcelFormat(new ExcelFormatOptions()));

        var withHeader = new TabularExportOptions { WriteHeader = true };

        return shape switch
        {
            "query" => export.ExportAsync(query, map, type, withHeader, Token),
            "set" => export.ExportAsync(context.Users, map, type, Token),
            _ => export.ExportAsync(query.AsAsyncEnumerable(), map, type, withHeader, Token),
        };
    }

    private static byte[] Bytes(
        Stream stream
    )
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }

    private sealed class ReaderCounter : DbCommandInterceptor
    {
        public int Synchronous { get; private set; }

        public int Asynchronous { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Synchronous++;

            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Asynchronous++;

            return ValueTask.FromResult(result);
        }
    }
}
