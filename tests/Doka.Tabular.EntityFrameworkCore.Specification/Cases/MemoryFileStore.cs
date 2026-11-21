namespace Doka.Tabular.EntityFrameworkCore.Specification;

/// <summary>Storage adapter in memory that can fail deletions on request.</summary>
public sealed class MemoryFileStore : ITabularFileStore
{
    public Dictionary<Guid, byte[]> Files { get; } = [];

    public bool FailDeletes { get; set; }

    public async Task<TabularStoredFile> SaveAsync(
        TabularFileWriteRequest request,
        Stream content,
        CancellationToken cancellationToken
    )
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[request.ExportId] = copy.ToArray();

        // A real adapter reads its metadata back from storage, so the hash is an equal but separate instance.
        return new TabularStoredFile(request.ExportId,
            request.ExportId,
            request.ByteSize,
            Sha256Hash.Parse(request.Sha256.Value));
    }

    public Task DeleteAsync(
        Guid exportId,
        CancellationToken cancellationToken
    )
    {
        if (FailDeletes)
        {
            throw new IOException("store unavailable");
        }

        Files.Remove(exportId);

        return Task.CompletedTask;
    }
}
