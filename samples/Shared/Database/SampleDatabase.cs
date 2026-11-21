// WHY: Linked sources compile in projects with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005
using System;
using System.Threading;
using System.Threading.Tasks;
using Doka.EntityFrameworkCore.MySql;
using Doka.Tabular.Testing;
using Microsoft.EntityFrameworkCore;

namespace Doka.Tabular.Samples;

/// <summary>Starts MariaDB 12.3 in a container and creates the database of a sample with utf8mb4.</summary>
internal sealed class SampleDatabase<TContext> : IAsyncDisposable
    where TContext : DbContext
{
    private readonly MariaDbServer _server;

    private SampleDatabase(
        MariaDbServer server,
        DbContextOptions<TContext> options
    )
    {
        _server = server;
        Options = options;
    }

    public DbContextOptions<TContext> Options { get; }

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    public static async Task<SampleDatabase<TContext>> StartAsync(
        Func<DbContextOptions<TContext>, TContext> create,
        Action<DbContextOptionsBuilder<TContext>>? configure,
        CancellationToken cancellationToken
    )
    {
        var server = await MariaDbServer.StartAsync("mariadb123", cancellationToken);
        try
        {
            var connection = await server.CreateDatabaseAsync("tabular_sample", cancellationToken);
            var builder =
                new DbContextOptionsBuilder<TContext>().UseMySql(connection,
                    MySqlServerVersion.MariaDb(server.Version));

            configure?.Invoke(builder);
            var database = new SampleDatabase<TContext>(server, builder.Options);
            await using var context = create(database.Options);
            await context.Database.EnsureCreatedAsync(cancellationToken);

            return database;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }
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
