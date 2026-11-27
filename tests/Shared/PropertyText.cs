// WHY: Linked sources compile in projects with different global usings, so they import what they use; in a
// project with matching global usings these directives look redundant, which the cleanup must not act on.
// ReSharper disable RedundantUsingDirective

#pragma warning disable IDE0005

// WHY: Every test application links these helpers; each application uses only some of them.
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
using System.Text;

namespace Doka.Tabular.Testing;

/// <summary>Shapes generated text for round trips through files that store text and not single UTF-16 units.</summary>
internal static class PropertyText
{
    /// <summary>
    /// Replaces unpaired surrogates, which UTF-8 and XML cannot store, with a letter; every other character stays.
    /// </summary>
    public static string? WellFormed(
        string? text
    )
    {
        if (text is null)
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character)
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]))
            {
                builder
                    .Append(character)
                    .Append(text[index + 1]);

                index++;
            }
            else
            {
                builder.Append(
                    char.IsSurrogate(character)
                        ? 'x'
                        : character);
            }
        }

        return builder.ToString();
    }

    /// <summary>The value a file returns for <paramref name="text" />: files do not tell null from empty text.</summary>
    public static string? Empty(
        string? text
    ) => string.IsNullOrEmpty(text)
        ? null
        : text;
}
