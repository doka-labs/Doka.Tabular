namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Import log settings of a destination view.</summary>
internal sealed record ImportLogBinding(
    TabularJournal Journal,
    TabularImportLogOptions Options
)
{
    public ImportJournalRun Start(
        WriteSession session
    ) => new(this, session);
}
