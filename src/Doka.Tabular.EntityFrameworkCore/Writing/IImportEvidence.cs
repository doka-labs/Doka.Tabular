namespace Doka.Tabular.EntityFrameworkCore;

/// <summary>Evidence of an import log written in the data transaction before every commit.</summary>
internal interface IImportEvidence
{
    Task RecordAsync(
        DbContext context,
        long rowsImported,
        CancellationToken cancellationToken
    );
}
