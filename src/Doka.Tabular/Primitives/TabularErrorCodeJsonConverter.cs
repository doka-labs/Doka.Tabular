using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Reads and writes a <see cref="TabularErrorCode" /> as a JSON string. Library codes read back as they were
/// written; a text that <see cref="TabularErrorCode.Create" /> rejects fails with a <see cref="JsonException" />.
/// </summary>
[PublicAPI]
public sealed class TabularErrorCodeJsonConverter : JsonConverter<TabularErrorCode>
{
    /// <inheritdoc />
    public override TabularErrorCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => TextValueJson.Read(ref reader, TabularErrorCode.Create);

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        TabularErrorCode value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
