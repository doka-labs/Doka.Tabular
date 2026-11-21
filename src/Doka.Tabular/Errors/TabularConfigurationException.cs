namespace Doka.Tabular;

/// <summary>Thrown by <c>Build()</c> when a map or another configuration is structurally invalid.</summary>
[PublicAPI]
public sealed class TabularConfigurationException : Exception
{
    /// <summary>Creates the exception with at least one error.</summary>
    /// <param name="errors">All independently detectable errors in a fixed order; never empty.</param>
    /// <exception cref="ArgumentException"><paramref name="errors" /> is empty.</exception>
    public TabularConfigurationException(
        IReadOnlyList<TabularError> errors
    )
        : base(BuildMessage(errors))
    {
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one error is required.", nameof(errors));
        }

        Errors = errors;
    }

    /// <summary>Gets all detected configuration errors in a fixed order.</summary>
    public IReadOnlyList<TabularError> Errors { get; }

    private static string BuildMessage(
        IReadOnlyList<TabularError> errors
    )
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.Count == 1
            ? $"The configuration is invalid: {errors[0].Message}"
            : $"The configuration is invalid ({errors.Count} errors). First: {errors[0].Message}";
    }
}
