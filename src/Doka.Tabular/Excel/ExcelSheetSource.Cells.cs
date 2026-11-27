using System.Xml;

namespace Doka.Tabular.Excel;

internal sealed partial class ExcelSheetSource
{
    private async ValueTask<SourceCell> ReadCellAsync(
        XmlReader reader,
        CancellationToken cancellationToken
    )
    {
        var type = TypeOf(reader);

        // WHY: A cell without style attribute uses the first cell format, the default of the workbook.
        var style = int.TryParse(
            reader.GetAttribute("s"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;

        var format = style < _package.CellFormats.Count
            ? _package.CellFormats[style]
            : NumberFormatKind.General;

        _valueStart = -1;
        _inlineStart = -1;
        _isFormula = false;
        if (!reader.IsEmptyElement)
        {
            await ReadCellContentAsync(reader, cancellationToken)
                .ConfigureAwait(false);
        }

        return Build(type, format);
    }

    /// <summary>The cell type from the t attribute.</summary>
    private static CellType TypeOf(
        XmlReader reader
    ) => reader.GetAttribute("t") switch
    {
        "s" => CellType.Shared,
        "inlineStr" => CellType.Inline,
        "str" => CellType.Text,
        "b" => CellType.Boolean,
        "e" => CellType.Error,
        "d" => CellType.Date,
        _ => CellType.Number,
    };

    /// <summary>Reads the value, inline text, and formula marker of a cell up to the end of the cell element.</summary>
    private async ValueTask ReadCellContentAsync(
        XmlReader reader,
        CancellationToken cancellationToken
    )
    {
        var depth = reader.Depth;
        var positioned = false;

        // WHY: Content reads and Skip already move to the following node; it must be examined before reading on.
        while (positioned
            || await reader
                .ReadAsync()
                .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == depth)
            {
                return;
            }

            positioned = reader is
                {
                    NodeType: XmlNodeType.Element,
                    NamespaceURI: ExcelXml.TransitionalMain or ExcelXml.StrictMain,
                }
                && await TakeElementAsync(reader)
                    .ConfigureAwait(false);
        }
    }

    /// <summary>Takes one child element of a cell; returns whether the reader already moved past it.</summary>
    private async ValueTask<bool> TakeElementAsync(
        XmlReader reader
    )
    {
        switch (reader.LocalName)
        {
            case "f":
                _isFormula = true;

                return false;
            case "rPh":
                // WHY: Phonetic runs are reading aids, not cell text; the next read follows their end.
                await reader
                    .SkipAsync()
                    .ConfigureAwait(false);

                return true;
            case "v" when !reader.IsEmptyElement:
                _valueStart = _text.Length;
                await AppendTextAsync(reader)
                    .ConfigureAwait(false);

                _valueLength = _text.Length - _valueStart;

                return true;
            case "v":
                // WHY: Excel stores the empty result of a formula such as ="" as <v/>; it is a stored empty
                // value, not a missing result.
                if (_valueStart < 0)
                {
                    _valueStart = _text.Length;
                    _valueLength = 0;
                }

                return false;
            case "t" when !reader.IsEmptyElement:
                MoveInlineToEnd();
                await AppendTextAsync(reader)
                    .ConfigureAwait(false);

                _inlineLength = _text.Length - _inlineStart;
                _package.CheckStoredLength(_inlineLength);

                return true;
            case "t":
                MoveInlineToEnd();
                _inlineLength = _text.Length - _inlineStart;

                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// Makes the inline text the last text of the row buffer, so the next run of a rich text extends it.
    /// </summary>
    private void MoveInlineToEnd()
    {
        if (_inlineStart < 0)
        {
            _inlineStart = _text.Length;
            _inlineLength = 0;

            return;
        }

        if (_inlineStart + _inlineLength == _text.Length)
        {
            return;
        }

        // WHY: A value element between two runs is not valid SpreadsheetML; the copy keeps such a file readable.
        var start = _text.Length;
        _text.Append(
            _text
                .Slice(_inlineStart)
                .Span[.._inlineLength]);

        _inlineStart = start;
    }

    /// <summary>Appends the text content of the current element to the row buffer; the reader ends after it.</summary>
    private async ValueTask AppendTextAsync(
        XmlReader reader
    )
    {
        var depth = reader.Depth;
        var start = _text.Length;
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
                    _text.Append(_chunk.AsSpan(0, read));
                    _package.CheckStoredLength(_text.Length - start);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == depth)
            {
                break;
            }
        }

        await reader
            .ReadAsync()
            .ConfigureAwait(false);
    }

    private SourceCell Build(
        CellType type,
        NumberFormatKind format
    )
    {
        var isFormula = _isFormula;
        var value = _valueStart < 0
            ? ReadOnlyMemory<char>.Empty
            : _text
                .Slice(_valueStart)[.._valueLength];

        switch (type)
        {
            case CellType.Shared:
                if (!int.TryParse(value.Span, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    || index >= _package.SharedStrings.Count)
                {
                    throw new FormatException("A shared string index is invalid.");
                }

                // WHY: A missing entry is a text beyond the field limit; a cell inside the range makes it an error.
                var shared = _package.SharedStrings[index] ?? throw _package.FieldLimitExceeded();
                _package.CheckLength(shared.Length);

                return SourceCell.FromText(shared.AsMemory(), isFormula);
            case CellType.Inline:
                return SourceCell.FromText(
                    Unescaped(
                        _inlineStart < 0
                            ? ReadOnlyMemory<char>.Empty
                            : _text
                                .Slice(_inlineStart)[.._inlineLength]),
                    isFormula);
            case CellType.Text:
                return _valueStart < 0 && isFormula
                    ? SourceCell.FormulaWithoutResult
                    : SourceCell.FromText(Unescaped(value), isFormula);
            case CellType.Boolean:
                return value.IsEmpty
                    ? Empty(isFormula)
                    : value.Span switch
                    {
                        // WHY: A native Boolean cell holds xsd:boolean; any other text is a damaged cell.
                        "1" or "true" => SourceCell.FromBoolean(true, isFormula),
                        "0" or "false" => SourceCell.FromBoolean(false, isFormula),
                        _ => throw new FormatException("A Boolean cell is neither 1 nor 0."),
                    };
            case CellType.Error:
                // WHY: Error cells are rare; their code is kept as a string.
                return SourceCell.FromError(
                    value.IsEmpty
                        ? "#N/A"
                        : value.ToString(),
                    isFormula);
            case CellType.Date:
                if (value.IsEmpty)
                {
                    return Empty(isFormula);
                }

                return ExcelDateText.TryToSerial(
                    value.ToString(),
                    _package.Uses1904DateSystem,
                    out var serial,
                    out var shape,
                    out var utc)
                    ? SourceCell.FromNumber(
                            serial,
                            format is NumberFormatKind.General or NumberFormatKind.Other
                                ? shape
                                : format,
                            isFormula) with
                        {
                            IsUtc = utc,
                        }
                    : throw new FormatException("A date cell is not an ISO 8601 date, time or duration.");
            case CellType.Number:
            default:
                if (value.IsEmpty)
                {
                    return Empty(isFormula);
                }

                return double.TryParse(value.Span, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? SourceCell.FromNumber(number, format, isFormula)
                    : throw new FormatException("A numeric cell is invalid.");
        }
    }

    /// <summary>
    /// The text with <c>_xHHHH_</c> escapes resolved, checked against the field limit; text without escapes is returned
    /// as it is.
    /// </summary>
    private ReadOnlyMemory<char> Unescaped(
        ReadOnlyMemory<char> text
    )
    {
        var value = text.Span.Contains("_x", StringComparison.Ordinal)
            ? ExcelXml
                .Unescape(text.ToString())
                .AsMemory()
            : text;

        _package.CheckLength(value.Length);

        return value;
    }

    /// <summary>
    /// A cell with an empty value: a stored empty result such as <c>&lt;v/&gt;</c> is an empty value of every type,
    /// a missing value element is a formula without a stored result.
    /// </summary>
    private SourceCell Empty(
        bool isFormula
    ) => _valueStart < 0
        ? Missing(isFormula)
        : isFormula
            ? SourceCell.EmptyFormula
            : SourceCell.Empty;

    /// <summary>A cell without a stored value: empty, or a formula whose result was not stored.</summary>
    private static SourceCell Missing(
        bool isFormula
    ) => isFormula
        ? SourceCell.FormulaWithoutResult
        : SourceCell.Empty;
}

/// <summary>The stored type of a cell, from its t attribute.</summary>
internal enum CellType
{
    Number,
    Shared,
    Inline,
    Text,
    Boolean,
    Error,
    Date,
}
