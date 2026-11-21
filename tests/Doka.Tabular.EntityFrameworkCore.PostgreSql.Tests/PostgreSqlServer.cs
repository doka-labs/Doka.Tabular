using Doka.Tabular.EntityFrameworkCore.PostgreSql.Tests;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgreSqlServer))]

namespace Doka.Tabular.EntityFrameworkCore.PostgreSql.Tests;

/// <summary>One PostgreSQL container for the test assembly; every test case gets its own database.</summary>
public sealed class PostgreSqlServer : ITestDatabaseProvider, IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder(DatabaseImages.Get("postgres")).Build();
        await _container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public bool SupportsConcurrentWriters => true;

    public async Task<TestDatabase> CreateAsync(
        CancellationToken cancellationToken
    )
    {
        var connection =
            new NpgsqlConnectionStringBuilder(_container!.GetConnectionString())
            {
                Database = $"tabular_{Guid.NewGuid():N}",
            }.ConnectionString;

        var options = new DbContextOptionsBuilder<TabularTestContext>()
            .UseNpgsql(connection)
            .UseTabularJournal()
            .Options;

        var database = new ServerDatabase(new OptionsContextFactory(options));
        await using var context = await database.Factory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return database;
    }
}

public sealed class PostgreSqlImportTests : ImportSpecification
{
    public PostgreSqlImportTests(
        PostgreSqlServer server
    )
        : base(server) { }
}

public sealed class PostgreSqlMultiTargetTests : MultiTargetSpecification
{
    public PostgreSqlMultiTargetTests(
        PostgreSqlServer server
    )
        : base(server) { }
}

public sealed class PostgreSqlJournalTests : JournalSpecification
{
    public PostgreSqlJournalTests(
        PostgreSqlServer server
    )
        : base(server) { }
}

public sealed class PostgreSqlExportTests : ExportSpecification
{
    public PostgreSqlExportTests(
        PostgreSqlServer server
    )
        : base(server) { }
}
