using System.Linq;
using Doka.Tabular.Csv;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>Export logs whose own updates fail: the export still reports, ends its log, and completes once.</summary>
public sealed partial class JournalWindowTests
{
    [Fact]
    public async Task FailingStoringUpdate_IsReportedWithoutStoringAndCompletesOnce()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailExportLogStoring());
        var completions = 0;
        var store = new MemoryFileStore();
        var target = TabularFileTarget
            .To(store, "users.csv")
            .WithExportLog(TabularJournal.ForFactory(database.Hooked), Completing(() => completions++));

        // Act
        var report = await s_csv.ExportAsync(new[] { new UserDto(1, "a@example.org", "Ada") },
            TestMaps.Users,
            target,
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorCodes.Persistence.WriteFailed),
            (report.Status, report.FatalError?.Code));

        Assert.Equal((0, 1), (store.Files.Count, completions));
    }

    [Fact]
    public async Task KnownConfigurationError_WritesNoExportLogAndRunsNoCompletion()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(database.Plain);
        var completions = 0;
        var target = TabularFileTarget
            .To(new MemoryFileStore(), "users.csv")
            .WithExportLog(journal, Completing(() => completions++));

        // Act
        var report = await s_csv.ExportAsync(new[] { new UserDto(1, "a@example.org", "Ada") },
            TestMaps.Users,
            target,
            new CsvExportOptions { ColumnKeys = [ColumnKey.Create("unknown")] },
            Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        Assert.Equal(TabularErrorKind.Configuration, report.FatalError?.Kind);
        Assert.Equal((0, 0),
            (await journal
                .QueryExports(check)
                .CountAsync(Token), completions));
    }

    [Fact]
    public async Task FailingReleaseAfterTheStartOfTheLog_EndsTheLogAndCompletes()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var completions = 0;
        var store = new MemoryFileStore();
        var target = TabularFileTarget
            .To(store, "users.csv")
            .WithExportLog(TabularJournal.ForFactory(new StartReleaseFactory(database.ConnectionString)),
                Completing(() => completions++));

        // Act
        var report = await s_csv.ExportAsync(new[] { new UserDto(1, "a@example.org", "Ada") },
            TestMaps.Users,
            target,
            Token);

        // Assert
        Assert.Equal((ReportStatus.Failed, TabularErrorCodes.Persistence.OperationFailed),
            (report.Status, report.FatalError?.Code));

        Assert.Equal((0, 1), (store.Files.Count, completions));
    }

    [Fact]
    public async Task FailingLogOfOneExpiredExport_LeavesTheOtherCandidates()
    {
        // Arrange
        var failing = new FailExportLogOf();
        await using var database = await FileDatabase.CreateAsync(failing);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var format = new CsvFormat(new CsvFormatOptions { TimeProvider = time });
        var journal = TabularJournal
            .ForFactory(database.Hooked)
            .WithOptions(new TabularJournalOptions { TimeProvider = time });

        var store = new MemoryFileStore();
        var ids = new List<Guid>();
        for (var number = 1; number <= 3; number++)
        {
            var report = await format.ExportAsync(new[] { new UserDto(number, $"u{number}@example.org", "U") },
                TestMaps.Users,
                TabularFileTarget
                    .To(store, $"users{number}.csv")
                    .WithRetention(TimeSpan.FromDays(1))
                    .WithExportLog(journal, new TabularExportLogOptions { Scope = s_scope }),
                Token);

            // WHY: The memory store names a stored file by its export identifier.
            ids.Add(report.FileId!.Value);
        }

        failing.ExportId = ids[1];
        time.Advance(TimeSpan.FromDays(2));

        // Act
        var result = await journal.DeleteExpiredExportsAsync(store, Token);

        // Assert
        Assert.Equal((JournalCallStatus.Failed, 2L, TabularErrorCodes.Persistence.WriteFailed),
            (result.Status, result.Count, result.Error?.Code));

        Assert.Equal([ids[1]], store.Files.Keys);
    }

    private static TabularExportLogOptions Completing(
        Action completed
    ) => new()
    {
        Scope = s_scope,
        OnCompletedAsync = (_, _) =>
        {
            completed();

            return Task.CompletedTask;
        },
    };

    /// <summary>Journal contexts whose release fails after they stored the start of an export log.</summary>
    private sealed class StartReleaseFactory : IDbContextFactory<StartReleaseContext>
    {
        private readonly string _connectionString;

        public StartReleaseFactory(
            string connectionString
        )
        {
            _connectionString = connectionString;
        }

        public StartReleaseContext CreateDbContext() => new(new DbContextOptionsBuilder<StartReleaseContext>()
            .UseSqlite(_connectionString)
            .UseTabularJournal()
            .Options);
    }

    private sealed class StartReleaseContext : DbContext
    {
        private bool _storedStart;

        public StartReleaseContext(
            DbContextOptions<StartReleaseContext> options
        )
            : base(options) { }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            var start = ChangeTracker
                .Entries<TabularExportLog>()
                .Any(entry => entry.State == EntityState.Added);

            var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            _storedStart |= start;

            return saved;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (_storedStart)
            {
                throw new InvalidOperationException("The disposal hook failed.");
            }
        }
    }

    /// <summary>Fails the save that marks an export as being stored.</summary>
    private sealed class FailExportLogStoring : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => eventData
            .Context!
            .ChangeTracker
            .Entries<TabularExportLog>()
            .Any(entry => entry is { State: EntityState.Modified, Entity.Status: TabularExportLogStatus.Storing })
            ? throw new DbUpdateException("The storing update failed.")
            : ValueTask.FromResult(result);
    }

    /// <summary>Fails every update of one export log, like a log that a parallel call changed.</summary>
    private sealed class FailExportLogOf : SaveChangesInterceptor
    {
        public Guid? ExportId { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => eventData
            .Context!
            .ChangeTracker
            .Entries<TabularExportLog>()
            .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == ExportId)
            ? throw new DbUpdateException("The log update failed.")
            : ValueTask.FromResult(result);
    }
}
