namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>A database on a shared server; it is dropped on dispose.</summary>
public sealed class ServerDatabase : TestDatabase
{
    public ServerDatabase(
        IDbContextFactory<TabularTestContext> factory
    )
    {
        Factory = factory;
    }

    public override IDbContextFactory<TabularTestContext> Factory { get; }

    public override async ValueTask DisposeAsync()
    {
        await using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        await context.Database.EnsureDeletedAsync(CancellationToken.None);
    }
}
