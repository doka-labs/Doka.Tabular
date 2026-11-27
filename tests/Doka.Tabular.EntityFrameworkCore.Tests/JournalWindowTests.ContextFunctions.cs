using Xunit;

namespace Doka.Tabular.EntityFrameworkCore.Tests;

/// <summary>
/// Database and journal descriptions over a context-creating function behave like those over an
/// <see cref="IDbContextFactory{TContext}" />: each step gets its own context that Tabular disposes, the function gets
/// the token of the call, and a failing function is a failed database operation.
/// </summary>
public sealed partial class JournalWindowTests
{
    [Fact]
    public async Task DatabaseOfAContextFunction_ImportsAndDisposesItsContexts()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var tracking = new TrackingFactory(database.Plain);
        var target = TestMaps.Upsert(TabularDatabase.ForFactory(_ => Task.FromResult(tracking.CreateDbContext())));

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(2)]), TestMaps.Users, target, Token);

        // Assert
        await using var check = await database.PlainContextAsync();
        Assert.Equal(
            (ReportStatus.Succeeded, 2, true),
            (report.Status, await check.Users.CountAsync(Token), tracking.AllDisposed));
    }

    [Fact]
    public async Task DatabaseOfAContextFunction_GetsTheTokenOfTheCall()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var tracking = new TrackingFactory(database.Plain);
        var function = new CancelingContextFunction(tracking, cancellation);
        var target = TestMaps.Upsert(TabularDatabase.ForFactory(function.CreateAsync));

        // Act
        var report = await s_csv.ImportAsync(
            TestMaps.Csv([.. TestMaps.Rows(1)]),
            TestMaps.Users,
            target,
            cancellation.Token);

        // Assert
        Assert.Equal((ReportStatus.Canceled, true, true), (report.Status, function.Forwarded, tracking.AllDisposed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingContextFunctionOfTheDatabase_IsAFailedWriteNotAnEscapedException(
        bool faulted
    )
    {
        // Arrange
        var target = TestMaps.Upsert(TabularDatabase.ForFactory(FailingContextFunction(faulted)));

        // Act
        var report = await s_csv.ImportAsync(TestMaps.Csv([.. TestMaps.Rows(1)]), TestMaps.Users, target, Token);

        // Assert
        Assert.Equal(
            (TabularErrorCodes.Persistence.OperationFailed, TabularErrorKind.Technical),
            (report.FatalError?.Code, report.FatalError?.Kind));
    }

    [Fact]
    public async Task JournalOfAContextFunction_DetectsTheRepeatedFileAndDisposesItsContexts()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        var tracking = new TrackingFactory(database.Plain);
        var journal = TabularJournal.ForFactory(_ => Task.FromResult(tracking.CreateDbContext()));
        await ImportAsync(database.Plain, journal, "1,a@example.org,Ada");

        // Act
        var repeated = await ImportAsync(database.Plain, journal, "1,a@example.org,Ada");

        // Assert
        Assert.Equal((ImportJournalDecision.Skipped, true), (repeated.Journal?.Decision, tracking.AllDisposed));
    }

    [Fact]
    public async Task JournalOfAContextFunction_GetsTheTokenOfTheCall()
    {
        // Arrange
        await using var database = await FileDatabase.CreateAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var tracking = new TrackingFactory(database.Plain);
        var function = new CancelingContextFunction(tracking, cancellation);
        var journal = TabularJournal.ForFactory(function.CreateAsync);

        // Act
        var result = await journal.RecoverImportAsync(Guid.NewGuid(), cancellation.Token);

        // Assert
        Assert.Equal(
            (JournalCallStatus.Canceled, true, true),
            (result.Status, function.Forwarded, tracking.AllDisposed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingContextFunctionOfTheJournal_ReturnsAFailedResult(
        bool faulted
    )
    {
        // Arrange
        var journal = TabularJournal.ForFactory(FailingContextFunction(faulted));

        // Act
        var result = await journal.RecoverImportAsync(Guid.NewGuid(), Token);

        // Assert
        Assert.Equal(JournalCallStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData("database factory")]
    [InlineData("database function")]
    [InlineData("journal factory")]
    [InlineData("journal function")]
    public void NullContextSource_IsRejectedByItsName(
        string overload
    )
    {
        // Arrange
        Func<object> describe = overload switch
        {
            "database factory" => () => TabularDatabase.ForFactory((IDbContextFactory<TabularTestContext>)null!),
            "database function" => () => TabularDatabase.ForFactory(
                (Func<CancellationToken, Task<TabularTestContext>>)null!),
            "journal factory" => () => TabularJournal.ForFactory((IDbContextFactory<TabularTestContext>)null!),
            _ => () => TabularJournal.ForFactory((Func<CancellationToken, Task<TabularTestContext>>)null!),
        };

        // Act
        var exception = Record.Exception(describe);

        // Assert
        Assert.Equal(
            "factory",
            Assert.IsType<ArgumentNullException>(exception)
                .ParamName);
    }

    /// <summary>
    /// A context function whose connection layer fails with an I/O error, either while it is called or in the task it
    /// returns.
    /// </summary>
    private static Func<CancellationToken, Task<TabularTestContext>> FailingContextFunction(
        bool faulted
    ) => faulted
        ? _ => Task.FromException<TabularTestContext>(new IOException("The database is not reachable."))
        : _ => throw new IOException("The database is not reachable.");

    /// <summary>A context function that cancels the call and notes whether the token it got sees the cancellation.</summary>
    private sealed class CancelingContextFunction
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly TrackingFactory _inner;

        public CancelingContextFunction(
            TrackingFactory inner,
            CancellationTokenSource cancellation
        )
        {
            _inner = inner;
            _cancellation = cancellation;
        }

        public bool Forwarded { get; private set; }

        public Task<TabularTestContext> CreateAsync(
            CancellationToken cancellationToken
        )
        {
            _cancellation.Cancel();
            Forwarded = cancellationToken.IsCancellationRequested;

            return Task.FromResult(_inner.CreateDbContext());
        }
    }
}
