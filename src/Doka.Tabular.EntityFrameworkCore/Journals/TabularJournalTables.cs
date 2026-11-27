namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Settings of a named journal registered with <c>AddTabularImportJournal</c> or <c>AddTabularExportJournal</c>.</summary>
[PublicAPI]
public sealed class TabularJournalTables
{
    /// <summary>Gets or sets the table prefix; the default is <c>DokaTabular_</c>, empty is allowed.</summary>
    public string TablePrefix { get; set; } = "DokaTabular_";

    /// <summary>Gets or sets whether import scopes are compared with <see cref="StringComparison.OrdinalIgnoreCase" />; the default is ordinal.</summary>
    public bool IgnoreImportScopeCase { get; set; }
}
