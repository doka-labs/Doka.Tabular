using Doka.EntityFrameworkCore.MySql;
using Doka.Tabular.EntityFrameworkCore.MySql.Tests;
using DotNet.Testcontainers.Containers;
using MySqlConnector;
using Testcontainers.MariaDb;
using Testcontainers.MySql;

[assembly: AssemblyFixture(typeof(MySqlServer))]

namespace Doka.Tabular.EntityFrameworkCore.MySql.Tests;

/// <summary>
/// One MySQL or MariaDB container for the test assembly; every test case gets its own utf8mb4 database. The target
/// is a stage of the image manifest, MariaDB 12.3 by default, changed with DOKA_TABULAR_MYSQL_TARGET.
/// </summary>
public sealed class MySqlServer : ITestDatabaseProvider, IAsyncLifetime
{
    private const string DefaultTarget = "mariadb123";

    private IDatabaseContainer? _container;
    private MySqlServerVersion? _version;

    public async ValueTask InitializeAsync()
    {
        var target = Environment.GetEnvironmentVariable("DOKA_TABULAR_MYSQL_TARGET") is { Length: > 0 } value
            ? value
            : DefaultTarget;

        var image = DatabaseImages.Get(target);
        var version = DatabaseImages.VersionOf(image);
        if (target.StartsWith("mariadb", StringComparison.Ordinal))
        {
            _container = new MariaDbBuilder(image)
                .WithUsername("root")
                .Build();

            _version = MySqlServerVersion.MariaDb(version);
        }
        else
        {
            _container = new MySqlBuilder(image)
                .WithUsername("root")
                .Build();

            _version = MySqlServerVersion.MySql(version);
        }

        await _container.StartAsync(TestContext.Current.CancellationToken);
        await using var connection = new MySqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW VARIABLES LIKE 'innodb_snapshot_isolation'";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        // WHY: Only MariaDB has the variable; with it ON, writing a row changed after the snapshot rolls back the transaction.
        ConcurrentChangeAbortsTransaction =
            await reader.ReadAsync(TestContext.Current.CancellationToken) && reader.GetString(1) == "ON";
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public bool SupportsConcurrentWriters => true;

    public bool ConcurrentChangeAbortsTransaction { get; private set; }

    public async Task<TestDatabase> CreateAsync(
        CancellationToken cancellationToken
    )
    {
        var name = $"tabular_{Guid.NewGuid():N}";
        await using (var server = new MySqlConnection(_container!.GetConnectionString()))
        {
            await server.OpenAsync(cancellationToken);
            await using var command = server.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var connection = new MySqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }
            .ConnectionString;

        var options = new DbContextOptionsBuilder<TabularTestContext>()
            .UseMySql(connection, _version!)
            .UseTabularJournal()
            .Options;

        var database = new ServerDatabase(new OptionsContextFactory(options));
        await using var context = await database.Factory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return database;
    }
}

public sealed class MySqlImportTests : ImportSpecification
{
    public MySqlImportTests(
        MySqlServer server
    )
        : base(server) { }
}

public sealed class MySqlMultiTargetTests : MultiTargetSpecification
{
    public MySqlMultiTargetTests(
        MySqlServer server
    )
        : base(server) { }
}

public sealed class MySqlJournalTests : JournalSpecification
{
    public MySqlJournalTests(
        MySqlServer server
    )
        : base(server) { }
}

public sealed class MySqlExportTests : ExportSpecification
{
    public MySqlExportTests(
        MySqlServer server
    )
        : base(server) { }
}
