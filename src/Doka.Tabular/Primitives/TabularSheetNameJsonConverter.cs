using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doka.Tabular;

/// <summary>
/// Reads and writes a <see cref="TabularSheetName" /> as a JSON string. A name that violates the rules of
/// <see cref="TabularSheetName.Create" /> fails with a <see cref="JsonException" />.
/// </summary>
[PublicAPI]
public sealed class TabularSheetNameJsonConverter : JsonConverter<TabularSheetName>
{
    /// <inheritdoc />
    public override TabularSheetName Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => TextValueJson.Read(ref reader, TabularSheetName.Create);

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        TabularSheetName value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.Value);
}
