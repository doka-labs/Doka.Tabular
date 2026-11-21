namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Result of writing one table through a session.</summary>
internal sealed class WriteResult
{
    public long RowsImported { get; set; }

    public long RowsPendingCommit { get; set; }

    public long RowsSkipped { get; set; }

    public long RowsUncertain { get; set; }

    public long RowsRejected { get; set; }

    public List<TabularError> Errors { get; } = [];

    public TabularError? Fatal { get; set; }

    public bool Canceled { get; set; }

    public bool Uncertain { get; set; }

    /// <summary>
    /// Gets or sets whether the last step of the table was a successful commit, also when releasing it failed later.
    /// </summary>
    public bool CommitConfirmed { get; set; }

    /// <summary>Gets or sets whether a rollback also removed the pending rows of earlier tables of the call.</summary>
    public bool ScopeRolledBack { get; set; }

    /// <summary>Gets the indexes of rows whose commit is confirmed.</summary>
    public List<int> Committed { get; } = [];
}
