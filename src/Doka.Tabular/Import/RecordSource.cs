namespace Doka.Tabular;

/// <summary>One delimited source record.</summary>
internal sealed class SourceRecord
{
    public SourceRecord(
        long rowNumber,
        IReadOnlyList<SourceCell> cells
    )
    {
        RowNumber = rowNumber;
        Cells = cells;
    }

    /// <summary>Gets the CSV line where the record starts or the worksheet row.</summary>
    public long RowNumber { get; private set; }

    public IReadOnlyList<SourceCell> Cells { get; private set; }

    /// <summary>Gets, per cell, the lines between the record start and the start of the cell; CSV only.</summary>
    private IReadOnlyList<int>? LineOffsets { get; set; }

    /// <summary>
    /// Points the record at the next row. A source reuses one record, like its cell list; the record is valid until
    /// the next read of the source.
    /// </summary>
    public SourceRecord Set(
        long rowNumber,
        IReadOnlyList<SourceCell> cells,
        IReadOnlyList<int>? lineOffsets = null
    )
    {
        RowNumber = rowNumber;
        Cells = cells;
        LineOffsets = lineOffsets;

        return this;
    }

    /// <summary>Gets the row where the cell starts; in CSV a field behind a multiline field starts later.</summary>
    public long RowNumberOf(
        int columnIndex
    ) => LineOffsets is { } offsets && columnIndex < offsets.Count
        ? RowNumber + offsets[columnIndex]
        : RowNumber;

    public SourceCell CellAt(
        int index
    ) => index < Cells.Count
        ? Cells[index]
        : SourceCell.Missing;
}

internal enum RecordReadKind
{
    Record,

    /// <summary>A record with a known boundary that is syntactically broken; it counts as read and rejected.</summary>
    BrokenRecord,

    /// <summary>The reader can no longer determine record boundaries; the run ends.</summary>
    Fatal,

    End,
}

internal readonly record struct RecordReadResult(
    RecordReadKind Kind,
    SourceRecord? Record,
    TabularError? Error
)
{
    public static RecordReadResult EndOfData { get; } = new(RecordReadKind.End, null, null);

    public static RecordReadResult Of(
        SourceRecord record
    ) => new(RecordReadKind.Record, record, null);

    public static RecordReadResult Broken(
        TabularError error
    ) => new(RecordReadKind.BrokenRecord, null, error);

    public static RecordReadResult FatalError(
        TabularError error
    ) => new(RecordReadKind.Fatal, null, error);
}

/// <summary>Forward-only reader of the records of one table (CSV file or worksheet).</summary>
internal interface IRecordSource
{
    /// <summary>Gets the worksheet, for error locations; <see langword="null" /> for CSV.</summary>
    TabularSheetName? SheetName { get; }

    /// <summary>Gets whether missing positions are structural errors (CSV) or empty cells (Excel).</summary>
    bool MissingIsStructural { get; }

    ValueTask<RecordReadResult> ReadAsync(
        CancellationToken cancellationToken
    );
}
