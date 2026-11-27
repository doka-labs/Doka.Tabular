using System.Xml;

// WHY: These writers write into the XmlWriter buffer of a ZIP entry whose output lands in the workbook's drain
// buffer first (AsyncDrainStream); element writes never reach the caller's stream synchronously, and the flushes
// that move data on are asynchronous.
// ReSharper disable MethodHasAsyncOverload

namespace Doka.Tabular.Excel;

internal readonly record struct CellStyleKey(
    int NumberFormatId,
    int FontId,
    int FillId,
    ExcelHorizontalAlignment? Horizontal,
    ExcelVerticalAlignment? Vertical
);

/// <summary>Collects the distinct cell formats of a workbook; equal formatting shares one format.</summary>
internal sealed class ExcelStyleRegistry
{
    private const string Ns = ExcelXml.TransitionalMain;

    private readonly List<ExcelColor?> _fills = [null, null];
    private readonly List<(ExcelColor? Color, bool Bold)> _fonts = [(null, false)];
    private readonly Dictionary<string, int> _numberFormats = new(StringComparer.Ordinal);
    private readonly Dictionary<CellStyleKey, int> _styleIndex = [];
    private readonly List<CellStyleKey> _styles = [new(0, 0, 0, null, null)];

    public ExcelStyleRegistry()
    {
        _styleIndex[_styles[0]] = 0;
    }

    public int Style(
        string? numberFormat,
        ExcelHorizontalAlignment? horizontal
    ) => Register(new CellStyleKey(NumberFormatId(numberFormat), 0, 0, horizontal, null));

    public int HeaderStyle(
        ExcelLayoutOptions layout,
        ExcelHorizontalAlignment? horizontal
    )
    {
        var fontId = 0;
        if (layout is { HeaderFontColor: not null } or { HeaderBold: true })
        {
            var font = (layout.HeaderFontColor, layout.HeaderBold is true);
            fontId = _fonts.IndexOf(font);
            if (fontId < 0)
            {
                _fonts.Add(font);
                fontId = _fonts.Count - 1;
            }
        }

        var fillId = 0;
        if (layout.HeaderFill is { } fill)
        {
            fillId = _fills.IndexOf(fill);
            if (fillId < 2)
            {
                _fills.Add(fill);
                fillId = _fills.Count - 1;
            }
        }

        return Register(new CellStyleKey(0, fontId, fillId, horizontal, layout.HeaderVerticalAlignment));
    }

    private int Register(
        CellStyleKey key
    )
    {
        if (!_styleIndex.TryGetValue(key, out var index))
        {
            _styles.Add(key);
            index = _styles.Count - 1;
            _styleIndex[key] = index;
        }

        return index;
    }

    private int NumberFormatId(
        string? code
    )
    {
        if (code is null)
        {
            return 0;
        }

        if (!_numberFormats.TryGetValue(code, out var id))
        {
            id = 164 + _numberFormats.Count;
            _numberFormats[code] = id;
        }

        return id;
    }

    /// <summary>Builds the styles part in memory.</summary>
    public byte[] ToXml()
    {
        using var memory = new MemoryStream();
        using (var writer = XmlWriter.Create(memory, ExcelXml.WriterSettings))
        {
            Write(writer);
        }

        return memory.ToArray();
    }

    private void Write(
        XmlWriter writer
    )
    {
        writer.WriteStartDocument(standalone: true);
        writer.WriteStartElement("styleSheet", Ns);
        WriteNumberFormats(writer);
        WriteFonts(writer);
        WriteFills(writer);
        writer.WriteRaw("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        writer.WriteRaw(
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");

        WriteCellFormats(writer);
        writer.WriteRaw("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
        writer.WriteRaw("<dxfs count=\"0\"/>");
        writer.WriteRaw(
            "<tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium2\" defaultPivotStyle=\"PivotStyleLight16\"/>");

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private void WriteNumberFormats(
        XmlWriter writer
    )
    {
        if (_numberFormats.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("numFmts", Ns);
        writer.WriteAttributeString("count", _numberFormats.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (code, id) in _numberFormats.OrderBy(entry => entry.Value))
        {
            writer.WriteStartElement("numFmt", Ns);
            writer.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("formatCode", ExcelXml.Escape(code, attribute: true));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private void WriteFonts(
        XmlWriter writer
    )
    {
        writer.WriteStartElement("fonts", Ns);
        writer.WriteAttributeString("count", _fonts.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (color, bold) in _fonts)
        {
            WriteFont(writer, color, bold);
        }

        writer.WriteEndElement();
    }

    /// <summary>Writes Calibri 11, the Excel default font, with an optional color and bold weight.</summary>
    private static void WriteFont(
        XmlWriter writer,
        ExcelColor? color,
        bool bold
    )
    {
        writer.WriteStartElement("font", Ns);
        if (bold)
        {
            writer.WriteElementString("b", Ns, null);
        }

        WriteValue(writer, "sz", "11");
        writer.WriteStartElement("color", Ns);
        if (color is null)
        {
            writer.WriteAttributeString("theme", "1");
        }
        else
        {
            writer.WriteAttributeString("rgb", color.Argb);
        }

        writer.WriteEndElement();
        WriteValue(writer, "name", "Calibri");
        WriteValue(writer, "family", "2");
        WriteValue(writer, "scheme", "minor");
        writer.WriteEndElement();
    }

    private static void WriteValue(
        XmlWriter writer,
        string element,
        string value
    )
    {
        writer.WriteStartElement(element, Ns);
        writer.WriteAttributeString("val", value);
        writer.WriteEndElement();
    }

    private void WriteFills(
        XmlWriter writer
    )
    {
        writer.WriteStartElement("fills", Ns);
        writer.WriteAttributeString("count", _fills.Count.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < _fills.Count; index++)
        {
            writer.WriteStartElement("fill", Ns);
            writer.WriteStartElement("patternFill", Ns);
            WritePattern(writer, index, _fills[index]);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    /// <summary>Excel reserves the first two fills for the patterns none and gray125; later fills are solid colors.</summary>
    private static void WritePattern(
        XmlWriter writer,
        int index,
        ExcelColor? color
    )
    {
        if (index < 2)
        {
            writer.WriteAttributeString(
                "patternType",
                index == 0
                    ? "none"
                    : "gray125");

            return;
        }

        writer.WriteAttributeString("patternType", "solid");
        writer.WriteStartElement("fgColor", Ns);
        writer.WriteAttributeString("rgb", color!.Argb);
        writer.WriteEndElement();
        writer.WriteStartElement("bgColor", Ns);
        writer.WriteAttributeString("indexed", "64");
        writer.WriteEndElement();
    }

    private void WriteCellFormats(
        XmlWriter writer
    )
    {
        writer.WriteStartElement("cellXfs", Ns);
        writer.WriteAttributeString("count", _styles.Count.ToString(CultureInfo.InvariantCulture));

        // WHY: Lists are read by index throughout Tabular; foreach is kept for arrays, spans, and sequences.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var styleIndex = 0; styleIndex < _styles.Count; styleIndex++)
        {
            var style = _styles[styleIndex];
            WriteCellFormat(writer, style);
        }

        writer.WriteEndElement();
    }

    private static void WriteCellFormat(
        XmlWriter writer,
        CellStyleKey style
    )
    {
        writer.WriteStartElement("xf", Ns);
        writer.WriteAttributeString("numFmtId", style.NumberFormatId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fontId", style.FontId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fillId", style.FillId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("xfId", "0");
        WriteFlag(writer, "applyNumberFormat", style.NumberFormatId != 0);
        WriteFlag(writer, "applyFont", style.FontId != 0);
        WriteFlag(writer, "applyFill", style.FillId != 0);
        if (style is not { Horizontal: null, Vertical: null })
        {
            writer.WriteAttributeString("applyAlignment", "1");
            WriteAlignment(writer, style);
        }

        writer.WriteEndElement();
    }

    private static void WriteFlag(
        XmlWriter writer,
        string attribute,
        bool set
    )
    {
        if (set)
        {
            writer.WriteAttributeString(attribute, "1");
        }
    }

    private static void WriteAlignment(
        XmlWriter writer,
        CellStyleKey style
    )
    {
        writer.WriteStartElement("alignment", Ns);
        if (style.Horizontal is { } horizontal)
        {
            writer.WriteAttributeString(
                "horizontal",
                horizontal
                    .ToString()
                    .ToLowerInvariant());
        }

        if (style.Vertical is { } vertical)
        {
            writer.WriteAttributeString(
                "vertical",
                vertical
                    .ToString()
                    .ToLowerInvariant());
        }

        writer.WriteEndElement();
    }
}
