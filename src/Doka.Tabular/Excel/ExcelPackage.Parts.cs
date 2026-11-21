using System.IO.Compression;
using System.Xml;

namespace Doka.Tabular.Excel;

internal sealed partial class ExcelPackage
{
    private static async Task<Dictionary<string, string>> ReadContentTypesAsync(
        ZipArchive archive,
        ExpansionBudget budget,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = archive.GetEntry("[Content_Types].xml");
        if (entry is null)
        {
            return result;
        }

        await using var stream = new ExpansionCountingStream(await entry
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false),
            budget);

        using var reader = XmlReader.Create(stream, ExcelXml.ReaderSettings);
        while (await reader
            .ReadAsync()
            .ConfigureAwait(false))
        {
            // WHY: One guarded branch; a switch statement would add a case scope without a readability gain.
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "Override" }
                && reader.GetAttribute("PartName") is { } part
                && reader.GetAttribute("ContentType") is { } type)
            {
                result[part.TrimStart('/')] = type;
            }
            else if (reader is { NodeType: XmlNodeType.Element, LocalName: "Default" }
                && reader.GetAttribute("Extension") is { } extension
                && reader.GetAttribute("ContentType") is { } defaultType)
            {
                result[DefaultKey + extension] = defaultType;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the content type of a part: its override, otherwise the default of its extension (ECMA-376 Part 2,
    /// Open Packaging Conventions).
    /// </summary>
    private static string? ContentTypeOf(
        Dictionary<string, string> contentTypes,
        string partPath
    )
    {
        if (contentTypes.TryGetValue(partPath, out var type))
        {
            return type;
        }

        var dot = partPath.LastIndexOf('.');

        return dot >= 0 && contentTypes.TryGetValue(DefaultKey + partPath[(dot + 1)..], out var fallback)
            ? fallback
            : null;
    }

    private static async Task<string?> FindWorkbookAsync(
        ZipArchive archive,
        ExpansionBudget budget,
        CancellationToken cancellationToken
    )
    {
        var relationships = await ReadRelationshipsAsync(archive, budget, "_rels/.rels", cancellationToken)
            .ConfigureAwait(false);

        return relationships.Values.FirstOrDefault(relation
            => relation.Type.EndsWith("/officeDocument", StringComparison.Ordinal)) is { } document
            ? Resolve(string.Empty, document.Target)
            : null;
    }

    private static async Task<Dictionary<string, Relationship>> ReadRelationshipsAsync(
        ZipArchive archive,
        ExpansionBudget budget,
        string path,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<string, Relationship>(StringComparer.Ordinal);
        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            return result;
        }

        await using var stream = new ExpansionCountingStream(await entry
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false),
            budget);

        using var reader = XmlReader.Create(stream, ExcelXml.ReaderSettings);
        while (await reader
            .ReadAsync()
            .ConfigureAwait(false))
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "Relationship" }
                && reader.GetAttribute("Id") is { } id
                && reader.GetAttribute("Type") is { } type
                && reader.GetAttribute("Target") is { } target
                && reader.GetAttribute("TargetMode") is not "External")
            {
                result[id] = new Relationship(type, target);
            }
        }

        return result;
    }

    private static string RelationshipsOf(
        string partPath
    )
    {
        var slash = partPath.LastIndexOf('/');

        return slash < 0
            ? $"_rels/{partPath}.rels"
            : $"{partPath[..slash]}/_rels/{partPath[(slash + 1)..]}.rels";
    }

    private static string Resolve(
        string directory,
        string target
    )
    {
        if (target.StartsWith('/'))
        {
            return target.TrimStart('/');
        }

        var segments = (directory + target)
            .Split('/')
            .ToList();

        for (var index = 0; index < segments.Count; index++)
        {
            // WHY: The branches change the segment list and the index; a switch would hide that side effect.
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (segments[index] == "..")
            {
                segments.RemoveAt(index);
                if (index > 0)
                {
                    segments.RemoveAt(index - 1);
                    index--;
                }

                index--;
            }
            else if (segments[index] == ".")
            {
                segments.RemoveAt(index);
                index--;
            }
        }

        return string.Join('/', segments);
    }
}
