// WHY: Linked sources compile in projects with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Doka.EntityFrameworkCore.MySql;
using Doka.Tabular.Testing;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Doka.Tabular.Samples;

/// <summary>
/// Gives a sample its own utf8mb4 database: by default on MariaDB 12.3 in a container that ends with the sample, or,
/// with <c>DOKA_TABULAR_SAMPLE_SERVER</c> set to a stage, on the server that <c>docker/compose.yml</c> runs for it.
/// </summary>
internal sealed class SampleDatabase<TContext> : IAsyncDisposable
    where TContext : DbContext
{
    private const string ServerVariable = "DOKA_TABULAR_SAMPLE_SERVER";

    private readonly MariaDbServer? _server;

    private SampleDatabase(
        MariaDbServer? server,
        DbContextOptions<TContext> options
    )
    {
        _server = server;
        Options = options;
    }

    public DbContextOptions<TContext> Options { get; }

    public ValueTask DisposeAsync() => _server?.DisposeAsync() ?? ValueTask.CompletedTask;

    public static async Task<SampleDatabase<TContext>> StartAsync(
        Func<DbContextOptions<TContext>, TContext> create,
        Action<DbContextOptionsBuilder<TContext>>? configure,
        CancellationToken cancellationToken
    )
    {
        var stage = Environment.GetEnvironmentVariable(ServerVariable);
        if (!string.IsNullOrEmpty(stage))
        {
            return await ConnectAsync(stage, create, configure, cancellationToken);
        }

        var server = await MariaDbServer.StartAsync("mariadb123", cancellationToken);
        try
        {
            var connection = await server.CreateDatabaseAsync("tabular_sample", cancellationToken);
            return await CreateAsync(server,
                connection,
                MySqlServerVersion.MariaDb(server.Version),
                create,
                configure,
                cancellationToken);
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Recreates the database of the sample on a running compose server. The data of the run stays there until the
    /// next run of the same sample, so it can be checked in an IDE; every run starts from an empty database.
    /// </summary>
    private static async Task<SampleDatabase<TContext>> ConnectAsync(
        string stage,
        Func<DbContextOptions<TContext>, TContext> create,
        Action<DbContextOptionsBuilder<TContext>>? configure,
        CancellationToken cancellationToken
    )
    {
        if (!ComposeServers.Ports.TryGetValue(stage, out var defaultPort))
        {
            throw new InvalidOperationException($"{ServerVariable} must name a MariaDB or MySQL stage of "
                + $"docker/compose.yml: {string.Join(", ", ComposeServers.Ports.Keys)}.");
        }

        var version = DatabaseImages.VersionOf(DatabaseImages.Get(stage));
        var serverVersion = stage.StartsWith("mariadb", StringComparison.Ordinal)
            ? MySqlServerVersion.MariaDb(version)
            : MySqlServerVersion.MySql(version);

        var server = new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1",
            Port = uint.Parse(Setting($"TABULAR_DEV_{stage.ToUpperInvariant()}_PORT",
                    defaultPort.ToString(CultureInfo.InvariantCulture)),
                CultureInfo.InvariantCulture),
            UserID = "root",
            Password = Setting("TABULAR_DEV_ROOT_PASSWORD", "tabular-local-root"),
        };

        // WHY: The samples share one server, so each keeps its data in a database named after its project.
        var name = "tabular_sample_"
            + typeof(TContext).Namespace!
                .Split('.')[^1]
                .ToLowerInvariant();

        await using (var connection = new MySqlConnection(server.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS `{name}`; "
                + $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        server.Database = name;

        return await CreateAsync(null, server.ConnectionString, serverVersion, create, configure, cancellationToken);
    }

    private static async Task<SampleDatabase<TContext>> CreateAsync(
        MariaDbServer? server,
        string connection,
        MySqlServerVersion serverVersion,
        Func<DbContextOptions<TContext>, TContext> create,
        Action<DbContextOptionsBuilder<TContext>>? configure,
        CancellationToken cancellationToken
    )
    {
        var builder = new DbContextOptionsBuilder<TContext>().UseMySql(connection, serverVersion);
        configure?.Invoke(builder);
        var database = new SampleDatabase<TContext>(server, builder.Options);
        await using var context = create(database.Options);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return database;
    }

    private static string Setting(
        string variable,
        string fallback
    ) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
        ? value
        : fallback;
}

/// <summary>The MariaDB and MySQL servers of <c>docker/compose.yml</c> that the samples can use.</summary>
internal static class ComposeServers
{
    // WHY: The defaults of docker/compose.yml; the same variables override them there and here.
    public static Dictionary<string, int> Ports { get; } = new(StringComparer.Ordinal)
    {
        ["mariadb1011"] = 34011,
        ["mariadb114"] = 34014,
        ["mariadb118"] = 34018,
        ["mariadb123"] = 34023,
        ["mysql84"] = 34084,
        ["mysql97"] = 34097,
    };
}

/// <summary>Context factory over fixed options.</summary>
internal sealed class SampleContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : DbContext
{
    private readonly Func<DbContextOptions<TContext>, TContext> _create;
    private readonly DbContextOptions<TContext> _options;

    public SampleContextFactory(
        DbContextOptions<TContext> options,
        Func<DbContextOptions<TContext>, TContext> create
    )
    {
        _options = options;
        _create = create;
    }

    public TContext CreateDbContext() => _create(_options);
}
