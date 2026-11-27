using System.Security.Cryptography;

namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>Import and export journals: duplicate protection, recovery, resolution, storage, retention, cleanup.</summary>
public abstract partial class JournalSpecification
{
    private static readonly CsvFormat s_csv = new(new CsvFormatOptions());

    private readonly ITestDatabaseProvider _provider;

    protected JournalSpecification(
        ITestDatabaseProvider provider
    )
    {
        _provider = provider;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstImportOfAFile_IsProcessed()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);

        // Act
        var first = await ImportAsync(database, journal, "users", "first.csv", null);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, first.Status);
        Assert.Equal(ImportJournalDecision.Processed, first.Journal!.Decision);
    }

    [Fact]
    public async Task SameBytesUnderAnotherName_AreSkippedAndReferToTheFirstImport()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var completed = new List<TabularImportCompleted>();
        var first = await ImportAsync(database, journal, "users", "first.csv", completed);

        // Act
        var second = await ImportAsync(database, journal, "users", "renamed.csv", completed);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, second.Status);
        Assert.Equal(ImportJournalDecision.Skipped, second.Journal!.Decision);
        Assert.Equal(first.Journal!.EntryId, second.Journal.ExistingImportId);
        Assert.Equal(0, second.RowsRead);
        Assert.Equal(
            [ImportJournalDecision.Processed, ImportJournalDecision.Skipped],
            completed.Select(entry => entry.Decision));
    }

    [Fact]
    public async Task LogOfTheFirstImport_StaysImportedAfterASkippedDuplicate()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var first = await ImportAsync(database, journal, "users", "first.csv", null);
        await ImportAsync(database, journal, "users", "renamed.csv", null);
        await using var context = await database.ContextAsync();

        // Act
        var log = await journal
            .QueryImports(context)
            .SingleAsync(entry => entry.Id == first.Journal!.EntryId, Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, 2L), (log.Status!.Value, log.RowsImported));
        Assert.NotNull(log.ImportedAt);
    }

    [Fact]
    public async Task SameBytesInAnotherScope_AreImportedAgain()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        await ImportAsync(database, journal, "users", "a.csv", null);

        // Act
        var report = await ImportAsync(database, journal, "other", "a.csv", null);

        // Assert
        Assert.Equal(ImportJournalDecision.Processed, report.Journal!.Decision);
    }

    [Theory]
    [InlineData("default", false)]
    [InlineData("caseless", true)]
    public async Task ScopeCase_IsComparedAsRegistered(
        string journalName,
        bool sameScope
    )
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = journalName == "default"
            ? TabularJournal.ForFactory(database.Factory)
            : TabularJournal.ForFactory(database.Factory, JournalName.Create(journalName));

        await ImportAsync(database, journal, "Users", "a.csv", null);

        // Act
        var report = await ImportAsync(database, journal, "users", "a.csv", null);

        // Assert
        Assert.Equal(
            sameScope
                ? ImportJournalDecision.Skipped
                : ImportJournalDecision.Processed,
            report.Journal!.Decision);
    }

    [Fact]
    public async Task RejectBehavior_ReportsAValidationErrorForTheDuplicate()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        await ImportAsync(database, journal, "users", "a.csv", null);

        // Act
        var report = await ImportAsync(
            database,
            journal,
            "users",
            "a.csv",
            null,
            behavior: ImportDuplicateBehavior.Reject);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(ImportJournalDecision.RejectedDuplicate, report.Journal!.Decision);
        Assert.Equal(
            TabularErrorCodes.Validation.Failed,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task StrictRollback_ReleasesTheClaimForANewAttempt()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        await database.SeedAsync(User.Create(99, "user1@example.org", "Taken"));

        await ImportAsync(database, journal, "users", "a.csv", null);
        await using (var context = await database.ContextAsync())
        {
            context.Users.Remove(await context.Users.SingleAsync(Token));
            await context.SaveChangesAsync(Token);
        }

        // Act
        var retried = await ImportAsync(database, journal, "users", "a.csv", null);

        // Assert
        Assert.Equal(ImportJournalDecision.Processed, retried.Journal!.Decision);
        Assert.Equal(ReportStatus.Succeeded, retried.Status);
    }

    [Fact]
    public async Task NamedJournal_DoesNotRecordInTheStandardJournal()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var named = TabularJournal.ForFactory(database.Factory, JournalName.Create("userImports"));

        // Act
        var report = await ImportAsync(database, named, "users", "a.csv", null);

        // Assert
        await using var context = await database.ContextAsync();
        Assert.True(
            await named
                .QueryImports(context)
                .AnyAsync(entry => entry.Id == report.Journal!.EntryId, Token));

        Assert.False(
            await TabularJournal
                .ForFactory(database.Factory)
                .QueryImports(context)
                .AnyAsync(Token));
    }

    [Fact]
    public async Task UnknownJournalName_IsAConfigurationErrorWithoutWriting()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);

        // Act
        var report = await ImportAsync(
            database,
            TabularJournal.ForFactory(database.Factory, JournalName.Create("missing")),
            "users",
            "a.csv",
            null);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);

        Assert.Empty(await database.UsersAsync());
    }

    [Fact]
    public async Task Journal_WithJoinExistingTransaction_IsAConfigurationError()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await using var context = await database.ContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var destination = TestMaps
            .Upsert(TabularDatabase.ForContext(context))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true })
            .WithImportLog(
                TabularJournal.ForFactory(database.Factory),
                new TabularImportLogOptions { Scope = JournalScope.Create("users") });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(1)]), TestMaps.Users, destination, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task CompletionFunction_Exception_KeepsTheCommitAndIsReportedSeparately()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var destination = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Factory))
            .WithImportLog(
                TabularJournal.ForFactory(database.Factory),
                new TabularImportLogOptions
                {
                    Scope = JournalScope.Create("users"),
                    OnCompletedAsync = (_, _) => throw new InvalidOperationException("publisher down"),
                });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(2)]), TestMaps.Users, destination, Token);

        // Assert
        Assert.Equal(2, report.RowsImported);
        Assert.Equal(
            TabularErrorCodes.Callback.Failed,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(2, (await database.UsersAsync()).Count);
    }

    [Fact]
    public async Task Recover_ClaimWithoutEvidence_IsUnresolved()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: false);

        // Act
        var recovery = await journal.RecoverImportAsync(importId, Token);

        // Assert
        Assert.Equal(
            (JournalCallStatus.Succeeded, ImportRecoveryState.Unresolved),
            (recovery.Status, recovery.State!.Value));
    }

    [Fact]
    public async Task ImportOfAFileWithAnUnresolvedClaim_IsRunning()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        await CrashedImportAsync(database, journal, committed: false);

        // Act
        var blocked = await ImportAsync(database, journal, "users", "a.csv", null);

        // Assert
        Assert.Equal(ImportJournalDecision.Running, blocked.Journal!.Decision);
    }

    [Fact]
    public async Task ResolveNoChanges_OfAClaimWithoutEvidence_Succeeds()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: false);
        await journal.RecoverImportAsync(importId, Token);

        // Act
        var resolution = await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Rollback confirmed.",
            },
            Token);

        // Assert
        Assert.Equal(JournalCallStatus.Succeeded, resolution.Status);
    }

    [Fact]
    public async Task SecondResolution_OfAResolvedClaim_IsAConflict()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: false);
        await journal.RecoverImportAsync(importId, Token);
        await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Rollback confirmed.",
            },
            Token);

        // Act
        var second = await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Again.",
            },
            Token);

        // Assert
        Assert.Equal(JournalCallStatus.Conflict, second.Status);
    }

    [Fact]
    public async Task ImportAfterAResolutionWithNoChanges_IsProcessedAgain()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: false);
        await journal.RecoverImportAsync(importId, Token);
        await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Rollback confirmed.",
            },
            Token);

        // Act
        var retried = await ImportAsync(database, journal, "users", "a.csv", null);

        // Assert
        Assert.Equal(ImportJournalDecision.Processed, retried.Journal!.Decision);
    }

    [Fact]
    public async Task Recover_ClaimWithEvidence_IsImported()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: true);

        // Act
        var recovery = await journal.RecoverImportAsync(importId, Token);

        // Assert
        Assert.Equal(ImportRecoveryState.Imported, recovery.State);
    }

    [Fact]
    public async Task ResolveNoChanges_OfAClaimWithEvidence_IsAConflict()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: true);
        await journal.RecoverImportAsync(importId, Token);

        // Act
        var resolution = await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Wrong.",
            },
            Token);

        // Assert
        Assert.Equal(JournalCallStatus.Conflict, resolution.Status);
    }

    [Fact]
    public async Task ResolveImported_OfACrashedImportWithEvidence_EndsItsLog()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: true);
        await journal.RecoverImportAsync(importId, Token);

        // Act
        var resolution =
            await journal.ResolveImportAsync(importId, Imported("The server crashed after a commit."), Token);

        // Assert
        await using var context = await database.ContextAsync();
        var log = await journal
            .QueryImports(context)
            .SingleAsync(Token);

        Assert.Equal(JournalCallStatus.Succeeded, resolution.Status);
        Assert.Equal((true, ImportResolutionOutcome.Imported), (log.CompletedAt is not null, log.ResolutionOutcome));
        Assert.Equal(
            ImportClaimState.Imported,
            (await journal
                .QueryImportClaims(context)
                .SingleAsync(Token)).State);
    }

    [Fact]
    public async Task CrashedImportWithEvidence_ResolvedAsImported_IsDeletedWithTheEndedLogs()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: true);
        await journal.ResolveImportAsync(importId, Imported("The server crashed after a commit."), Token);

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal((JournalCallStatus.Succeeded, 1L), (deleted.Status, deleted.Count));
    }

    [Fact]
    public async Task ResolveImported_ASecondTime_IsAConflict()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: true);
        await journal.ResolveImportAsync(importId, Imported("First."), Token);

        // Act
        var second = await journal.ResolveImportAsync(importId, Imported("Second."), Token);

        // Assert
        Assert.Equal(JournalCallStatus.Conflict, second.Status);
    }

    [Fact]
    public async Task ResolveImported_OfAnImportThatEndedItself_IsAConflict()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(2)]),
            TestMaps.Users,
            TestMaps
                .Upsert(TabularDatabase.ForFactory(database.Factory))
                .WithImportLog(journal, new TabularImportLogOptions { Scope = JournalScope.Create("users") }),
            Token);

        // Act
        var resolution = await journal.ResolveImportAsync(report.Journal!.EntryId, Imported("Late."), Token);

        // Assert
        Assert.Equal(JournalCallStatus.Conflict, resolution.Status);
    }

    [Fact]
    public async Task ResolveImported_WhileTheImportRunsInThisProcess_IsAConflictAndTheImportEnds()
    {
        // Arrange
        Assert.SkipUnless(
            _provider.SupportsConcurrentWriters,
            "The engine serializes writers; another call cannot write while the data transaction is open.");

        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        JournalCallStatus? during = null;
        var destination = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: dto =>
                {
                    if (dto.PersonnelNumber == 2)
                    {
                        during = ResolveDuringTheImport();
                    }

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                },
                match: TestMaps.UserMatch)
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 1 })
            .WithImportLog(journal, new TabularImportLogOptions { Scope = JournalScope.Create("users") });

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(3)]),
            TestMaps.Users,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(JournalCallStatus.Conflict, during);
        Assert.Equal((ReportStatus.Succeeded, 3L), (report.Status, report.RowsImported));

        return;

        JournalCallStatus ResolveDuringTheImport()
        {
            // ReSharper disable once AccessToDisposedClosure
            // WHY: The callback runs during the awaited import, before the captured database is disposed.
            using var other = database.Factory.CreateDbContext();
            var importId = other
                .Set<TabularImportLog>(JournalModel.ImportLog(journal.Name))
                .Single()
                .Id;

            // WHY: The callback of the row is synchronous; the call waits for the resolution in its own context.
            return journal
                .ResolveImportAsync(importId, Imported("Too early."), Token)
                .GetAwaiter()
                .GetResult()
                .Status;
        }
    }

    [Theory]
    [InlineData(1, ImportResolutionOutcome.NoChanges)]
    [InlineData(2, ImportResolutionOutcome.Imported)]
    public async Task ResolveOfAnUnresolvedClaim_WhileTheImportRunsInThisProcess_IsAConflictAndTheImportEnds(
        int atRow,
        ImportResolutionOutcome outcome
    )
    {
        // Arrange
        // WHY: A recovery in another process takes the running import for crashed and marks its claim unresolved;
        // this process knows that the import still runs, so a resolution here must not end it.
        Assert.SkipUnless(
            _provider.SupportsConcurrentWriters,
            "The engine serializes writers; another call cannot write while the data transaction is open.");

        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        JournalCallStatus? during = null;
        var destination = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: dto =>
                {
                    if (dto.PersonnelNumber == atRow)
                    {
                        during = ResolveAfterAForeignRecovery();
                    }

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                },
                match: TestMaps.UserMatch)
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 1 })
            .WithImportLog(journal, new TabularImportLogOptions { Scope = JournalScope.Create("users") });

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(3)]),
            TestMaps.Users,
            destination,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        // WHY: An engine that aborts the transaction at a concurrent change rejects the next evidence write of the
        // import, since the foreign recovery changed its claim; the groups before that change stay committed.
        Assert.Equal(JournalCallStatus.Conflict, during);
        Assert.Equal(
            _provider.ConcurrentChangeAbortsTransaction
                ? (ReportStatus.Failed, atRow - 1L, TabularErrorCodes.Persistence.WriteFailed)
                : (ReportStatus.Succeeded, 3L, null),
            (report.Status, report.RowsImported, report.FatalError?.Code));

        return;

        JournalCallStatus ResolveAfterAForeignRecovery()
        {
            // ReSharper disable once AccessToDisposedClosure
            // WHY: The callback runs during the awaited import, before the captured database is disposed.
            using var other = database.Factory.CreateDbContext();
            var claim = other
                .Set<TabularImportClaim>(JournalModel.ImportClaim(journal.Name))
                .Single();

            claim.Set(ImportClaimState.Unresolved, DateTimeOffset.UtcNow);
            JournalStore.Touch(other, claim);
            other.SaveChanges();

            // WHY: The callback of the row is synchronous; the call waits for the resolution in its own context.
            return journal
                .ResolveImportAsync(
                    claim.CurrentImportId,
                    new ImportResolution
                    {
                        Outcome = outcome,
                        Reason = "Taken for crashed.",
                    },
                    Token)
                .GetAwaiter()
                .GetResult()
                .Status;
        }
    }

    private static ImportResolution Imported(
        string reason
    ) => new()
    {
        Outcome = ImportResolutionOutcome.Imported,
        Reason = reason,
    };

    [Fact]
    public async Task ImportOfAFileWithAClaimWithEvidence_IsSkipped()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: true);
        await journal.RecoverImportAsync(importId, Token);
        await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Wrong.",
            },
            Token);

        // Act
        var repeated = await ImportAsync(database, journal, "users", "a.csv", null);

        // Assert
        Assert.Equal(ImportJournalDecision.Skipped, repeated.Journal!.Decision);
    }

    [Fact]
    public async Task DeleteImportLogs_DeletesTheEndedLogs()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        await ImportAsync(database, journal, "users", "a.csv", null);

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal(1, deleted.Count);
    }

    [Fact]
    public async Task DeleteImportLogs_KeepsTheClaimSoTheFileStaysADuplicate()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        await ImportAsync(database, journal, "users", "a.csv", null);
        await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Act
        var repeated = await ImportAsync(database, journal, "users", "a.csv", null);

        // Assert
        Assert.Equal(ImportJournalDecision.Skipped, repeated.Journal!.Decision);
    }

    private static Task<int> Available(
        TabularJournal journal,
        DbContext context,
        DateTimeOffset now
    ) => journal
        .QueryExports(context)
        .CountAsync(
            log => log.Status == TabularExportLogStatus.Ready && (log.ExpiresAt == null || log.ExpiresAt > now),
            Token);

    private static async Task<ImportReport<UserDto>> ImportAsync(
        TestDatabase database,
        TabularJournal journal,
        string scope,
        string fileName,
        List<TabularImportCompleted>? completed,
        ImportDuplicateBehavior behavior = ImportDuplicateBehavior.Skip,
        bool mapDatabaseErrors = true
    )
    {
        var destination = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Factory), mapDatabaseErrors)
            .WithImportLog(
                journal,
                new TabularImportLogOptions
                {
                    Scope = JournalScope.Create(scope),
                    FileName = fileName,
                    DuplicateBehavior = behavior,
                    OnCompletedAsync = (entry, _) =>
                    {
                        completed?.Add(entry);

                        return Task.CompletedTask;
                    },
                });

        return await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(2)]), TestMaps.Users, destination, Token);
    }

    [Fact]
    public async Task CrashedImportWithEvidence_StaysAfterTheRecovery_SinceTheEvidenceIsNoEnd()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: true);
        await journal.RecoverImportAsync(importId, Token);

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal((JournalCallStatus.Succeeded, 0L), (deleted.Status, deleted.Count));
    }

    [Fact]
    public async Task CrashedImportWithoutEvidence_IsDeletedAfterTheApplicationResolvedIt()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: false);
        await journal.RecoverImportAsync(importId, Token);
        await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Rollback confirmed.",
            },
            Token);

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal(1L, deleted.Count);
    }

    [Fact]
    public async Task CrashedImportWithoutEvidence_StaysUntilTheApplicationResolvesIt()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: false);
        await journal.RecoverImportAsync(importId, Token);

        // Act
        var deleted = await journal.DeleteImportLogsAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);

        // Assert
        Assert.Equal(0L, deleted.Count);
    }

    [Fact]
    public async Task ResolvedCrashedImport_IsEndedInItsLog()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var importId = await CrashedImportAsync(database, journal, committed: false);
        await journal.RecoverImportAsync(importId, Token);
        await journal.ResolveImportAsync(
            importId,
            new ImportResolution
            {
                Outcome = ImportResolutionOutcome.NoChanges,
                Reason = "Rollback confirmed.",
            },
            Token);

        await using var context = await database.ContextAsync();

        // Act
        var log = await journal
            .QueryImports(context)
            .SingleAsync(Token);

        // Assert
        Assert.Equal((true, ImportResolutionOutcome.NoChanges), (log.CompletedAt is not null, log.ResolutionOutcome));
    }

    [Theory]
    [InlineData(ImportStrategy.Partial, false, new[] { 1 })]
    [InlineData(ImportStrategy.Partial, true, new[] { 1 })]
    [InlineData(ImportStrategy.Strict, false, new int[0])]
    [InlineData(ImportStrategy.Strict, true, new int[0])]
    public async Task RunningImportWhoseLogAnotherCallEnded_RollsBackItsNextGroup(
        ImportStrategy strategy,
        bool deleted,
        int[] stored
    )
    {
        // Arrange
        // WHY: Another call ends or deletes the log between the first and the second commit group, as a resolution
        // and a cleanup in another process would; the run must not write evidence into an ended or missing log.
        Assert.SkipUnless(
            _provider.SupportsConcurrentWriters,
            "The engine serializes writers; another call cannot write while the data transaction is open.");

        await using var database = await _provider.CreateAsync(Token);
        var journal = TabularJournal.ForFactory(database.Factory);
        var destination = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareInsert<UserDto, User>(
                set: context => context.Users,
                create: dto =>
                {
                    if (dto.PersonnelNumber == 2)
                    {
                        EndTheLogElsewhere();
                    }

                    return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                },
                match: TestMaps.UserMatch)
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 1 })
            .WithImportLog(journal, new TabularImportLogOptions { Scope = JournalScope.Create("users") });

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(3)]),
            TestMaps.Users,
            destination,
            new CsvImportOptions { Strategy = strategy },
            Token);

        // Assert
        // WHY: A partial import committed the first group before the log ended; a strict one holds all rows in the
        // group that is rolled back. An engine that aborts the transaction at a concurrent change fails the evidence
        // write itself instead of finding no log, so the group is not committed either.
        Assert.Equal(
            _provider.ConcurrentChangeAbortsTransaction
                ? TabularErrorCodes.Persistence.WriteFailed
                : TabularErrorCodes.Persistence.Conflict,
            report.FatalError?.Code);

        Assert.Equal(stored, (await database.UsersAsync()).Select(user => user.PersonnelNumber));

        return;

        void EndTheLogElsewhere()
        {
            // ReSharper disable once AccessToDisposedClosure
            // WHY: The callback runs during the awaited import, before the captured database is disposed.
            using var other = database.Factory.CreateDbContext();
            var logs = other.Set<TabularImportLog>(JournalModel.ImportLog(journal.Name));
            if (deleted)
            {
                logs.ExecuteDelete();

                return;
            }

            var log = logs.Single();
            log.EndUnreported(DateTimeOffset.UtcNow);
            JournalStore.Touch(other, log);
            other.SaveChanges();
        }
    }

    /// <summary>Simulates a process that ended after the gate: the claim stays running, optionally with committed evidence.</summary>
    private static async Task<Guid> CrashedImportAsync(
        TestDatabase database,
        TabularJournal journal,
        bool committed
    )
    {
        var hash = Sha256Hash.FromBytes(SHA256.HashData(((MemoryStream)TestMaps.Csv([.. TestMaps.Rows(2)])).ToArray()));
        var now = DateTimeOffset.UtcNow;
        await using var context = await database.ContextAsync();
        var log = new TabularImportLog(
            Guid.NewGuid(),
            JournalScope.Create("users"),
            "a.csv",
            null,
            Guid.NewGuid(),
            now);

        log.Hashed(hash, 0);

        var claim = new TabularImportClaim("users", hash, log.Id, now);
        if (committed)
        {
            claim.Set(ImportClaimState.Imported, now);
        }

        context
            .Set<TabularImportLog>(JournalModel.ImportLog(journal.Name))
            .Add(log);

        JournalStore.AddClaim(context, journal.Name, claim);
        JournalStore.Touch(context, log);
        await context.SaveChangesAsync(Token);

        return log.Id;
    }
}
