namespace Doka.Tabular.Tests;

/// <summary>
/// The temporary-bytes budget belongs to the call: every temporary file of the call draws from it at the same time,
/// and a deleted file returns its bytes.
/// </summary>
public sealed class TemporaryBudgetTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tabular-{Guid.NewGuid():N}");

    public TemporaryBudgetTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task TwoSpilledBuffersOfOneCall_ShareTheBudget(
        int limitOffset,
        bool succeeds
    )
    {
        // Arrange
        // WHY: Each buffer alone stays far below the limit; only their sum reaches it.
        var files = Files(32 + limitOffset);
        await using var first = new SpillBuffer(0, true, files, "the first buffer");
        await using var second = new SpillBuffer(0, true, files, "the second buffer");
        await first.WriteAsync(new byte[16], TestFiles.Token);

        // Act
        var exception = await Record.ExceptionAsync(() => second
            .WriteAsync(new byte[16], TestFiles.Token)
            .AsTask());

        // Assert
        Assert.Equal(succeeds
                ? null
                : TabularErrorCodes.Resource.LimitExceeded,
            (exception as TabularResourceException)?.Error.Code);

        Assert.Equal(succeeds, exception is null);
    }

    [Fact]
    public void DeletedTemporaryFile_ReturnsItsBytesToTheBudget()
    {
        // Arrange
        var files = Files(16);
        var first = files.Create();
        first.Account(16);
        first.TryDelete();
        var second = files.Create();

        // Act
        var exception = Record.Exception(() => second.Account(16));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void OpenTemporaryFile_KeepsItsBytesFromAnotherFile()
    {
        // Arrange
        var files = Files(16);
        var first = files.Create();
        first.Account(16);
        var second = files.Create();

        // Act
        var exception = Record.Exception(() => second.Account(1));

        // Assert
        Assert.Equal(TabularErrorCodes.Resource.LimitExceeded,
            Assert.IsType<TabularResourceException>(exception)
                .Error
                .Code);

        Assert.True(files.CleanupAll());
    }

    private TemporaryFiles Files(
        long maxTemporaryBytes
    ) => new(new ResourceLimits(0,
        0,
        true,
        _directory,
        maxTemporaryBytes,
        null,
        null,
        null));
}
