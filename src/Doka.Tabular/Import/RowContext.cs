namespace Doka.Tabular;

/// <summary>Errors and identity of the record being processed; one instance is reused for every record of a table.</summary>
internal sealed class RowContext
{
    public RowContext(
        TabularSheetName? sheetName,
        CancellationToken callCancellation
    )
    {
        SheetName = sheetName;
        CallCancellation = callCancellation;
    }

    /// <summary>Gets the token of the call; it tells whether a cancellation in application code is its own.</summary>
    public CancellationToken CallCancellation { get; }

    private long RowNumber { get; set; }

    private SourceRecord? Record { get; set; }

    public long RecordNumber { get; private set; }

    private TabularSheetName? SheetName { get; }

    public List<TabularError> Errors { get; } = [];

    /// <summary>Gets or sets the technical error that ends the run; set by callbacks that throw unexpectedly.</summary>
    public TabularError? Fatal { get; set; }

    public bool HasErrors => Errors.Count > 0 || Fatal is not null;

    /// <summary>Starts the next record; the errors of the previous record must have been taken.</summary>
    public void Start(
        SourceRecord record,
        long recordNumber
    )
    {
        Record = record;
        RowNumber = record.RowNumber;
        RecordNumber = recordNumber;
        Errors.Clear();
        Fatal = null;
    }

    public TabularErrorLocation Location(
        int? columnIndex = null,
        ColumnKey? key = null,
        TabularPropertyPath? path = null,
        string? header = null
    ) => new()
    {
        RowNumber = columnIndex is { } index && Record is { } record
            ? record.RowNumberOf(index)
            : RowNumber,
        RecordNumber = RecordNumber,
        ColumnIndex = columnIndex,
        ColumnKey = key,
        PropertyPath = path,
        Header = header,
        SheetName = SheetName,
    };
}
