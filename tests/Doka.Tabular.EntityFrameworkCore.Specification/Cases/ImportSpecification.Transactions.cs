using System.Data;

namespace Doka.Tabular.EntityFrameworkCore.Specification;

public abstract partial class ImportSpecification
{
    [Fact]
    public async Task PassedContext_WithPendingChange_FailsBeforeWritingAndKeepsTheChange()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await using var context = await database.ContextAsync();
        context.Entries.Add(new ProcessingEntry { State = "pending" });

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(1)]),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(
            EntityState.Added,
            context
                .ChangeTracker
                .Entries<ProcessingEntry>()
                .Single()
                .State);

        Assert.Empty(await database.UsersAsync());
    }

    [Fact]
    public async Task PassedContext_WithActiveTransactionWithoutJoin_FailsBeforeWriting()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await using var context = await database.ContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(Token);

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(1)]),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task JoinExistingTransaction_ValidRows_ArePendingUntilTheApplicationCommits()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await using var context = await database.ContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var target = TestMaps
            .Upsert(TabularDatabase.ForContext(context))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        // WHY: The pending rows reach the database with the commit of the application, so both form one action.
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(3)]), TestMaps.Users, target, Token);
        await transaction.CommitAsync(Token);

        // Assert
        Assert.Equal(ReportStatus.Succeeded, report.Status);
        Assert.Equal((0L, 3L), (report.RowsImported, report.RowsPendingCommit));
        Assert.Equal(3, (await database.UsersAsync()).Count);
    }

    [Fact]
    public async Task JoinExistingTransaction_StrictFailure_RollsBackToTheImportSavepointOnly()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await using var context = await database.ContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var entry = new ProcessingEntry { State = "started" };
        context.Entries.Add(entry);
        await context.SaveChangesAsync(Token);
        var lines = TestMaps
            .Rows(3)
            .ToList();

        lines[2] = "3,user1@example.org,User 3";
        var target = TestMaps
            .Upsert(TabularDatabase.ForContext(context))
            .WithOptions(new TabularWriteOptions { JoinExistingTransaction = true });

        // Act
        // WHY: The application continues and commits its own work after the failed import, so all steps form one
        // action.
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. lines]), TestMaps.Users, target, Token);
        entry.State = "failed";
        await context.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(0, report.RowsPendingCommit);
        await using var check = await database.ContextAsync();
        Assert.Equal("failed", (await check.Entries.SingleAsync(Token)).State);
        Assert.Empty(await database.UsersAsync());
    }

    [Fact]
    public async Task JoinExistingTransaction_WithCommitGroupSize_IsAConfigurationError()
    {
        // Arrange
        await using var database = await _provider.CreateAsync(Token);
        await using var context = await database.ContextAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        var target = TestMaps
            .Upsert(TabularDatabase.ForContext(context))
            .WithOptions(
                new TabularWriteOptions
                {
                    JoinExistingTransaction = true,
                    CommitGroupSize = 100,
                });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(1)]), TestMaps.Users, target, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Configuration.Invalid,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task Concurrency_WithReadCommitted_IsARowConflictOnEveryEngine()
    {
        // Arrange
        Assert.SkipUnless(
            _provider.SupportsConcurrentWriters,
            "The engine serializes writers; a change between read and save needs a server database.");

        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(User.Create(1, "old@example.org", "Old"), User.Create(2, "b@example.org", "B"));
        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareUpsert<UserDto, User>(
                set: context => context.Users,
                match: TestMaps.UserMatch,
                create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
                update: (entity, dto) =>
                {
                    if (dto.PersonnelNumber == 1)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        // WHY: The callback runs during the awaited import, before the captured object is disposed.
                        using var other = database.Factory.CreateDbContext();
                        other
                            .Users
                            .Single(user => user.PersonnelNumber == 1)
                            .SetEmail("other@example.org");

                        other.SaveChanges();
                    }

                    entity.SetEmail(dto.Email);
                })
            .WithOptions(
                new TabularWriteOptions
                {
                    IsolationLevel = IsolationLevel.ReadCommitted,
                    CommitGroupSize = 10,
                });

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv("2,b2@example.org,B", "1,new@example.org,New"),
            TestMaps.Users,
            target,
            Partial,
            Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Persistence.Conflict,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(["other@example.org", "b2@example.org"], (await database.UsersAsync()).Select(user => user.Email));
    }

    [Fact]
    public async Task Concurrency_ChangedByAnotherContext_IsARowConflict()
    {
        // Arrange
        Assert.SkipUnless(
            _provider.SupportsConcurrentWriters,
            "The engine serializes writers; a change between read and save needs a server database.");

        Assert.SkipWhen(
            _provider.ConcurrentChangeAbortsTransaction,
            "The engine aborts the transaction at a concurrent change; that case has its own test.");

        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(User.Create(1, "old@example.org", "Old"));
        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareUpsert<UserDto, User>(
                set: context => context.Users,
                match: TestMaps.UserMatch,
                create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
                update: (entity, dto) =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    // WHY: The callback runs during the awaited import, before the captured object is disposed.
                    using var other = database.Factory.CreateDbContext();
                    other
                        .Users
                        .Single()
                        .SetEmail("other@example.org");

                    other.SaveChanges();
                    entity.SetEmail(dto.Email);
                });

        // Act
        var report =
            await s_csv.ImportAsync(TestMaps.Csv("1,new@example.org,New"), TestMaps.Users, target, Partial, Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Persistence.Conflict,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal(
            "other@example.org",
            Assert.Single(await database.UsersAsync())
                .Email);
    }

    [Fact]
    public async Task Concurrency_ChangedByAnotherContextOnAnEngineThatAbortsTheTransaction_IsAnUnknownRollback()
    {
        // Arrange
        Assert.SkipUnless(
            _provider.SupportsConcurrentWriters,
            "The engine serializes writers; a change between read and save needs a server database.");

        Assert.SkipUnless(
            _provider.ConcurrentChangeAbortsTransaction,
            "The engine keeps the transaction at a concurrent change; that case has its own test.");

        await using var database = await _provider.CreateAsync(Token);
        await database.SeedAsync(User.Create(1, "old@example.org", "Old"));
        var target = TabularDatabase
            .ForFactory(database.Factory)
            .PrepareUpsert<UserDto, User>(
                set: context => context.Users,
                match: TestMaps.UserMatch,
                create: dto => User.Create(dto.PersonnelNumber, dto.Email, dto.Name),
                update: (entity, dto) =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    // WHY: The callback runs during the awaited import, before the captured object is disposed.
                    using var other = database.Factory.CreateDbContext();
                    other
                        .Users
                        .Single()
                        .SetEmail("other@example.org");

                    other.SaveChanges();
                    entity.SetEmail(dto.Email);
                });

        // Act
        var report =
            await s_csv.ImportAsync(TestMaps.Csv("1,new@example.org,New"), TestMaps.Users, target, Partial, Token);

        // Assert
        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Equal(TabularErrorCodes.Persistence.RollbackUnknown, report.FatalError?.Code);

        Assert.Equal(
            "other@example.org",
            Assert.Single(await database.UsersAsync())
                .Email);
    }
}
