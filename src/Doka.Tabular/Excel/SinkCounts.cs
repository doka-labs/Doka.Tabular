namespace Doka.Tabular.Excel;

internal sealed record SinkCounts(
    long RowsImported,
    long RowsPendingCommit,
    long RowsSkipped,
    long RowsUncertain,
    long RowsRejected,
    bool HadErrors,
    bool Canceled,
    bool CommitConfirmed,
    bool Failed,
    bool ScopeRolledBack,
    bool PersistenceUncertain
);
