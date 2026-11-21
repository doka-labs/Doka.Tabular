namespace Doka.Tabular.Excel;

internal sealed record TableCounts(
    long RowsRead,
    long RowsRejected,
    bool HadDataErrors,
    bool Aborted,
    bool Canceled,
    bool SchemaFailed,
    long ValidRows
)
{
    public bool HasErrors => HadDataErrors || Aborted || SchemaFailed;
}
