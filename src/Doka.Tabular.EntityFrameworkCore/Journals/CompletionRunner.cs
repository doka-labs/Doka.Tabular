namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Runs a completion function with its own token and time limit, separate from the call token.</summary>
internal static class CompletionRunner
{
    /// <summary>Gets the longest time limit a cancellation timer supports, about 49.7 days.</summary>
    public static TimeSpan MaxTimeout { get; } = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    public static async Task<TabularError?> RunAsync(
        Func<CancellationToken, Task> completion,
        TimeSpan timeout,
        TimeProvider time
    )
    {
        using var limit = new CancellationTokenSource(timeout, time);
        try
        {
            // WHY: The function belongs to the application and may use its scope; the time limit only cancels the
            // token, the call still awaits the end of the function before it returns.
            await completion(limit.Token)
                .ConfigureAwait(false);

            return limit.IsCancellationRequested
                ? Exceeded()
                : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return limit.IsCancellationRequested
                ? Exceeded()
                : TabularError.Create(
                    TabularErrorCodes.Callback.Failed,
                    TabularErrorKind.Technical,
                    "The completion function threw an exception.");
        }
    }

    private static TabularError Exceeded() => TabularError.Create(
        TabularErrorCodes.Callback.Failed,
        TabularErrorKind.Technical,
        "The completion function exceeded CompletionTimeout.");
}
