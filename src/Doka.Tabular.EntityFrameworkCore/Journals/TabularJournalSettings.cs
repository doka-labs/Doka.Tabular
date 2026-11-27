namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Settings of the standard journal registered with <see cref="TabularJournalModelExtensions.UseTabularJournal" />.</summary>
[PublicAPI]
public sealed class TabularJournalSettings
{
    /// <summary>Gets or sets the table prefix; the default is <c>DokaTabular_</c>, empty is allowed.</summary>
    public string TablePrefix { get; set; } = "DokaTabular_";

    /// <summary>Gets or sets whether the import journal is registered; the default is <see langword="true" />.</summary>
    public bool ImportEnabled { get; set; } = true;

    /// <summary>Gets or sets whether the export journal is registered; the default is <see langword="true" />.</summary>
    public bool ExportEnabled { get; set; } = true;

    /// <summary>Gets or sets whether import scopes are compared with <see cref="StringComparison.OrdinalIgnoreCase" />; the default is ordinal.</summary>
    public bool IgnoreImportScopeCase { get; set; }
}
