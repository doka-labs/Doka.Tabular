namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Write settings of a target, set with <c>WithOptions</c>.</summary>
[PublicAPI]
public sealed record TabularWriteOptions
{
    /// <summary>Gets whether the report returns the confirmed committed records; the default is <see langword="false" />.</summary>
    public bool ReturnItems { get; init; }

    /// <summary>Gets whether an active transaction of a passed context is joined; Tabular then never commits.</summary>
    public bool JoinExistingTransaction { get; init; }

    /// <summary>Gets the number of valid rows per commit in a partial import; the default is 100, 1 commits every row.</summary>
    public int CommitGroupSize
    {
        get;
        init
        {
            field = value;
            IsCommitGroupSizeSet = true;
        }
    } = 100;

    /// <summary>Gets the isolation level of an own transaction; <see langword="null" /> uses the provider default.</summary>
    public IsolationLevel? IsolationLevel { get; init; }

    /// <summary>Gets whether a multi-sheet import uses own transaction boundaries per sheet; the default is <see langword="false" />.</summary>
    public bool TransactionPerSheet { get; init; }

    /// <summary>Gets whether <see cref="CommitGroupSize" /> was set; a set size is a configuration error with <see cref="JoinExistingTransaction" />.</summary>
    internal bool IsCommitGroupSizeSet { get; private init; }
}
