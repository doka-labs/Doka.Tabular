using Microsoft.Data.Sqlite;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>One SQLite file per test case; pooling is off so the file can be deleted after the connections close.</summary>
public sealed class SqliteProvider : ITestDatabaseProvider
{
    public bool SupportsConcurrentWriters => false;

    public async Task<TestDatabase> CreateAsync(
        CancellationToken cancellationToken
    )
    {
        var path = Path.Combine(Path.GetTempPath(), $"tabular-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
            }.ConnectionString)
            .UseTabularJournal()
            .Options;

        var database = new SqliteDatabase(path, new OptionsContextFactory(options));
        await using var context = await database.Factory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return database;
    }

    private sealed class SqliteDatabase : TestDatabase
    {
        private readonly string _path;

        public SqliteDatabase(
            string path,
            IDbContextFactory<TabularTestContext> factory
        )
        {
            Factory = factory;
            _path = path;
        }

        public override IDbContextFactory<TabularTestContext> Factory { get; }

        public override ValueTask DisposeAsync()
        {
            SqliteFile.Delete(_path);

            return ValueTask.CompletedTask;
        }
    }
}

public sealed class SqliteImportTests : ImportSpecification
{
    public SqliteImportTests()
        : base(new SqliteProvider()) { }
}

public sealed class SqliteMultiTargetTests : MultiTargetSpecification
{
    public SqliteMultiTargetTests()
        : base(new SqliteProvider()) { }
}

public sealed class SqliteJournalTests : JournalSpecification
{
    public SqliteJournalTests()
        : base(new SqliteProvider()) { }
}

public sealed class SqliteExportTests : ExportSpecification
{
    public SqliteExportTests()
        : base(new SqliteProvider()) { }
}
