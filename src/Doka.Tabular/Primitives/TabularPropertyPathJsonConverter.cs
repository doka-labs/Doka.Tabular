using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Reads and writes a <see cref="TabularPropertyPath" /> as a JSON string. An empty text or one with surrounding
/// white space fails with a <see cref="JsonException" />.
/// </summary>
[PublicAPI]
public sealed class TabularPropertyPathJsonConverter : JsonConverter<TabularPropertyPath>
{
    /// <inheritdoc />
    public override TabularPropertyPath Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => TextValueJson.Read(ref reader, TabularPropertyPath.FromText);

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        TabularPropertyPath value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
