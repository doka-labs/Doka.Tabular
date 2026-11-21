namespace Doka.Tabular;

/// <summary>Result of reading and checking one table.</summary>
internal sealed class TableOutcome<TItem>
{
    public List<ImportedRow<TItem>> Valid { get; } = [];

    public long RowsRead { get; set; }

    public long RowsRejected { get; set; }

    public bool Aborted { get; set; }

    public bool Canceled { get; set; }

    public bool HadDataErrors { get; set; }

    /// <summary>Gets or sets whether the header did not match, so that no data row was processed.</summary>
    public bool SchemaFailed { get; set; }
}
