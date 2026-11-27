using System.Text;
using System.Xml;

namespace Doka.Tabular.Excel;

internal sealed partial class ExcelPackage
{
    private async Task LoadWorkbookAsync(
        string workbookPath,
        CancellationToken cancellationToken
    )
    {
        var relationships =
            await ReadRelationshipsAsync(_archive, _budget, RelationshipsOf(workbookPath), cancellationToken)
                .ConfigureAwait(false);

        var directory = workbookPath.Contains('/', StringComparison.Ordinal)
            ? workbookPath[..(workbookPath.LastIndexOf('/') + 1)]
            : string.Empty;

        await ReadWorkbookPartAsync(workbookPath, relationships, directory, cancellationToken)
            .ConfigureAwait(false);

        foreach (var relation in relationships.Values)
        {
            if (relation.Type.EndsWith("/sharedStrings", StringComparison.Ordinal))
            {
                SharedStrings = await ReadSharedStringsAsync(Resolve(directory, relation.Target), cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (relation.Type.EndsWith("/styles", StringComparison.Ordinal))
            {
                CellFormats = await ReadStylesAsync(Resolve(directory, relation.Target), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads the sheets and the date system.</summary>
    private async Task ReadWorkbookPartAsync(
        string workbookPath,
        Dictionary<string, Relationship> relationships,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var sheets = new List<ExcelSheetInfo>();
        await using var stream = await OpenPartAsync(workbookPath, cancellationToken)
            .ConfigureAwait(false);

        using var reader = XmlReader.Create(stream, ExcelXml.ReaderSettings);
        while (await reader
            .ReadAsync()
            .ConfigureAwait(false))
        {
            // WHY: Extension lists repeat local names in other namespaces; Excel writes
            // <x15:workbookPr chartTrackingRefBase="1"/> after <workbookPr date1904="1"/>, and reading it would
            // switch the date system back to 1900.
            if (reader is not
                {
                    NodeType: XmlNodeType.Element,
                    NamespaceURI: ExcelXml.TransitionalMain or ExcelXml.StrictMain,
                })
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "workbook":
                    break;
                case "workbookPr":
                    Uses1904DateSystem = reader.GetAttribute("date1904") is "1" or "true";
                    break;
                case "sheet" when SheetOf(reader, relationships, directory) is { } sheet:
                    sheets.Add(sheet);
                    break;
            }
        }

        Sheets = sheets;
    }

    /// <summary>The sheet of a sheet element; <see langword="null" /> when its relationship is missing.</summary>
    private static ExcelSheetInfo? SheetOf(
        XmlReader reader,
        Dictionary<string, Relationship> relationships,
        string directory
    )
    {
        var name = ExcelXml.Unescape(reader.GetAttribute("name") ?? string.Empty);

        return (reader.GetAttribute("id", TransitionalRelationships)
                ?? reader.GetAttribute("id", StrictRelationships)) is { } id
            && relationships.TryGetValue(id, out var target)
                ? new ExcelSheetInfo(TabularSheetName.FromWorkbook(name), Resolve(directory, target.Target))
                : null;
    }

    private async Task<IReadOnlyList<string?>> ReadSharedStringsAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = await OpenPartAsync(path, cancellationToken)
            .ConfigureAwait(false);

        using var reader = XmlReader.Create(stream, ExcelXml.ReaderSettings);
        var table = new SharedStringTable(this);
        while (await reader
            .ReadAsync()
            .ConfigureAwait(false))
        {
            await table
                .TakeAsync(reader)
                .ConfigureAwait(false);
        }

        return table.Strings;
    }

    private async Task<IReadOnlyList<NumberFormatKind>> ReadStylesAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        var custom = new Dictionary<int, string>();
        var formats = new List<NumberFormatKind>();
        await using var stream = await OpenPartAsync(path, cancellationToken)
            .ConfigureAwait(false);

        using var reader = XmlReader.Create(stream, ExcelXml.ReaderSettings);
        var inCellXfs = false;
        while (await reader
            .ReadAsync()
            .ConfigureAwait(false))
        {
            switch (reader)
            {
                case { NamespaceURI: not (ExcelXml.TransitionalMain or ExcelXml.StrictMain) }:
                    break;
                case { NodeType: XmlNodeType.Element, LocalName: "numFmt" }:
                    if (int.TryParse(
                        reader.GetAttribute("numFmtId"),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var id))
                    {
                        // WHY: formatCode is an ST_Xstring; an escaped code such as _x0079_ for y is a date format.
                        custom[id] = ExcelXml.Unescape(reader.GetAttribute("formatCode") ?? string.Empty);
                    }

                    break;
                case { NodeType: XmlNodeType.Element, LocalName: "cellXfs" }:
                    inCellXfs = !reader.IsEmptyElement;
                    break;
                case { NodeType: XmlNodeType.Element, LocalName: "xf" } when inCellXfs:
                    _ = int.TryParse(
                        reader.GetAttribute("numFmtId"),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var formatId);

                    formats.Add(NumberFormats.Classify(formatId, custom.GetValueOrDefault(formatId)));
                    break;
                case { NodeType: XmlNodeType.EndElement, LocalName: "cellXfs" }:
                    inCellXfs = false;
                    break;
            }
        }

        return formats;
    }

    /// <summary>Collects the text of every string item; phonetic runs are reading aids and are left out.</summary>
    private sealed class SharedStringTable
    {
        private readonly StringBuilder _builder = new();
        private readonly char[] _chunk = new char[1024];
        private readonly ExcelPackage _package;
        private bool _inItem;
        private bool _overlong;
        private int _phoneticDepth = -1;

        public SharedStringTable(
            ExcelPackage package
        )
        {
            _package = package;
        }

        public List<string?> Strings { get; } = [];

        /// <summary>Takes one node; the text of a <c>t</c> element is read up to its end element.</summary>
        public async ValueTask TakeAsync(
            XmlReader reader
        )
        {
            switch (reader)
            {
                case { NamespaceURI: not (ExcelXml.TransitionalMain or ExcelXml.StrictMain) }:
                    break;
                case { NodeType: XmlNodeType.Element, LocalName: "si" }:
                    _inItem = !reader.IsEmptyElement;
                    _overlong = false;
                    _builder.Clear();
                    if (reader.IsEmptyElement)
                    {
                        Strings.Add(string.Empty);
                    }

                    break;
                case { NodeType: XmlNodeType.Element, LocalName: "rPh" }:
                    _phoneticDepth = reader.Depth;
                    break;
                case { NodeType: XmlNodeType.Element, LocalName: "t", IsEmptyElement: false }
                    when _inItem && _phoneticDepth < 0:
                    await ReadTextAsync(reader)
                        .ConfigureAwait(false);

                    break;
                case { NodeType: XmlNodeType.EndElement, LocalName: "rPh" }
                    when reader.Depth == _phoneticDepth:
                    _phoneticDepth = -1;
                    break;
                case { NodeType: XmlNodeType.EndElement, LocalName: "si" }:
                    // WHY: The field limit applies to the cells that use the text inside the read range; a long text
                    // used only outside of it is no field value, so it is only marked here. The stored length bounds
                    // the text while it is read; the decoded length decides.
                    Strings.Add(
                        _overlong
                            ? null
                            : ExcelXml.Unescape(_builder.ToString()) is var text
                            && !_package.ExceedsFieldLimit(text.Length)
                                ? text
                                : null);

                    _inItem = false;
                    break;
            }
        }

        /// <summary>
        /// Appends the text of a <c>t</c> element in chunks; a text beyond the field limit is not kept further, so it
        /// never needs more memory than the limit allows. The reader ends on the end element.
        /// </summary>
        private async ValueTask ReadTextAsync(
            XmlReader reader
        )
        {
            var depth = reader.Depth;
            while (await reader
                .ReadAsync()
                .ConfigureAwait(false))
            {
                if (reader.NodeType is XmlNodeType.Text
                    or XmlNodeType.CDATA
                    or XmlNodeType.SignificantWhitespace
                    or XmlNodeType.Whitespace)
                {
                    int read;
                    while ((read = await reader
                            .ReadValueChunkAsync(_chunk, 0, _chunk.Length)
                            .ConfigureAwait(false))
                        > 0)
                    {
                        if (_overlong)
                        {
                            continue;
                        }

                        _builder.Append(_chunk.AsSpan(0, read));
                        if (_package.IsOverlong(_builder.Length))
                        {
                            _overlong = true;
                            _builder.Clear();
                        }
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement
                    && reader.Depth == depth)
                {
                    return;
                }
            }
        }
    }
}
