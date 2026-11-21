using Doka.Tabular.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Doka.Tabular.Benchmarks;

/// <summary>The stored form of a standard record.</summary>
public sealed class ProfileEntity
{
    private ProfileEntity() { }

    public long RowId { get; private init; }

    public int Id { get; private init; }

    public string Name { get; private init; } = string.Empty;

    public decimal Amount { get; private init; }

    public DateOnly Date { get; private init; }

    public bool Active { get; private init; }

    public string PostalCode { get; private init; } = string.Empty;

    public Guid Key { get; private init; }

    public static ProfileEntity From(
        ProfileRow row
    ) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Amount = row.Amount,
        Date = row.Date,
        Active = row.Active,
        PostalCode = row.PostalCode,
        Key = row.Key,
    };
}

public sealed class ProfileContext : DbContext
{
    public ProfileContext(
        DbContextOptions<ProfileContext> options
    )
        : base(options) { }

    public DbSet<ProfileEntity> Entries => Set<ProfileEntity>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder
    ) => modelBuilder
        .Entity<ProfileEntity>()
        .HasKey(entry => entry.RowId);
}

/// <summary>
/// A SQLite database in a unique temporary file with pooling off, so the file can be deleted after the connections
/// close; every measured import writes into a new one.
/// </summary>
public sealed class ProfileDatabase : IDbContextFactory<ProfileContext>, IDisposable
{
    private readonly DbContextOptions<ProfileContext> _options;
    private readonly string _path;

    private ProfileDatabase(
        string path
    )
    {
        _path = path;
        _options = new DbContextOptionsBuilder<ProfileContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ConnectionString)
            .Options;
    }

    public ProfileContext CreateDbContext() => new(_options);

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm", _path + "-journal" })
        {
            File.Delete(file);
        }
    }

    public static ProfileDatabase Create()
    {
        var database = new ProfileDatabase(Path.Combine(Path.GetTempPath(), $"tabular-profile-{Guid.NewGuid():N}.db"));
        using var context = database.CreateDbContext();
        context.Database.EnsureCreated();

        return database;
    }

    /// <summary>The target of the standard records.</summary>
    public TabularWriteTarget<ProfileRow> Target() => TabularDatabase
        .ForFactory(this)
        .PrepareInsert<ProfileRow, ProfileEntity>(set: context => context.Entries, create: ProfileEntity.From);

    public int Count()
    {
        using var context = CreateDbContext();

        return context.Entries.Count();
    }
}

/// <summary>A file store in memory, so a measurement of the file target contains no disk access of the store.</summary>
public sealed class MemoryStore : ITabularFileStore
{
    public Dictionary<Guid, byte[]> Files { get; } = [];

    public async Task<TabularStoredFile> SaveAsync(
        TabularFileWriteRequest request,
        Stream content,
        CancellationToken cancellationToken
    )
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[request.ExportId] = copy.ToArray();

        return new TabularStoredFile(request.ExportId, request.ExportId, request.ByteSize, request.Sha256);
    }

    public Task DeleteAsync(
        Guid exportId,
        CancellationToken cancellationToken
    )
    {
        Files.Remove(exportId);

        return Task.CompletedTask;
    }
}
