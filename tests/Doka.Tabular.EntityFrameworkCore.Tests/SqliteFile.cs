using Microsoft.Data.Sqlite;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// A unique temporary SQLite file with one open connection; pooling is off, so closing the connection releases the
/// file, and disposing deletes it with its side files.
/// </summary>
internal sealed class SqliteFile : IAsyncDisposable
{
    private readonly string _path;

    private SqliteFile(
        string path
    )
    {
        _path = path;
        Connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ConnectionString);
    }

    public SqliteConnection Connection { get; }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        Delete(_path);
    }

    /// <summary>Deletes a database file together with its journal and write-ahead log files.</summary>
    public static void Delete(
        string path
    )
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            File.Delete(file);
        }
    }

    public static async Task<SqliteFile> OpenAsync(
        CancellationToken cancellationToken
    )
    {
        var file = new SqliteFile(Path.Combine(Path.GetTempPath(), $"tabular-{Guid.NewGuid():N}.db"));
        try
        {
            await file.Connection.OpenAsync(cancellationToken);

            return file;
        }
        catch
        {
            await file.DisposeAsync();

            throw;
        }
    }
}
