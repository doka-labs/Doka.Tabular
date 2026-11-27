using System.Linq;
using Doka.Tabular.Csv;
using Doka.Tabular.Excel;
using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>Import logs whose own end fails: the import still reports and completes once.</summary>
public sealed partial class JournalWindowTests
{
    [Fact]
    public async Task FailingReleaseAfterTheEndOfTheImportLog_IsReportedAndCompletesOnce()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var completions = 0;
        var target = ImportLogged(database, new EndReleaseFactory(database.ConnectionString), () => completions++);

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        Assert.Equal(
            TabularErrorCodes.Persistence.OperationFailed,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal((1L, 1, 1), (report.RowsImported, completions, await check.Users.CountAsync(Token)));
    }

    [Fact]
    public async Task FailingReleaseAfterTheEndOfTheImportLog_IsReportedAndCompletesOnceForAWorkbook()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var completions = 0;
        var target = ImportLogged(database, new EndReleaseFactory(database.ConnectionString), () => completions++);
        var excel = new ExcelFormat(new ExcelFormatOptions());
        var export = ExcelExport.Create();
        export.Add(TabularSheetName.Create("Users"), [new UserDto(1, "a@example.org", "Ada")], TestMaps.Users);
        var file = new MemoryStream();
        await excel.ExportAsync(export.Build(), file, Token);
        file.Position = 0;
        var sheets = ExcelImport.Create();
        sheets.Add(TabularSheetName.Create("Users"), TestMaps.Users, target);

        // Act
        var report = await excel.ImportAsync(file, sheets.Build(), Token);

        // Assert
        Assert.Equal(
            TabularErrorCodes.Persistence.OperationFailed,
            Assert.Single(report.Errors)
                .Code);

        Assert.Equal((1L, 1), (report.RowsImported, completions));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkbookWithAnImportLogOverSeveralDatabases_IsAConfigurationErrorBeforeTheLog(
        bool logOnTheFirstSheet
    )
    {
        // Arrange
        await using var logged = await FileDatabase.CreateAsync();
        await using var other = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(logged.Plain);
        var completions = 0;
        var withLog = TestMaps
            .Upsert(TabularDatabase.ForFactory(logged.Plain))
            .WithImportLog(
                journal,
                new TabularImportLogOptions
                {
                    Scope = s_scope,
                    OnCompletedAsync = (_, _) =>
                    {
                        completions++;

                        return Task.CompletedTask;
                    },
                });

        var withoutLog = TestMaps.Upsert(TabularDatabase.ForFactory(other.Plain));
        var sheets = ExcelImport.Create();
        sheets.Add(
            TabularSheetName.Create("First"),
            TestMaps.Users,
            logOnTheFirstSheet
                ? withLog
                : withoutLog);

        sheets.Add(
            TabularSheetName.Create("Second"),
            TestMaps.Users,
            logOnTheFirstSheet
                ? withoutLog
                : withLog);

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
            await WorkbookAsync([new UserDto(1, "a@example.org", "Ada")], [new UserDto(2, "b@example.org", "Bob")]),
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        await using var logs = await logged.PlainContextAsync();
        await using var check = await other.PlainContextAsync();
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorKind.Configuration),
            (report.Status, report.Errors[0].Kind));

        Assert.Equal(
            (0L, 0, 0, 0),
            (report.RowsImported, await journal
                    .QueryImports(logs)
                    .CountAsync(Token), await check.Users.CountAsync(Token),
                completions));
    }

    [Fact]
    public async Task WorkbookWithAnImportLogAndASheetWithoutTarget_ImportsTheSheetWithTarget()
    {
        // Arrange
        await using var logged = await FileDatabase.CreateAsync();
        var bytes = await HalfTargetedWorkbookAsync();

        // Act
        var report = await ImportHalfTargetedAsync(logged, bytes);

        // Assert
        Assert.Equal((ReportStatus.CompletedWithErrors, 1L), (report.Status, report.RowsImported));
    }

    [Fact]
    public async Task WorkbookWithAnImportLogAndASheetWithoutTarget_KeepsTheFileImportedForReturnedRows()
    {
        // Arrange
        await using var logged = await FileDatabase.CreateAsync();
        var bytes = await HalfTargetedWorkbookAsync();
        await ImportHalfTargetedAsync(logged, bytes);

        // Act
        // WHY: The returned rows of the sheet without target may have been processed by the application.
        var repeated = await ImportHalfTargetedAsync(logged, bytes);

        // Assert
        Assert.Equal(ImportJournalDecision.Skipped, repeated.Journal?.Decision);
    }

    private static async Task<byte[]> HalfTargetedWorkbookAsync() => (await WorkbookAsync(
            [new UserDto(0, "a@example.org", "Ada")],
            [new UserDto(2, "b@example.org", "Bob")]))
        .ToArray();

    private static Task<ExcelWorkbookImportReport> ImportHalfTargetedAsync(
        FileDatabase logged,
        byte[] bytes
    )
    {
        var sheets = ExcelImport.Create();
        sheets.Add(
            TabularSheetName.Create("First"),
            TestMaps.Users,
            TestMaps
                .Upsert(TabularDatabase.ForFactory(logged.Plain))
                .WithImportLog(
                    TabularJournal.ForFactory(logged.Plain),
                    new TabularImportLogOptions { Scope = s_scope }));

        sheets.Add(TabularSheetName.Create("Second"), TestMaps.Users);

        return new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
            new MemoryStream(bytes),
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);
    }

    [Fact]
    public async Task WorkbookOverSeveralDatabasesWithoutImportLog_Imports()
    {
        // Arrange
        await using var first = await FileDatabase.CreateAsync();
        await using var second = await FileDatabase.CreateAsync();
        var sheets = ExcelImport.Create();
        sheets.Add(
            TabularSheetName.Create("First"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(first.Plain)));

        sheets.Add(
            TabularSheetName.Create("Second"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(second.Plain)));

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
            await WorkbookAsync([new UserDto(1, "a@example.org", "Ada")], [new UserDto(2, "b@example.org", "Bob")]),
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal((ReportStatus.Succeeded, 2L), (report.Status, report.RowsImported));
    }

    [Fact]
    public async Task PartialImportWhoseRowsAllFail_ImportsNothing()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();

        // Act
        var report = await ImportFailingRowAsync(database);

        // Assert
        Assert.Equal((ReportStatus.CompletedWithErrors, 0L), (report.Status, report.RowsImported));
    }

    [Fact]
    public async Task PartialImportWhoseRowsAllFail_AllowsANewAttempt()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        await ImportFailingRowAsync(database);

        // Act
        var repeated = await ImportFailingRowAsync(database);

        // Assert
        Assert.Equal(ImportJournalDecision.Processed, repeated.Journal?.Decision);
    }

    private static Task<ImportReport<UserDto>> ImportFailingRowAsync(
        FileDatabase database
    ) => s_csv.ImportAsync(
        TestMaps.Csv("0,a@example.org,Ada"),
        TestMaps.Users,
        TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(TabularJournal.ForFactory(database.Plain), new TabularImportLogOptions { Scope = s_scope }),
        new CsvImportOptions { Strategy = ImportStrategy.Partial },
        Token);

    [Fact]
    public async Task CancellationWhileATargetIsPrepared_IsACanceledWorkbookReportAndReleasesTheOtherTargets()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var prepared = new TrackingFactory(database.Plain);
        var file = await WorkbookAsync(
            [new UserDto(1, "a@example.org", "Ada")],
            [new UserDto(2, "b@example.org", "Bob")]);

        var sheets = ExcelImport.Create();
        sheets.Add(
            TabularSheetName.Create("First"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(prepared)));

        sheets.Add(
            TabularSheetName.Create("Second"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(new CancelingFactory(database.Plain, cancellation))));

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, 0L, true), (report.Status, report.RowsImported, prepared.AllDisposed));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AbortWhileTheInputIsHashed_EndsTheLogWithoutHashAndCompletesOnce(
        bool cancel,
        bool workbook
    )
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var journal = TabularJournal.ForFactory(database.Plain);
        var completions = 0;
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(
                journal,
                new TabularImportLogOptions
                {
                    Scope = s_scope,
                    OnCompletedAsync = (_, _) =>
                    {
                        completions++;

                        return Task.CompletedTask;
                    },
                });

        var bytes = workbook
            ? (await WorkbookAsync([new UserDto(1, "a@example.org", "Ada")], [])).ToArray()
            : "1,a@example.org,Ada\r\n"u8.ToArray();

        var input = new FailingSecondReadStream(
            bytes,
            () =>
            {
                if (!cancel)
                {
                    return new IOException("The input broke off.");
                }

                // ReSharper disable once AccessToDisposedClosure
                // WHY: The stream is read during the awaited import, before the captured object is disposed.
                cancellation.Cancel();

                return new OperationCanceledException();
            });

        // Act
        ReportStatus status;
        ImportJournalResult? reference;
        if (workbook)
        {
            var sheets = ExcelImport.Create();
            sheets.Add(TabularSheetName.Create("First"), TestMaps.Users, target);
            var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
                input,
                sheets.Build(),
                cancellation.Token);

            (status, reference) = (report.Status, report.Journal);
        }
        else
        {
            var report = await s_csv.ImportAsync(input, TestMaps.Users, target, cancellation.Token);
            (status, reference) = (report.Status, report.Journal);
        }

        // Assert
        await using var check = await database.PlainContextAsync();
        var log = await journal
            .QueryImports(check)
            .SingleAsync(Token);

        Assert.Equal(
            cancel
                ? ReportStatus.Canceled
                : ReportStatus.Failed,
            status);

        Assert.Equal((null, status, 1), (log.Hash, log.Status, completions));
        Assert.Equal(log.Id, reference?.EntryId);
    }

    [Fact]
    public async Task UnrequestedCancellationOfAContextFactory_IsAFailedReportAndReleasesTheOtherTargets()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var prepared = new TrackingFactory(database.Plain);
        var failing = new UnrequestedCancelFactory(database.Plain);
        var file = await WorkbookAsync(
            [new UserDto(1, "a@example.org", "Ada")],
            [new UserDto(2, "b@example.org", "Bob")]);

        var sheets = ExcelImport.Create();
        sheets.Add(
            TabularSheetName.Create("First"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(prepared)));

        sheets.Add(
            TabularSheetName.Create("Second"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(failing)));

        // Act
        var report = await new ExcelFormat(new ExcelFormatOptions()).ImportAsync(
            file,
            sheets.Build(),
            new ExcelWorkbookImportOptions { Strategy = ImportStrategy.Partial },
            Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorCodes.Persistence.OperationFailed, true),
            (report.Status, report.FatalError?.Code, prepared.AllDisposed));
    }

    [Fact]
    public async Task UnrequestedCancellationOfAContextFactory_IsAFailedReportOfASingleTarget()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var failing = new UnrequestedCancelFactory(database.Plain);

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv("1,a@example.org,Ada"),
            TestMaps.Users,
            TestMaps.Upsert(TabularDatabase.ForFactory(failing)),
            Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorCodes.Persistence.OperationFailed),
            (report.Status, report.FatalError?.Code));
    }

    [Fact]
    public async Task UnrequestedCancellationOfTheJournalFactory_IsAFailedReport()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(
                TabularJournal.ForFactory(new UnrequestedCancelFactory(database.Plain)),
                new TabularImportLogOptions { Scope = s_scope });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

        // Assert
        Assert.Equal(
            (ReportStatus.Failed, TabularErrorCodes.Persistence.WriteFailed),
            (report.Status, report.FatalError?.Code));
    }

    [Fact]
    public async Task FailingGateAfterTheStartOfTheLog_KeepsTheReferenceInTheReport()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var journal = TabularJournal.ForFactory(new FailingSecondContextFactory(database.Plain));
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(journal, new TabularImportLogOptions { Scope = s_scope });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        var log = await journal
            .QueryImports(check)
            .SingleAsync(Token);

        Assert.Equal((ReportStatus.Failed, log.Id), (report.Status, report.Journal?.EntryId));
    }

    [Fact]
    public async Task FailingStartOfTheLog_RefersToTheStoredLog()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var completions = 0;
        var journal = TabularJournal.ForFactory(new StartFailureFactory(database.ConnectionString, true));
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(
                journal,
                new TabularImportLogOptions
                {
                    Scope = s_scope,
                    OnCompletedAsync = (_, _) =>
                    {
                        completions++;

                        return Task.CompletedTask;
                    },
                });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        var logs = await journal
            .QueryImports(check)
            .ToListAsync(Token);

        Assert.Equal(ReportStatus.Failed, report.Status);
        var log = Assert.Single(logs);
        Assert.Equal((log.Id, 1, ReportStatus.Failed), (report.Journal?.EntryId, completions, log.Status));
    }

    [Fact]
    public async Task FailingStartOfTheLog_WithoutAStoredLog_RefersToNoLog()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var completions = 0;
        var journal = TabularJournal.ForFactory(new StartFailureFactory(database.ConnectionString, false));
        var target = TestMaps
            .Upsert(TabularDatabase.ForFactory(database.Plain))
            .WithImportLog(
                journal,
                new TabularImportLogOptions
                {
                    Scope = s_scope,
                    OnCompletedAsync = (_, _) =>
                    {
                        completions++;

                        return Task.CompletedTask;
                    },
                });

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv("1,a@example.org,Ada"), TestMaps.Users, target, Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        var logs = await journal
            .QueryImports(check)
            .ToListAsync(Token);

        Assert.Equal(ReportStatus.Failed, report.Status);
        Assert.Empty(logs);
        Assert.Equal((null, 0), (report.Journal, completions));
    }

    private static TabularWriteTarget<UserDto> ImportLogged(
        FileDatabase database,
        EndReleaseFactory journal,
        Action completed
    ) => TestMaps
        .Upsert(TabularDatabase.ForFactory(database.Plain))
        .WithImportLog(
            TabularJournal.ForFactory(journal),
            new TabularImportLogOptions
            {
                Scope = s_scope,
                OnCompletedAsync = (_, _) =>
                {
                    completed();

                    return Task.CompletedTask;
                },
            });

    /// <summary>Returns its bytes in the first read and fails the next one with the given exception.</summary>
    private sealed class FailingSecondReadStream : Stream
    {
        private readonly byte[] _bytes;
        private readonly Func<Exception> _failure;
        private bool _read;

        public FailingSecondReadStream(
            byte[] bytes,
            Func<Exception> failure
        )
        {
            _bytes = bytes;
            _failure = failure;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (_read)
            {
                throw _failure();
            }

            _read = true;
            _bytes.CopyTo(buffer);

            return ValueTask.FromResult(_bytes.Length);
        }

        public override void Flush() { }

        public override long Seek(
            long offset,
            SeekOrigin origin
        ) => throw new NotSupportedException();

        public override void SetLength(
            long value
        ) => throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count
        ) => throw new NotSupportedException();
    }

    /// <summary>Journal contexts: the first one starts the log, the next one fails like an unavailable database.</summary>
    private sealed class FailingSecondContextFactory : IDbContextFactory<TabularTestContext>
    {
        private readonly IDbContextFactory<TabularTestContext> _inner;
        private int _created;

        public FailingSecondContextFactory(
            IDbContextFactory<TabularTestContext> inner
        )
        {
            _inner = inner;
        }

        public TabularTestContext CreateDbContext() => ++_created == 2
            ? throw new InvalidOperationException("The database is not available.")
            : _inner.CreateDbContext();
    }

    /// <summary>A factory whose asynchronous creation reports a cancellation that the call did not request.</summary>
    private sealed class UnrequestedCancelFactory : IDbContextFactory<TabularTestContext>
    {
        private readonly IDbContextFactory<TabularTestContext> _inner;

        public UnrequestedCancelFactory(
            IDbContextFactory<TabularTestContext> inner
        )
        {
            _inner = inner;
        }

        public TabularTestContext CreateDbContext() => _inner.CreateDbContext();

        public Task<TabularTestContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        ) => throw new OperationCanceledException();
    }

    /// <summary>A factory whose creation of a context observes a cancellation that it requests itself.</summary>
    private sealed class CancelingFactory : IDbContextFactory<TabularTestContext>
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly IDbContextFactory<TabularTestContext> _inner;

        public CancelingFactory(
            IDbContextFactory<TabularTestContext> inner,
            CancellationTokenSource cancellation
        )
        {
            _inner = inner;
            _cancellation = cancellation;
        }

        public TabularTestContext CreateDbContext() => _inner.CreateDbContext();

        public Task<TabularTestContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            _cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_inner.CreateDbContext());
        }
    }

    /// <summary>A journal whose first context fails: after storing the start when it is released, or while storing.</summary>
    private sealed class StartFailureFactory : IDbContextFactory<StartFailureContext>
    {
        private readonly string _connectionString;
        private readonly bool _stored;
        private int _created;

        public StartFailureFactory(
            string connectionString,
            bool stored
        )
        {
            _connectionString = connectionString;
            _stored = stored;
        }

        public StartFailureContext CreateDbContext() => new(
            new DbContextOptionsBuilder<StartFailureContext>()
                .UseSqlite(_connectionString)
                .UseTabularJournal()
                .Options,
            ++_created == 1,
            _stored);
    }

    private sealed class StartFailureContext : DbContext
    {
        private readonly bool _failing;
        private readonly bool _stored;

        public StartFailureContext(
            DbContextOptions<StartFailureContext> options,
            bool failing,
            bool stored
        )
            : base(options)
        {
            _failing = failing;
            _stored = stored;
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        ) => _failing && !_stored
            ? throw new InvalidOperationException("The start cannot be stored.")
            : await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (_failing && _stored)
            {
                throw new IOException("The release of the context failed.");
            }
        }
    }

    /// <summary>Journal contexts whose release fails after they stored the end of an import log.</summary>
    private sealed class EndReleaseFactory : IDbContextFactory<EndReleaseContext>
    {
        private readonly string _connectionString;

        public EndReleaseFactory(
            string connectionString
        )
        {
            _connectionString = connectionString;
        }

        public EndReleaseContext CreateDbContext() => new(
            new DbContextOptionsBuilder<EndReleaseContext>()
                .UseSqlite(_connectionString)
                .UseTabularJournal()
                .Options);
    }

    private sealed class EndReleaseContext : DbContext
    {
        private bool _storedEnd;

        public EndReleaseContext(
            DbContextOptions<EndReleaseContext> options
        )
            : base(options) { }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            var end = ChangeTracker
                .Entries<TabularImportLog>()
                .Any(entry => entry is { State: EntityState.Modified, Entity.Status: not null });

            var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            _storedEnd |= end;

            return saved;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (_storedEnd)
            {
                throw new IOException("The disposal hook failed.");
            }
        }
    }
}
