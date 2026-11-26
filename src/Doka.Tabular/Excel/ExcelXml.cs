using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Doka.Tabular.Excel;

/// <summary>XML settings and <c>ST_Xstring</c> escaping (ECMA-376 Part 1, 22.9.2.19).</summary>
internal static partial class ExcelXml
{
    public const string TransitionalMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public const string StrictMain = "http://purl.oclc.org/ooxml/spreadsheetml/main";

    public static XmlReaderSettings ReaderSettings { get; } = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        Async = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    public static XmlWriterSettings WriterSettings { get; } = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Async = true,
        Indent = false,
        NewLineHandling = NewLineHandling.None,
        CheckCharacters = true,
    };

    /// <summary>Decodes every <c>_xHHHH_</c> sequence left to right, including <c>_x005F_</c>.</summary>
    public static string Unescape(
        string text
    ) => text.Contains("_x", StringComparison.Ordinal)
        ? EscapedCharacter()
            .Replace(text, match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString())
        : text;

    /// <summary>Escapes characters that XML 1.0 does not allow and CR; protects literal <c>_xHHHH_</c> text.</summary>
    public static string Escape(
        string text,
        bool attribute
    )
    {
        StringBuilder? builder = null;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            string? replacement = null;
            if (character == '_'
                && LiteralEscape()
                    .IsMatch(text.AsSpan(index)))
            {
                replacement = "_x005F_";
            }
            else if (character == '\r'
                || (attribute && character is '\n' or '\t')
                || !IsXmlChar(text, index))
            {
                replacement = $"_x{(int)character:X4}_";
            }

            if (replacement is null)
            {
                builder?.Append(character);
                if (char.IsHighSurrogate(character)
                    && index + 1 < text.Length
                    && char.IsLowSurrogate(text[index + 1]))
                {
                    builder?.Append(text[index + 1]);
                    index++;
                }

                continue;
            }

            builder ??= new StringBuilder(text, 0, index, text.Length + 16);
            builder.Append(replacement);
        }

        return builder?.ToString() ?? text;
    }

    /// <summary>Whether <see cref="Escape" /> would change the text, so it can be written as it is otherwise.</summary>
    public static bool NeedsEscape(
        ReadOnlySpan<char> text,
        bool attribute
    )
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if ((character == '_'
                    && LiteralEscape()
                        .IsMatch(text[index..]))
                || character == '\r'
                || (attribute && character is '\n' or '\t')
                || !IsXmlChar(text, index))
            {
                return true;
            }

            if (char.IsHighSurrogate(character))
            {
                index++;
            }
        }

        return false;
    }

    /// <summary>Writes a cell reference such as <c>AB12</c>; returns the number of characters.</summary>
    public static int WriteReference(
        Span<char> destination,
        int columnIndex,
        long row
    )
    {
        var letters = 0;
        for (var value = columnIndex + 1; value > 0; value = (value - 1) / 26)
        {
            letters++;
        }

        var position = letters;
        for (var value = columnIndex + 1; value > 0; value = (value - 1) / 26)
        {
            destination[--position] = (char)('A' + (value - 1) % 26);
        }

        row.TryFormat(destination[letters..], out var digits, default, CultureInfo.InvariantCulture);

        return letters + digits;
    }

    private static bool IsXmlChar(
        ReadOnlySpan<char> text,
        int index
    )
    {
        var character = text[index];
        if (char.IsHighSurrogate(character))
        {
            return index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
        }

        if (char.IsLowSurrogate(character))
        {
            return index > 0 && char.IsHighSurrogate(text[index - 1]);
        }

        return character is '\t' or '\n' or '\r'
            || (character >= 0x20 && character <= 0xD7FF)
            || (character >= 0xE000 && character <= 0xFFFD);
    }

    [GeneratedRegex("_x([0-9A-Fa-f]{4})_", RegexOptions.CultureInvariant)]
    private static partial Regex EscapedCharacter();

    [GeneratedRegex("^_x[0-9A-Fa-f]{4}_", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralEscape();

    /// <summary>Converts a column letter reference (A, AB) to a zero-based index.</summary>
    public static int ColumnIndex(
        ReadOnlySpan<char> reference
    )
    {
        var index = 0;
        foreach (var character in reference)
        {
            if (!char.IsAsciiLetter(character))
            {
                break;
            }

            index = index * 26 + (char.ToUpperInvariant(character) - 'A') + 1;
        }

        return index - 1;
    }

    public static long RowOf(
        ReadOnlySpan<char> reference
    )
    {
        var start = 0;
        while (start < reference.Length
            && char.IsAsciiLetter(reference[start]))
        {
            start++;
        }

        return long.TryParse(reference[start..], NumberStyles.None, CultureInfo.InvariantCulture, out var row)
            ? row
            : 0;
    }

    private static string ColumnName(
        int index
    )
    {
        var name = new StringBuilder();
        var value = index + 1;
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            name.Insert(0, (char)('A' + remainder));
            value = (value - 1) / 26;
        }

        return name.ToString();
    }

    public static string Reference(
        int columnIndex,
        long row
    ) => ColumnName(columnIndex) + row.ToString(CultureInfo.InvariantCulture);
}
