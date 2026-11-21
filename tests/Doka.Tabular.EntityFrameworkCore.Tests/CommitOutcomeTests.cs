using System.Data.Common;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>A commit whose outcome is not proven by an answer of the server counts as uncertain.</summary>
public sealed class CommitOutcomeTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterThePhysicalCommit_IsAnUncertainOutcome(
        bool databaseError
    )
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var plain = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(plain))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        await using var context = new TabularTestContext(new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .AddInterceptors(new ThrowAfterCommit(databaseError))
            .Options);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(TestMaps.Csv("1,a@example.org,Ada"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            Token);

        // Assert
        await using var check = new TabularTestContext(plain);
        Assert.Equal(TabularErrorCodes.Persistence.CommitUnknown, report.FatalError?.Code);
        Assert.Equal(1, report.RowsUncertain);
        Assert.Equal(1, await check.Users.CountAsync(Token));
    }

    [Theory]
    [InlineData(ImportStrategy.Strict)]
    [InlineData(ImportStrategy.Partial)]
    public async Task CancellationDuringTheLastStatement_CommitsNothing(
        ImportStrategy strategy
    )
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var plain = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(plain))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        using var cancellation = new CancellationTokenSource();
        await using var context = new TabularTestContext(new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .AddInterceptors(new CancelAfterSave(cancellation))
            .Options);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(TestMaps.Csv("1,a@example.org,Ada"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            new CsvImportOptions { Strategy = strategy },
            cancellation.Token);

        // Assert
        await using var check = new TabularTestContext(plain);
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Equal(0, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task CancellationInARejectedLastRow_CommitsNothing()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var options = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(options))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        using var cancellation = new CancellationTokenSource();
        var code = TabularErrorCode.Create("user.rejected");
        await using var context = new TabularTestContext(options);
        var target = TabularDatabase
            .ForContext(context)
            .PrepareUpsert<UserDto, User>(set: users => users.Users,
                match: TestMaps.UserMatch,
                create: dto =>
                {
                    if (dto.PersonnelNumber != 2)
                    {
                        return User.Create(dto.PersonnelNumber, dto.Email, dto.Name);
                    }

                    // ReSharper disable once AccessToDisposedClosure
                    // WHY: The callback runs during the awaited import, before the captured object is disposed.
                    cancellation.Cancel();

                    throw new InvalidOperationException("rejected");
                },
                update: (_, _) => { },
                errors: errors => errors.Expect<InvalidOperationException>(code))
            .WithOptions(new TabularWriteOptions { CommitGroupSize = 10 });

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(
            TestMaps.Csv("1,a@example.org,Ada", "2,b@example.org,Bob"),
            TestMaps.Users,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            cancellation.Token);

        // Assert
        await using var check = new TabularTestContext(options);
        Assert.Equal(ReportStatus.Canceled, report.Status);
        Assert.Equal(0, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task AssignedOperationCanceledExceptionOfCreate_IsARowError()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var options = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(options))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        var code = TabularErrorCode.Create("user.canceledByRule");
        await using var context = new TabularTestContext(options);
        var target = TabularDatabase
            .ForContext(context)
            .PrepareUpsert(set: users => users.Users,
                match: TestMaps.UserMatch,
                create: _ => throw new OperationCanceledException(),
                update: (_, _) => { },
                errors: errors => errors.Expect<OperationCanceledException>(code));

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(TestMaps.Csv("1,a@example.org,Ada"),
            TestMaps.Users,
            target,
            new CsvImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(code,
            Assert.Single(report.Errors)
                .Code);
    }

    [Fact]
    public async Task DatabaseErrorOfTheCommit_IsAnUncertainOutcomeAlsoWhenNothingWasCommitted()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var plain = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(plain))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        await using var context = new TabularTestContext(new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .AddInterceptors(new RejectCommit())
            .Options);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(TestMaps.Csv("1,a@example.org,Ada"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            Token);

        // Assert
        await using var check = new TabularTestContext(plain);

        // WHY: A rejection and a failure after the commit look the same from outside; nothing proves the rejection.
        Assert.Equal(TabularErrorCodes.Persistence.CommitUnknown, report.FatalError?.Code);
        Assert.Equal(1, report.RowsUncertain);
        Assert.Equal(0, await check.Users.CountAsync(Token));
    }

    [Fact]
    public async Task FailingReleaseOfTheTransaction_KeepsTheConfirmedCommit()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var plain = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(plain))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        var builder = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .ReplaceService<IRelationalTransactionFactory, ThrowingReleaseFactory>();

        await using var context = new TabularTestContext(builder.Options);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(TestMaps.Csv("1,a@example.org,Ada"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            Token);

        // Assert
        await using var check = new TabularTestContext(plain);
        Assert.Equal((1L, 1), (report.RowsImported, await check.Users.CountAsync(Token)));
        Assert.Equal(TabularErrorCodes.Persistence.OperationFailed, report.FatalError?.Code);
    }

    [Fact]
    public async Task FailingReleaseOfTheTransaction_KeepsTheUnknownCommit()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var plain = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(plain))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        var builder = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .ReplaceService<IRelationalTransactionFactory, ThrowingReleaseFactory>();

        builder.AddInterceptors(new RejectCommit());

        await using var context = new TabularTestContext(builder.Options);

        // Act
        var report = await new CsvFormat(new CsvFormatOptions()).ImportAsync(TestMaps.Csv("1,a@example.org,Ada"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForContext(context)),
            Token);

        // Assert
        Assert.Equal((TabularErrorCodes.Persistence.CommitUnknown, 1L),
            (report.FatalError?.Code, report.RowsUncertain));
    }

    [Fact]
    public async Task FailingReleaseAfterTheCommitOfAStrictWorkbook_KeepsEverySheet()
    {
        // Arrange
        await using var database = await SqliteFile.OpenAsync(Token);
        var connection = database.Connection;
        var plain = new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new TabularTestContext(plain))
        {
            await setup.Database.EnsureCreatedAsync(Token);
        }

        await using var context = new TabularTestContext(new DbContextOptionsBuilder<TabularTestContext>()
            .UseSqlite(connection)
            .ReplaceService<IRelationalTransactionFactory, ThrowingReleaseFactory>()
            .Options);

        var target = TestMaps.Upsert(TabularDatabase.ForContext(context));
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
        sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users, target);

        var excel = new ExcelFormat(new ExcelFormatOptions());

        // Act
        var report = await excel.ImportAsync(await WorkbookAsync(excel),
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Strict },
            Token);

        // Assert
        await using var check = new TabularTestContext(plain);
        Assert.Equal((2L, 2), (report.RowsImported, await check.Users.CountAsync(Token)));
        Assert.Equal(TabularErrorCodes.Persistence.OperationFailed, report.FatalError?.Code);
    }

    private static async Task<MemoryStream> WorkbookAsync(
        ExcelFormat excel
    )
    {
        var sheets = ExcelExport.Create();
        sheets.Add(TabularSheetName.Create("First"), [new UserDto(1, "a@example.org", "Ada")], TestMaps.Users);
        sheets.Add(TabularSheetName.Create("Second"), [new UserDto(2, "b@example.org", "Bob")], TestMaps.Users);
        var file = new MemoryStream();
        await excel.ExportAsync(sheets.Build(), file, Token);
        file.Position = 0;

        return file;
    }

    /// <summary>Cancels the call after a save, before the group is committed.</summary>
    private sealed class CancelAfterSave : SaveChangesInterceptor
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelAfterSave(
            CancellationTokenSource cancellation
        )
        {
            _cancellation = cancellation;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            _cancellation.Cancel();

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Creates transactions whose release fails, like a provider that fails while cleaning up.</summary>

    // WHY: EF creates the factory through its service provider after ReplaceService.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class ThrowingReleaseFactory : RelationalTransactionFactory
    {
        public ThrowingReleaseFactory(
            RelationalTransactionFactoryDependencies dependencies
        )
            : base(dependencies) { }

        public override RelationalTransaction Create(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned
        ) => new ThrowingReleaseTransaction(connection,
            transaction,
            transactionId,
            logger,
            transactionOwned,
            Dependencies.SqlGenerationHelper);
    }

    private sealed class ThrowingReleaseTransaction : RelationalTransaction
    {
        private bool _commitAttempted;

        public ThrowingReleaseTransaction(
            IRelationalConnection connection,
            DbTransaction transaction,
            Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger,
            bool transactionOwned,
            ISqlGenerationHelper sqlGenerationHelper
        )
            : base(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper) { }

        public override Task CommitAsync(
            CancellationToken cancellationToken = default
        )
        {
            _commitAttempted = true;

            return base.CommitAsync(cancellationToken);
        }

        // WHY: Only the release after a commit fails; the check before the first write opens and releases its own.
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (_commitAttempted)
            {
                throw new InvalidOperationException("The release of the transaction failed.");
            }
        }
    }

    /// <summary>Rejects the commit with a database error while the transaction is still active.</summary>
    private sealed class RejectCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => throw new SqliteException("The commit was rejected.", 19);
    }

    /// <summary>Throws after the transaction was committed, like a failing diagnostic hook of the application.</summary>
    private sealed class ThrowAfterCommit : DbTransactionInterceptor
    {
        private readonly bool _databaseError;

        public ThrowAfterCommit(
            bool databaseError
        )
        {
            _databaseError = databaseError;
        }

        // WHY: A hook that writes its own audit row can fail with a database error after the data commit.
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => _databaseError
            ? throw new SqliteException("The audit write after the commit failed.", 19)
            : throw new InvalidOperationException("The hook after the commit failed.");
    }
}
