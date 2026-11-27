using Microsoft.Extensions.Time.Testing;

namespace Doka.Tabular.EntityFrameworkCore.Specification;

public abstract partial class JournalSpecification
{
    [Fact]
    public async Task StoredExport_WithRetention_StoresTheFileWithItsId()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var retained = Retained(database);

        // Act
        var report = await retained.ExportAsync();

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.NotNull(report.FileId);
    }

    [Fact]
    public async Task StoredExport_WithRetention_IsReadyWithItsExpiry()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var retained = Retained(database);
        await retained.ExportAsync();
        await using var context = await database.ContextAsync();

        // Act
        var log = await retained
            .Journal
            .QueryExports(context)
            .SingleAsync(Token);

        // Assert
        Assert.Equal((TabularExportLogStatus.Ready, retained.Expiry), (log.Status, log.ExpiresAt!.Value));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    public async Task StoredExport_WithRetention_IsAvailableUpToTheLastTickBeforeItsExpiry(
        long ticks,
        int expected
    )
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var retained = Retained(database);
        await retained.ExportAsync();
        await using var context = await database.ContextAsync();

        // Act
        var available = await Available(retained.Journal, context, retained.Expiry.AddTicks(ticks));

        // Assert
        Assert.Equal(expected, available);
    }

    [Fact]
    public async Task ExpiredExport_RetriesAFailedDeletionAndMarksTheLogDeleted()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var retained = Retained(database);
        await retained.ExportAsync();
        retained.Time.Advance(TimeSpan.FromDays(7));

        // Act
        // WHY: The retry after the failed deletion is the checked contract, so both calls form one action.
        retained.Store.FailDeletes = true;
        var failed = await retained.Journal.DeleteExpiredExportsAsync(retained.Store, Token);
        retained.Store.FailDeletes = false;
        var done = await retained.Journal.DeleteExpiredExportsAsync(retained.Store, Token);

        // Assert
        Assert.Equal(JournalCallStatus.Failed, failed.Status);
        Assert.Equal((JournalCallStatus.Succeeded, 1L), (done.Status, done.Count));
        Assert.Empty(retained.Store.Files);
        await using var check = await database.ContextAsync();
        Assert.Equal(
            TabularExportLogStatus.Deleted,
            (await retained
                .Journal
                .QueryExports(check)
                .SingleAsync(Token)).Status);
    }

    private static RetainedExport Retained(
        TestDatabase database
    )
    {
        var time = new FakeTimeProvider(
            new DateTimeOffset(
                2026,
                11,
                17,
                8,
                0,
                0,
                TimeSpan.Zero));

        var journal = TabularJournal
            .ForFactory(database.Factory)
            .WithOptions(new TabularJournalOptions { TimeProvider = time });

        return new RetainedExport(time, journal, new MemoryFileStore());
    }

    [Fact]
    public async Task StoredExport_WithoutJournal_StoresTheFile()
    {
        // Arrange
        var store = new MemoryFileStore();

        // Act
        var report = await s_csv.ExportAsync(
            new[] { new UserDto(1, "a@example.org", "A") },
            TestMaps.Users,
            TabularFileTarget.To(store, "users.csv"),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal(store.Files.Keys.Single(), report.FileId);
    }

    /// <summary>An export with a retention of seven days on a fake clock, its journal, and its store.</summary>
    private sealed record RetainedExport(
        FakeTimeProvider Time,
        TabularJournal Journal,
        MemoryFileStore Store
    )
    {
        public DateTimeOffset Expiry { get; } = Time
            .GetUtcNow()
            .AddDays(7);

        public Task<ExportReport> ExportAsync() => new CsvFormat(new CsvFormatOptions { TimeProvider = Time })
            .ExportAsync(
                new[] { new UserDto(1, "a@example.org", "A") },
                TestMaps.Users,
                TabularFileTarget
                    .To(Store, "users.csv")
                    .WithRetention(TimeSpan.FromDays(7))
                    .WithExportLog(Journal, new TabularExportLogOptions { Scope = JournalScope.Create("users") }),
                Token);
    }
}
