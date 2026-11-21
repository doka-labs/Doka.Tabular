namespace Doka.Tabular;

/// <summary>Location of an error. Properties that cannot be determined stay <see langword="null" />.</summary>
[PublicAPI]
public sealed class TabularErrorLocation
{
    /// <summary>Gets an empty location.</summary>
    public static TabularErrorLocation None { get; } = new();

    /// <summary>
    /// Gets the visible row starting at 1, including a header row: for CSV the physical line in which the field
    /// starts, for Excel the worksheet row.
    /// </summary>
    public long? RowNumber { get; init; }

    /// <summary>Gets the logical record starting at 1, without a header row.</summary>
    public long? RecordNumber { get; init; }

    /// <summary>Gets the actual zero-based source index; for header-name binding the position in the file.</summary>
    public int? ColumnIndex { get; init; }

    /// <summary>Gets the column key of the mapped field.</summary>
    public ColumnKey? ColumnKey { get; init; }

    /// <summary>Gets the header read from the file.</summary>
    public string? Header { get; init; }

    /// <summary>Gets the expected header.</summary>
    public string? ExpectedHeader { get; init; }

    /// <summary>Gets the target path, for example <c>Address.PostalCode</c>.</summary>
    public TabularPropertyPath? PropertyPath { get; init; }

    /// <summary>Gets the Excel worksheet.</summary>
    public TabularSheetName? SheetName { get; init; }
}
