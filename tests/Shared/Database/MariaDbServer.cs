// WHY: Linked sources compile in projects with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005
using System;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace Doka.Tabular.Testing;

/// <summary>Starts a MariaDB container from the image manifest and creates utf8mb4 databases on it.</summary>
internal sealed class MariaDbServer : IAsyncDisposable
{
    private readonly MariaDbContainer _container;

    private MariaDbServer(
        MariaDbContainer container,
        Version version
    )
    {
        _container = container;
        Version = version;
    }

    public Version Version { get; }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public static async Task<MariaDbServer> StartAsync(
        string stage,
        CancellationToken cancellationToken
    )
    {
        var image = DatabaseImages.Get(stage);
        var container = new MariaDbBuilder(image)
            .WithUsername("root")
            .Build();

        try
        {
            await container.StartAsync(cancellationToken);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }

        return new MariaDbServer(container, DatabaseImages.VersionOf(image));
    }

    /// <summary>Creates an empty database with utf8mb4 and utf8mb4_unicode_ci and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        await using (var connection = new MySqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new MySqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
