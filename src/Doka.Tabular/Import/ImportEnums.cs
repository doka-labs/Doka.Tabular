namespace Doka.Tabular;

/// <summary>Error strategy of an import.</summary>
[PublicAPI]
public enum ImportStrategy
{
    /// <summary>One error and nothing is taken over.</summary>
    Strict,

    /// <summary>Rows with errors are skipped; the logical record is the unit.</summary>
    Partial,
}

/// <summary>How source columns are bound to mapped fields.</summary>
[PublicAPI]
public enum ColumnBinding
{
    /// <summary>No header row; the first row is a record; binding by position.</summary>
    ByIndex,

    /// <summary>A header row exists and is skipped; binding by position.</summary>
    ByIndexWithHeader,

    /// <summary>A header row exists and binds the configured headers; columns may be reordered.</summary>
    ByHeaderName,
}
