using System.Runtime.InteropServices;
using System.Xml;

namespace Doka.Tabular.Excel;

internal sealed partial class ExcelSheetWriter
{
    /// <summary>Builds the table part in memory; the caller writes it to the archive asynchronously.</summary>
    private byte[] TableXml(
        int id,
        string range,
        bool filter
    )
    {
        using var memory = new MemoryStream();
        using (var writer = XmlWriter.Create(memory, ExcelXml.WriterSettings))
        {
            WriteTable(writer, id, range, filter);
        }

        return memory.ToArray();
    }

    private void WriteTable(
        XmlWriter writer,
        int id,
        string range,
        bool filter
    )
    {
        writer.WriteStartDocument(standalone: true);
        writer.WriteStartElement("table", MainNamespace);
        writer.WriteAttributeString("id", id.ToString(CultureInfo.InvariantCulture));
        var name = ExcelXml.Escape(_plan.TableName!.Value, attribute: true);
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("displayName", name);
        writer.WriteAttributeString("ref", range);
        writer.WriteAttributeString("totalsRowShown", "0");
        if (filter)
        {
            writer.WriteStartElement("autoFilter", MainNamespace);
            writer.WriteAttributeString("ref", range);
            writer.WriteEndElement();
        }

        writer.WriteStartElement("tableColumns", MainNamespace);
        writer.WriteAttributeString("count", _plan.Columns.Count.ToString(CultureInfo.InvariantCulture));
        for (var position = 0; position < _plan.Columns.Count; position++)
        {
            writer.WriteStartElement("tableColumn", MainNamespace);
            writer.WriteAttributeString("id", (position + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString(
                "name",
                ExcelXml.Escape(_plan.Columns[position].Header ?? string.Empty, attribute: true));

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteStartElement("tableStyleInfo", MainNamespace);
        if (_plan.TableStyle != ExcelTableStyle.None)
        {
            writer.WriteAttributeString("name", "TableStyle" + _plan.TableStyle);
        }

        writer.WriteAttributeString("showFirstColumn", "0");
        writer.WriteAttributeString("showLastColumn", "0");
        writer.WriteAttributeString(
            "showRowStripes",
            _plan.BandedRows
                ? "1"
                : "0");

        writer.WriteAttributeString("showColumnStripes", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private void WriteViews(
        XmlWriter xml
    )
    {
        var freezeRows = _plan.WriteHeader && _plan.Layout.FreezeHeader != ExcelHeaderFeature.Disabled
            ? _plan.Layout.StartRow
            : 0;

        var freezeColumns = _plan.Layout.FrozenColumns > 0
            ? _plan.Layout.StartColumn - 1 + _plan.Layout.FrozenColumns
            : 0;

        xml.WriteStartElement("sheetViews", MainNamespace);
        xml.WriteStartElement("sheetView", MainNamespace);
        xml.WriteAttributeString("workbookViewId", "0");
        if (freezeRows > 0
            || freezeColumns > 0)
        {
            xml.WriteStartElement("pane", MainNamespace);
            if (freezeColumns > 0)
            {
                xml.WriteAttributeString("xSplit", freezeColumns.ToString(CultureInfo.InvariantCulture));
            }

            if (freezeRows > 0)
            {
                xml.WriteAttributeString("ySplit", freezeRows.ToString(CultureInfo.InvariantCulture));
            }

            xml.WriteAttributeString("topLeftCell", ExcelXml.Reference(freezeColumns, freezeRows + 1));
            xml.WriteAttributeString(
                "activePane",
                (frozenRows: freezeRows > 0, frozenColumns: freezeColumns > 0) switch
                {
                    (true, true) => "bottomRight",
                    (true, false) => "bottomLeft",
                    (false, _) => "topRight",
                });

            xml.WriteAttributeString("state", "frozen");
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private void WriteHeaderRow(
        XmlWriter xml
    )
    {
        var row = _plan.Layout.StartRow;
        xml.WriteStartElement("row", MainNamespace);
        xml.WriteAttributeString("r", row.ToString(CultureInfo.InvariantCulture));
        if (_plan.Layout.HeaderHeight is { } height)
        {
            xml.WriteAttributeString("ht", height.ToString("0.##", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("customHeight", "1");
        }

        for (var position = 0; position < _plan.Columns.Count; position++)
        {
            var header = _plan.Columns[position].Header;
            var reference = ExcelXml.Reference(_plan.Layout.StartColumn - 1 + position, row);
            xml.WriteStartElement("c", MainNamespace);
            xml.WriteAttributeString("r", reference);
            if (_headerStyles[position] != 0)
            {
                xml.WriteAttributeString(
                    "s",
                    _headerStyles[position]
                        .ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(header))
            {
                xml.WriteAttributeString("t", "inlineStr");
                WriteInline(xml, header);
            }

            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private void WriteDataRow(
        IReadOnlyList<OutputCell> cells,
        long row
    )
    {
        var xml = _xml!;
        xml.WriteStartElement("row", MainNamespace);
        WriteNumberAttribute(xml, "r", row);
        for (var position = 0; position < cells.Count; position++)
        {
            if (cells[position].Kind != OutputKind.Empty)
            {
                WriteCell(xml, cells[position], position, row);
            }
        }

        xml.WriteEndElement();
    }

    private int StyleOf(
        string? numberFormat,
        int position
    )
    {
        if (_lastStyles[position] < 0
            || !ReferenceEquals(_lastFormats[position], numberFormat))
        {
            _lastStyles[position] = _workbook.Styles.Style(numberFormat, _alignments[position]);
            _lastFormats[position] = numberFormat;
        }

        return _lastStyles[position];
    }

    private void WriteCell(
        XmlWriter xml,
        in OutputCell cell,
        int position,
        long row
    )
    {
        var style = StyleOf(
            cell.Kind == OutputKind.Number
                ? cell.NumberFormat
                : null,
            position);

        xml.WriteStartElement("c", MainNamespace);
        xml.WriteStartAttribute("r");
        xml.WriteChars(_scratch, 0, ExcelXml.WriteReference(_scratch, _plan.Layout.StartColumn - 1 + position, row));
        xml.WriteEndAttribute();
        if (style != 0)
        {
            WriteNumberAttribute(xml, "s", style);
        }

        switch (cell.Kind)
        {
            case OutputKind.Number:
                cell.Number.TryFormat(_scratch, out var written, "R", CultureInfo.InvariantCulture);
                xml.WriteStartElement("v", MainNamespace);
                xml.WriteChars(_scratch, 0, written);
                xml.WriteEndElement();
                break;
            case OutputKind.Boolean:
                xml.WriteAttributeString("t", "b");
                xml.WriteElementString(
                    "v",
                    MainNamespace,
                    cell.Boolean
                        ? "1"
                        : "0");

                break;
            case OutputKind.Text:
                xml.WriteAttributeString("t", "inlineStr");
                WriteInline(xml, cell.Text);
                break;
            case OutputKind.Empty:
            default:
                throw new UnreachableException("Empty cells are skipped before the cell element starts.");
        }

        xml.WriteEndElement();
    }

    /// <summary>Writes a whole number as an attribute value without an intermediate string.</summary>
    private void WriteNumberAttribute(
        XmlWriter xml,
        string name,
        long value
    )
    {
        value.TryFormat(_scratch, out var written, default, CultureInfo.InvariantCulture);
        xml.WriteStartAttribute(name);
        xml.WriteChars(_scratch, 0, written);
        xml.WriteEndAttribute();
    }

    private static void WriteInline(
        XmlWriter xml,
        string text
    ) => WriteInline(xml, text.AsMemory());

    /// <summary>Writes an inline string; text without escapes is written from its buffer as it is.</summary>
    private static void WriteInline(
        XmlWriter xml,
        ReadOnlyMemory<char> text
    )
    {
        xml.WriteStartElement("is", MainNamespace);
        xml.WriteStartElement("t", MainNamespace);
        if (ExcelXml.NeedsEscape(text.Span, attribute: false))
        {
            var escaped = ExcelXml.Escape(text.ToString(), attribute: false);
            PreserveSpace(xml, escaped);
            xml.WriteString(escaped);
        }
        else
        {
            PreserveSpace(xml, text.Span);
            WriteText(xml, text);
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void PreserveSpace(
        XmlWriter xml,
        ReadOnlySpan<char> text
    )
    {
        if (!text.IsEmpty
            && (char.IsWhiteSpace(text[0])
                || char.IsWhiteSpace(text[^1])
                || text.ContainsAny('\n', '\t')))
        {
            xml.WriteAttributeString("xml", "space", null, "preserve");
        }
    }

    private static void WriteText(
        XmlWriter xml,
        ReadOnlyMemory<char> text
    )
    {
        // WHY: An empty slice can start at the end of the reused buffer, and XmlWriter.WriteChars reads the start
        // index even for zero characters.
        if (text.IsEmpty)
        {
            return;
        }

        if (MemoryMarshal.TryGetArray(text, out var segment))
        {
            xml.WriteChars(segment.Array!, segment.Offset, segment.Count);
        }
        else if (MemoryMarshal.TryGetString(text, out var whole, out var start, out var length)
            && start == 0
            && length == whole.Length)
        {
            xml.WriteString(whole);
        }
        else
        {
            xml.WriteString(text.ToString());
        }
    }

    private static double BaseWidth(
        ExportColumn column
    ) => column.Converter!.WidthClass switch
    {
        ExcelWidthClass.Guid => 38,
        ExcelWidthClass.Integer => 22,
        ExcelWidthClass.Decimal => 24,
        ExcelWidthClass.Date => 12,
        ExcelWidthClass.Time => 16,
        ExcelWidthClass.DateTime => 30,
        ExcelWidthClass.Duration => 20,
        ExcelWidthClass.Boolean => 10,
        _ => 24,
    };
}
