using System.Text;

namespace Doka.Tabular;

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
