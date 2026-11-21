using System.Data.Common;
using Doka.Tabular.Csv;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// EF passes exceptions of interceptors and factories through unchanged. Whatever their type, they are failures of the
/// database operation: confirmed groups stay in the report, and journal calls return a failed result.
/// </summary>
public sealed partial class JournalWindowTests
{
    [Fact]
    public async Task UnexpectedExceptionOfAQueryAfterAConfirmedGroup_KeepsTheConfirmedRows()
    {
        // Arrange
        var committed = new CommitSignal();
        var failing = new FailQueriesAfterCommit(committed);
        await using var database = await FileDatabase.CreateAsync(committed, failing);
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Hooked))
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 1 });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(3)]),
            TestMaps.Users,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        Assert.Equal((1L, TabularErrorCodes.Persistence.OperationFailed, TabularErrorKind.Technical),
            (report.RowsImported, report.FatalError?.Code, report.FatalError?.Kind));

        Assert.Equal(1, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task UnexpectedExceptionOfTheContextFactory_IsAFailedWriteNotAnEscapedException()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var target = TestMaps.Upsert(TabularDatabase.ForFactory(new ThrowingFactory()));

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(1)]), TestMaps.Users, target, Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Persistence.OperationFailed, TabularErrorKind.Technical),
            (report.FatalError?.Code, report.FatalError?.Kind));
    }

    [Theory]
    [InlineData("recover")]
    [InlineData("resolve")]
    [InlineData("delete export")]
    public async Task JournalCallsWithAFailingContextFactory_ReturnAFailedResult(
        string call
    )
    {
        // Arrange
        var journal = TabularJournal.ForFactory(new ThrowingFactory());

        // Act
        var status = call switch
        {
            "recover" => (await journal.RecoverImportAsync(Guid.NewGuid(), Token)).Status,
            "resolve" => (await journal.ResolveImportAsync(Guid.NewGuid(),
                new ImportResolution
                {
                    Outcome = ImportResolutionOutcome.Imported,
                    Reason = "Checked by hand.",
                },
                Token)).Status,
            _ => (await journal.DeleteExportAsync(Guid.NewGuid(), new MemoryFileStore(), Token)).Status,
        };

        // Assert
        Assert.Equal(JournalCallStatus.Failed, status);
    }

    [Theory]
    [InlineData(60, false)]
    [InlineData(49, true)]
    public async Task CompletionTimeoutOfTheImportLog_IsBoundedByTheTimerLimitBeforeTheData(
        int days,
        bool accepted
    )
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(TabularJournal.ForFactory(database.Plain),
                new TabularImportLogOptions
                {
                    Scope = s_scope,
                    CompletionTimeout = TimeSpan.FromDays(days),
                });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(1)]), TestMaps.Users, target, Token);

        // Assert
        await using var check = await database.PlainContextAsync();

        Assert.Equal(accepted
                ? (ReportStatus.Succeeded, 1)
                : (ReportStatus.Failed, 0),
            (report.Status, await check.Users.CountAsync(Token)));
    }

    [Fact]
    public void CompletionTimeoutOfTheExportLogBeyondTheTimerLimit_IsRejected()
    {
        // Arrange
        var journal = TabularJournal.ForFactory(new ThrowingFactory());
        var target = TabularFileTarget.To(new MemoryFileStore(), "users.csv");

        // Act
        var exception = Record.Exception(() => target.WithExportLog(journal,
            new TabularExportLogOptions
            {
                Scope = s_scope,
                CompletionTimeout = TimeSpan.FromDays(60),
            }));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void CompletionTimeoutOfTheExportLogWithinTheTimerLimit_IsAccepted()
    {
        // Arrange
        var journal = TabularJournal.ForFactory(new ThrowingFactory());
        var target = TabularFileTarget.To(new MemoryFileStore(), "users.csv");

        // Act
        var exception = Record.Exception(() => target.WithExportLog(journal,
            new TabularExportLogOptions
            {
                Scope = s_scope,
                CompletionTimeout = TimeSpan.FromDays(49),
            }));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task RetentionBeyondTheLatestDate_IsAConfigurationErrorBeforeTheFileIsStored()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(database.Plain);
        var store = new MemoryFileStore();
        var target = TabularFileTarget
            .To(store, "users.csv")
            .WithRetention(TimeSpan.MaxValue)
            .WithExportLog(journal, new TabularExportLogOptions { Scope = s_scope });

        // Act
        var report = await s_csv.ExportAsync(new[] { new UserDto(1, "a@example.org", "Ada") },
            TestMaps.Users,
            target,
            Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        Assert.Equal(TabularErrorKind.Configuration, report.FatalError?.Kind);
        Assert.Empty(store.Files);
        Assert.Equal(0,
            await journal
                .QueryExports(check)
                .CountAsync(Token));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public async Task RetentionThatPassesTheCheck_IsReadyEvenWhenTheClockMovedBeyondItsReachUntilReady(
        int secondsUntilReady,
        bool capped
    )
    {
        // Arrange
        // WHY: The retention ends one second before the latest date at the start; the clock moves while the store saves,
        // so with three seconds Ready plus the retention lies beyond the latest representable date.
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var start = time.GetUtcNow();
        var retention = DateTimeOffset.MaxValue - start - TimeSpan.FromSeconds(1);
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal
            .ForFactory(database.Plain)
            .WithOptions(new TabularJournalOptions { TimeProvider = time });

        var store = new AdvancingStore(new MemoryFileStore(), time, TimeSpan.FromSeconds(secondsUntilReady));
        var target = TabularFileTarget
            .To(store, "users.csv")
            .WithRetention(retention)
            .WithExportLog(journal, new TabularExportLogOptions { Scope = s_scope });

        // Act
        var report = await new CsvFormat(new CsvFormatOptions { TimeProvider = time }).ExportAsync(
            new[] { new UserDto(1, "a@example.org", "Ada") },
            TestMaps.Users,
            target,
            Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        var log = await journal
            .QueryExports(check)
            .SingleAsync(Token);

        Assert.Equal((null, TabularExportLogStatus.Ready), (report.FatalError?.Code, log.Status));
        Assert.Equal(capped
                ? DateTimeOffset.MaxValue
                : start + retention,
            log.ExpiresAt);
    }

    [Fact]
    public async Task RecoveryInTheSameProcess_WhileTheInputIsHashed_SeesTheRunningImport()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(database.Plain);
        ImportRecoveryState? during = null;
        var input = new ReadingStream(((MemoryStream)TestMaps.Csv("1,a@example.org,Ada")).ToArray(),
            async () =>
            {
                // ReSharper disable once AccessToDisposedClosure
                // WHY: The stream calls back during the awaited import, before the database is disposed.
                await using var context = await database.PlainContextAsync();
                var started = await journal
                    .QueryImports(context)
                    .SingleAsync(Token);

                during = (await journal.RecoverImportAsync(started.Id, Token)).State;
            });

        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(journal, new TabularImportLogOptions { Scope = s_scope });

        // Act
        // WHY: The recovery during the hashing and the one after the end are one sequence of the same attempt.
        var report = await s_csv.ImportAsync(input, TestMaps.Users, target, Token);
        var after = await journal.RecoverImportAsync(report.Journal!.EntryId, Token);

        // Assert
        Assert.Equal(ImportRecoveryState.InProgress, during);
        Assert.Equal((ReportStatus.Succeeded, ImportRecoveryState.Imported), (report.Status, after.State));
    }

    [Fact]
    public async Task RecoveryOfAnAttemptThatNeverEndedAndHoldsNoClaim_IsUnresolved()
    {
        // Arrange
        var (database, journal, importId) = await AbandonedAttemptAsync();
        await using var owned = database;

        // Act
        var recovered = await journal.RecoverImportAsync(importId, Token);

        // Assert
        Assert.Equal((JournalCallStatus.Succeeded, ImportRecoveryState.Unresolved),
            (recovered.Status, recovered.State));
    }

    [Fact]
    public async Task ResolvingAnAbandonedAttemptWithNoChanges_EndsItsLog()
    {
        // Arrange
        var (database, journal, importId) = await AbandonedAttemptAsync();
        await using var owned = database;

        // Act
        var resolved = await journal.ResolveImportAsync(importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "The server stopped while it read the file.",
            },
            Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        var log = await journal
            .QueryImports(check)
            .SingleAsync(Token);

        Assert.Equal(JournalCallStatus.Succeeded, resolved.Status);
        Assert.Equal((ImportResolutionOutcome.NoChanges, true), (log.ResolutionOutcome, log.CompletedAt is not null));
    }

    [Fact]
    public async Task RecoveryOfAnAbandonedAttemptResolvedWithNoChanges_IsNoChanges()
    {
        // Arrange
        var (database, journal, importId) = await AbandonedAttemptAsync();
        await using var owned = database;
        await journal.ResolveImportAsync(importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "The server stopped while it read the file.",
            },
            Token);

        // Act
        var recovered = await journal.RecoverImportAsync(importId, Token);

        // Assert
        Assert.Equal(ImportRecoveryState.NoChanges, recovered.State);
    }

    [Fact]
    public async Task ResolvingAnAbandonedAttemptAsImported_IsAConflict()
    {
        // Arrange
        var (database, journal, importId) = await AbandonedAttemptAsync();
        await using var owned = database;

        // Act
        var resolved = await journal.ResolveImportAsync(importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.Imported,
                Reason = "Checked by hand.",
            },
            Token);

        // Assert
        Assert.Equal(JournalCallStatus.Conflict, resolved.Status);
    }

    [Fact]
    public async Task AbandonedAttemptResolvedWithNoChanges_IsDeletedWithTheEndedLogs()
    {
        // Arrange
        var (database, journal, importId) = await AbandonedAttemptAsync();
        await using var owned = database;
        await journal.ResolveImportAsync(importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "The server stopped while it read the file.",
            },
            Token);

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal((JournalCallStatus.Succeeded, 1L), (deleted.Status, deleted.Count));
    }

    [Fact]
    public async Task AbandonedAttemptNotYetResolved_StaysForTheRecovery()
    {
        // Arrange
        var (database, journal, _) = await AbandonedAttemptAsync();
        await using var owned = database;

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal(0L, deleted.Count);
    }

    [Theory]
    [InlineData(Failure.CommitRejected, true, 1L)]
    [InlineData(Failure.CommitRejected, false, 0L)]
    [InlineData(Failure.CommitThrowsAfterwards, false, 1L)]
    public async Task UncertainAttempt_IsDeletedOnlyOnceItsOutcomeIsSettled(
        Failure failure,
        bool resolved,
        long expected
    )
    {
        // Arrange
        // WHY: A rejected commit leaves no evidence, so its outcome stays open until the application resolves it; a
        // commit that failed afterwards left its evidence, and the claim records the import.
        var (database, journal, importId) = await UncertainAttemptAsync(failure);
        await using var owned = database;
        await journal.RecoverImportAsync(importId, Token);
        if (resolved)
        {
            await journal.ResolveImportAsync(importId,
                new ImportResolution
                {
                    Outcome = ImportResolutionOutcome.NoChanges,
                    Reason = "The rollback was confirmed.",
                },
                Token);
        }

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal(expected, deleted.Count);
    }

    /// <summary>An import whose commit fails as given, so its log ends without a safe status.</summary>
    private static async Task<(FileDatabase Database, TabularJournal Journal, Guid ImportId)> UncertainAttemptAsync(
        Failure failure
    )
    {
        var faults = new TransactionFaults(failure) { Armed = true };
        var database = await FileDatabase.CreateAsync(faults);
        var journal = TabularJournal.ForFactory(database.Plain);
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(2)]),
            TestMaps.Users,
            TestMaps
                .Upsert(TabularDatabase.ForFactory(database.Hooked))
                .WithImportLog(journal, new TabularImportLogOptions { Scope = s_scope }),
            Token);

        faults.Armed = false;
        if (report.FatalError?.Code != TabularErrorCodes.Persistence.CommitUnknown)
        {
            throw new InvalidOperationException("The fault did not leave an uncertain commit.");
        }

        return (database, journal, report.Journal!.EntryId);
    }

    /// <summary>
    /// An attempt as another process leaves it after a crash while hashing: the start is stored, the claim was never
    /// taken, and the end of the log was never written.
    /// </summary>
    private static async Task<(FileDatabase Database, TabularJournal Journal, Guid ImportId)> AbandonedAttemptAsync()
    {
        var database = await FileDatabase.CreateAsync(new FailFinalImportLogSave());
        var journal = TabularJournal.ForFactory(database.Hooked);
        var bytes = ((MemoryStream)TestMaps.Csv([.. TestMaps.Rows(3)])).ToArray();
        var report = await s_csv.ImportAsync(
            new FailingSecondReadStream(bytes[..(bytes.Length / 2)], () => new IOException("The input broke off.")),
            TestMaps.Users,
            TestMaps
                .Upsert(TabularDatabase.ForFactory(database.Plain))
                .WithImportLog(journal, new TabularImportLogOptions { Scope = s_scope }),
            Token);

        return (database, journal, report.Journal!.EntryId);
    }

    /// <summary>A file store whose save takes the given time on the clock of the export.</summary>
    private sealed class AdvancingStore(
        MemoryFileStore inner,
        FakeTimeProvider time,
        TimeSpan duration
    ) : ITabularFileStore
    {
        public async Task<TabularStoredFile> SaveAsync(
            TabularFileWriteRequest request,
            Stream content,
            CancellationToken cancellationToken
        )
        {
            var stored = await inner.SaveAsync(request, content, cancellationToken);
            time.Advance(duration);

            return stored;
        }

        public Task DeleteAsync(
            Guid exportId,
            CancellationToken cancellationToken
        ) => inner.DeleteAsync(exportId, cancellationToken);
    }

    /// <summary>A stream that runs a callback before it hands out its first bytes.</summary>
    private sealed class ReadingStream : MemoryStream
    {
        private Func<Task>? _beforeFirstRead;

        public ReadingStream(
            byte[] bytes,
            Func<Task> beforeFirstRead
        )
            : base(bytes)
        {
            _beforeFirstRead = beforeFirstRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_beforeFirstRead is { } callback)
            {
                _beforeFirstRead = null;
                await callback();
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }

    /// <summary>A context factory whose connection layer fails with an I/O error, as a network share might.</summary>
    private sealed class ThrowingFactory : IDbContextFactory<TabularTestContext>
    {
        public TabularTestContext CreateDbContext() => throw new IOException("The database is not reachable.");
    }

    /// <summary>Notes the first committed transaction.</summary>
    private sealed class CommitSignal : DbTransactionInterceptor
    {
        public bool Committed { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Committed = true;

            return Task.CompletedTask;
        }
    }

    /// <summary>Fails every query with an I/O error once a transaction has committed.</summary>
    private sealed class FailQueriesAfterCommit : DbCommandInterceptor
    {
        private readonly CommitSignal _signal;

        public FailQueriesAfterCommit(
            CommitSignal signal
        )
        {
            _signal = signal;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        ) => _signal.Committed
            ? throw new IOException("The connection broke.")
            : ValueTask.FromResult(result);
    }
}
