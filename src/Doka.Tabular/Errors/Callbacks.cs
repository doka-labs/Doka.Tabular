namespace Doka.Tabular;

/// <summary>The rule for exceptions from application functions such as rules, factories, and write functions.</summary>
internal static class Callbacks
{
    /// <summary>
    /// Whether an exception of an application function is its failure. A cancellation while the call itself is
    /// canceled is the cancellation of the call and passes on; a cancellation the call did not request is a failure.
    /// </summary>
    public static bool Failed(
        Exception exception,
        CancellationToken callCancellation
    ) => exception is not OutOfMemoryException
        && !(exception is OperationCanceledException && callCancellation.IsCancellationRequested);
}
