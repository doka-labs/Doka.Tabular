namespace Doka.Tabular;

internal static class TextRules
{
    public static void RequireTrimmedNonEmpty(
        string value,
        string parameterName
    )
    {
        if (value.Length == 0)
        {
            throw new ArgumentException("The value must not be empty.", parameterName);
        }

        if (char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1]))
        {
            throw new ArgumentException("The value must not begin or end with white space.", parameterName);
        }
    }
}
