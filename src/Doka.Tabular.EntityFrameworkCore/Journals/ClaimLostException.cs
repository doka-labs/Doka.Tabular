namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>The running import lost its claim or its log to another call; its group is not committed.</summary>
internal sealed class ClaimLostException : InvalidOperationException
{
    public ClaimLostException(
        string message
    )
        : base(message) { }
}
