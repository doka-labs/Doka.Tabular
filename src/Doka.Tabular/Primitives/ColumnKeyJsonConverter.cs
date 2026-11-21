using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Reads and writes a <see cref="ColumnKey" /> as a JSON string, also as a dictionary key. A text that
/// <see cref="ColumnKey.Create" /> rejects fails with a <see cref="JsonException" />.
/// </summary>
[PublicAPI]
public sealed class ColumnKeyJsonConverter : JsonConverter<ColumnKey>
{
    /// <inheritdoc />
    public override ColumnKey Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => TextValueJson.Read(ref reader, ColumnKey.Create);

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        ColumnKey value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);

    /// <inheritdoc />
    public override ColumnKey ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => TextValueJson.ReadName(ref reader, ColumnKey.Create);

    /// <inheritdoc />
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        ColumnKey value,
        JsonSerializerOptions options
    ) => writer.WritePropertyName(value.Value);
}
