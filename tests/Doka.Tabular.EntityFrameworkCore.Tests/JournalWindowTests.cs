using System.Linq;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>Journal states in the windows between saves: lost takeovers, owner registration, completion, cleanup.</summary>
public sealed partial class JournalWindowTests
{
    private static readonly CsvFormat s_csv = new(new CsvFormatOptions());

    private static readonly JournalScope s_scope = JournalScope.Create("users");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void ConflictingStandardJournalRegistrations_Throw()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TabularTestContext>().UseTabularJournal(settings
            => settings.TablePrefix = "First_");

        // Act
        var exception = Record.Exception(() => options.UseTabularJournal(settings
            => settings.TablePrefix = "Second_"));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task SameStandardJournalRegistration_CanBeRepeated()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();

        // Act
        var options = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(database.ConnectionString)
            .UseTabularJournal()
            .UseTabularJournal()
            .Options;

        // Assert
        await using var context = new TabularTestContext(options);
        Assert.Contains(context.Model.GetEntityTypes(), type => type.ClrType == typeof(TabularImportLog));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableInputStream_StoresNoImportLog(
        bool workbook
    )
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(database.Plain);
        var target = Insert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(journal, new TabularImportLogOptions { Scope = s_scope });

        // Act
        TabularErrorKind? kind;
        if (workbook)
        {
            var sheets = ExcelImport.Create();
            sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
            kind = (await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(new WriteOnlyStream(),
                sheets.Build(),
                Token)).Errors[0].Kind;
        }
        else
        {
            kind = (await s_csv.ImportAsync(new WriteOnlyStream(), TestMaps.Users, target, Token)).Errors[0].Kind;
        }

        // Assert
        await using var check = await database.PlainContextAsync();
        Assert.Equal(TabularErrorKind.Configuration, kind);
        Assert.Equal(0,
            await journal
                .QueryImports(check)
                .CountAsync(Token));
    }

    [Fact]
    public async Task CommitEvidenceOfAStrictWorkbook_CountsOnlyImportedRows()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailFinalImportLogSave());
        await database.SeedAsync(User.Create(1, "a@example.org", "Ada"));
        var journal = TabularJournal.ForFactory(database.Hooked);
        var data = TabularDatabase.ForFactory(database.Plain);
        var first = Insert(data);
        var second = Insert(data)
            .WithImportLog(journal, new TabularImportLogOptions { Scope = s_scope });

        var file = await WorkbookAsync([new UserDto(1, "a@example.org", "Ada")],
            [new UserDto(2, "b@example.org", "Bob")]);

        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, first);
        sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users, second);

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Strict },
            Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        var log = await journal
            .QueryImports(check)
            .SingleAsync(Token);

        Assert.Equal((1L, 1L), (report.RowsImported, report.RowsSkipped));
        Assert.Equal(1L, log.RowsImported);
    }

    [Fact]
    public async Task LostTakeoverOfAReleasedClaim_IsARunningDecision()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var plain = TabularJournal.ForFactory(database.Plain);
        await database.SeedAsync(User.Create(99, "a@example.org", "Taken"));
        var failed = await ImportAsync(database.Plain, plain, "1,a@example.org,Ada");
        await using (var context = await database.PlainContextAsync())
        {
            context.Users.Remove(await context.Users.SingleAsync(Token));
            await context.SaveChangesAsync(Token);
        }

        var takeover = new TakeClaimBeforeRetake(database.Plain);
        var hooked = new OptionsContextFactory(database.Options(takeover));

        // Act
        var report = await ImportAsync(hooked, TabularJournal.ForFactory(hooked), "1,a@example.org,Ada");

        // Assert
        Assert.Equal(ReportStatus.Failed, failed.Status);
        Assert.True(takeover.Fired);
        Assert.Equal(ImportJournalDecision.Running, report.Journal?.Decision);
        Assert.Null(report.FatalError);
    }

    [Fact]
    public async Task RecoveryInTheSameProcess_RightAfterTheClaim_SeesTheRunningImport()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var recovery = new RecoverAfterClaim(TabularJournal.ForFactory(database.Plain), database.Plain);
        var hooked = new OptionsContextFactory(database.Options(recovery));

        // Act
        var report = await ImportAsync(hooked, TabularJournal.ForFactory(hooked), "1,a@example.org,Ada");

        // Assert
        Assert.Equal(ImportRecoveryState.InProgress, recovery.State);
        Assert.Equal(ReportStatus.Succeeded, report.Status);
    }

    [Fact]
    public async Task RepeatedImportAfterARecoveryRightAfterTheClaim_IsSkipped()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var plain = TabularJournal.ForFactory(database.Plain);
        var hooked = new OptionsContextFactory(database.Options(new RecoverAfterClaim(plain, database.Plain)));
        await ImportAsync(hooked, TabularJournal.ForFactory(hooked), "1,a@example.org,Ada");

        // Act
        var repeated = await ImportAsync(database.Plain, plain, "1,a@example.org,Ada");

        // Assert
        Assert.Equal(ImportJournalDecision.Skipped, repeated.Journal?.Decision);
    }

    [Fact]
    public async Task CompletionFunction_RunsAfterTheOwnContextIsReleased()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var data = new TrackingFactory(database.Plain);
        bool? released = null;
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(data))
            .WithImportLog(TabularJournal.ForFactory(database.Plain),
                new TabularImportLogOptions
                {
                    Scope = s_scope,
                    OnCompletedAsync = (_, _) =>
                    {
                        released = data.AllDisposed;

                        return Task.CompletedTask;
                    },
                });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.True(released);
    }

    [Fact]
    public async Task CompletionFunctionBeyondItsTimeout_IsAwaitedBeforeTheCallReturns()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var journal = TabularJournal
            .ForFactory(database.Plain)
            .WithOptions(new TabularJournalOptions { TimeProvider = time });

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        var target = TabularFileTarget
            .To(new MemoryFileStore(), "users.csv")
            .WithExportLog(journal,
                new TabularExportLogOptions
                {
                    Scope = s_scope,
                    CompletionTimeout = TimeSpan.FromSeconds(30),
                    OnCompletedAsync = async (_, token) =>
                    {
                        try
                        {
                            await using (token.Register(() => canceled.TrySetResult()))
                            {
                                started.SetResult();
                                await canceled.Task;
                            }
                        }
                        finally
                        {
                            await release.Task;
                            finished = true;
                        }
                    },
                });

        // WHY: The timer of the completion function runs on the clock of the format.
        var format = new CsvFormat(new CsvFormatOptions { TimeProvider = time });

        // Act
        // WHY: The held completion function and its release are one concurrency scenario.
        var export = format.ExportAsync(new[] { new UserDto(1, "a@example.org", "Ada") },
            TestMaps.Users,
            target,
            Token);

        await started.Task;
        time.Advance(TimeSpan.FromSeconds(31));
        await canceled.Task;
        var returnedWhileHeld = export.IsCompleted;
        release.SetResult();
        var report = await export;

        // Assert
        Assert.False(returnedWhileHeld);
        Assert.True(finished);
        Assert.Equal(TabularErrorCodes.Callback.Failed,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task DeletingAnExportWhileItIsStored_IsAConflictAndTheExportStaysReady()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(database.Plain);
        var store = new PausingStore();
        var target = TabularFileTarget
            .To(store, "users.csv")
            .WithExportLog(journal, new TabularExportLogOptions { Scope = s_scope });

        // Act
        // WHY: The deletion during the storing and the end of the export are one concurrency scenario.
        var export = s_csv.ExportAsync(new[] { new UserDto(1, "a@example.org", "Ada") },
            TestMaps.Users,
            target,
            Token);

        var exportId = await store.Saving.Task;
        var deleted = await journal.DeleteExportAsync(exportId, store, Token);
        store.Release.SetResult();
        var report = await export;

        // Assert
        await using var check = await database.PlainContextAsync();
        var log = await journal
            .QueryExports(check)
            .SingleAsync(Token);

        Assert.Equal(JournalCallStatus.Conflict, deleted.Status);
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal((TabularExportLogStatus.Ready, false), (log.Status, store.Deleted));
    }

    [Fact]
    public async Task FailingReleaseOfTheOwnContext_IsTheFatalErrorAndTheCompletionStillRuns()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var completed = false;
        var target = TabularDatabase
            .ForFactory(new ThrowingDisposeFactory(database.ConnectionString))
            .PrepareUpsert<UserDto, User>(set: context => context.Users,
                match: TestMaps.UserMatch,
                create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
                update: (_, _) => { })
            .WithImportLog(TabularJournal.ForFactory(database.Plain),
                new TabularImportLogOptions
                {
                    Scope = s_scope,
                    OnCompletedAsync = (_, _) =>
                    {
                        completed = true;

                        return Task.CompletedTask;
                    },
                });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        Assert.Equal(TabularErrorCodes.Persistence.OperationFailed, report.FatalError?.Code);
        Assert.True(completed);
        Assert.Equal(1, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task ExportLeftStoringByJournalFailures_StaysStoring()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailExportLogEnd());
        await ExportThroughTheHookedJournalAsync(database);
        await using var check = await database.PlainContextAsync();

        // Act
        var log = await TabularJournal
            .ForFactory(database.Plain)
            .QueryExports(check)
            .SingleAsync(Token);

        // Assert
        Assert.Equal(TabularExportLogStatus.Storing, log.Status);
    }

    [Fact]
    public async Task ExportLeftStoring_IsNotDeletedWithoutAllowingUnfinishedExports()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailExportLogEnd());
        var (store, logId) = await ExportThroughTheHookedJournalAsync(database);
        var plain = TabularJournal.ForFactory(database.Plain);

        // Act
        var refused = await plain.DeleteExportAsync(logId, store, Token);

        // Assert
        Assert.Equal(JournalCallStatus.Conflict, refused.Status);
        Assert.NotEmpty(store.Files);
    }

    [Fact]
    public async Task ExportLeftStoring_IsDeletedWhenUnfinishedExportsAreAllowed()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailExportLogEnd());
        var (store, logId) = await ExportThroughTheHookedJournalAsync(database);
        var plain = TabularJournal.ForFactory(database.Plain);

        // Act
        var allowed = await plain.DeleteExportAsync(logId,
            store,
            new ExportDeletionOptions { AllowUnfinished = true },
            Token);

        // Assert
        Assert.Equal(JournalCallStatus.Succeeded, allowed.Status);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task ExportLeftStoringAndDeleted_IsPrunedWithTheLogs()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailExportLogEnd());
        var (store, logId) = await ExportThroughTheHookedJournalAsync(database);
        var plain = TabularJournal.ForFactory(database.Plain);
        await plain.DeleteExportAsync(logId,
            store,
            new ExportDeletionOptions { AllowUnfinished = true },
            Token);

        // Act
        var pruned = await plain.DeleteExportLogsAsync(DateTimeOffset.UtcNow.AddDays(1), Token);

        // Assert
        Assert.Equal(1L, pruned.Count);
    }

    /// <summary>Exports one file with the journal whose saves the database interceptor fails.</summary>
    private static async Task<(MemoryFileStore Store, Guid LogId)> ExportThroughTheHookedJournalAsync(
        FileDatabase database
    )
    {
        var journal = TabularJournal.ForFactory(database.Hooked);
        var store = new MemoryFileStore();
        var target = TabularFileTarget
            .To(store, "users.csv")
            .WithExportLog(journal, new TabularExportLogOptions { Scope = s_scope });

        await s_csv.ExportAsync(new[] { new UserDto(1, "a@example.org", "Ada") }, TestMaps.Users, target, Token);
        await using var check = await database.PlainContextAsync();
        var log = await journal
            .QueryExports(check)
            .AsNoTracking()
            .SingleAsync(Token);

        return (store, log.Id);
    }

    [Fact]
    public async Task FailedReadyLogSave_LeavesAFailedLogThatRefersToTheStoredFile()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailReadyExportLogSave());
        await ExportThroughTheHookedJournalAsync(database);
        await using var check = await database.PlainContextAsync();

        // Act
        var log = await TabularJournal
            .ForFactory(database.Plain)
            .QueryExports(check)
            .SingleAsync(Token);

        // Assert
        Assert.Equal((TabularExportLogStatus.Failed, true), (log.Status, log.FileId.HasValue));
    }

    [Fact]
    public async Task LogCleanup_KeepsAFailedLogThatStillRefersToAStoredFile()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailReadyExportLogSave());
        _ = await ExportThroughTheHookedJournalAsync(database);
        var journal = TabularJournal.ForFactory(database.Hooked);

        // Act
        var pruned = await journal.DeleteExportLogsAsync(DateTimeOffset.UtcNow.AddDays(1), Token);

        // Assert
        Assert.Equal(0L, pruned.Count);
    }

    [Fact]
    public async Task FailedLogThatRefersToAStoredFile_IsDeletedWithItsFile()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailReadyExportLogSave());
        var (store, logId) = await ExportThroughTheHookedJournalAsync(database);
        var journal = TabularJournal.ForFactory(database.Hooked);

        // Act
        var deleted = await journal.DeleteExportAsync(logId, store, Token);

        // Assert
        Assert.Equal(JournalCallStatus.Succeeded, deleted.Status);
        Assert.Empty(store.Files);
    }

    [Fact]
    public async Task ExpiredCleanup_RetriesAFailedDeletionOfAFailedExport()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync(new FailReadyExportLogSave());
        var (store, logId) = await ExportThroughTheHookedJournalAsync(database);
        var journal = TabularJournal.ForFactory(database.Hooked);

        // Act
        // WHY: The retry after the failed deletion is the checked contract, so both calls form one action.
        store.FailDeletes = true;
        var failed = await journal.DeleteExportAsync(logId, store, Token);
        store.FailDeletes = false;
        var cleaned = await journal.DeleteExpiredExportsAsync(store, Token);

        // Assert
        Assert.Equal(JournalCallStatus.Failed, failed.Status);
        Assert.Equal((JournalCallStatus.Succeeded, 1L), (cleaned.Status, cleaned.Count));
        Assert.Empty(store.Files);
    }

    private static Task<ImportReport<UserDto>> ImportAsync(
        IDbContextFactory<TabularTestContext> data,
        TabularJournal journal,
        string csv
    ) => s_csv.ImportAsync(TestMaps.Csv(csv),
        TestMaps.Users,
        TestMaps
            .Upsert(TabularDatabase.ForFactory(data))
            .WithImportLog(journal, new TabularImportLogOptions { Scope = s_scope }),
        new CsvImportOptions { Strategy = ImportStrategy.Strict },
        Token);

    private static TabularWriteTarget<UserDto, User> Insert(
        TabularDatabase<TabularTestContext> data
    ) => data.PrepareInsert<UserDto, User>(set: context => context.Users,
        create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
        match: TestMaps.UserMatch);

    private static async Task<MemoryStream> WorkbookAsync(
        UserDto[] first,
        UserDto[] second
    )
    {
        var sheets = ExcelExport.Create();
        sheets.Add(TabularSheetName.Create("First"), first, TestMaps.Users);
        sheets.Add(TabularSheetName.Create("Second"), second, TestMaps.Users);
        var file = new MemoryStream();
        await new ExcelFormat(new ExcelFormatOptions()).ExportAsync(sheets.Build(), file, Token);
        file.Position = 0;

        return file;
    }

    /// <summary>A SQLite file with the test model; contexts with or without the interceptors of the test case.</summary>
    /// <summary>A stream that can only be written, like a file opened for writing.</summary>
    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }

    private sealed class FileDatabase : IAsyncDisposable
    {
        private readonly string _path;

        private FileDatabase(
            string path,
            IInterceptor[] interceptors
        )
        {
            _path = path;
            Plain = new OptionsContextFactory(Options());
            Hooked = new OptionsContextFactory(Options(interceptors));
        }

        public string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Pooling = false,
        }.ConnectionString;

        public IDbContextFactory<TabularTestContext> Plain { get; }

        public IDbContextFactory<TabularTestContext> Hooked { get; }

        public ValueTask DisposeAsync()
        {
            SqliteFile.Delete(_path);

            return ValueTask.CompletedTask;
        }

        public static async Task<FileDatabase> CreateAsync(
            params IInterceptor[] interceptors
        )
        {
            var database = new FileDatabase(Path.Combine(Path.GetTempPath(), $"tabular-{Guid.NewGuid():N}.db"),
                interceptors);

            await using var context = await database.PlainContextAsync();
            await context.Database.EnsureCreatedAsync(Token);

            return database;
        }

        public DbContextOptions<TabularTestContext> Options(
            params IInterceptor[] interceptors
        ) => new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(ConnectionString)
            .UseTabularJournal()
            .AddInterceptors(interceptors)
            .Options;

        public async Task<TabularTestContext> PlainContextAsync() => await Plain.CreateDbContextAsync(Token);

        public async Task SeedAsync(
            params object[] entities
        )
        {
            await using var context = await PlainContextAsync();
            context.AddRange(entities);
            await context.SaveChangesAsync(Token);
        }
    }

    /// <summary>Contexts on the user table whose disposal fails, like a failing disposal hook of the application.</summary>
    private sealed class ThrowingDisposeFactory : IDbContextFactory<ThrowingDisposeContext>
    {
        private readonly string _connectionString;

        public ThrowingDisposeFactory(
            string connectionString
        )
        {
            _connectionString = connectionString;
        }

        // WHY: The journal gate writes its claim through the data context, so the journal belongs to its model.
        public ThrowingDisposeContext CreateDbContext() => new(new DbContextOptionsBuilder<ThrowingDisposeContext>()
            .UseSqlite(_connectionString)
            .UseTabularJournal()
            .Options);
    }

    private sealed class ThrowingDisposeContext : DbContext
    {
        public ThrowingDisposeContext(
            DbContextOptions<ThrowingDisposeContext> options
        )
            : base(options) { }

        public DbSet<User> Users => Set<User>();

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();

            throw new InvalidOperationException("The disposal hook failed.");
        }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder
        )
        {
            var users = modelBuilder.Entity<User>();
            users.HasKey(user => user.Id);
            users
                .HasIndex(user => user.PersonnelNumber)
                .IsUnique();

            users
                .Property(user => user.Version)
                .IsConcurrencyToken();
        }
    }

    /// <summary>A store that pauses in the save until the test releases it.</summary>
    private sealed class PausingStore : ITabularFileStore
    {
        public TaskCompletionSource<Guid> Saving { get; } = new();

        public TaskCompletionSource Release { get; } = new();

        public bool Deleted { get; private set; }

        public async Task<TabularStoredFile> SaveAsync(
            TabularFileWriteRequest request,
            Stream content,
            CancellationToken cancellationToken
        )
        {
            Saving.SetResult(request.ExportId);
            await Release.Task;

            return new TabularStoredFile(request.ExportId, request.ExportId, request.ByteSize, request.Sha256);
        }

        public Task DeleteAsync(
            Guid exportId,
            CancellationToken cancellationToken
        )
        {
            Deleted = true;

            return Task.CompletedTask;
        }
    }

    /// <summary>A factory that remembers its contexts so a test can check whether all of them were disposed.</summary>
    private sealed class TrackingFactory : IDbContextFactory<TabularTestContext>
    {
        private readonly List<TabularTestContext> _created = [];
        private readonly IDbContextFactory<TabularTestContext> _inner;

        public TrackingFactory(
            IDbContextFactory<TabularTestContext> inner
        )
        {
            _inner = inner;
        }

        public bool AllDisposed => _created.Count > 0 && _created.All(IsDisposed);

        public TabularTestContext CreateDbContext()
        {
            var context = _inner.CreateDbContext();
            _created.Add(context);

            return context;
        }

        private static bool IsDisposed(
            TabularTestContext context
        )
        {
            try
            {
                _ = context.Model;

                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }

    /// <summary>Fails the save that completes the import log, after the data commit.</summary>
    private sealed class FailFinalImportLogSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => eventData
            .Context!
            .ChangeTracker
            .Entries<TabularImportLog>()
            .Any(entry => entry is { State: EntityState.Modified, Entity.Status: not null })
            ? throw new DbUpdateException("The final log save failed.")
            : ValueTask.FromResult(result);
    }

    /// <summary>Fails every save after the file was stored, like a journal database that fails at the end.</summary>
    private sealed class FailExportLogEnd : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => eventData
            .Context!
            .ChangeTracker
            .Entries<TabularExportLog>()
            .Any(entry => entry is
            {
                State: EntityState.Modified,
                Entity.Status: TabularExportLogStatus.Ready or TabularExportLogStatus.Failed,
            })
            ? throw new DbUpdateException("The journal database is unavailable.")
            : ValueTask.FromResult(result);
    }

    /// <summary>Fails the save that marks the export ready, after the file was stored.</summary>
    private sealed class FailReadyExportLogSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => eventData
            .Context!
            .ChangeTracker
            .Entries<TabularExportLog>()
            .Any(entry => entry is { State: EntityState.Modified, Entity.Status: TabularExportLogStatus.Ready })
            ? throw new DbUpdateException("The ready update failed.")
            : ValueTask.FromResult(result);
    }

    /// <summary>Lets another attempt take the released claim right before this attempt saves its takeover.</summary>
    private sealed class TakeClaimBeforeRetake : SaveChangesInterceptor
    {
        private readonly IDbContextFactory<TabularTestContext> _other;

        public TakeClaimBeforeRetake(
            IDbContextFactory<TabularTestContext> other
        )
        {
            _other = other;
        }

        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (Fired
                || eventData
                    .Context!
                    .ChangeTracker
                    .Entries<TabularImportClaim>()
                    .All(entry => entry.State != EntityState.Modified))
            {
                return result;
            }

            Fired = true;
            await using var other = await _other.CreateDbContextAsync(cancellationToken);
            await TabularJournal
                .ForFactory(_other)
                .QueryImportClaims(other)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(claim => claim.CurrentImportId, Guid.NewGuid())
                        .SetProperty(claim => EF.Property<Guid>(claim, "Version"), Guid.NewGuid()),
                    cancellationToken);

            return result;
        }
    }

    /// <summary>Runs a recovery of the import right after its claim was stored.</summary>
    private sealed class RecoverAfterClaim : SaveChangesInterceptor
    {
        private readonly IDbContextFactory<TabularTestContext> _factory;
        private readonly TabularJournal _journal;

        public RecoverAfterClaim(
            TabularJournal journal,
            IDbContextFactory<TabularTestContext> factory
        )
        {
            _journal = journal;
            _factory = factory;
        }

        public ImportRecoveryState? State { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (State is not null
                || !eventData
                    .Context!
                    .ChangeTracker
                    .Entries<TabularImportClaim>()
                    .Any())
            {
                return result;
            }

            await using var context = await _factory.CreateDbContextAsync(cancellationToken);
            var importId = await _journal
                .QueryImports(context)
                .Select(log => log.Id)
                .SingleAsync(cancellationToken);

            State = (await _journal.RecoverImportAsync(importId, cancellationToken)).State;

            return result;
        }
    }
}
