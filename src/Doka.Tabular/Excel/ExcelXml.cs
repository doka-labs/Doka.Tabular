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

/// <summary>Classifies Excel number formats as date, time, or other (ECMA-376 Part 1, 18.8.30).</summary>
internal static class NumberFormats
{
    public static NumberFormatKind Classify(
        int id,
        string? code
    )
    {
        if (code is null)
        {
            return id switch
            {
                14 or 15 or 16 or 17 or >= 27 and <= 31 or >= 34 and <= 36 or >= 50 and <= 58 => NumberFormatKind.Date,
                18 or 19 or 20 or 21 or 45 or 46 or 47 or 32 or 33 => NumberFormatKind.Time,
                22 => NumberFormatKind.DateTime,
                0 => NumberFormatKind.General,
                _ => NumberFormatKind.Other,
            };
        }

        return ClassifyPlaceholders(Placeholders(Tokens(code)));
    }

    private static NumberFormatKind ClassifyPlaceholders(
        string text
    )
    {
        if (text is "general" or "")
        {
            return NumberFormatKind.General;
        }

        var hasDate = text.Contains('d', StringComparison.Ordinal)
            || text.Contains('y', StringComparison.Ordinal)
            || (text.Contains('m', StringComparison.Ordinal)
                && !text.Contains('h', StringComparison.Ordinal)
                && !text.Contains('s', StringComparison.Ordinal));

        var hasTime = text.Contains('h', StringComparison.Ordinal) || text.Contains('s', StringComparison.Ordinal);

        return (hasDate, hasTime) switch
        {
            (true, true) => NumberFormatKind.DateTime,
            (true, false) => NumberFormatKind.Date,
            (false, true) => NumberFormatKind.Time,
            (false, false) => NumberFormatKind.Other,
        };
    }

    /// <summary>
    /// Whether the format shows a negative value readably. Excel applies the second of two or more sections to negative
    /// values; that section must show the number with digit placeholders and be no date or time format, which the 1900
    /// date system cannot show below zero. A code with conditions such as <c>[&lt;0]</c> chooses sections itself and is
    /// judged whole. Quoted text, escaped characters, padding, and fill are literals and never conditions.
    /// </summary>
    public static bool ShowsNegative(
        string code
    )
    {
        var tokens = Tokens(code);
        var sections = Sections(tokens);
        var negative = sections.Count >= 2 && !tokens.Any(IsCondition)
            ? sections[1]
            : tokens;

        var placeholders = Placeholders(negative);

        return ClassifyPlaceholders(placeholders) is NumberFormatKind.General or NumberFormatKind.Other
            && (placeholders == "general"
                || placeholders
                    .AsSpan()
                    .IndexOfAny("0#?")
                >= 0);
    }

    /// <summary>
    /// Splits a format code into its parts once, so that sections, conditions, and placeholders follow the same rules for
    /// quotes, escapes, and brackets (ECMA-376 Part 1, 18.8.31).
    /// </summary>
    private static List<Token> Tokens(
        string code
    )
    {
        var tokens = new List<Token>();
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];
            switch (character)
            {
                case '"':
                    var close = code.IndexOf('"', index + 1);
                    var end = close < 0
                        ? code.Length
                        : close;

                    tokens.Add(new Token(TokenKind.Literal, code[(index + 1)..end]));
                    index = end;
                    break;
                case '\\' or '_' or '*':
                    // WHY: An escape shows the next character, padding takes the width of it, fill repeats it.
                    if (index + 1 < code.Length)
                    {
                        tokens.Add(new Token(TokenKind.Literal,
                            character == '_'
                                ? string.Empty
                                : code[index + 1]
                                    .ToString()));
                    }

                    index++;
                    break;
                case '[':
                    var bracketEnd = code.IndexOf(']', index + 1);
                    var inner = bracketEnd < 0
                        ? code[(index + 1)..]
                        : code[(index + 1)..bracketEnd];

                    tokens.Add(new Token(TokenKind.Bracket, inner));
                    index = bracketEnd < 0
                        ? code.Length
                        : bracketEnd;

                    break;
                case ';':
                    tokens.Add(new Token(TokenKind.Separator, string.Empty));
                    break;
                default:
                    tokens.Add(new Token(TokenKind.Code, character.ToString()));
                    break;
            }
        }

        return tokens;
    }

    /// <summary>The sections of a format code, split at its separators.</summary>
    private static List<List<Token>> Sections(
        List<Token> tokens
    )
    {
        var sections = new List<List<Token>> { new() };
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Separator)
            {
                sections.Add([]);
            }
            else
            {
                sections[^1]
                    .Add(token);
            }
        }

        return sections;
    }

    private static bool IsCondition(
        Token token
    ) => token is { Kind: TokenKind.Bracket, Value: ['<' or '>' or '=', ..] };

    /// <summary>
    /// The lowercase placeholders of the code characters; of the brackets only elapsed time units such as <c>[h]</c>
    /// remain, as their unit letter. Literals and separators play no part.
    /// </summary>
    private static string Placeholders(
        List<Token> tokens
    )
    {
        var placeholders = new StringBuilder();
        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.Code:
                    placeholders.Append(char.ToLowerInvariant(token.Value[0]));
                    break;
                case TokenKind.Bracket:
                    var inner = token.Value.ToLowerInvariant();
                    if (inner is "h" or "hh" or "m" or "mm" or "s" or "ss")
                    {
                        placeholders.Append(inner[0]);
                    }

                    break;
            }
        }

        return placeholders.ToString();
    }

    /// <summary>The kinds of the parts of a format code.</summary>
    private enum TokenKind
    {
        /// <summary>A quoted text, an escaped character, padding, or a fill character: shown as it is.</summary>
        Literal,

        /// <summary>A bracket section such as a color, a condition, or an elapsed time unit; the value is its inner text.</summary>
        Bracket,

        /// <summary>A character of the code itself, such as a placeholder or a plain sign.</summary>
        Code,

        Separator,
    }

    private readonly record struct Token(
        TokenKind Kind,
        string Value
    );
}
