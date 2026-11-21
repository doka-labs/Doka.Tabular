namespace Doka.Tabular;

/// <summary>File format chosen per call at the common entry point.</summary>
[PublicAPI]
public enum TabularType
{
    /// <summary>CSV.</summary>
    Csv,

    /// <summary>Excel workbook (XLSX).</summary>
    Excel,
}

/// <summary>MIME type and file extension of a <see cref="TabularType" />.</summary>
[PublicAPI]
public static class TabularTypeExtensions
{
    extension(
        TabularType type
    )
    {
        /// <summary>Gets the MIME type without charset.</summary>
        public string ContentType => type switch
        {
            TabularType.Csv => "text/csv",
            TabularType.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The format is not defined."),
        };

        /// <summary>Gets the file extension including the dot.</summary>
        public string FileExtension => type switch
        {
            TabularType.Csv => ".csv",
            TabularType.Excel => ".xlsx",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The format is not defined."),
        };
    }
}
