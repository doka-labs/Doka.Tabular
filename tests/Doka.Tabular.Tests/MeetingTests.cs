namespace Doka.Tabular.Tests;

/// <summary>The meeting of concurrency tests has no deadline; a participant that fails before it arrives releases the others.</summary>
public sealed class MeetingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ParticipantThatFailsBeforeItArrives_ReleasesTheOthers()
    {
        // Arrange
        using var meeting = new Meeting(3);

        // Act
        // WHY: The release of the waiting participants by the failing one is the checked contract, so all three form
        // one action.
        // ReSharper disable once AccessToDisposedClosure
        // WHY: The participants are awaited in the act, before the captured meeting is disposed.
        var first = meeting.JoinAsync(() => ArriveAsync(meeting), Token);

        // ReSharper disable once AccessToDisposedClosure
        // WHY: The participants are awaited in the act, before the captured meeting is disposed.
        var second = meeting.JoinAsync(() => ArriveAsync(meeting), Token);
        var failing = meeting.JoinAsync<int>(
            () => throw new InvalidOperationException("Failed before the meeting."),
            Token);

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(first, second, failing));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal((2, 1, 1), (meeting.Met, await first, await second));
    }

    [Fact]
    public async Task AllParticipantsThatArrive_PassTogether()
    {
        // Arrange
        using var meeting = new Meeting(2);

        // Act
        // WHY: Two participants meet, so their start and the wait for both form one action.
        // ReSharper disable once AccessToDisposedClosure
        // WHY: The participants are awaited in the act, before the captured meeting is disposed.
        var first = meeting.JoinAsync(() => ArriveAsync(meeting), Token);

        // ReSharper disable once AccessToDisposedClosure
        // WHY: The participants are awaited in the act, before the captured meeting is disposed.
        var second = meeting.JoinAsync(() => ArriveAsync(meeting), Token);
        var both = await Task.WhenAll(first, second);

        // Assert
        Assert.Equal([1, 1], both);
        Assert.Equal(2, meeting.Met);
    }

    [Fact]
    public void Arrival_OutsideOfAParticipant_IsAnError()
    {
        // Arrange
        using var meeting = new Meeting(1);

        // Act
        // ReSharper disable once AccessToDisposedClosure
        // WHY: The record runs the arrival at once, before the captured meeting is disposed.
        var exception = Record.Exception(() => meeting.Arrive(Token));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    private static Task<int> ArriveAsync(
        Meeting meeting
    )
    {
        meeting.Arrive(Token);

        return Task.FromResult(1);
    }
}
