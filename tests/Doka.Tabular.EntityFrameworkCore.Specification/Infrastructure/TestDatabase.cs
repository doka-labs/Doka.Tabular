using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>A database of one test case; providers create it empty with the test model and delete it on dispose.</summary>
public abstract class TestDatabase : IAsyncDisposable
{
    /// <summary>Gets the engine name for messages.</summary>
    /// <summary>Gets a factory of contexts with the journal registered through the context options.</summary>
    public abstract IDbContextFactory<TabularTestContext> Factory { get; }

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();

    /// <summary>Gets a factory of contexts on the same database whose options also carry the given interceptors.</summary>
    public IDbContextFactory<TabularTestContext> FactoryWith(
        params IInterceptor[] interceptors
    ) => new OptionsContextFactory(
        new DbContextOptionsBuilder<TabularTestContext>(
                Assert
                    .IsType<OptionsContextFactory>(Factory)
                    .Options)
            .AddInterceptors(interceptors)
            .Options);

    public async Task<TabularTestContext> ContextAsync()
        => await Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

    public async Task<List<User>> UsersAsync()
    {
        await using var context = await ContextAsync();

        return await context
            .Users
            .AsNoTracking()
            .OrderBy(user => user.PersonnelNumber)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task SeedAsync(
        params object[] entities
    )
    {
        await using var context = await ContextAsync();
        context.AddRange(entities);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>Creates one empty database per test case.</summary>
public interface ITestDatabaseProvider
{
    /// <summary>Gets whether a second connection can write while an import transaction is open.</summary>
    bool SupportsConcurrentWriters { get; }

    /// <summary>
    /// Gets whether the server rolls back the whole transaction when a row changed after the snapshot is written,
    /// as MariaDB does with innodb_snapshot_isolation; the row cannot be reset to its savepoint then.
    /// </summary>
    bool ConcurrentChangeAbortsTransaction => false;

    Task<TestDatabase> CreateAsync(
        CancellationToken cancellationToken
    );
}

/// <summary>Context factory over fixed options.</summary>
public sealed class OptionsContextFactory : IDbContextFactory<TabularTestContext>
{
    public OptionsContextFactory(
        DbContextOptions<TabularTestContext> options
    )
    {
        Options = options;
    }

    public DbContextOptions<TabularTestContext> Options { get; }

    public TabularTestContext CreateDbContext() => new(Options);
}
