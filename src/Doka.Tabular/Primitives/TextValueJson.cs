using System.Text.Json;

namespace Doka.Tabular;

/// <summary>Shared reading rules of the JSON converters for the own text value types.</summary>
internal static class TextValueJson
{
    /// <summary>Reads a JSON string value; any other token fails with a <see cref="JsonException" />.</summary>
    public static TValue Read<TValue>(
        ref Utf8JsonReader reader,
        Func<string, TValue> create
    )
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException();
        }

        return Create(reader.GetString()!, create);
    }

    /// <summary>Reads a JSON property name.</summary>
    public static TValue ReadName<TValue>(
        ref Utf8JsonReader reader,
        Func<string, TValue> create
    ) => Create(reader.GetString()!, create);

    // WHY: A JsonException without its own message lets the serializer name the JSON path of the
    // value; the rejected rule stays available as the inner exception.
    private static TValue Create<TValue>(
        string value,
        Func<string, TValue> create
    )
    {
        try
        {
            return create(value);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(null, exception);
        }
    }
}
